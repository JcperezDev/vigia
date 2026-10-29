using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.RegularExpressions;

namespace Vigia;

public sealed record OptVolume(string Letter, string Label, string Media, string Action, string? LastRun, string? LastOp, int? Fragmented);
public sealed record OptStatus(IReadOnlyList<OptVolume> Volumes, bool Scheduled);
public sealed record OptProgress(string Letter, string Line, int? Pct);
public sealed record OptResult(string Letter, bool Ok, int ExitCode, double Seconds, string Summary, bool Cancelled = false);

/// <summary>
/// Optimización con la herramienta de Windows (defrag.exe /O): TRIM en SSD, desfragmentación en HDD.
/// Es lo mismo que hace "Desfragmentar y optimizar unidades" cada semana. No toca archivos ni configuración.
/// </summary>
public static class Optimize
{
    static readonly string Defrag = Path.Combine(Environment.SystemDirectory, "defrag.exe");
    static Process? _running;
    static bool _cancelled;
    static readonly object _lock = new();

    static Optimize() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static OptStatus Status(bool analyzeHdd, string? only = null)
    {
        var letters = Health.LettersByDisk();
        var media = Health.MediaByDisk();
        var last = LastRuns();
        var vols = new List<OptVolume>();
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.DriveFormat is "NTFS" or "ReFS"))
        {
            var l = d.Name.TrimEnd('\\');
            var disk = letters.FirstOrDefault(kv => kv.Value.Contains(l, StringComparer.OrdinalIgnoreCase)).Key;
            var m = media.GetValueOrDefault(disk, "Desconocido");
            last.TryGetValue(l, out var lr);
            // con una optimización en marcha no se analiza: defrag rechaza la segunda petición sobre el mismo disco
            int? frag = analyzeHdd && m == "HDD" && !Busy && (only is null || only.Equals(l, StringComparison.OrdinalIgnoreCase)) ? Analyze(l) : null;
            vols.Add(new OptVolume(l, string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disco local" : d.VolumeLabel, m,
                m == "SSD" ? "Recortar (TRIM)" : m == "HDD" ? "Desfragmentar" : "Optimizar",
                lr.when, lr.op, frag));
        }
        return new OptStatus(vols, ScheduledEnabled());
    }

    /// <summary>Última optimización por letra, del registro de eventos (Microsoft-Windows-Defrag 258).</summary>
    static Dictionary<string, (string? when, string? op)> LastRuns()
    {
        // Una pasada de Windows deja varios eventos (en SSD: re-trim + una consolidación ligera).
        // Por unidad: la pasada más reciente = eventos a menos de 5 min del último.
        var last = new Dictionary<string, (DateTime t, SortedSet<string> ops)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var r = new EventLogReader(new EventLogQuery("Application", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Defrag'] and EventID=258]]") { ReverseDirection = true });
            for (var (e, n) = (r.ReadEvent(), 0); e is not null && n < 300; e = r.ReadEvent(), n++)
                using (e)
                {
                    var op = e.Properties.Count > 0 ? e.Properties[0].Value?.ToString() : null;
                    var vol = e.Properties.Count > 1 ? e.Properties[1].Value?.ToString() ?? "" : "";
                    var m = Regex.Match(vol, @"\(([A-Z]:)\)");
                    if (op is null or "analysis" || !m.Success || e.TimeCreated is not DateTime t) continue;   // un análisis (/A) no optimiza nada
                    var name = op switch { "retrim" or "re-trim" => "TRIM", "defragmentation" => "desfragmentación", "boot optimization" => "arranque", "slab consolidation" => "consolidación", _ => op };
                    var key = m.Groups[1].Value;
                    if (!last.TryGetValue(key, out var cur)) last[key] = (t, new SortedSet<string>(StringComparer.Ordinal) { name });
                    else if ((cur.t - t).TotalMinutes < 5) cur.ops.Add(name);
                }
        }
        catch { }
        return last.ToDictionary(kv => kv.Key, kv => ((string?)Ago(kv.Value.t),
            // TRIM primero: es lo que importa en un SSD
            (string?)string.Join(" + ", kv.Value.ops.OrderBy(o => o == "TRIM" ? 0 : 1))), StringComparer.OrdinalIgnoreCase);
    }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalHours < 1 ? "hace menos de 1 h" : d.TotalDays < 1 ? $"hace {d.TotalHours:0} h" : d.TotalDays < 2 ? "ayer" : $"hace {d.TotalDays:0} días";
    }

    /// <summary>¿Está activa la tarea de Windows que optimiza las unidades cada semana? Se lee el XML de la tarea
    /// (no el texto de schtasks, que cambia con el idioma): sin &lt;Enabled&gt;false&lt;/Enabled&gt; está activa.</summary>
    internal static bool ScheduledEnabled()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), @"/query /tn \Microsoft\Windows\Defrag\ScheduledDefrag /xml")
                { RedirectStandardOutput = true, StandardOutputEncoding = Oem(), CreateNoWindow = true, UseShellExecute = false })!;
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
            return p.ExitCode == 0 && TaskXmlEnabled(o);
        }
        catch { return false; }
    }

    internal static bool TaskXmlEnabled(string xml)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml.Trim('\uFEFF', ' ', '\r', '\n', '\0'));
            var en = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Settings")?.Elements().FirstOrDefault(e => e.Name.LocalName == "Enabled");
            return en is null || !en.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    static Encoding Oem() { try { return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { return Encoding.UTF8; } }

    /// <summary>Solo análisis (Win32_Volume.DefragAnalysis, lo mismo que defrag /A): % fragmentado. No modifica el disco
    /// y no depende del idioma de Windows.</summary>
    public static int? Analyze(string letter)
    {
        if (!Regex.IsMatch(letter, "^[A-Za-z]:$")) return null;
        try
        {
            using var s = new System.Management.ManagementObjectSearcher($"SELECT * FROM Win32_Volume WHERE DriveLetter = '{letter.ToUpperInvariant()}'");
            foreach (System.Management.ManagementObject v in s.Get())
                using (v)
                {
                    var r = v.InvokeMethod("DefragAnalysis", null, null);
                    if (r?["ReturnValue"] is not uint rc || rc != 0 || r["DefragAnalysis"] is not System.Management.ManagementBaseObject a) return null;
                    return Convert.ToInt32(a["TotalPercentFragmentation"]);
                }
        }
        catch { }
        return null;
    }

    static int _active;
    public static bool Busy { get { lock (_lock) return _active > 0 || _running is { HasExited: false }; } }

    /// <summary>Optimiza una unidad con defrag /O (TRIM o desfragmentación según el medio). Reporta progreso por línea.</summary>
    public static async Task<OptResult> RunAsync(string letter, Action<OptProgress> progress)
    {
        if (!Regex.IsMatch(letter, "^[A-Za-z]:$")) return new OptResult(letter, false, -1, 0, "Unidad no válida.");
        lock (_lock) { if (_active > 0 || _running is { HasExited: false }) return new OptResult(letter, false, -1, 0, "Ya hay una optimización en curso."); _active++; _cancelled = false; }
        try { return await Loop(letter, progress); }
        finally { lock (_lock) _active--; }
    }

    static async Task<OptResult> Loop(string letter, Action<OptProgress> progress)
    {
        var sw = Stopwatch.StartNew();
        for (var attempt = 0; ; attempt++)
        {
            var (code, tail, errText, cancelled) = await RunOnce(letter, progress);
            if (code is null) return new OptResult(letter, false, -1, 0, "Ya hay una optimización en curso.");
            // defrag avisa de sus fallos con «(0x89xxxxxx)» en la salida, a veces con código de salida 0
            var hr = tail.Concat(errText.Split('\n')).Select(l => Regex.Match(l, @"\(0x(89[0-9A-Fa-f]{6})\)")).LastOrDefault(m => m.Success);
            var hres = hr is null ? (uint?)null : Convert.ToUInt32(hr.Groups[1].Value, 16);
            // 0x89000000 = la unidad está ocupada (un análisis, la pasada semanal de Windows o una que se acaba de detener)
            if (hres == 0x89000000 && !cancelled && attempt < 4 && sw.Elapsed.TotalSeconds < 30) { await Task.Delay(3000); continue; }
            // 0x89000006 = cancelado
            cancelled |= unchecked((uint)code.Value) == 0x89000006 || hres == 0x89000006;
            var ok = code == 0 && hres is null && !cancelled;
            var why = errText.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.Contains('%'))
                ?? tail.LastOrDefault(l => !l.Contains('%') && !l.EndsWith("...", StringComparison.Ordinal));
            var summary = ok ? "Listo."
                : cancelled ? "Detenida."
                : hres == 0x89000000 ? "Windows ya la está optimizando. Prueba en unos minutos."
                : why ?? $"Windows no pudo optimizarla (código 0x{unchecked((uint)code.Value):X8}).";
            return new OptResult(letter, ok, code.Value, Math.Round(sw.Elapsed.TotalSeconds, 1), summary, cancelled);
        }
    }

    static async Task<(int? code, List<string> tail, string err, bool cancelled)> RunOnce(string letter, Action<OptProgress> progress)
    {
        Process p;
        lock (_lock)
        {
            if (_running is { HasExited: false }) return (null, [], "", false);   // no debería pasar: RunAsync ya lo impide
            if (_cancelled) { _cancelled = false; return (-1, [], "", true); }   // se pidió detener mientras se esperaba para reintentar
            p = new Process { StartInfo = new ProcessStartInfo(Defrag, $"{letter} /O /U")
                { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Oem(), StandardErrorEncoding = Oem(), CreateNoWindow = true, UseShellExecute = false } };
            p.Start();
            _running = p;
        }
        var err = p.StandardError.ReadToEndAsync();   // leerlo a la vez: si se llena sin leer, defrag se queda colgado
        var tail = new List<string>();
        // defrag /U escribe el progreso con \r; leemos por caracteres para no esperar al salto de línea
        var buf = new StringBuilder();
        var reader = p.StandardOutput;
        var chunk = new char[256];
        int n;
        while ((n = await reader.ReadAsync(chunk, 0, chunk.Length)) > 0)
            for (var i = 0; i < n; i++)
            {
                var c = chunk[i];
                if (c is '\r' or '\n')
                {
                    var line = buf.ToString().Trim(); buf.Clear();
                    if (line.Length == 0) continue;
                    var m = Regex.Match(line, @"(\d{1,3})\s*%");
                    progress(new OptProgress(letter, line, m.Success ? Math.Min(100, int.Parse(m.Groups[1].Value)) : null));
                    tail.Add(line); if (tail.Count > 12) tail.RemoveAt(0);
                }
                else buf.Append(c);
            }
        await p.WaitForExitAsync();
        var errText = (await err).Trim();
        bool cancelled;
        lock (_lock) { _running = null; cancelled = _cancelled; _cancelled = false; }
        return (p.ExitCode, tail, errText, cancelled);
    }

    /// <summary>Detiene la optimización en curso (defrag se puede interrumpir sin riesgo).</summary>
    public static void Cancel()
    {
        lock (_lock) { if (_active > 0 || _running is { HasExited: false }) { _cancelled = true; try { if (_running is { HasExited: false }) _running.Kill(true); } catch { } } }
    }

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        const string on = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Settings><Hidden>true</Hidden></Settings></Task>";
        const string off = "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Settings><Enabled>false</Enabled></Settings></Task>";
        Check(TaskXmlEnabled(on), "tarea sin <Enabled> = activa");
        Check(!TaskXmlEnabled(off), "tarea con <Enabled>false = apagada");
        Check(!TaskXmlEnabled("Error: El sistema no puede encontrar el archivo"), "texto que no es XML = no se da por activa");
        Check(Analyze("C:\\..\\x") is null && Analyze("ZZ") is null, "analizar una unidad no válida se rechaza");
        var sched = ScheduledEnabled();
        log.Add($"info tarea semanal de Windows: {(sched ? "activa" : "apagada o no se pudo leer")}");
        var st = Status(false);
        Check(st.Volumes.Count > 0 && st.Volumes.All(v => v.Media is "SSD" or "HDD" or "Desconocido"), $"unidades detectadas: {string.Join(" ", st.Volumes.Select(v => v.Letter + "=" + v.Media))}");
        // análisis real (solo lee) en el primer disco mecánico, si hay
        if (st.Volumes.FirstOrDefault(v => v.Media == "HDD") is { } hdd && !Busy)
        {
            var f = Analyze(hdd.Letter);
            Check(f is >= 0 and <= 100, $"análisis de fragmentación de {hdd.Letter} sin leer texto de defrag: {f?.ToString() ?? "sin dato"} %");
        }
        return string.Join(Environment.NewLine, log);
    }
}
