using System.Management;
using LibreHardwareMonitor.Hardware;

namespace Vigia;

public sealed record CpuInfo(string Name, int Cores, int Threads, double? ClockGhz, double? Load, double? Temp);
public sealed record GpuInfo(string Name, double? Load, double? Temp, double? CoreClockMhz, double? VramUsedGb, double? VramTotalGb);
public sealed record RamInfo(double UsedGb, double TotalGb, int? SpeedMts, string? Type, int? Cl, double? LatencyNs);
public sealed record DiskInfo(string Letter, string Label, double UsedGb, double FreeGb, double TotalGb, double? Temp);
public sealed record Snapshot(long Ts, bool Elevated, CpuInfo Cpu, GpuInfo? Gpu, RamInfo Ram, IReadOnlyList<DiskInfo> Drives, IReadOnlyList<string> Notes);

/// <summary>Lecturas de hardware: LibreHardwareMonitor para sensores, WMI para datos estáticos.</summary>
public sealed class Telemetry : IDisposable
{
    readonly Computer _pc = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true };
    readonly object _lock = new();
    readonly string _cpuName; readonly int _cores, _threads;
    readonly int? _ramSpeed; readonly string? _ramType;
    readonly List<string> _notes = new();

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

    static IEnumerable<ManagementBaseObject> Wmi(string q)
    {
        ManagementObjectCollection? res = null;
        try { res = new ManagementObjectSearcher(q).Get(); } catch { }
        if (res is null) yield break;
        foreach (var o in res) yield return o;
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

            // GPU (dedicada primero, luego integrada)
            var gpuH = hw.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
                       ?? hw.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
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
            // CL desde SPD si LHM expone la DIMM (requiere admin + PawnIO)
            int? cl = null;
            foreach (var d in hw.Where(h => h.HardwareType == HardwareType.Memory))
                foreach (var s in d.Sensors)
                    if (s.Name.Contains("CAS", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("tCL", StringComparison.OrdinalIgnoreCase))
                        if (s.Value is float v && v > 0) { cl ??= (int)v; }
            double? ns = cl is int c && _ramSpeed is int sp && sp > 0 ? Math.Round(c * 2000.0 / sp, 1) : null;
            var ram = new RamInfo(Math.Round(used, 1), Math.Round(used + avail, 1), _ramSpeed, _ramType, cl, ns);

            // Discos
            var storTemps = hw.Where(h => h.HardwareType == HardwareType.Storage).Select(h => S(h, SensorType.Temperature, "Temperature", "Composite Temperature")).Where(t => t is not null).ToList();
            var drives = System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).Select((d, i) =>
                new DiskInfo(d.Name.TrimEnd('\\'), string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disco local" : d.VolumeLabel,
                    Math.Round((d.TotalSize - d.TotalFreeSpace) / 1073741824.0, 1), Math.Round(d.TotalFreeSpace / 1073741824.0, 1),
                    Math.Round(d.TotalSize / 1073741824.0, 1), i < storTemps.Count ? storTemps[i] : null)).ToList();

            return new Snapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Elevated, cpu, gpu, ram, drives, _notes);
        }
    }

    static double? Gb(double? mb) => mb is null ? null : Math.Round(mb.Value / 1024, 1);

    /// <summary>Volcado de todos los sensores (diagnóstico).</summary>
    public IEnumerable<string> Dump()
    {
        lock (_lock)
            foreach (var h in _pc.Hardware.SelectMany(All))
            {
                h.Update();
                foreach (var s in h.Sensors) yield return $"{h.HardwareType}|{h.Name}|{s.SensorType}|{s.Name}|{s.Value}";
            }
    }

    public void Dispose() { try { _pc.Close(); } catch { } }
}
