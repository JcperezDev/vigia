using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Vigia;

/// <summary>
/// FPS reales de una partida. Cuenta los fotogramas que el juego entrega a Windows (eventos Present de DXGI por ETW,
/// lo mismo que miden PresentMon o la superposición de la tarjeta). No toca el juego ni se inyecta en él.
/// Solo corre cuando el usuario lo pide: espera a que haya un juego delante, mide y se apaga sola.
/// </summary>
public static class Fps
{
    /// <summary>Una medición. Los campos del final son nuevos: en mediciones viejas de fps.json quedan en null.
    /// Source: "present" (DXGI: DX10/11/12) o "d3d9". Ojo: se cuentan llamadas Present del juego, no fotogramas que de verdad llegaron a la pantalla.</summary>
    public record Result(string Id, string At, string Game, string Exe, double Seconds, double Avg, double Low1, double Max,
        int Frames, string Label, Dictionary<string, string> Setup,
        double? Low01 = null, double? AvgMs = null, double? P95Ms = null, double? P99Ms = null, double? P999Ms = null,
        int? Stutters = null, double? StutterMs = null, string Source = "present");
    public record WatchState(bool Active, string Phase, string? Game, int Done, int Target, string? Error);

    static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    const int PresentStart = 42;   // Present_Start
    // DX9: el runtime de Direct3D 9 tiene su propio proveedor; evento 1 = Present (inicio).
    static readonly Guid D3d9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    const int D3d9PresentStart = 1;
    // Núcleo gráfico de Windows: no se usa para medir, solo para saber si el juego dibuja por otra vía (Vulkan, OpenGL…)
    // y decir «no se pudo medir» en vez de dar un número falso. Palabra clave «Present» (0x8000000); eventos Blit, Flip,
    // PresentHistory (inicio) y Present, que salen en el contexto del proceso que dibuja.
    static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    const ulong DxgKrnlPresentKeyword = 0x8000000;
    static readonly HashSet<int> KernelPresentIds = [166, 168, 171, 184];
    /// <summary>El juego estaba delante y dibujaba, pero con una API que Vigía no cuenta: no es 0 FPS, es que no hay medida.</summary>
    public sealed class ApiNotSupported(string message) : Exception(message);
    internal const string ApiNotSupportedMsg = "Este juego dibuja con una API que Vigía todavía no mide (probablemente Vulkan u OpenGL). No hay medición: no es que vaya a 0 FPS.";
    const string SessionName = "Vigia-FPS";

    /// <summary>Juegos conocidos: nombre de proceso → nombre bonito. El resto se reconoce si está a pantalla completa.</summary>
    public static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FortniteClient-Win64-Shipping"] = "Fortnite",
        ["VALORANT-Win64-Shipping"] = "Valorant",
        ["cs2"] = "Counter-Strike 2",
        ["r5apex"] = "Apex Legends", ["r5apex_dx12"] = "Apex Legends",
        ["RocketLeague"] = "Rocket League",
        ["RainbowSix"] = "Rainbow Six Siege", ["RainbowSix_Vulkan"] = "Rainbow Six Siege",
        ["Overwatch"] = "Overwatch 2",
        ["cod"] = "Call of Duty", ["Marvel-Win64-Shipping"] = "Marvel Rivals",
        ["Minecraft.Windows"] = "Minecraft",
    };

    static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "Vigia", "msedgewebview2", "msedge", "chrome", "firefox", "opera", "brave", "Discord", "steam", "steamwebhelper",
        "EpicGamesLauncher", "MarvelRivals_Launcher", "RiotClientServices", "Battle.net", "EADesktop", "upc", "EpicWebHelper", "dwm", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "TextInputHost", "ApplicationFrameHost",
        "LockApp", "obs64", "Spotify", "Code", "WindowsTerminal", "powershell", "cmd", "Taskmgr", "mpc-hc64", "vlc",
    };

    static string FilePath => Path.Combine(Settings.Dir, "fps.json");
    static readonly object Gate = new();
    static CancellationTokenSource? _cts;
    static WatchState _state = new(false, "idle", null, 0, 0, null);
    public static WatchState State { get { lock (Gate) return _state; } }
    public static event Action<Result>? Finished;
    public static event Action<WatchState>? Changed;

    static void Set(WatchState s) { lock (Gate) _state = s; Changed?.Invoke(s); }

    // ---------- historial ----------
    public static List<Result> History()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<Result>>(File.ReadAllText(FilePath), Program.Json) ?? [] : []; }
        catch { return []; }
    }

    static void Append(Result r)
    {
        lock (Gate)
        {
            var all = History(); all.Add(r);
            Directory.CreateDirectory(Settings.Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all.TakeLast(200).ToList(), Program.Json));
            File.Move(tmp, FilePath, true);
        }
    }

    public static void Delete(string id)
    {
        lock (Gate)
        {
            var all = History().Where(r => r.Id != id).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, Program.Json));
        }
    }

    // ---------- espera y medición ----------
    /// <summary>Empieza a esperar un juego. Cuando aparece delante, deja pasar unos segundos y mide <paramref name="seconds"/> de juego.</summary>
    public static bool Start(int seconds, string label)
    {
        lock (Gate)
        {
            if (_state.Active) return false;
            _cts = new CancellationTokenSource();
        }
        var ct = _cts.Token;
        Set(new(true, "waiting", null, 0, seconds, null));
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await WatchAndMeasure(seconds, label, ct);
                if (r is not null) { Append(r); Finished?.Invoke(r); Set(new(false, "done", r.Game, seconds, seconds, null)); }
                else Set(new(false, ct.IsCancellationRequested ? "idle" : "error", null, 0, seconds, ct.IsCancellationRequested ? null : "No hubo un juego delante el tiempo suficiente."));
            }
            catch (OperationCanceledException) { Set(new(false, "idle", null, 0, seconds, null)); }
            catch (Exception e) { Set(new(false, "error", null, 0, seconds, e.Message)); }
        }, CancellationToken.None);
        return true;
    }

    public static void Cancel() { lock (Gate) _cts?.Cancel(); }

    static async Task<Result?> WatchAndMeasure(int seconds, string label, CancellationToken ct)
    {
        // esperar con poco gasto: mirar la ventana de delante cada 3 s, como mucho 4 horas
        var until = DateTime.UtcNow.AddHours(4);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < until)
        {
            var g = ForegroundGame();
            if (g is not null)
            {
                // que pase la pantalla de carga y que siga delante
                Set(new(true, "found", g.Value.Name, 0, seconds, null));
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                if (ForegroundGame() is { } g2 && g2.Pid == g.Value.Pid)
                {
                    var r = Measure(g2.Pid, g2.Exe, g2.Name, seconds, label, ct);
                    if (r is not null) return r;
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
        return null;
    }

    /// <summary>
    /// Mide al proceso <paramref name="pid"/>: solo cuentan los segundos en que el juego está delante (si cambias de ventana, se pausa).
    /// Termina al juntar <paramref name="seconds"/> de juego o si el juego se cierra (con al menos 20 s se guarda igual).
    /// </summary>
    public static Result? Measure(int pid, string exe, string game, int seconds, string label, CancellationToken ct, bool requireForeground = true)
    {
        if (!(TraceEventSession.IsElevated() ?? false)) throw new InvalidOperationException("Medir FPS necesita permisos de administrador.");
        var stamps = new List<double>(seconds * 300);   // DXGI (DX10/11/12)
        var stamps9 = new List<double>();                // DX9
        int kernel = 0;                                  // presentaciones vistas por el núcleo, de cualquier API
        var gate = new object();
        bool counting = !requireForeground;
        using var session = new TraceEventSession(SessionName) { StopOnDispose = true };
        session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue);
        session.EnableProvider(D3d9Provider, TraceEventLevel.Informational, ulong.MaxValue);
        try { session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Informational, DxgKrnlPresentKeyword); } catch { }   // solo para el aviso
        session.Source.AllEvents += e =>
        {
            if (e.ProcessID != pid) return;
            int id = (int)e.ID; var g = e.ProviderGuid;
            if (g == DxgiProvider && id == PresentStart) { lock (gate) if (counting) stamps.Add(e.TimeStampRelativeMSec); }
            else if (g == D3d9Provider && id == D3d9PresentStart) { lock (gate) if (counting) stamps9.Add(e.TimeStampRelativeMSec); }
            else if (g == DxgKrnlProvider && KernelPresentIds.Contains(id)) { lock (gate) if (counting) kernel++; }
        };
        var pump = new Thread(() => { try { session.Source.Process(); } catch { } }) { IsBackground = true, Name = "vigia-fps" };
        pump.Start();

        // segundos de juego: se marca cada corte (cambio de ventana) para no medir el hueco
        var cuts = new List<int>(); var cuts9 = new List<int>();
        int done = 0, lastReport = -1;
        var sw = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested && done < seconds)
        {
            Thread.Sleep(1000);
            if (!Alive(pid)) break;
            var front = !requireForeground || ForegroundPid() == pid;
            lock (gate)
            {
                if (front && !counting) { cuts.Add(stamps.Count); cuts9.Add(stamps9.Count); }   // vuelve a contar: el primer intervalo tras el corte no vale
                counting = front;
            }
            if (front) done++;
            if (done != lastReport) { lastReport = done; Set(new(true, "measuring", game, done, seconds, null)); }
            if (sw.Elapsed > TimeSpan.FromSeconds(seconds * 6 + 600)) break;   // demasiado tiempo fuera del juego
        }
        session.Stop();
        pump.Join(3000);
        if (done < Math.Min(20, seconds)) return null;

        double[] ts; int[] cutArr; int k; string source = "present";
        lock (gate)
        {
            // misma metodología con las dos: llamadas Present del juego. DXGI manda si hay de las dos (DX9 sobre DXGI).
            bool dx9 = stamps.Count < 10 && stamps9.Count >= 10;
            ts = (dx9 ? stamps9 : stamps).ToArray(); cutArr = (dx9 ? cuts9 : cuts).ToArray(); k = kernel;
            if (dx9) source = "d3d9";
        }
        var st = Stats(ts, cutArr, done);
        if (st.Frames < 10)
        {
            if (Unsupported(st.Frames, k, done)) throw new ApiNotSupported(ApiNotSupportedMsg);
            return null;
        }
        return new Result($"{DateTime.Now:yyyyMMdd-HHmmss}", DateTime.Now.ToString("s"), game, exe, done, st.Avg, st.Low1, st.Max, st.Frames,
            label, Setup(exe), st.Low01, st.AvgMs, st.P95Ms, st.P99Ms, st.P999Ms, st.Stutters, st.StutterMs, source);
    }

    /// <summary>Sin Present de DXGI ni de DX9 pero el núcleo vio al juego dibujar al menos una vez por segundo: API que no medimos.</summary>
    internal static bool Unsupported(int frames, int kernelPresents, int seconds) => frames < 10 && seconds > 0 && kernelPresents >= seconds;

    /// <summary>Prueba: cuántos Present hace cada proceso durante unos segundos.</summary>
    public static string Probe(int seconds)
    {
        var counts = new Dictionary<int, int>(); var c9 = new Dictionary<int, int>(); var ck = new Dictionary<int, int>();
        using var session = new TraceEventSession(SessionName + "-probe") { StopOnDispose = true };
        session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue);
        session.EnableProvider(D3d9Provider, TraceEventLevel.Informational, ulong.MaxValue);
        try { session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Informational, DxgKrnlPresentKeyword); } catch { }
        session.Source.AllEvents += e =>
        {
            int id = (int)e.ID; var g = e.ProviderGuid;
            var d = g == DxgiProvider && id == PresentStart ? counts : g == D3d9Provider && id == D3d9PresentStart ? c9
                : g == DxgKrnlProvider && KernelPresentIds.Contains(id) ? ck : null;
            if (d is null) return;
            lock (counts) d[e.ProcessID] = d.GetValueOrDefault(e.ProcessID) + 1;
        };
        var pump = new Thread(() => { try { session.Source.Process(); } catch { } }) { IsBackground = true };
        pump.Start();
        Thread.Sleep(seconds * 1000);
        session.Stop(); pump.Join(3000);
        var lines = new List<string>();
        lock (counts)
            foreach (var pid in counts.Keys.Union(c9.Keys).Union(ck.Keys).OrderByDescending(p => ck.GetValueOrDefault(p) + counts.GetValueOrDefault(p)))
            {
                string name; try { using var p = Process.GetProcessById(pid); name = p.ProcessName; } catch { name = "?"; }
                int n = counts.GetValueOrDefault(pid);
                lines.Add($"{pid}\t{name}\t{n} presents\t{n / (double)seconds:F1}/s\tdx9 {c9.GetValueOrDefault(pid)}\tnúcleo {ck.GetValueOrDefault(pid)}");
            }
        return lines.Count == 0 ? "sin presents" : string.Join(Environment.NewLine, lines);
    }

    /// <param name="Low01">0,1 % bajos; null con menos de 1000 intervalos (no hay muestras suficientes).</param>
    /// <param name="P999Ms">Percentil 99,9 del frametime; null con menos de 1000 intervalos.</param>
    /// <param name="Stutters">Tirones: intervalos de al menos el doble de la mediana y 4 ms más largos que ella.</param>
    public record Numbers(int Frames, double Avg, double Low1, double Max,
        double? Low01 = null, double AvgMs = 0, double P95Ms = 0, double P99Ms = 0, double? P999Ms = null, int Stutters = 0, double StutterMs = 0);

    /// <summary>Percentil por rango más cercano sobre una lista ya ordenada de menor a mayor (p entre 0 y 1).</summary>
    internal static double Percentile(IReadOnlyList<double> ascending, double p)
    {
        if (ascending.Count == 0) return 0;
        var rank = (int)Math.Ceiling(p * ascending.Count - 1e-9);   // sin que el redondeo de p*n suba un puesto
        return ascending[Math.Clamp(rank, 1, ascending.Count) - 1];
    }

    /// <summary>FPS medios, 1 % y 0,1 % bajos (el FPS medio del 1 % / 0,1 % de intervalos más lentos), el mejor segundo,
    /// percentiles del frametime y tirones.</summary>
    public static Numbers Stats(double[] ts, int[] cuts, double seconds)
    {
        var cutSet = new HashSet<int>(cuts);
        var ft = new List<double>(ts.Length);
        for (int i = 1; i < ts.Length; i++)
        {
            if (cutSet.Contains(i)) continue;
            var d = ts[i] - ts[i - 1];
            if (d > 0 && d < 1000) ft.Add(d);
        }
        if (ft.Count == 0) return new(0, 0, 0, 0);
        var total = ft.Sum();
        var avg = ft.Count / (total / 1000.0);
        var sorted = ft.OrderByDescending(x => x).ToList();
        var n = Math.Max(1, ft.Count / 100);
        var low1 = 1000.0 / sorted.Take(n).Average();
        double? low01 = ft.Count >= 1000 ? 1000.0 / sorted.Take(ft.Count / 1000).Average() : null;
        var asc = Enumerable.Reverse(sorted).ToList();
        var median = Percentile(asc, 0.5);
        var stutter = ft.Where(d => d >= 2 * median && d >= median + 4).ToList();
        // máximo: el mejor segundo completo
        double max = 0, acc = 0; int cnt = 0;
        foreach (var d in ft) { acc += d; cnt++; if (acc >= 1000) { max = Math.Max(max, cnt / (acc / 1000.0)); acc = 0; cnt = 0; } }
        if (max == 0) max = avg;
        return new(ft.Count + 1, Math.Round(avg, 1), Math.Round(low1, 1), Math.Round(max, 1),
            low01 is { } l ? Math.Round(l, 1) : null, Math.Round(total / ft.Count, 2),
            Math.Round(Percentile(asc, 0.95), 2), Math.Round(Percentile(asc, 0.99), 2),
            ft.Count >= 1000 ? Math.Round(Percentile(asc, 0.999), 2) : null, stutter.Count, Math.Round(stutter.Sum(), 1));
    }

    /// <summary>Con qué se midió: cuántos ajustes de Vigía estaban puestos, resolución y el modo del juego si se sabe.</summary>
    static Dictionary<string, string> Setup(string exe)
    {
        var d = new Dictionary<string, string>();
        try { d["ajustes"] = Tweaks.List().Count(t => t.State == "applied").ToString(); } catch { }
        try { if (Display.Status().Current is { } c) d["pantalla"] = $"{c.Width}×{c.Height} {c.Hz} Hz"; } catch { }
        try { if (exe.StartsWith("FortniteClient", StringComparison.OrdinalIgnoreCase) && Games.Fortnite() is { Found: true } f) { d["modo"] = f.Mode; d["perfil"] = f.ProfileApplied ? "sí" : "no"; } } catch { }
        return d;
    }

    // ---------- ventana de delante ----------
    public record struct Fg(int Pid, string Exe, string Name);

    public static Fg? ForegroundGame()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        GetWindowThreadProcessId(h, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return null;
        string exe;
        try { using var p = Process.GetProcessById((int)pid); exe = p.ProcessName; } catch { return null; }
        if (Known.TryGetValue(exe, out var name)) return new((int)pid, exe, name);
        if (NotGames.Contains(exe)) return null;
        return IsFullscreen(h) ? new((int)pid, exe, exe) : null;
    }

    static int ForegroundPid()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(h, out var pid);
        return (int)pid;
    }

    static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch { return false; }
    }

    static bool IsFullscreen(IntPtr h)
    {
        if (!GetWindowRect(h, out var r)) return false;
        var mon = MonitorFromWindow(h, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);

    // ---------- pruebas ----------
    public static string SelfTest()
    {
        var o = new List<string>();
        void Check(bool ok, string what) => o.Add($"{(ok ? "OK  " : "FAIL")} fps: {what}");
        // 1000 fotogramas a 4 ms exactos -> 250 FPS de media y de 1 % bajos
        var ts = Enumerable.Range(0, 1001).Select(i => i * 4.0).ToArray();
        var a = Stats(ts, [], 4);
        Check(Math.Abs(a.Avg - 250) < 0.5 && Math.Abs(a.Low1 - 250) < 0.5, $"ritmo fijo 4 ms -> media {a.Avg}, 1% {a.Low1}");
        // mismo ritmo con 10 tirones de 20 ms -> la media casi igual, el 1 % bajo cae a 50
        var t2 = new List<double> { 0 };
        for (int i = 1; i <= 1000; i++) t2.Add(t2[^1] + (i % 100 == 0 ? 20 : 4));
        var b = Stats(t2.ToArray(), [], 4);
        Check(b.Avg is > 230 and < 250 && Math.Abs(b.Low1 - 50) < 0.5, $"tirones -> media {b.Avg}, 1% {b.Low1}");
        // un corte por cambio de ventana: el hueco de 5 s no cuenta
        var t3 = Enumerable.Range(0, 500).Select(i => i * 4.0).Concat(Enumerable.Range(0, 500).Select(i => 7000 + i * 4.0)).ToArray();
        var c = Stats(t3, [500], 4);
        Check(Math.Abs(c.Avg - 250) < 0.5, $"corte ignorado -> media {c.Avg}");
        // percentiles y tirones con los mismos 10 tirones de 20 ms entre 990 de 4 ms
        Check(b.Stutters == 10 && Math.Abs(b.StutterMs - 200) < 0.01, $"tirones contados -> {b.Stutters} ({b.StutterMs} ms)");
        Check(b.P95Ms == 4 && b.P99Ms == 4 && b.P999Ms == 20, $"percentiles -> P95 {b.P95Ms}, P99 {b.P99Ms}, P99.9 {b.P999Ms}");
        Check(b.Low01 is { } l01 && Math.Abs(l01 - 50) < 0.5, $"0,1 % bajos -> {b.Low01}");
        Check(Math.Abs(b.AvgMs - 4.16) < 0.01, $"frametime medio -> {b.AvgMs} ms");
        // pocas muestras: 0,1 % y P99.9 no se inventan
        var few = Stats(Enumerable.Range(0, 200).Select(i => i * 5.0).ToArray(), [], 1);
        Check(few.Low01 is null && few.P999Ms is null && few.Stutters == 0, "con 199 intervalos: sin 0,1 % ni P99.9");
        // percentil por rango más cercano
        var asc = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Check(Percentile(asc, 0.5) == 5 && Percentile(asc, 0.95) == 10 && Percentile(asc, 0.1) == 1 && Percentile([], 0.5) == 0, "percentil por rango");
        Check(Unsupported(0, 3600, 60) && !Unsupported(0, 20, 60) && !Unsupported(5000, 6000, 60), "API sin medir: el núcleo la ve dibujar y no hay Present -> aviso, no 0 FPS");
        Check(!Known.ContainsKey("MarvelRivals_Launcher") && Known["Marvel-Win64-Shipping"] == "Marvel Rivals" && NotGames.Contains("MarvelRivals_Launcher"), "Marvel Rivals: cuenta el juego, no el lanzador");
        return string.Join(Environment.NewLine, o);
    }
}
