using System.Management;
using LibreHardwareMonitor.Hardware;

namespace Vigia;

public sealed record CpuInfo(string Name, int Cores, int Threads, double? ClockGhz, double? Load, double? Temp);
public sealed record GpuInfo(string Name, double? Load, double? Temp, double? CoreClockMhz, double? VramUsedGb, double? VramTotalGb);
public sealed record RamInfo(double UsedGb, double TotalGb, int? SpeedMts, string? Type, int? Cl, double? LatencyNs, double? Temp);
public sealed record DiskInfo(string Letter, string Label, double UsedGb, double FreeGb, double TotalGb, double? Temp, string? Model);
public sealed record Snapshot(long Ts, bool Elevated, CpuInfo Cpu, GpuInfo? Gpu, RamInfo Ram, IReadOnlyList<DiskInfo> Drives, IReadOnlyList<string> Notes);

/// <summary>Lecturas de hardware: LibreHardwareMonitor para sensores, WMI para datos estáticos.</summary>
public sealed class Telemetry : IDisposable
{
    readonly Computer _pc = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true };
    readonly object _lock = new();
    // Tras Close() LHM ya liberó sus librerías nativas (ADL de AMD, NVAPI): tocarlas tumba el proceso
    // con una AccessViolation que .NET no deja atrapar. Todo acceso al hardware pasa por _lock y mira _closed.
    bool _closed;
    Snapshot? _last;
    readonly string _cpuName; readonly int _cores, _threads;
    readonly int? _ramSpeed; readonly string? _ramType;
    readonly List<string> _notes = new();
    readonly Dictionary<string, string> _diskModel = new(StringComparer.OrdinalIgnoreCase);
    static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public Telemetry()
    {
        try { _pc.Open(); } catch (Exception e) { _notes.Add("LibreHardwareMonitor no abrió: " + e.Message); }

        (_cpuName, _cores, _threads) = ("CPU", Environment.ProcessorCount, Environment.ProcessorCount);
        foreach (var o in Wmi("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
        {
            _cpuName = ((string)o["Name"]).Trim();
            _cores = Convert.ToInt32(o["NumberOfCores"]);
            _threads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
            break;
        }
        foreach (var o in Wmi("SELECT ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            _ramSpeed ??= o["ConfiguredClockSpeed"] is null ? null : Convert.ToInt32(o["ConfiguredClockSpeed"]);
            _ramType ??= Convert.ToInt32(o["SMBIOSMemoryType"] ?? 0) switch { 34 => "DDR5", 26 => "DDR4", 24 => "DDR3", _ => null };
        }
        // letra de unidad -> modelo del disco físico
        var models = new Dictionary<uint, string>();
        foreach (var disk in Wmi("SELECT Index, Model FROM Win32_DiskDrive"))
            if (disk["Index"] is uint ix && ((string?)disk["Model"])?.Trim() is { Length: > 0 } m) models[ix] = m;
        foreach (var p in Wmi("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", @"root\Microsoft\Windows\Storage"))
            if ((p["DriveLetter"] switch { char ch => ch, ushort u => (char)u, _ => '\0' }) is var c && char.IsLetter(c) && models.TryGetValue(Convert.ToUInt32(p["DiskNumber"]), out var model))
                _diskModel[$"{c}:"] = model;
        if (!Elevated) _notes.Add("Sin privilegios de administrador: temperaturas y timings pueden no estar disponibles.");
    }

    public static bool Elevated
    {
        get
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    // Materializa dentro del try: WMI puede fallar durante la enumeración, no solo en Get().
    static List<ManagementBaseObject> Wmi(string q, string scope = @"root\cimv2")
    {
        var list = new List<ManagementBaseObject>();
        try { using var s = new ManagementObjectSearcher(scope, q); foreach (var o in s.Get()) list.Add(o); } catch { }
        return list;
    }

    static double? S(IHardware h, SensorType t, params string[] names)
    {
        foreach (var n in names)
            foreach (var s in h.Sensors)
                if (s.SensorType == t && s.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && s.Value is float v && !float.IsNaN(v))
                    return Math.Round(v, 2);
        return null;
    }

    static IEnumerable<IHardware> All(IHardware h) => new[] { h }.Concat(h.SubHardware.SelectMany(All));

    public Snapshot Read()
    {
        lock (_lock)
        {
            if (_closed) return _last ?? new Snapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Elevated,
                new CpuInfo(_cpuName, _cores, _threads, null, null, null), null, new RamInfo(0, 0, 0, null, null, null, null), Array.Empty<DiskInfo>(), _notes);
            var hw = _pc.Hardware.SelectMany(All).ToList();
            foreach (var h in hw) { try { h.Update(); } catch { } }

            // CPU
            var cpuH = hw.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            double? load = cpuH is null ? null : S(cpuH, SensorType.Load, "CPU Total");
            double? temp = cpuH is null ? null : S(cpuH, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Average", "Core Max");
            double? clock = null;
            if (cpuH is not null)
            {
                var clocks = cpuH.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("Core #") && s.Value > 0).Select(s => (double)s.Value!.Value).ToList();
                if (clocks.Count > 0) clock = Math.Round(clocks.Max() / 1000, 2);
            }
            if (clock is null)
                foreach (var o in Wmi("SELECT CurrentClockSpeed FROM Win32_Processor")) { clock = Math.Round(Convert.ToDouble(o["CurrentClockSpeed"]) / 1000, 2); break; }
            var cpu = new CpuInfo(_cpuName, _cores, _threads, clock, load, temp);

            // GPU: la de más VRAM dedicada (evita escoger la integrada del procesador)
            var gpuH = hw.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                         .OrderByDescending(h => S(h, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total") ?? 0)
                         .FirstOrDefault();
            GpuInfo? gpu = gpuH is null ? null : new GpuInfo(
                gpuH.Name,
                S(gpuH, SensorType.Load, "GPU Core", "D3D 3D"),
                S(gpuH, SensorType.Temperature, "GPU Core", "GPU Hot Spot"),
                S(gpuH, SensorType.Clock, "GPU Core"),
                Gb(S(gpuH, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used")),
                Gb(S(gpuH, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")));

            // RAM
            var memH = hw.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                       ?? hw.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            double used = 0, avail = 0;
            if (memH is not null) { used = S(memH, SensorType.Data, "Memory Used") ?? 0; avail = S(memH, SensorType.Data, "Memory Available") ?? 0; }
            if (used + avail <= 0)
                foreach (var o in Wmi("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    var tot = Convert.ToDouble(o["TotalVisibleMemorySize"]) / 1048576; avail = Convert.ToDouble(o["FreePhysicalMemory"]) / 1048576; used = tot - avail;
                }
            // CL: el SPD da tAA en nanosegundos (no en ciclos). CL = ceil(tAA / tCK) a la velocidad actual;
            // en DDR5 el CL es par. Es el CL JEDEC del módulo, no el timing leído del controlador.
            var dimms = hw.Where(h => h.HardwareType == HardwareType.Memory && h.Sensors.Any(s => s.SensorType == SensorType.Timing)).ToList();
            double? taa = dimms.Select(d => S(d, SensorType.Timing, "tAA (CAS Latency Time)")).FirstOrDefault(v => v > 0);
            int? cl = null; double? ns = null;
            if (taa is double t && _ramSpeed is int sp && sp > 0)
            {
                var tck = 2000.0 / sp;
                var c = (int)Math.Ceiling(t / tck - 0.025);
                if (_ramType == "DDR5" && c % 2 == 1) c++;
                cl = c; ns = Math.Round(c * tck, 1);
            }
            var ramTemp = dimms.Select(d => d.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Name.StartsWith("DIMM #") && s.Value > 0)
                                                     .Select(s => (double?)s.Value).Max()).Max();
            var ram = new RamInfo(Math.Round(used, 1), Math.Round(used + avail, 1), _ramSpeed, _ramType, cl, ns, ramTemp is double rt ? Math.Round(rt, 1) : null);

            // Discos
            // Temperatura por letra: letra -> modelo físico (WMI) -> sensor LHM con ese nombre
            var storage = hw.Where(h => h.HardwareType == HardwareType.Storage).ToList();
            var drives = System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).Select(d =>
            {
                var letter = d.Name.TrimEnd('\\');
                _diskModel.TryGetValue(letter, out var model);
                var sh = model is null ? null : storage.FirstOrDefault(h => Norm(h.Name) == Norm(model))
                         ?? storage.FirstOrDefault(h => Norm(h.Name).Contains(Norm(model)) || Norm(model).Contains(Norm(h.Name)));
                return new DiskInfo(letter, string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disco local" : d.VolumeLabel,
                    Math.Round((d.TotalSize - d.TotalFreeSpace) / 1073741824.0, 1), Math.Round(d.TotalFreeSpace / 1073741824.0, 1),
                    Math.Round(d.TotalSize / 1073741824.0, 1),
                    sh is null ? null : S(sh, SensorType.Temperature, "Composite Temperature", "Temperature"),
                    model);
            }).ToList();

            return _last = new Snapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Elevated, cpu, gpu, ram, drives, _notes);
        }
    }

    static double? Gb(double? mb) => mb is null ? null : Math.Round(mb.Value / 1024, 1);

    /// <summary>Sensores de cada disco (LHM): nombre del hardware -> "Tipo|Sensor" -> valor.</summary>
    public Dictionary<string, Dictionary<string, double>> StorageSensors()
    {
        lock (_lock)
        {
            var res = new Dictionary<string, Dictionary<string, double>>();
            if (_closed) return res;
            foreach (var h in _pc.Hardware.SelectMany(All).Where(h => h.HardwareType == HardwareType.Storage))
            {
                try { h.Update(); } catch { }
                var d = res.TryGetValue(h.Name, out var ex) ? ex : res[h.Name] = new();
                foreach (var s in h.Sensors)
                    if (s.Value is float v && !float.IsNaN(v)) d[$"{s.SensorType}|{s.Name}"] = v;
            }
            return res;
        }
    }

    internal static string NormKey(string s) => Norm(s);

    /// <summary>Volcado de todos los sensores (diagnóstico).</summary>
    public IEnumerable<string> Dump()
    {
        lock (_lock)
            foreach (var h in _closed ? Enumerable.Empty<IHardware>() : _pc.Hardware.SelectMany(All))
            {
                h.Update();
                foreach (var s in h.Sensors) yield return $"{h.HardwareType}|{h.Name}|{s.SensorType}|{s.Name}|{s.Value}";
            }
    }

    // Espera a que termine la lectura en curso antes de liberar las librerías nativas.
    public void Dispose() { lock (_lock) { if (_closed) return; _closed = true; try { _pc.Close(); } catch { } } }
}
