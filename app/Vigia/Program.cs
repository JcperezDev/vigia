using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Vigia;

static class Program
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [STAThread]
    static int Main(string[] args)
    {
        // Modo sin ventana para verificación: Vigia.exe --probe salida.json
        if (args.Length >= 2 && args[0] == "--probe")
        {
            using var t = new Telemetry();
            t.Read(); Thread.Sleep(1200);
            var snap = t.Read();
            File.WriteAllText(args[1], JsonSerializer.Serialize(new
            {
                snapshot = snap, clean = Cleanup.Scan(), health = Health.Scan(t), diag = Diagnostics.Run(),
                optimize = Optimize.Status(analyzeHdd: true), memory = Memory.Status(), tweaks = Tweaks.List(),
                debloat = Debloat.List(), tools = Toolbox.List(Settings.Load().ToolsRoot), restorePoints = RestorePoint.Count(),
                sensors = t.Dump().ToList(),
            }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--startup")   // solo lectura: lo que arranca con Windows
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { startup = Startup.List(), display = Display.Status() }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--original")   // crea (si falta) la copia del estado original y lista las copias
        {
            var ok = Backups.EnsureOriginal(withRestorePoint: args.Contains("--rp"));
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { ok, list = Backups.List() }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return ok ? 0 : 1;
        }
        if (args.Length >= 2 && args[0] == "--backups")   // solo lectura: lista de copias
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(Backups.List(), new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--backup-restore")   // prueba: Vigia --backup-restore <id>
        {
            var r = Backups.Restore(args[1]);
            Console.WriteLine($"{r.Ok} {r.Message} {string.Join("; ", r.Errors)}");
            return r.Ok ? 0 : 1;
        }
        if (args.Length >= 3 && args[0] == "--startup-set")   // prueba: Vigia --startup-set <id> on|off
        {
            var r = Startup.Set(args[1], args[2] == "on");
            Console.WriteLine($"{r.Ok} {r.Message}");
            return r.Ok ? 0 : 1;
        }
        if (args.Length >= 3 && args[0] == "--display-test")   // prueba sin cambiar nada: ¿acepta Windows ese modo estirado?
        {
            var ok = Display.Test(int.Parse(args[1]), int.Parse(args[2]));
            File.WriteAllText(args[3], ok);
            return ok.StartsWith("OK") ? 0 : 1;
        }
        if (args.Length >= 2 && args[0] == "--selftest")
        {
            var report = Cleanup.SelfTest() + Environment.NewLine + "--- ajustes" + Environment.NewLine + Tweaks.SelfTest();
            File.WriteAllText(args[1], report);
            return report.Contains("FAIL") ? 1 : 0;
        }
        // Prueba de la descarga de Puente: Vigia.exe --puente-get <carpeta> -> baja Puente ahí y comprueba la firma
        if (args.Length >= 2 && args[0] == "--puente-get")
        {
            var err = Updates.EnsurePuenteAsync(Path.Combine(args[1], "Puente.exe")).GetAwaiter().GetResult();
            Console.WriteLine(err ?? "ok");
            return err is null ? 0 : 1;
        }
        // Menú de la bandeja en cada idioma, sin mostrar nada: Vigia.exe --tray-dump salida.txt
        if (args.Length >= 2 && args[0] == "--tray-dump")
        {
            ApplicationConfiguration.Initialize();
            using var f = new MainForm();
            File.WriteAllText(args[1], MainForm.Dump(f));
            return 0;
        }
        // Una sola ventana: si ya está abierta, no abrir otra
        using var mutex = new Mutex(true, @"Local\Vigia.SingleInstance", out var first);
        if (!first)
        {
            // ya está abierta (quizá escondida en la bandeja): pedirle que se muestre
            try { using var ev = EventWaitHandle.OpenExisting(MainForm.ShowEventName); ev.Set(); } catch { }
            return 0;
        }
        ApplicationConfiguration.Initialize();
        // Verificación visual: Vigia.exe --snap salida.png [storage] -> captura la UI real tras ~8 s y sale
        // Consumo real: Vigia.exe --bench salida.json -> 30 s abierta, 60 s minimizada, y sale
        Application.Run(args.Length >= 2 && args[0] == "--snap" ? new MainForm(args[1], args.ElementAtOrDefault(2))
            : args.Length >= 2 && args[0] == "--bench" ? new MainForm(null, null, args[1]) : new MainForm());
        return 0;
    }
}

sealed partial class MainForm : Form
{
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(14, 10, 24) };
    readonly Telemetry _tel = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    bool _busy;
    int _tick;
    Settings _cfg = Settings.Load();
    readonly string? _snapPath, _snapView, _benchPath;
    bool _resting;   // minimizada: sin sensores ni dibujo, solo el vistazo barato a la RAM para la liberación automática

    public MainForm(string? snapPath = null, string? snapView = null, string? benchPath = null) : this()
    {
        _snapPath = snapPath; _snapView = snapView; _benchPath = benchPath;
    }

    public MainForm()
    {
        Text = "Vigía";
        Width = 1360; Height = 860; MinimumSize = new Size(1024, 680);
        BackColor = Color.FromArgb(14, 10, 24);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "vigia.ico")); } catch { }
        Controls.Add(_web);
        Load += async (_, _) => { if (_snapPath is null) InitTray(); /* --bench mide con el icono puesto */ await InitAsync(); };
        FormClosed += (_, _) => { _timer.Stop(); Optimize.Cancel(); _tel.Dispose(); };
        Resize += async (_, _) => await SetRestingAsync(WindowState == FormWindowState.Minimized);
        Activated += (_, _) => { if (!_resting) _timer.Interval = 1000; };
        Deactivate += (_, _) => { if (!_resting) _timer.Interval = 2000; };   // detrás de otra ventana: la mitad de lecturas
    }

    async Task InitAsync()
    {
        // --snap usa su propio perfil: si Vigía ya está abierta, compartir la carpeta bloquea WebView2
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia", _snapPath is null ? "WebView2" : "WebView2-snap");
        var env = await CoreWebView2Environment.CreateAsync(null, data);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        var ui = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        core.SetVirtualHostNameToFolderMapping("app.vigia", ui, CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        // Solo se navega a la UI local
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://app.vigia/")) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += OnMessage;
        core.Navigate("https://app.vigia/index.html");
        _timer.Tick += async (_, _) => await PushAsync();
        _timer.Start();
        if (_snapPath is not null) _ = SnapAsync();
        else if (_benchPath is not null) _ = BenchAsync();
        // primera vez: foto de cómo estaba el sistema antes de que Vigía toque nada (una sola vez, en segundo plano)
        if (_snapPath is null && _benchPath is null) _ = Task.Run(() => Backups.EnsureOriginal(withRestorePoint: true));
        else if (_cfg.UpdateCheck) _ = UpdateLoopAsync();
    }

    async Task SnapAsync()
    {
        await Task.Delay(5000);
        if (_snapView is { Length: > 0 } v)
        {
            await _web.CoreWebView2.ExecuteScriptAsync($"window.vigiaSnap && vigiaSnap({JsonSerializer.Serialize(v)})");
            // VIGIA_SNAP_WAIT=segundos: esperar más (p. ej. para que la gráfica se llene en capturas de la web)
            var wait = int.TryParse(Environment.GetEnvironmentVariable("VIGIA_SNAP_WAIT"), out var w) && w > 0 ? w * 1000 : 12000;
            await Task.Delay(wait);
        }
        else await Task.Delay(3000);
        var metrics = await _web.CoreWebView2.ExecuteScriptAsync(
            "JSON.stringify({vw:innerWidth,vh:innerHeight,dpr:devicePixelRatio,main:document.querySelector('main').clientHeight,scroll:document.querySelector('main').scrollHeight,text:(document.querySelector('.view:not([hidden])')||document.body).innerText.slice(0,20000)," +
            // elementos que se salen de su caja o del área principal: así se revisa el diseño sin mirar la imagen
            "bad:[...document.querySelectorAll('.view:not([hidden]) *')].filter(e=>{const r=e.getBoundingClientRect(),m=document.querySelector('main').getBoundingClientRect(),cs=getComputedStyle(e);if(!r.width)return false;return r.right>m.right+1||r.left<m.left-1||(e.scrollWidth>e.clientWidth+2&&cs.textOverflow!=='ellipsis'&&cs.overflowX!=='auto')}).slice(0,15).map(e=>e.tagName+'.'+e.className+' '+(e.innerText||'').slice(0,40)+' w='+Math.round(e.getBoundingClientRect().width))})");
        File.WriteAllText(_snapPath + ".json", $"{{\"form\":\"{ClientSize.Width}x{ClientSize.Height}\",\"web\":\"{_web.Width}x{_web.Height}\",\"page\":{System.Text.Json.JsonSerializer.Deserialize<string>(metrics)}}}");
        using (var fs = File.Create(_snapPath!))
            await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
        _quitting = true; Close();
    }

    /// <summary>
    /// Minimizada, Vigía no lee sensores ni dibuja: WebView2 se suspende (sus procesos quedan congelados y Windows puede
    /// sacar su memoria) y el reloj pasa de 1 s a 10 s solo para la liberación automática de RAM, que es una llamada barata.
    /// Al volver, todo arranca otra vez y se piden datos al momento.
    /// </summary>
    async Task SetRestingAsync(bool rest)
    {
        if (rest == _resting || _web.CoreWebView2 is null) return;
        if (Environment.GetEnvironmentVariable("VIGIA_NOREST") == "1") return;   // para medir el antes en --bench
        _resting = rest;
        var core = _web.CoreWebView2;
        if (rest)
        {
            _timer.Interval = 10_000;
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            _web.Visible = false;   // sin esto WebView2 no se deja suspender
            try { await core.TrySuspendAsync(); } catch { /* si no se puede, al menos ya no dibuja */ }
            // devolver a Windows la memoria que Vigía ya no usa
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
        }
        else
        {
            _web.Visible = true;
            try { core.Resume(); } catch { }
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            _timer.Interval = ContainsFocus ? 1000 : 2000;
            await PushAsync();
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr proc, nint min, nint max);

    async Task BenchAsync()
    {
        await Task.Delay(10_000);                                   // que termine de arrancar
        var a = await Task.Run(Bench.Take);
        await Task.Delay(30_000);
        var b = await Task.Run(Bench.Take);
        WindowState = FormWindowState.Minimized;
        await Task.Delay(8_000);                                    // que se asiente la suspensión
        var c = await Task.Run(Bench.Take);
        await Task.Delay(60_000);
        var d = await Task.Run(Bench.Take);
        // al volver: tiene que leer otra vez en menos de 3 s
        WindowState = FormWindowState.Normal;
        await Task.Delay(1500);
        var t1 = await _web.CoreWebView2.ExecuteScriptAsync("window.vigiaLastTs||0");
        await Task.Delay(3000);
        var t2 = await _web.CoreWebView2.ExecuteScriptAsync("window.vigiaLastTs||0");
        var vuelve = double.TryParse(t1, out var x1) && double.TryParse(t2, out var x2) && x2 > x1;
        File.WriteAllText(_benchPath!, JsonSerializer.Serialize(new
        {
            version = Updates.Current,
            abierta = new { cpuNucleoPct = Bench.CorePct(a, b), privadaMb = b.PrivateMb, ramMb = b.WorkingMb, procesos = b.Procs },
            minimizada = new { cpuNucleoPct = Bench.CorePct(c, d), privadaMb = d.PrivateMb, ramMb = d.WorkingMb, procesos = d.Procs },
            alVolverLee = vuelve,
        }, new JsonSerializerOptions { WriteIndented = true }));
        _quitting = true; Close();
    }

    async Task UpdateLoopAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(20));
        while (!IsDisposed)
        {
            var u = await Updates.CheckAsync();
            if (u.Available && !IsDisposed) Send("update", u);
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    async Task PushAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_resting)   // minimizada: solo la liberación automática
            {
                var c = _cfg;
                if (c.MemAuto) await Task.Run(() => Memory.AutoTick(c));
                return;
            }
            Send("telemetry", await Task.Run(_tel.Read));
            // cada 2 lecturas: estado de la memoria y, si toca, liberación automática
            if (_tick++ % 2 == 0)
            {
                var cfg = _cfg;
                var auto = await Task.Run(() => Memory.AutoTick(cfg));
                if (auto is not null) Send("memFreed", auto);
                Send("memStatus", await Task.Run(Memory.Status));
            }
        }
        catch (Exception e) { Send("error", e.Message); }
        finally { _busy = false; }
    }

    void Send(string type, object payload) =>
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, payload }, Program.Json));

    async void OnMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = JsonNode.Parse(e.WebMessageAsJson)!;
            switch ((string?)msg["cmd"])
            {
                case "cleanScan":
                    Send("cleanScan", await Task.Run(Cleanup.Scan));
                    break;
                case "cleanRun":
                    var ids = msg["ids"]!.AsArray().Select(x => (string)x!).ToList();
                    Send("cleanRun", await Task.Run(() => Cleanup.Run(ids)));
                    break;
                case "healthScan":
                    Send("healthScan", await Task.Run(() => Health.Scan(_tel)));
                    break;
                case "diagRun":
                    Send("diagRun", await Task.Run(() => Diagnostics.Run(7)));
                    break;
                case "optStatus":
                    var analyze = (bool?)msg["analyze"] ?? false;
                    Send("optStatus", await Task.Run(() => Optimize.Status(analyze)));
                    break;
                case "optRun":
                    var letter = (string)msg["letter"]!;
                    // el progreso llega desde otro hilo: volver al hilo de la UI para hablar con WebView2
                    var res = await Task.Run(() => Optimize.RunAsync(letter, p => BeginInvoke(() => Send("optProgress", p))));
                    Send("optDone", res);
                    Send("optStatus", await Task.Run(() => Optimize.Status(false)));
                    break;
                case "optCancel":
                    Optimize.Cancel();
                    break;
                case "memFree":
                    var o = msg["opts"].Deserialize<MemOptions>(Program.Json) ?? new MemOptions();
                    Send("memFreed", await Task.Run(() => Memory.Free(o)));
                    break;
                case "settingsGet":
                    Send("settings", new { cfg = _cfg, lab = Tweaks.Lab });
                    break;
                case "settingsSet":
                    var next = msg["cfg"].Deserialize<Settings>(Program.Json);
                    if (next is not null)
                    {
                        next.MemAutoThreshold = Math.Clamp(next.MemAutoThreshold, 50, 98);
                        next.MemAutoIntervalMin = Math.Clamp(next.MemAutoIntervalMin, 1, 120);
                        _cfg = next; _cfg.Save();
                    }
                    Send("settings", new { cfg = _cfg, lab = Tweaks.Lab });
                    break;
                case "tweaksList":
                    Send("tweaks", new { items = await Task.Run(Tweaks.List), restorePoints = await Task.Run(RestorePoint.Count) });
                    break;
                case "tweaksApply":
                case "tweaksRevert":
                    var tids = msg["ids"]!.AsArray().Select(x => (string)x!).ToList();
                    var batch = (string?)msg["cmd"] == "tweaksApply" ? await Task.Run(() => Tweaks.Apply(tids)) : await Task.Run(() => Tweaks.Revert(tids));
                    Send("tweaksDone", batch);
                    Send("tweaks", new { items = await Task.Run(Tweaks.List), restorePoints = await Task.Run(RestorePoint.Count) });
                    break;
                case "bloatList":
                    Send("bloat", await Task.Run(Debloat.List));
                    break;
                case "bloatRemove":
                    var bids = msg["ids"]!.AsArray().Select(x => (string)x!).ToList();
                    await Task.Run(() => RestorePoint.Create("Vigía: antes de quitar apps"));
                    Send("bloatDone", await Task.Run(() => Debloat.Remove(bids)));
                    Send("bloat", await Task.Run(Debloat.List));
                    break;
                case "toolsList":
                    Send("tools", new { root = _cfg.ToolsRoot, items = Toolbox.List(_cfg.ToolsRoot) });
                    break;
                case "toolOpen":
                    var tid = (string)msg["id"]!;
                    Send("toolDone", tid == "restorepoint" ? await Task.Run(() => Toolbox.Open(_cfg.ToolsRoot, tid)) : Toolbox.Open(_cfg.ToolsRoot, tid));
                    break;
                case "startupList":
                    Send("startup", await Task.Run(Startup.List));
                    break;
                case "startupSet":
                    var sid = (string)msg["id"]!; var son = (bool)msg["on"]!;
                    Send("startupDone", await Task.Run(() => Startup.Set(sid, son)));
                    Send("startup", await Task.Run(Startup.List));
                    break;
                case "backupsList":
                    Send("backups", await Task.Run(Backups.List));
                    break;
                case "backupRestore":
                    var bid = (string)msg["id"]!;
                    Send("backupDone", await Task.Run(() => Backups.Restore(bid)));
                    Send("backups", await Task.Run(Backups.List));
                    break;
                case "displayStatus":
                    Send("display", Display.Status());
                    break;
                case "displayStretch":
                    var dw = (int)msg["w"]!; var dh = (int)msg["h"]!;
                    Send("displayDone", Display.Stretch(dw, dh));
                    Send("display", Display.Status());
                    break;
                case "displayRestore":
                    Send("displayDone", Display.Restore());
                    Send("display", Display.Status());
                    break;
                case "version":
                    Send("update", new UpdateInfo(Updates.Current.ToString(), null, false, null, File.Exists(Updates.PuentePath), null));
                    break;
                case "updateCheck":
                    Send("update", await Updates.CheckAsync());
                    break;
                case "updateRun":
                    if (await Updates.EnsurePuenteAsync() is string perr) { Send("update", (await Updates.CheckAsync()) with { Error = perr }); break; }
                    if (Updates.StartPuente()) { _quitting = true; Close(); }   // salir de verdad, no a la bandeja
                    else Send("update", (await Updates.CheckAsync()) with { Error = "No pude abrir Puente." });
                    break;
                case "utilidad-externa":
                    var rp = await Task.Run(() => RestorePoint.Create("Vigía: antes de utilidad externa"));
                    Send("utilidad-externaRp", rp);
                    Toolbox.Openutilidad externa();
                    break;
            }
        }
        catch (Exception ex) { Send("error", ex.Message); }
    }
}
