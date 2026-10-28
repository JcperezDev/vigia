using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.RegularExpressions;

namespace Vigia;

public sealed record OptVolume(string Letter, string Label, string Media, string Action, string? LastRun, string? LastOp, int? Fragmented);
public sealed record OptStatus(IReadOnlyList<OptVolume> Volumes, bool Scheduled);
public sealed record OptProgress(string Letter, string Line, int? Pct);
public sealed record OptResult(string Letter, bool Ok, int ExitCode, double Seconds, string Summary);

/// <summary>
/// Optimización con la herramienta de Windows (defrag.exe /O): TRIM en SSD, desfragmentación en HDD.
/// Es lo mismo que hace "Desfragmentar y optimizar unidades" cada semana. No toca archivos ni configuración.
/// </summary>
public static class Optimize
{
    static readonly string Defrag = Path.Combine(Environment.SystemDirectory, "defrag.exe");
    static Process? _running;
    static readonly object _lock = new();

    static Optimize() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static OptStatus Status(bool analyzeHdd)
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
            int? frag = analyzeHdd && m == "HDD" ? Analyze(l) : null;
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

    static bool ScheduledEnabled()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("schtasks.exe", @"/query /tn \Microsoft\Windows\Defrag\ScheduledDefrag /fo csv /nh")
                { RedirectStandardOutput = true, CreateNoWindow = true, UseShellExecute = false })!;
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
            return p.ExitCode == 0 && !o.Contains("Disabled", StringComparison.OrdinalIgnoreCase) && !o.Contains("Deshabilitad", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    static Encoding Oem() { try { return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { return Encoding.UTF8; } }

    /// <summary>Solo análisis (defrag /A): devuelve % fragmentado. No modifica el disco.</summary>
    public static int? Analyze(string letter)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(Defrag, $"{letter} /A")
                { RedirectStandardOutput = true, StandardOutputEncoding = Oem(), CreateNoWindow = true, UseShellExecute = false })!;
            var o = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(120_000)) { p.Kill(); return null; }
            var line = o.Split('\n').FirstOrDefault(x => x.Contains("fragment", StringComparison.OrdinalIgnoreCase) && x.Contains('%'));
            var m = line is null ? Match.Empty : Regex.Match(line, @"(\d+)\s*%");
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }
        catch { return null; }
    }

    public static bool Busy { get { lock (_lock) return _running is { HasExited: false }; } }

    /// <summary>Optimiza una unidad con defrag /O (TRIM o desfragmentación según el medio). Reporta progreso por línea.</summary>
    public static async Task<OptResult> RunAsync(string letter, Action<OptProgress> progress)
    {
        if (!Regex.IsMatch(letter, "^[A-Za-z]:$")) return new OptResult(letter, false, -1, 0, "Unidad no válida.");
        var sw = Stopwatch.StartNew();
        Process p;
        lock (_lock)
        {
            if (_running is { HasExited: false }) return new OptResult(letter, false, -1, 0, "Ya hay una optimización en curso.");
            p = new Process { StartInfo = new ProcessStartInfo(Defrag, $"{letter} /O /U")
                { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Oem(), CreateNoWindow = true, UseShellExecute = false } };
            p.Start();
            _running = p;
        }
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
        lock (_lock) _running = null;
        var ok = p.ExitCode == 0;
        var summary = ok ? "Listo." : tail.LastOrDefault(l => !l.Contains('%')) ?? $"defrag terminó con código {p.ExitCode}.";
        return new OptResult(letter, ok, p.ExitCode, Math.Round(sw.Elapsed.TotalSeconds, 1), summary);
    }

    /// <summary>Detiene la optimización en curso (defrag se puede interrumpir sin riesgo).</summary>
    public static void Cancel()
    {
        lock (_lock) { try { _running?.Kill(true); } catch { } }
    }
}
