using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>
/// «Tu PC para jugar»: lo que más pesa en los FPS y casi nadie revisa. Pantallas a su frecuencia máxima,
/// RAM con su perfil EXPO/XMP, Resizable BAR, modo juego, HAGS y plan de energía.
/// Lo de la BIOS solo se detecta y se explica (Vigía nunca escribe en la BIOS); lo de Windows se cambia con copia.
/// </summary>
public static partial class Rig
{
    /// <summary>Level: ok | warn | info. Fix: qué puede hacer Vigía (hz:DEVICE:HZ, tweak:ID) o null si es cosa de la BIOS.</summary>
    public record Check(string Key, string Level, string Title, string Detail, string? How, string? Fix, string? FixLabel);
    public record Screen(string Device, string Name, int Width, int Height, int Hz, int MaxHz, bool Primary);
    public record HzDone(bool Ok, string Message, bool Pending);

    public static List<Check> Checks()
    {
        var list = new List<Check>();
        string board = "";
        try { using var s = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_BaseBoard"); foreach (var o in s.Get()) board = (o["Manufacturer"] as string ?? "").Trim(); } catch { }

        foreach (var sc in Screens())
        {
            var name = sc.Primary ? $"{sc.Name} (principal)" : sc.Name;
            list.Add(sc.MaxHz > sc.Hz
                ? new("hz:" + sc.Device, "warn", $"Pantalla: {name}", $"Va a {sc.Hz} Hz y puede ir a {sc.MaxHz} Hz en {sc.Width} × {sc.Height}. Estás viendo menos fotogramas de los que da tu juego.",
                      "Si el cambio no se ve bien, se vuelve solo en 15 segundos. Si no aparece la frecuencia que esperas, revisa que el cable sea DisplayPort o HDMI 2.1.",
                      $"hz:{sc.Device}:{sc.MaxHz}", $"Poner a {sc.MaxHz} Hz")
                : new("hz:" + sc.Device, "ok", $"Pantalla: {name}", $"A {sc.Hz} Hz, lo máximo que ofrece en {sc.Width} × {sc.Height}.", null, null, null));
        }

        list.Add(Ram(board));
        if (ReBar(board) is { Key: "rebar" } rb && !rb.Detail.StartsWith("Solo se puede comprobar")) list.Add(rb);   // solo con NVIDIA, que es donde se puede medir

        var tw = Tweaks.List().ToDictionary(t => t.Id);
        // soft: si está apagado no es un fallo, solo se informa (no hay evidencia de que mejore en todos los equipos)
        void Tw(string id, string title, string on, string off, string label, bool soft = false)
        {
            on = on.Replace("{plan}", Tweaks.PlanName() ?? "Alto rendimiento"); off = off.Replace("{plan}", Tweaks.PlanName() ?? "Equilibrado");
            if (!tw.TryGetValue(id, out var t) || t.State == "na") return;
            var applied = t.State == "applied";
            list.Add(new("tweak:" + id, applied ? "ok" : soft ? "info" : "warn", title, applied ? on : off,
                applied ? null : t.Reboot ? "Hace falta reiniciar para que se note." : null,
                applied || t.Blocked is not null ? null : "tweak:" + id, applied ? null : label));
        }
        Tw("game-mode", "Modo juego", "Encendido.",
           "Apagado: Windows puede actualizar en plena partida.", "Encender modo juego");
        Tw("hags", "Programación de GPU por hardware (HAGS)", "Encendida.",
           "Apagada. Solo hace falta para la generación de fotogramas de DLSS; en lo demás depende del equipo: mide con Frametimes si la pruebas.", "Encender HAGS", soft: true);
        Tw("power-ultimate", "Plan de energía", "Usas «{plan}»: enchufado, el procesador va siempre a tope.",
           "Usas «{plan}»: el procesador baja de velocidad cuando no hace nada. En la mayoría de PCs sube al instante; si notas tirones, prueba el plan de Vigía y mide.", "Aplicar plan de energía de Vigía", soft: true);
        return list;
    }

    // ---------- RAM ----------
    internal static Check Ram(string board)
    {
        int conf = 0, rated = 0, n = 0; string type = "DDR";
        try
        {
            using var s = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed, PartNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            foreach (var o in s.Get())
            {
                n++;
                var c = Convert.ToInt32(o["ConfiguredClockSpeed"] ?? 0); if (c > 0) conf = conf == 0 ? c : Math.Min(conf, c);
                rated = Math.Max(rated, Math.Max(Convert.ToInt32(o["Speed"] ?? 0), RatedFromPart(o["PartNumber"] as string)));
                type = Convert.ToInt32(o["SMBIOSMemoryType"] ?? 0) switch { 34 => "DDR5", 26 => "DDR4", _ => "DDR" };
            }
        }
        catch { }
        if (conf == 0) return new("ram", "info", "Memoria RAM", "No se pudo leer la velocidad de la RAM.", null, null, null);
        var amd = Cpu().Contains("AMD", StringComparison.OrdinalIgnoreCase);
        var profile = amd ? "EXPO" : "XMP";
        if (rated > conf + 100)
            return new("ram", "warn", "Memoria RAM", $"Va a {conf} MT/s y es de {rated} MT/s: el perfil {profile} está apagado.",
                       Cap(BiosRam(board, profile)), null, null);
        return new("ram", "ok", "Memoria RAM", $"{type} a {conf} MT/s, su velocidad de fábrica{(n > 0 ? $" ({n} módulos)" : "")}.", null, null, null);
    }

    /// <summary>La velocidad que pone en la etiqueta del módulo (p. ej. F5-6000J3038, CMK32GX5M2B6000C36, KF560C36).</summary>
    internal static int RatedFromPart(string? part)
    {
        if (string.IsNullOrWhiteSpace(part)) return 0;
        part = part.Trim().ToUpperInvariant();
        var k = KingstonRx().Match(part);   // Kingston: KF5 60 = DDR5-6000, KF4 32 = DDR4-3200
        if (k.Success) return int.Parse(k.Groups[1].Value[1..]) * 100;
        var best = 0;
        foreach (Match m in SpeedRx().Matches(part)) { var v = int.Parse(m.Value); if (v is >= 2133 and <= 9600) best = Math.Max(best, v); }
        return best;
    }
    [GeneratedRegex(@"^KF(4\d\d|5\d\d)C")] private static partial Regex KingstonRx();
    [GeneratedRegex(@"(?<!\d)\d{4}(?!\d)")] private static partial Regex SpeedRx();

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string BiosRam(string board, string p) => Vendor(board) switch
    {
        "msi" => $"pulsa Supr al encender y, en la pantalla principal, activa «{p}» con el perfil 1. Guarda con F10.",
        "asus" => $"pulsa Supr al encender, F7 para el modo avanzado, Ai Tweaker → Ai Overclock Tuner → {p} I. Guarda con F10.",
        "gigabyte" => $"pulsa Supr al encender, Tweaker → Extreme Memory Profile → Profile 1. Guarda con F10.",
        "asrock" => $"pulsa F2 al encender, OC Tweaker → DRAM Configuration → Load {p} Setting → Profile 1. Guarda con F10.",
        _ => $"busca «{p}» o «Memory Profile» en la BIOS y elige el perfil 1.",
    };

    internal static string SelfTest()
    {
        var cases = new (string Part, int Want)[]
        {
            ("UD5-6000", 6000), ("F5-6000J3038F16G", 6000), ("CMK32GX5M2B6000C36", 6000), ("KF560C36BBE-16", 6000),
            ("KF432C16BB/8", 3200), ("FLBD532G6000HC38A", 6000), ("M425R1GB4BB0-CQKOL", 0), ("", 0), ("HMCG78AGBUA081N", 0),
        };
        return string.Join(Environment.NewLine, cases.Select(c =>
        {
            var got = RatedFromPart(c.Part);
            return $"{(got == c.Want ? "OK" : "FAIL")} ram «{c.Part}» -> {got} (esperado {c.Want})";
        }));
    }

    // ---------- Resizable BAR ----------
    internal static Check ReBar(string board)
    {
        const string title = "Resizable BAR";
        var how = $"Se activa en la BIOS: {BiosReBar(board)}";
        var smi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (!File.Exists(smi)) return new("rebar", "info", title, "Solo se puede comprobar en tarjetas NVIDIA. Con otras, mira en la BIOS que esté en «Enabled».", how, null, null);
        var txt = Run(smi, "-q -d MEMORY");
        int fb = Total(txt, "FB Memory Usage"), bar = Total(txt, "BAR1 Memory Usage");
        if (fb == 0 || bar == 0) return new("rebar", "info", title, "No se pudo leer de la tarjeta gráfica.", how, null, null);
        // Apagado, la ventana se queda en 256 MB. Con cualquier tamaño mayor ya está funcionando, aunque algunas placas
        // lo dejen en 8 GB en vez de toda la memoria: eso no es «apagado».
        if (bar > 256) return new("rebar", "ok", title, "Activado.", null, null, null);
        // 256 MB: puede estar en Enabled en la BIOS y aun así no usarse. Decir por qué, no «actívalo».
        var gpu = Run(smi, "--query-gpu=name --format=csv,noheader").Trim();
        var m = Regex.Match(gpu, @"RTX\s*(?:A\d+|(\d)\d{2,3})", RegexOptions.IgnoreCase);
        var gen = m.Success && m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;   // 2 = RTX 20, 3 = RTX 30...
        if (gpu.Length > 0 && gen is > 0 and < 3 || Regex.IsMatch(gpu, @"\bGTX\b|\bGT\s?\d", RegexOptions.IgnoreCase))
            return new("rebar", "ok", title, $"Tu {gpu.Replace("NVIDIA ", "")} no lo admite (NVIDIA lo trae desde las RTX 30), aunque la BIOS lo tenga activado. No hay nada que hacer.", null, null, null);
        if (!Bios.Uefi())
            return new("rebar", "warn", title, "No funciona: Windows arranca en modo antiguo (Legacy/CSM). Aunque la BIOS lo tenga en Enabled, así no se puede usar.",
                "Para usarlo hay que pasar el disco a GPT y arrancar en UEFI sin CSM. No lo hagas sin copia de seguridad.", null, null);
        var rtx30 = gen == 3
            ? " " + "Con RTX 30, si la tarjeta es de los primeros lotes, a veces hace falta actualizar la BIOS de la tarjeta desde la web de su fabricante."
            : "";
        return new("rebar", "warn", title, "La tarjeta no lo está usando: el procesador solo ve 256 MB de su memoria a la vez. Algunos juegos van más fluidos con él.",
            $"Si en la BIOS ya está en Enabled, revisa que «Above 4G» también lo esté y que CSM esté apagado ({BiosReBar(board)}).{rtx30} Después apaga el PC del todo y vuelve a encender.", null, null);
    }

    static int Total(string txt, string section)
    {
        var i = txt.IndexOf(section, StringComparison.Ordinal); if (i < 0) return 0;
        var m = Regex.Match(txt[i..], @"Total\s*:\s*(\d+)\s*MiB");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    static string BiosReBar(string board) => Vendor(board) switch
    {
        "msi" => "Supr al encender, F7, Settings → Advanced → PCI Subsystem Settings: «Above 4G memory» y «Re-Size BAR Support» en Enabled. Guarda con F10.",
        "asus" => "Supr al encender, F7, Advanced → PCI Subsystem Settings: «Above 4G Decoding» y «Re-Size BAR Support» en Enabled. Guarda con F10.",
        "gigabyte" => "Supr al encender, Settings → IO Ports: «Above 4G Decoding» y «Re-Size BAR Support» en Enabled. Guarda con F10.",
        "asrock" => "F2 al encender, Advanced → PCI Configuration: «Above 4G Decoding» y «Re-Size BAR Support» en Enabled. Guarda con F10.",
        _ => "activa «Above 4G Decoding» y «Re-Size BAR Support». Necesita el arranque UEFI, sin CSM.",
    };

    internal static string Vendor(string board)
    {
        var b = board.ToLowerInvariant();
        return b.Contains("micro-star") || b.Contains("msi") ? "msi" : b.Contains("asus") ? "asus"
             : b.Contains("gigabyte") ? "gigabyte" : b.Contains("asrock") ? "asrock" : "";
    }

    internal static string Cpu()
    {
        try { using var s = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_Processor"); foreach (var o in s.Get()) return o["Manufacturer"] as string ?? ""; } catch { }
        return "";
    }

    static string Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            var o = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return ""; }
            return o.Result;
        }
        catch { return ""; }
    }

    // ---------- pantallas ----------
    public static List<Screen> Screens()
    {
        var names = MonitorNames();
        var list = new List<Screen>();
        for (uint i = 0; ; i++)
        {
            var ad = NewDD();
            if (!EnumDisplayDevicesW(null, i, ref ad, 0)) break;
            if ((ad.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
            var dm = NewDM();
            if (!EnumDisplaySettingsW(ad.DeviceName, ENUM_REGISTRY_SETTINGS, ref dm) && !EnumDisplaySettingsW(ad.DeviceName, ENUM_CURRENT_SETTINGS, ref dm)) continue;
            int w = dm.dmPelsWidth, h = dm.dmPelsHeight, hz = dm.dmDisplayFrequency, max = hz;
            for (var m = 0; ; m++)
            {
                var x = NewDM();
                if (!EnumDisplaySettingsW(ad.DeviceName, m, ref x)) break;
                if (x.dmPelsWidth == w && x.dmPelsHeight == h && x.dmBitsPerPel >= 32) max = Math.Max(max, x.dmDisplayFrequency);
            }
            var mon = NewDD();
            string name = "";
            if (EnumDisplayDevicesW(ad.DeviceName, 0, ref mon, EDD_GET_DEVICE_INTERFACE_NAME))
            {
                // \\?\DISPLAY#MSI3CA9#5&xx&0&UID4353#{...} → DISPLAY\MSI3CA9\5&xx&0&UID4353, igual que WmiMonitorID
                var parts = mon.DeviceID.Split('#');
                if (parts.Length >= 3 && names.TryGetValue($"DISPLAY\\{parts[1]}\\{parts[2]}".ToUpperInvariant(), out var nm)) name = nm;
                if (name == "" && !mon.DeviceString.Contains("Generic", StringComparison.OrdinalIgnoreCase) && !mon.DeviceString.Contains("PnP", StringComparison.OrdinalIgnoreCase)) name = mon.DeviceString;
            }
            if (name == "") name = $"Pantalla {list.Count + 1}";
            list.Add(new(ad.DeviceName, name, w, h, hz, max, (ad.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0));
        }
        return list.OrderByDescending(s => s.Primary).ToList();
    }

    static Dictionary<string, string> MonitorNames()
    {
        var d = new Dictionary<string, string>();
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
            foreach (var o in s.Get())
            {
                var inst = (o["InstanceName"] as string ?? "").ToUpperInvariant();
                var u = inst.LastIndexOf('_'); if (u > 0) inst = inst[..u];
                if (o["UserFriendlyName"] is ushort[] arr)
                {
                    var nm = new string(arr.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                    if (nm != "") d[inst] = nm;
                }
            }
        }
        catch { }
        return d;
    }

    static readonly object HzGate = new();
    static System.Threading.Timer? _revert;

    /// <summary>
    /// Sube la frecuencia con copia previa. Si en 15 s no llega Keep(), vuelve sola a la de antes
    /// (por si la pantalla se queda negra o con parpadeos).
    /// </summary>
    public static HzDone SetHz(string device, int hz, Action<HzDone>? reverted = null)
    {
        lock (HzGate)
        {
            var sc = Screens().FirstOrDefault(s => s.Device == device);
            if (sc is null) return new(false, "Esa pantalla ya no está conectada.", false);
            if (hz <= sc.Hz) return new(true, $"Ya está a {sc.Hz} Hz.", false);
            if (hz > sc.MaxHz) return new(false, $"{sc.Name} no ofrece {hz} Hz en {sc.Width} × {sc.Height}.", false);
            var item = new JsonObject { ["t"] = "hz", ["device"] = device, ["w"] = sc.Width, ["h"] = sc.Height, ["hz"] = sc.Hz, ["label"] = $"{sc.Name}: {sc.Width} × {sc.Height} a {sc.Hz} Hz" };
            if (Backups.Save("hz", $"Frecuencia de {sc.Name}", new[] { item }) is null) return new(false, "No se pudo guardar la copia, así que no se cambió nada.", false);
            var r = Change(device, sc.Width, sc.Height, hz);
            if (r != 0) return new(false, $"Windows no aceptó {hz} Hz (código {r}).", false);
            _revert?.Dispose();
            _revert = new System.Threading.Timer(_ =>
            {
                lock (HzGate)
                {
                    if (_revert is null) return;
                    _revert.Dispose(); _revert = null;
                    Change(device, sc.Width, sc.Height, sc.Hz);
                }
                reverted?.Invoke(new(true, $"Volvió a {sc.Hz} Hz porque no confirmaste.", false));
            }, null, 15000, Timeout.Infinite);
            return new(true, $"{sc.Name} ahora va a {hz} Hz. ¿Se ve bien?", true);
        }
    }

    public static HzDone Keep()
    {
        lock (HzGate) { _revert?.Dispose(); _revert = null; }
        return new(true, "Listo, se queda así.", false);
    }

    /// <summary>Para restaurar una copia. Devuelve null si salió bien.</summary>
    internal static string? Restore(string device, int w, int h, int hz)
    {
        var r = Change(device, w, h, hz);
        return r == 0 ? null : $"Windows no aceptó {w} × {h} a {hz} Hz (código {r}).";
    }

    static int Change(string device, int w, int h, int hz)
    {
        var dm = NewDM();
        dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmDisplayFrequency = hz;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        var t = ChangeDisplaySettingsExW(device, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        return t != 0 ? t : ChangeDisplaySettingsExW(device, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
    }

    // ---------- Win32 ----------
    const int ENUM_CURRENT_SETTINGS = -1, ENUM_REGISTRY_SETTINGS = -2;
    const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    const int CDS_UPDATEREGISTRY = 1, CDS_TEST = 2;
    const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 1, DISPLAY_DEVICE_PRIMARY_DEVICE = 4;
    const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;

    static DEVMODE NewDM() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };
    static DISPLAY_DEVICE NewDD() => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>(), DeviceName = "", DeviceString = "", DeviceID = "", DeviceKey = "" };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevicesW(string? device, uint i, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string? device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
}
