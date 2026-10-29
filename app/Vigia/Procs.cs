using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Vigia;

/// <summary>
/// Lo que más consume y si es raro. Solo mide mientras la vista Monitor está a la vista: sin hilo propio,
/// sin avisos, sin nada en la bandeja. Los juegos son consumo normal; lo que se marca es una app que no es
/// juego y lleva rato comiéndose el procesador (sobre todo sin ventana) o una parte grande de la memoria.
/// </summary>
public static class Procs
{
    public record Row(string Name, string Exe, int Count, double Cpu, double CpuAvg, double RamGb, bool Window,
        string Kind, string Level, string? Why, string? Path, bool CanClose);
    public record View(long Ts, int Seconds, string? Game, List<Row> Top);
    public record Done(bool Ok, string Message);

    const int Window = 30;          // segundos para el promedio
    const double CpuHigh = 25;      // % de todo el procesador, con ventana
    const double CpuBack = 10;      // % sin ventana
    const double RamShare = 0.25;   // parte de la RAM total

    static readonly object Gate = new();
    static Dictionary<int, (string Name, TimeSpan Cpu)> _prev = new();
    static DateTime _prevAt;
    static readonly Dictionary<string, Queue<(DateTime At, double Cpu)>> Hist = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<int, string?> Paths = new();
    static DateTime _since;

    // de Windows: pueden subir un rato (actualizaciones, indexado, análisis) y bajan solos
    static readonly Dictionary<string, string> WindowsWork = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TiWorker"] = "Windows Update instalando. Baja sola al terminar.",
        ["TrustedInstaller"] = "Windows Update instalando. Baja sola al terminar.",
        ["MoUsoCoreWorker"] = "Windows Update buscando o bajando. Baja sola al terminar.",
        ["wuauserv"] = "Windows Update.",
        ["MsMpEng"] = "Defender analizando. Baja solo al terminar.",
        ["MpDefenderCoreService"] = "Defender.",
        ["SearchIndexer"] = "Windows indexando archivos para la búsqueda.",
        ["CompatTelRunner"] = "Tarea de compatibilidad de Windows; corre de vez en cuando.",
        ["mscorsvw"] = ".NET preparando programas tras una actualización.",
        ["ngen"] = ".NET preparando programas tras una actualización.",
        ["dllhost"] = "Proceso auxiliar de Windows (miniaturas, etc.).",
        ["System"] = "Núcleo y drivers. Si se queda alto mucho rato, suele ser un driver.",
        ["Memory Compression"] = "Windows comprimiendo memoria.",
        ["dwm"] = "Dibuja el escritorio y las ventanas.",
        ["audiodg"] = "Audio de Windows (efectos y ecualizadores).",
    };

    // nunca se ofrecen para cerrar
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
        "dwm", "explorer", "fontdrvhost", "Memory Compression", "sihost", "ctfmon", "MsMpEng", "MpDefenderCoreService",
        "SecurityHealthService", "TiWorker", "TrustedInstaller", "audiodg", "spoolsv", "WUDFHost", "conhost", "Vigia", "msedgewebview2",
    };

    static readonly string[] GameDirs = [@"\steamapps\common\", @"\Epic Games\", @"\Riot Games\", @"\XboxGames\", @"\WindowsApps\Microsoft.Minecraft", @"\Battle.net\", @"\Call of Duty\", @"\Ubisoft Game Launcher\games\", @"\EA Games\"];

    /// <summary>Empieza a contar de cero (al abrir la vista).</summary>
    public static void Reset()
    {
        lock (Gate) { _prev.Clear(); Hist.Clear(); Paths.Clear(); _prevAt = default; _since = DateTime.UtcNow; }
    }

    public static View Sample()
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (_since == default) _since = now;
            var threads = Environment.ProcessorCount;
            var totalRam = TotalRamBytes();
            var windows = WindowPids();
            var fg = Fps.ForegroundGame();

            var cur = new Dictionary<int, (string Name, TimeSpan Cpu)>();
            var groups = new Dictionary<string, (int Count, double Cpu, long Ram, bool Win, int Pid)>(StringComparer.OrdinalIgnoreCase);
            var dt = _prevAt == default ? 0 : (now - _prevAt).TotalSeconds;
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == 0) continue;
                    string name; TimeSpan cpu; long ram;
                    try { name = p.ProcessName; ram = p.WorkingSet64; } catch { continue; }
                    try { cpu = p.TotalProcessorTime; } catch { cpu = TimeSpan.Zero; }   // protegidos: sin CPU, con RAM
                    cur[p.Id] = (name, cpu);
                    double pct = 0;
                    if (dt > 0 && _prev.TryGetValue(p.Id, out var o) && o.Name == name && cpu >= o.Cpu)
                        pct = (cpu - o.Cpu).TotalSeconds / dt / threads * 100;
                    groups.TryGetValue(name, out var g);
                    groups[name] = (g.Count + 1, g.Cpu + pct, g.Ram + ram, g.Win || windows.Contains(p.Id), g.Count == 0 ? p.Id : g.Pid);
                }
            }
            foreach (var pid in Paths.Keys.Where(k => !cur.ContainsKey(k)).ToList()) Paths.Remove(pid);
            _prev = cur; _prevAt = now;
            if (dt == 0) return new View(Ms(now), 0, fg?.Name, []);

            foreach (var (name, g) in groups)
            {
                if (!Hist.TryGetValue(name, out var q)) Hist[name] = q = new();
                q.Enqueue((now, g.Cpu));
                while (q.Count > 0 && (now - q.Peek().At).TotalSeconds > Window) q.Dequeue();
            }
            foreach (var k in Hist.Keys.Where(k => !groups.ContainsKey(k)).ToList()) Hist.Remove(k);

            var watched = (int)Math.Min(Window, (now - _since).TotalSeconds);
            var rows = new List<Row>();
            foreach (var (name, g) in groups)
            {
                if (name == "Idle" || g.Pid == Environment.ProcessId) continue;
                var q = Hist[name];
                var avg = q.Average(x => x.Cpu);
                var full = q.Count > 0 && (now - q.Peek().At).TotalSeconds >= Window - 3;
                rows.Add(Classify(name, g.Count, g.Cpu, avg, g.Ram, g.Win, g.Pid, full, totalRam, fg));
            }
            var top = rows
                .OrderByDescending(r => r.Level == "warn")
                .ThenByDescending(r => Math.Max(r.CpuAvg, r.Cpu) * 4 + r.RamGb * 100 / Math.Max(1, totalRam / 1e9))
                .Take(10).ToList();
            Closable = rows.Where(r => r.CanClose).Select(r => r.Exe).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new View(Ms(now), watched, fg?.Name, top);
        }
    }

    static Row Classify(string name, int count, double cpu, double avg, long ram, bool win, int pid, bool full, long totalRam, Fps.Fg? fg)
    {
        string? path = null;
        bool needPath = avg >= CpuBack / 2 || cpu >= CpuBack || ram > totalRam * RamShare / 2;
        if (needPath) path = PathOf(pid);
        var gb = Math.Round(ram / 1073741824.0, 2);
        var canClose = !Protected.Contains(name);

        bool game = Fps.Known.ContainsKey(name) || (fg is { } f && f.Exe.Equals(name, StringComparison.OrdinalIgnoreCase))
                    || (path is not null && GameDirs.Any(d => path.Contains(d, StringComparison.OrdinalIgnoreCase)));
        if (game) return new(Pretty(name), name, count, R(cpu), R(avg), gb, win, "game", "ok", null, path, canClose);
        if (WindowsWork.TryGetValue(name, out var ww))
            return new(Pretty(name), name, count, R(cpu), R(avg), gb, win, "windows", "ok", ww, path, false);

        string? why = null;
        if (full && avg >= CpuHigh)
            why = $"Lleva {Window} s usando en promedio el {R(avg)} % de todo el procesador.";
        else if (full && !win && avg >= CpuBack)
            why = $"No tiene ventana abierta y lleva {Window} s usando el {R(avg)} % del procesador por detrás.";
        else if (ram >= totalRam * RamShare)
            why = $"Está ocupando {gb:0.#} GB, más de la cuarta parte de tu memoria.";
        if (why is not null && path is null) why += " No se pudo ver de dónde se abrió.";
        return new(Pretty(name), name, count, R(cpu), R(avg), gb, win, win ? "app" : "background", why is null ? "ok" : "warn", why, path, canClose);
    }

    /// <summary>Cierra todas las instancias de ese programa. Solo a petición y nunca los de Windows.</summary>
    static HashSet<string> Closable = new(StringComparer.OrdinalIgnoreCase);

    public static Done Close(string exe)
    {
        bool shown; lock (Gate) shown = Closable.Contains(exe ?? "");
        if (!shown) return new(false, "Ese programa no está en la lista; vuelve a mirar.");
        if (string.IsNullOrWhiteSpace(exe) || Protected.Contains(exe) || WindowsWork.ContainsKey(exe))
            return new(false, "Ese proceso es de Windows y no se cierra desde aquí.");
        int ok = 0, fail = 0;
        foreach (var p in Process.GetProcessesByName(exe))
            using (p)
            {
                if (p.Id == Environment.ProcessId) continue;
                try { p.Kill(entireProcessTree: false); p.WaitForExit(3000); ok++; } catch { fail++; }
            }
        return ok > 0 && fail == 0 ? new(true, $"{Pretty(exe)} cerrado.")
             : ok > 0 ? new(true, $"Se cerraron {ok}; {fail} no se dejaron (pueden necesitar administrador).")
             : new(false, fail > 0 ? "No se dejó cerrar (puede necesitar administrador o ya no estaba)." : "Ya no estaba abierto.");
    }

    public static void Reveal(string exe)
    {
        string? path = null;
        foreach (var p in Process.GetProcessesByName(exe)) using (p) { path ??= PathOf(p.Id); }
        if (path is null || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
    }

    static string Pretty(string name) => Fps.Known.TryGetValue(name, out var n) ? n : name;
    static double R(double v) => Math.Round(v, 1);
    static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

    static string? PathOf(int pid)
    {
        if (Paths.TryGetValue(pid, out var c)) return c;
        string? path = null;
        var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION: lo mínimo, como el Administrador de tareas
        if (h != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024); int len = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref len)) path = sb.ToString(0, len);
            }
            finally { CloseHandle(h); }
        }
        Paths[pid] = path;
        return path;
    }

    static HashSet<int> WindowPids()
    {
        var set = new HashSet<int>();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && GetWindowTextLength(h) > 0 && GetWindow(h, 4 /*GW_OWNER*/) == IntPtr.Zero)
            { GetWindowThreadProcessId(h, out var pid); set.Add((int)pid); }
            return true;
        }, IntPtr.Zero);
        return set;
    }

    static long TotalRamBytes()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (long)m.ullTotalPhys : 16L << 30;
    }

    public static string SelfTest()
    {
        Reset();
        Sample(); Thread.Sleep(1000);
        var v = Sample();
        return v.Top.Count > 0 ? $"ok {v.Top.Count} filas; arriba: {v.Top[0].Name} {v.Top[0].Cpu} % {v.Top[0].RamGb} GB" : "sin filas";
    }

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int len);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
}
