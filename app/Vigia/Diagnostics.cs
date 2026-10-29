using System.Diagnostics.Eventing.Reader;
using System.Management;

namespace Vigia;

public sealed record DiagItem(string Level, string Title, string Detail, IReadOnlyList<string> Examples);
public sealed record SystemInfo(string Os, string Build, string Board, string Bios, double UptimeHours, DateTime? LastBoot);
public sealed record DiagReport(SystemInfo System, int Days, IReadOnlyList<DiagItem> Items, long Ts, IReadOnlyList<Crash> Crashes);

/// <summary>Diagnóstico de solo lectura: registro de eventos de Windows, volcados y estado del antivirus. No cambia nada.</summary>
public static partial class Diagnostics
{
    /// <summary>Qué significa un código de pantallazo azul común, dicho en simple.</summary>
    static string? BugWhat(string code)
    {
        if (!ulong.TryParse(code.Replace("0x", "", StringComparison.OrdinalIgnoreCase), System.Globalization.NumberStyles.HexNumber, null, out var c)) return null;
        return c switch
        {
            0x1a => "Problema con la memoria RAM (o su overclock/EXPO)",
            0x50 or 0x7e or 0x8e => "Un driver o la RAM leyó donde no debía",
            0x3b => "Un driver falló dentro de Windows; suele ser el de la gráfica",
            0x0a or 0xd1 => "Un driver falló; suele ser de red, sonido o gráfica",
            0x116 or 0x117 or 0x119 => "El driver de la gráfica dejó de responder",
            0x124 => "Error de hardware: overclock, temperatura o fuente de poder",
            0x101 => "Un núcleo del procesador dejó de responder: overclock o voltaje",
            0x133 => "Un driver tardó demasiado en responder",
            0x9f => "Un driver no supo despertar o dormir el PC",
            0xef => "Un proceso vital de Windows se cerró",
            0x7a or 0x77 => "No se pudo leer del disco: disco o cable",
            0xc2 or 0x19 => "Un driver dañó la memoria del sistema",
            0x139 => "Un driver hizo algo prohibido; suele ser antitrampas o antivirus",
            _ => null,
        };
    }

    /// <summary>Qué significa un aviso de disco común, dicho en simple.</summary>
    static string? DiskWhat(string provider, int id) => (provider, id) switch
    {
        ("disk", 7) => "Sector dañado: el disco no pudo leer una parte",
        ("disk", 11) => "Error de conexión con el disco: suele ser el cable o el puerto",
        ("disk", 51) => "Error al leer o escribir: cable, puerto o disco cansado",
        ("disk", 153) => "Windows tuvo que repetir una lectura: cable o disco lento",
        ("disk", 157) => "Se quitó un disco sin expulsarlo (USB o disco virtual)",
        ("stornvme", 11) or ("stornvme", 129) => "El disco NVMe se reinició: driver o el disco se calentó",
        ("storahci", 129) => "El disco SATA no respondió a tiempo: cable o disco",
        ("Ntfs", 55) or ("Microsoft-Windows-Ntfs", 55) => "El sistema de archivos tiene un error; Windows suele arreglarlo solo al reiniciar",
        ("Ntfs", 137) or ("Microsoft-Windows-Ntfs", 137) => "Windows no pudo deshacer un cambio en el disco; suele pasar tras un apagado de golpe",
        ("Ntfs", 140) or ("Microsoft-Windows-Ntfs", 140) => "Windows no pudo terminar una escritura; suele pasar tras un apagado de golpe",
        _ => null,
    };

    /// <summary>Qué es un servicio que falla a menudo y por qué casi nunca importa, dicho en simple.</summary>
    static string? SvcWhat(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("luafv")) return "Ayuda a programas muy viejos a guardar archivos. No arranca cuando el control de cuentas de Windows está bajo; no afecta a nada.";
        if (n.Contains("easy anti-cheat") || n.Contains("easyanticheat")) return "Antitrampas de Fortnite y otros juegos. Solo corre con el juego abierto; un fallo al cerrarlo no afecta.";
        if (n == "vgc" || n == "vgk" || n.Contains("vanguard")) return "Antitrampas de Valorant. Si Valorant abre bien, no pasa nada; si no, reinicia el PC.";
        if (n.Contains("battleye") || n == "beservice") return "Antitrampas de algunos juegos. Solo corre con el juego abierto; no afecta.";
        if (n.Contains("hvhost") || n.Contains("hv host")) return "Parte de las máquinas virtuales de Windows. Si no usas máquinas virtuales, no afecta.";
        if (n.Contains("nested network virtualization")) return "Red de máquinas virtuales dentro de otras. Si no usas eso, no afecta.";
        if (n.Contains("hyper-v") || n.StartsWith("vmcompute") || n.StartsWith("vmms")) return "Parte de las máquinas virtuales de Windows. Si no usas máquinas virtuales, no afecta.";
        if (n.Contains("windows search") || n == "wsearch") return "La búsqueda de Windows. Se reinicia sola; no afecta.";
        if (n == "bits" || n.Contains("background intelligent")) return "Descargas de Windows Update en segundo plano. Se reintenta sola; no afecta.";
        if (n.Contains("edge") && n.Contains("update")) return "El actualizador de Edge. Se reintenta solo; no afecta.";
        if (n.Contains("google") && n.Contains("update")) return "El actualizador de Chrome. Se reintenta solo; no afecta.";
        if (n.Contains("xbox") || n.StartsWith("xbl")) return "Servicio de Xbox. Solo hace falta para juegos de Xbox o Game Pass.";
        if (n.Contains("nvidia") || n.Contains("nvcontainer")) return "Servicio de NVIDIA. Si la gráfica y sus juegos van bien, no afecta.";
        if (n.Contains("amd") && !n.Contains("chipset")) return "Servicio de AMD. Si todo va bien, no afecta.";
        if (n.Contains("bluetooth")) return "Bluetooth. Si tus aparatos Bluetooth conectan bien, no afecta.";
        if (n.Contains("print") || n.Contains("spooler") || n.Contains("cola de impresión")) return "La cola de impresión. Solo importa si imprimes.";
        if (n.Contains("diagtrack") || n.Contains("connected user experiences")) return "Telemetría de Windows. No hace falta para usar el PC.";
        return null;
    }

    static string P(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    static List<ManagementBaseObject> Wmi(string scope, string q)
    {
        var list = new List<ManagementBaseObject>();
        try { using var s = new ManagementObjectSearcher(scope, q); foreach (var o in s.Get()) list.Add(o); } catch { }
        return list;
    }

    sealed record Ev(DateTime Time, string Provider, int Id, string? Message, IList<EventProperty> Props);

    static List<Ev> Query(string log, string xpath, int max = 400)
    {
        var res = new List<Ev>();
        try
        {
            using var r = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true });
            for (EventRecord? e; res.Count < max && (e = r.ReadEvent()) is not null;)
                using (e)
                {
                    string? msg = null;
                    try { msg = e.FormatDescription(); } catch { }
                    res.Add(new Ev(e.TimeCreated ?? DateTime.MinValue, e.ProviderName ?? "", e.Id, msg, e.Properties));
                }
        }
        catch { }
        return res;
    }

    static string Since(int days) => $"TimeCreated[timediff(@SystemTime) <= {days * 86400000L}]";
    static string When(DateTime t) => t.ToString("ddd d MMM, HH:mm", new System.Globalization.CultureInfo("es-ES"));
    static string First(string? s) => (s ?? "").Split('\n')[0].Trim().TrimEnd('.');

    public static DiagReport Run(int days = 7)
    {
        var items = new List<DiagItem>();

        // --- Sistema ---
        string os = "Windows", build = "", board = "", bios = ""; DateTime? boot = null;
        foreach (var o in Wmi(@"root\cimv2", "SELECT Caption, Version, BuildNumber, LastBootUpTime FROM Win32_OperatingSystem"))
        {
            os = ((string)o["Caption"]).Replace("Microsoft ", "").Trim();
            build = (string)o["Version"];
            try { boot = ManagementDateTimeConverter.ToDateTime((string)o["LastBootUpTime"]); } catch { }
        }
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k?.GetValue("DisplayVersion") is string dv) build = $"{dv} · build {k.GetValue("CurrentBuild")}.{k.GetValue("UBR")}";
        }
        catch { }
        foreach (var o in Wmi(@"root\cimv2", "SELECT Manufacturer, Product FROM Win32_BaseBoard")) board = $"{o["Manufacturer"]} {o["Product"]}".Trim();
        foreach (var o in Wmi(@"root\cimv2", "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
        {
            bios = (string?)o["SMBIOSBIOSVersion"] ?? "";
            try { bios += " · " + ManagementDateTimeConverter.ToDateTime((string)o["ReleaseDate"]).ToString("dd/MM/yyyy"); } catch { }
        }
        var uptime = Math.Round(TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours, 1);
        var sys = new SystemInfo(os, build, board, bios, uptime, boot);

        // --- Pantallazos azules ---
        var bsod = Query("System", $"*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001 and {Since(days)}]]");
        var dumps = new List<string>();
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            if (Directory.Exists(dir))
                dumps = new DirectoryInfo(dir).GetFiles("*.dmp").Where(f => f.LastWriteTime > DateTime.Now.AddDays(-days))
                    .OrderByDescending(f => f.LastWriteTime).Select(f => $"{f.Name} · {When(f.LastWriteTime)}").ToList();
        }
        catch { }
        items.Add(bsod.Count == 0 && dumps.Count == 0
            ? new DiagItem("ok", "Pantallazos azules", $"Ninguno en los últimos {days} días.", Array.Empty<string>())
            : new DiagItem("bad", "Pantallazos azules", $"{Math.Max(bsod.Count, dumps.Count)} en los últimos {days} días. Casi siempre es un driver, la RAM o un overclock; más abajo, en «¿Por qué se cerró?», sale la causa con más detalle.",
                bsod.Take(5).Select(e => $"{When(e.Time)} · código {BugCode(e)}" + (BugWhat(BugCode(e)) is { } bw ? $"\n{bw}" : "")).Concat(dumps.Take(3)).ToList()));

        // --- Apagados inesperados (corte de luz, cuelgue total, botón de encendido) ---
        var kp = Query("System", $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and {Since(days)}]]");
        items.Add(kp.Count == 0
            ? new DiagItem("ok", "Apagados inesperados", $"Ninguno en los últimos {days} días.", Array.Empty<string>())
            : new DiagItem(kp.Count >= 3 ? "bad" : "warn", "Apagados inesperados",
                $"Se apagó {P(kp.Count, "vez", "veces")} sin cerrar Windows: corte de luz, cuelgue o el botón de encendido apretado. Si fue un corte de luz, no pasa nada; si se cuelga solo, revisa temperaturas y el overclock.",
                kp.Take(5).Select(e => When(e.Time)).ToList()));

        // --- Errores de hardware (WHEA): CPU, RAM, PCIe ---
        var whea = Query("System", $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and {Since(days)}]]");
        var wheaBad = whea.Where(e => e.Id is 1 or 18 or 19 or 20 or 46 or 47).ToList();
        items.Add(whea.Count == 0
            ? new DiagItem("ok", "Errores de hardware (WHEA)", "El procesador, la RAM y el PCIe no reportaron errores.", Array.Empty<string>())
            : new DiagItem(wheaBad.Any(e => e.Id is 1 or 18 or 46) ? "bad" : "warn", "Errores de hardware (WHEA)",
                $"{P(whea.Count, "aviso", "avisos")} en los últimos {days} días: el procesador, la RAM o una tarjeta avisaron de un error que Windows corrigió. Si el PC no se cuelga, solo hay que vigilarlo; si se cuelga, baja el overclock o el EXPO de la RAM.",
                whea.Take(5).Select(e => $"{When(e.Time)} · {First(e.Message)}").ToList()));

        // --- Errores de disco y sistema de archivos ---
        var disk = Query("System", $"*[System[(Provider[@Name='disk'] or Provider[@Name='Ntfs'] or Provider[@Name='stornvme'] or Provider[@Name='storahci'] or Provider[@Name='Microsoft-Windows-Ntfs']) and (Level=1 or Level=2 or Level=3) and {Since(days)}]]");
        // Solo discos físicos que siguen conectados: una ISO o un disco virtual montado (y luego quitado)
        // deja avisos de «Harddisk N» que no son fallos del PC.
        var phys = Wmi(@"root\cimv2", "SELECT Index, Model FROM Win32_DiskDrive")
            .Where(o => !((o["Model"] as string ?? "").Contains("Virtual", StringComparison.OrdinalIgnoreCase)))
            .Select(o => Convert.ToInt32(o["Index"])).ToHashSet();
        bool OnRealDisk(string? msg)
        {
            var m = System.Text.RegularExpressions.Regex.Match(msg ?? "", @"Harddisk(\d+)");
            return !m.Success || phys.Count == 0 || phys.Contains(int.Parse(m.Groups[1].Value));
        }
        var diskReal = disk.Where(e => !(e.Provider.Contains("Ntfs") && e.Id == 98) && OnRealDisk(e.Message)).ToList(); // 98 = volumen revisado y OK
        items.Add(diskReal.Count == 0
            ? new DiagItem("ok", "Errores de disco", "Sin errores de lectura/escritura ni del sistema de archivos.", Array.Empty<string>())
            : new DiagItem(diskReal.Any(e => e.Provider == "disk" && e.Id is 7 or 11 or 51) ? "warn" : "info", "Errores de disco",
                $"{P(diskReal.Count, "aviso", "avisos")} de los discos en los últimos {days} días. Si son pocos, suele ser un cable flojo o un apagado de golpe; en «Salud de discos» ves si el disco está bien.",
                diskReal.Take(5).Select(e => $"{When(e.Time)} · {e.Provider} {e.Id}" + (DiskWhat(e.Provider, e.Id) is { } dw ? $"\n{dw}" : $" · {First(e.Message)}")).ToList()));

        // --- Drivers/servicios que fallaron ---
        var svc = Query("System", $"*[System[Provider[@Name='Service Control Manager'] and (EventID=7000 or EventID=7001 or EventID=7023 or EventID=7024 or EventID=7031 or EventID=7034) and {Since(days)}]]");
        var groups = svc.GroupBy(e => e.Props.Count > 0 ? e.Props[0].Value?.ToString() ?? "?" : "?").OrderByDescending(g => g.Count()).ToList();
        var svcNames = groups.Take(6).Select(g => $"{g.Key} · {P(g.Count(), "vez", "veces")}" + (SvcWhat(g.Key) is { } w ? $"\n{w}" : "")).ToList();
        if (groups.Count > 6) svcNames.Add($"Otros {groups.Count - 6} servicios · {P(groups.Skip(6).Sum(g => g.Count()), "vez", "veces")}");
        bool allKnown = groups.All(g => SvcWhat(g.Key) is not null);
        items.Add(svc.Count == 0
            ? new DiagItem("ok", "Servicios y drivers", "Ningún servicio falló al arrancar o se cerró solo.", Array.Empty<string>())
            : new DiagItem("info", "Servicios y drivers",
                $"{P(svc.Count, "aviso", "avisos")} en los últimos {days} días de servicios que no arrancaron o se cerraron solos. "
                + (allKnown ? "Todos son de cosas que no hacen falta para usar el PC: no hay que hacer nada."
                            : "Si el PC va bien, no hay que hacer nada. Abajo dice qué es cada uno."), svcNames));

        // --- Antivirus (solo lectura) ---
        var av = Wmi(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge, QuickScanAge FROM MSFT_MpComputerStatus").FirstOrDefault();
        var avOthers = Wmi(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct").Select(o => (string)o["displayName"]).Where(n => !n.Contains("Defender")).ToList();
        if (av is null)
            items.Add(new DiagItem(avOthers.Count > 0 ? "ok" : "info", "Antivirus",
                avOthers.Count > 0 ? $"Protegido por {string.Join(", ", avOthers)}." : "No se pudo leer el estado de Microsoft Defender.", Array.Empty<string>()));
        else
        {
            var on = av["AntivirusEnabled"] is true;
            var rt = av["RealTimeProtectionEnabled"] is true;
            var age = Convert.ToInt32(av["AntivirusSignatureAge"] ?? 0);
            var lv = !rt && avOthers.Count == 0 ? "bad" : rt && age > 7 ? "warn" : "ok";
            var detail = rt ? $"Microsoft Defender activo, firmas de hace {P(age, "día", "días")}."
                : avOthers.Count > 0 ? $"Defender en pausa; protege {string.Join(", ", avOthers)}."
                : !on ? "Microsoft Defender está desactivado y no hay otro antivirus. El equipo no tiene protección contra malware."
                : "Defender está instalado pero la protección en tiempo real está apagada.";
            items.Add(new DiagItem(lv, "Antivirus", detail, Array.Empty<string>()));
        }

        // --- Reinicio pendiente ---
        var pending = false;
        try
        {
            using var lm = Microsoft.Win32.Registry.LocalMachine;
            pending = lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null
                   || lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null;
        }
        catch { }
        items.Add(new DiagItem(pending ? "info" : "ok", "Reinicio pendiente",
            pending ? $"Windows tiene actualizaciones esperando un reinicio (encendido hace {uptime:0} h)." : $"Nada pendiente. Encendido hace {uptime:0} h.",
            Array.Empty<string>()));

        items.Add(Clock());

        List<Crash> crashes; try { crashes = Crashes(days); } catch { crashes = []; }
        return new DiagReport(sys, days, items, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), crashes);
    }

    // --- Reloj del sistema ---
    // Guías viejas de «más FPS» dejan opciones de arranque que empeoran la latencia en CPUs modernas.
    // Solo se leen; Vigía no las cambia.
    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    static extern int NtQueryTimerResolution(out uint min, out uint max, out uint cur);

    static DiagItem Clock()
    {
        var bad = new List<string>();
        try
        {
            var bcd = Tweaks.Run("bcdedit.exe", "/enum {current}").output;
            foreach (var (key, why) in new[] {
                ("useplatformclock", "useplatformclock: obliga a usar el reloj HPET, más lento. Suele bajar FPS."),
                ("useplatformtick", "useplatformtick: tick fijo de la placa en vez del de la CPU."),
                ("disabledynamictick", "disabledynamictick: la CPU no descansa entre ticks. Más consumo, casi sin ganancia."),
                ("tscsyncpolicy", "tscsyncpolicy: cambia cómo se sincroniza el reloj entre núcleos.") })
                if (System.Text.RegularExpressions.Regex.IsMatch(bcd, $@"^\s*{key}\s+(Yes|Enhanced|Legacy|Default)\b", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    bad.Add(why);
        }
        catch { }
        var res = "";
        try { if (NtQueryTimerResolution(out _, out var max, out var cur) == 0) { var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES"); res = $" Resolución actual del temporizador: {(cur / 10000.0).ToString("0.0##", es)} ms (mejor posible {(max / 10000.0).ToString("0.0##", es)} ms)."; } }
        catch { }
        return bad.Count == 0
            ? new DiagItem("ok", "Reloj del sistema", "El arranque usa el reloj por defecto de Windows." + res, Array.Empty<string>())
            : new DiagItem("warn", "Reloj del sistema",
                $"El arranque tiene {bad.Count} {(bad.Count == 1 ? "opción cambiada" : "opciones cambiadas")} por alguna guía de optimización. Para quitarla: bcdedit /deletevalue <nombre> como administrador." + res, bad);
    }

    static string BugCode(Ev e)
    {
        // El primer parámetro del 1001 es el código de parada (p. ej. 0x0000001a)
        var p = e.Props.Count > 0 ? e.Props[0].Value?.ToString() : null;
        return string.IsNullOrWhiteSpace(p) ? "desconocido" : p!;
    }
}
