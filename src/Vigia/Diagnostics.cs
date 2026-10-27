using System.Diagnostics.Eventing.Reader;
using System.Management;

namespace Vigia;

public sealed record DiagItem(string Level, string Title, string Detail, IReadOnlyList<string> Examples);
public sealed record SystemInfo(string Os, string Build, string Board, string Bios, double UptimeHours, DateTime? LastBoot);
public sealed record DiagReport(SystemInfo System, int Days, IReadOnlyList<DiagItem> Items, long Ts);

/// <summary>Diagnóstico de solo lectura: registro de eventos de Windows, volcados y estado del antivirus. No cambia nada.</summary>
public static class Diagnostics
{
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
            : new DiagItem("bad", "Pantallazos azules", $"{Math.Max(bsod.Count, dumps.Count)} en los últimos {days} días.",
                bsod.Take(5).Select(e => $"{When(e.Time)} · código {BugCode(e)}").Concat(dumps.Take(3)).ToList()));

        // --- Apagados inesperados (corte de luz, cuelgue total, botón de encendido) ---
        var kp = Query("System", $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and {Since(days)}]]");
        items.Add(kp.Count == 0
            ? new DiagItem("ok", "Apagados inesperados", $"Ninguno en los últimos {days} días.", Array.Empty<string>())
            : new DiagItem(kp.Count >= 3 ? "bad" : "warn", "Apagados inesperados",
                $"{kp.Count} vez/veces se apagó sin cerrar Windows (corte de luz, cuelgue o se mantuvo el botón).",
                kp.Take(5).Select(e => When(e.Time)).ToList()));

        // --- Errores de hardware (WHEA): CPU, RAM, PCIe ---
        var whea = Query("System", $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and {Since(days)}]]");
        var wheaBad = whea.Where(e => e.Id is 1 or 18 or 19 or 20 or 46 or 47).ToList();
        items.Add(whea.Count == 0
            ? new DiagItem("ok", "Errores de hardware (WHEA)", "El procesador, la RAM y el PCIe no reportaron errores.", Array.Empty<string>())
            : new DiagItem(wheaBad.Any(e => e.Id is 1 or 18 or 46) ? "bad" : "warn", "Errores de hardware (WHEA)",
                $"{P(whea.Count, "reporte", "reportes")}. Suele indicar overclock/EXPO inestable, RAM o un dispositivo PCIe con problemas.",
                whea.Take(5).Select(e => $"{When(e.Time)} · {First(e.Message)}").ToList()));

        // --- Errores de disco y sistema de archivos ---
        var disk = Query("System", $"*[System[(Provider[@Name='disk'] or Provider[@Name='Ntfs'] or Provider[@Name='stornvme'] or Provider[@Name='storahci'] or Provider[@Name='Microsoft-Windows-Ntfs']) and (Level=1 or Level=2 or Level=3) and {Since(days)}]]");
        var diskReal = disk.Where(e => !(e.Provider.Contains("Ntfs") && e.Id == 98)).ToList(); // 98 = volumen revisado y OK
        items.Add(diskReal.Count == 0
            ? new DiagItem("ok", "Errores de disco", "Sin errores de lectura/escritura ni del sistema de archivos.", Array.Empty<string>())
            : new DiagItem(diskReal.Any(e => e.Provider == "disk" && e.Id is 7 or 11 or 51) ? "warn" : "info", "Errores de disco",
                $"{P(diskReal.Count, "aviso", "avisos")} del controlador de disco o NTFS.",
                diskReal.Take(5).Select(e => $"{When(e.Time)} · {e.Provider} {e.Id} · {First(e.Message)}").ToList()));

        // --- Drivers/servicios que fallaron ---
        var svc = Query("System", $"*[System[Provider[@Name='Service Control Manager'] and (EventID=7000 or EventID=7001 or EventID=7023 or EventID=7024 or EventID=7031 or EventID=7034) and {Since(days)}]]");
        var svcNames = svc.Select(e => e.Props.Count > 0 ? e.Props[0].Value?.ToString() ?? "?" : "?")
            .GroupBy(n => n).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} · {g.Count()} vez/veces").ToList();
        items.Add(svc.Count == 0
            ? new DiagItem("ok", "Servicios y drivers", "Ningún servicio falló al arrancar o se cerró solo.", Array.Empty<string>())
            : new DiagItem("info", "Servicios y drivers", $"{P(svc.Count, "fallo", "fallos")} de servicios. Casi siempre es inofensivo si no notas nada raro.", svcNames));

        // --- Programas que se cerraron o colgaron ---
        var apps = Query("Application", $"*[System[(Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002)] and System[{Since(days)}]]");
        var appTop = apps.Select(e => e.Props.Count > 0 ? e.Props[0].Value?.ToString() ?? "?" : "?")
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} · {g.Count()} vez/veces").ToList();
        items.Add(apps.Count == 0
            ? new DiagItem("ok", "Programas que se cerraron", "Ningún programa se cerró de golpe ni se colgó.", Array.Empty<string>())
            : new DiagItem(apps.Count >= 15 ? "warn" : "info", "Programas que se cerraron", $"{P(apps.Count, "cierre o cuelgue", "cierres o cuelgues")}.", appTop));

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

        return new DiagReport(sys, days, items, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    static string BugCode(Ev e)
    {
        // El primer parámetro del 1001 es el código de parada (p. ej. 0x0000001a)
        var p = e.Props.Count > 0 ? e.Props[0].Value?.ToString() : null;
        return string.IsNullOrWhiteSpace(p) ? "desconocido" : p!;
    }
}
