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
        // Ayudante con permisos (lo abre la propia Vigía con UAC). Lista cerrada de operaciones; ver Elevation.cs.
        if (args.Length >= 1 && args[0] == "--broker") return Elevation.RunBroker(args);
        // Lo usa Puente antes de activar una versión nueva: ¿están sus piezas y se pueden leer? Solo lee; no abre ventanas.
        if (args.Length >= 1 && args[0] == "--install-check") return Updates.InstallCheck();
        if (args.Length >= 2 && args[0] == "--probe")
        {
            using var t = new Telemetry();
            t.Read(); Thread.Sleep(1200);
            var snap = t.Read();
            File.WriteAllText(args[1], JsonSerializer.Serialize(new
            {
                snapshot = snap, clean = Cleanup.Scan(), health = Health.Scan(t), diag = Diagnostics.Run(),
                optimize = Optimize.Status(analyzeHdd: true), memory = Memory.Status(), tweaks = Tweaks.List(),
                debloat = Debloat.List(), tools = Toolbox.List(), restorePoints = RestorePoint.Count(),
                sensors = t.Dump().ToList(),
            }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--apps-scan")   // solo lectura: inventario de apps clasificado (no quita nada)
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(Debloat.View(force: true), new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--apps-plan")   // solo lectura: Vigia --apps-plan <claves separadas por |> <salida.json> [safe|custom|advanced]
        {
            var mode = args.Length >= 4 ? args[3] : "safe";
            var plan = Debloat.Preview(new PlanRequest(mode, args[1].Split('|', StringSplitOptions.RemoveEmptyEntries).Select(k => (k, false)).ToList()));
            File.WriteAllText(args[2], JsonSerializer.Serialize(plan, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--suggest")   // solo lectura: sugerencias con lo medido ahora
        {
            using var t = new Telemetry();
            t.Read(); Thread.Sleep(1200);
            File.WriteAllText(args[1], JsonSerializer.Serialize(Suggestions.Run(t), new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--startup")   // solo lectura: lo que arranca con Windows
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { startup = Startup.List(), display = Display.Status() }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        // «Apagar todos los programas» por consola, solo en laboratorio (VIGIA_LAB=1): Vigia.exe --startup-offall <salida.json>
        if (args.Length >= 2 && args[0] == "--startup-offall" && Tweaks.Lab)
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { done = Startup.OffAll(), after = Startup.List() }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        // «Abrir Vigía al iniciar Windows» por consola, solo en laboratorio: Vigia.exe --autostart <on|off|review> <salida.json>
        if (args.Length >= 3 && args[0] == "--autostart" && Tweaks.Lab)
        {
            object res = args[1] switch
            {
                "on" or "off" => AutoStart.Set(args[1] == "on") is var (ok, m) ? new { ok, message = m, enabled = AutoStart.Enabled() } : null!,
                _ => AutoStart.Review(),
            };
            File.WriteAllText(args[2], JsonSerializer.Serialize(res, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        // Fortnite por consola (laboratorio y soporte): Vigia.exe --fortnite <read|unlock|profile|perf|dx12> <salida.json>
        if (args.Length >= 3 && args[0] == "--fortnite")
        {
            object res = args[1] switch
            {
                "read" => Games.Fortnite(),
                "unlock" => Games.ApplyFortnite(false, null),
                "profile" => Games.ApplyFortnite(true, null),
                "perf" or "dx12" => Games.ApplyFortnite(false, args[1]),
                _ => new Games.Done(false, "uso: read|unlock|profile|perf|dx12"),
            };
            File.WriteAllText(args[2], JsonSerializer.Serialize(res, Json));
            return res is Games.Done { Ok: false } ? 1 : 0;
        }
        // Prueba de la medición: Vigia.exe --fps-probe <segundos> <salida.txt> [pid]
        // sin pid: cuenta los Present de todos los procesos (qué dibuja ahora mismo en el PC); con pid: estadística completa
        if (args.Length >= 3 && args[0] == "--fps-probe")
        {
            var secs = int.Parse(args[1]);
            File.WriteAllText(args[2], args.Length >= 4
                ? JsonSerializer.Serialize(Fps.Measure(int.Parse(args[3]), "probe", "probe", secs, "", CancellationToken.None, requireForeground: false), Json)
                : Fps.Probe(secs));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--restore-point")   // laboratorio: crea un punto (y activa la protección si se pide)
        {
            var lines = new List<string> { $"off antes: {RestorePoint.ProtectionOff()}", $"puntos antes: {RestorePoint.Count()}" };
            if (args.Contains("--enable")) { var en = RestorePoint.EnableC(); lines.Add($"enable: {en.Ok} {en.Message}"); }
            var r = RestorePoint.Create("Vigía: prueba de laboratorio");
            lines.Add($"create: ok={r.Ok} off={r.Off} {r.Message}");
            lines.Add($"off después: {RestorePoint.ProtectionOff()}"); lines.Add($"puntos después: {RestorePoint.Count()}");
            File.WriteAllLines(args[1], lines);
            return r.Ok ? 0 : 1;
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
        // Deshace ya los ajustes retirados que Vigía puso antes: Vigia.exe --undo-retired salida.json
        if (args.Length >= 2 && args[0] == "--undo-retired")
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize((object?)Tweaks.UndoRetired() ?? new { nothing = true }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--simple-plan")   // solo lectura: qué haría el modo simple con las respuestas por defecto
        {
            var ans = args.Length >= 3 ? JsonSerializer.Deserialize<Simple.Answers>(File.ReadAllText(args[2]), Json)! : new Simple.Answers();
            File.WriteAllText(args[1], JsonSerializer.Serialize(Simple.Build(ans), new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--msdl-probe" && Environment.GetEnvironmentVariable("VIGIA_LAB") == "1")
        {
            ApplicationConfiguration.Initialize();
            return IsoDownloadProbe.Run(args[1]);
        }
        if (args.Length >= 2 && args[0] == "--simple-run" && Environment.GetEnvironmentVariable("VIGIA_LAB") == "1")   // solo en la VM de pruebas
        {
            var tr = args[1] + ".trace.txt";
            void T(string m) { try { File.AppendAllText(tr, $"{DateTime.Now:HH:mm:ss.fff} {m}\r\n"); } catch { } }
            T("inicio");
            T("tweaks " + Tweaks.SnapshotAll().Count());
            T("startup " + Startup.SnapshotAll().Count());
            T("display " + Display.Status().Current?.Hz);
            T("debloat " + Debloat.List().Count);
            var ans = args.Length >= 3 ? JsonSerializer.Deserialize<Simple.Answers>(File.ReadAllText(args[2]), Json)! : new Simple.Answers();
            T("respuestas leídas");
            var steps = new List<Simple.Step>();
            var plan = Simple.Build(ans); T("plan " + plan.Stages.Count);
            var rep = Simple.Run(plan, st => { lock (steps) steps.Add(st); T($"etapa {st.Index} {st.Id} {st.State} {st.Detail}"); }, _ => { });
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { rep, steps }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return rep.Ok ? 0 : 1;
        }
        if (args.Length >= 2 && args[0] == "--drift")   // solo lectura: Vigia --drift <salida.json> [segundos]
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(Drift.Run(args.Length >= 3 ? int.Parse(args[2]) : 10), new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--optimize")   // prueba: Vigia --optimize <D:> <salida.json> [analyze|cancel]
        {
            var mode = args.Length >= 4 ? args[3] : "";
            var lines = new List<string>();
            if (mode == "recancel") { var t0 = Optimize.RunAsync(args[1], _ => { }); Thread.Sleep(1500); Optimize.Cancel(); t0.GetAwaiter().GetResult(); }
            var task = Optimize.RunAsync(args[1], p => { lock (lines) lines.Add(p.Line); });
            int? frag = null;
            if (mode == "analyze") { Thread.Sleep(1500); frag = Optimize.Status(true).Volumes.First(v => v.Letter == args[1]).Fragmented; }
            if (mode == "cancel") { Thread.Sleep(1500); Optimize.Cancel(); }
            var r = task.GetAwaiter().GetResult();
            var after = Optimize.Status(r.Ok, args[1]).Volumes.First(v => v.Letter == args[1]);
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { r, frag, busyAfter = Optimize.Busy, after, lines = lines.Where(l => !l.Contains('%')).TakeLast(15).ToList() }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--rates")   // solo lectura: Vigia --rates <salida.json> [segundos]
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(Drift.Rates(args.Length >= 3 ? int.Parse(args[2]) : 5), new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--crashes")   // solo lectura: Vigia --crashes <salida.json> [días]
        {
            var days = args.Length >= 3 ? int.Parse(args[2]) : 7;
            File.WriteAllText(args[1], JsonSerializer.Serialize(Diagnostics.Crashes(days), new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--procs")   // solo lectura: Vigia --procs <salida.json> [segundos]
        {
            Procs.Reset(); Procs.Sample();
            var secs = args.Length >= 3 ? int.Parse(args[2]) : 3;
            Procs.View? v = null;
            for (int i = 0; i < secs; i++) { Thread.Sleep(1000); v = Procs.Sample(); }
            File.WriteAllText(args[1], JsonSerializer.Serialize(v, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
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
        // Pruebas de laboratorio (VM): Vigia --lab <apply|revert|debloat|list> <ids|all> <salida.json>
        if (args.Length >= 4 && args[0] == "--lab")
        {
            object res;
            IEnumerable<string> Ids(IEnumerable<string> all) => args[2] == "all" ? all : args[2].Split(',');
            switch (args[1])
            {
                case "apply": res = Tweaks.Apply(Ids(Tweaks.List().Select(t => t.Id)).ToList(), restorePoint: false); break;
                case "revert": res = Tweaks.Revert(Ids(Tweaks.List().Select(t => t.Id)).ToList()); break;
                case "debloat": res = Debloat.Remove(Ids(Debloat.List().Where(a => a.Installed).Select(a => a.Id)).ToList()); break;
                default: res = new { tweaks = Tweaks.List(), apps = Debloat.List(), lab = Tweaks.Lab }; break;
            }
            File.WriteAllText(args[3], JsonSerializer.Serialize(res, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        // Laboratorio del antivirus: Vigia.exe --sec <status|enable|scan> <salida.json>
        // ISO limpia desde la línea de órdenes (laboratorio): --iso inspect <iso> <salida.json> | --iso build <opciones.json> <salida.json>
        // USB (laboratorio): --usb list <salida.json> | --usb write <iso> <disco> <serie> <salida.json>  (write BORRA la memoria)
        if (args.Length >= 3 && args[0] == "--usb")
        {
            var jo = new JsonSerializerOptions(Json) { WriteIndented = true };
            if (args[1] == "list") { File.WriteAllText(args[2], JsonSerializer.Serialize(IsoBuild.UsbList(), jo)); return 0; }
            if (args[1] == "write" && args.Length >= 6)
            {
                var plog = args[5] + ".log"; string last = "";
                var res = IsoBuild.ToUsb(args[2], int.Parse(args[3]), args[4], pr =>
                {
                    var line = $"{pr.Num}/{pr.Of} {pr.Step} {(pr.Pct is { } v ? $"{v:0}%" : "")}";
                    if (line != last) { last = line; File.AppendAllText(plog, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); }
                });
                File.WriteAllText(args[5], JsonSerializer.Serialize(res, jo));
                return res.Ok ? 0 : 1;
            }
            return 2;
        }
        if (args.Length >= 4 && args[0] == "--iso")
        {
            var jo = new JsonSerializerOptions(Json) { WriteIndented = true };
            if (args[1] == "inspect") { File.WriteAllText(args[3], JsonSerializer.Serialize(IsoBuild.Inspect(args[2]), jo)); return 0; }
            var opt = JsonSerializer.Deserialize<IsoBuild.Options>(File.ReadAllText(args[2]), Json)!;
            if (Environment.GetEnvironmentVariable("VIGIA_ISO_LAB_XML") is { Length: > 0 } lab) IsoBuild.LabUnattend = File.ReadAllText(lab);
            var plog = args[3] + ".log"; string last = "";
            var res = IsoBuild.Build(opt, pr =>
            {
                var line = $"{pr.Num}/{pr.Of} {pr.Step} {(pr.Pct is { } v ? $"{v:0}%" : "")}";
                if (line != last) { last = line; File.AppendAllText(plog, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); }
            });
            File.WriteAllText(args[3], JsonSerializer.Serialize(res, jo));
            return res.Ok ? 0 : 1;
        }
        // Vigia --wallpaper <status|set|restore> <salida.json>  (set/restore solo en laboratorio: cambian el fondo)
        if (args.Length >= 3 && args[0] == "--wallpaper")
        {
            object wres = args[1] switch
            {
                "set" when Environment.GetEnvironmentVariable("VIGIA_LAB") == "1" => Wallpaper.Apply(),
                "restore" when Environment.GetEnvironmentVariable("VIGIA_LAB") == "1" && Wallpaper.Status().BackupId is { } wb => Backups.Restore(wb),
                _ => Wallpaper.Status(),
            };
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { result = wres, after = Wallpaper.Status() }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--bios") { File.WriteAllText(args[1], JsonSerializer.Serialize(Bios.Get(), new JsonSerializerOptions(Json) { WriteIndented = true })); return 0; }
        if (args.Length >= 3 && args[0] == "--sec")
        {
            var log = new List<string>();
            object res = args[1] switch
            {
                "enable" => Security.Enable(log.Add),
                "scan" => Security.UpdateAndScan(log.Add),
                _ => Security.Status(),
            };
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { result = res, progress = log, after = Security.Status() },
                new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        // Lectura de «Tu PC para jugar» y del antivirus, sin cambiar nada: Vigia.exe --rig <salida.json>
        if (args.Length >= 2 && args[0] == "--rig")
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { screens = Rig.Screens(), checks = Rig.Checks(), security = Security.Status() },
                new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--selftest")
        {
            // Las pruebas nunca usan la clave real de %ProgramData%\Vigia\claves (ni la crean): si el PC tiene UAC apagado
            // el selftest corre como administrador y, sin esto, dejaría una clave de verdad en el sistema.
            var testKeys = Path.Combine(Path.GetTempPath(), "vigia-selftest-claves-" + Guid.NewGuid().ToString("N")[..8]);
            BackupGuard.KeyDirOverride = testKeys;
            var report = Cleanup.SelfTest() + Environment.NewLine + "--- ajustes" + Environment.NewLine + Tweaks.SelfTest()
                + Environment.NewLine + "--- catálogo" + Environment.NewLine + Tweaks.CatalogSelfTest()
                + Environment.NewLine + "--- mensajes de la interfaz" + Environment.NewLine + UiMessage.SelfTest()
                + Environment.NewLine + "--- PawnIO (sin instalar nada)" + Environment.NewLine + Pawn.SelfTest()
                + Environment.NewLine + "--- sugerencias" + Environment.NewLine + Suggestions.SelfTest()
                + Environment.NewLine + "--- juegos" + Environment.NewLine + Games.SelfTest()
                + Environment.NewLine + "--- fps" + Environment.NewLine + Fps.SelfTest()
                + Environment.NewLine + "--- procesos" + Environment.NewLine + Procs.SelfTest()
                + Environment.NewLine + "--- equipo" + Environment.NewLine + Rig.SelfTest()
                + Environment.NewLine + "--- herramientas" + Environment.NewLine + Toolbox.SelfTest()
                + Environment.NewLine + "--- antitrampas" + Environment.NewLine + AntiCheat.SelfTest()
                + Environment.NewLine + "--- memoria" + Environment.NewLine + Memory.SelfTest()
                + Environment.NewLine + "--- discos" + Environment.NewLine + Optimize.SelfTest()
                + Environment.NewLine + "--- seguridad de Windows (solo lectura)" + Environment.NewLine + Security.SelfTest()
                + Environment.NewLine + "--- versiones" + Environment.NewLine + Updates.SelfTest()
                + Environment.NewLine + "--- apps de Windows (lectura de PowerShell)" + Environment.NewLine + Debloat.SelfTest()
                + Environment.NewLine + "--- ISO (cuentas)" + Environment.NewLine + IsoBuild.SelfTest()
                + Environment.NewLine + "--- arranque (bcdedit en cualquier idioma)" + Environment.NewLine + Diagnostics.BcdSelfTest()
                + Environment.NewLine + "--- permisos (carpeta protegida)" + Environment.NewLine + InstallTrust.SelfTest()
                + Environment.NewLine + "--- permisos (operaciones de administrador)" + Environment.NewLine + AdminOps.SelfTest()
                + Environment.NewLine + "--- inicio con Windows" + Environment.NewLine + AutoStart.SelfTest()
                + Environment.NewLine + "--- copias (restauración)" + Environment.NewLine + BackupGuard.SelfTest();
            try { Directory.Delete(testKeys, true); } catch { }
            BackupGuard.KeyDirOverride = null;
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
            // ya está abierta (quizá escondida en la bandeja): pedirle que se muestre.
            // Si esta segunda copia viene del arranque con Windows (--tray), no: sacaría de un juego a pantalla completa.
            if (args.Length >= 1 && args[0] == AutoStart.Arg) return 0;
            try { using var ev = EventWaitHandle.OpenExisting(MainForm.ShowEventName); ev.Set(); } catch { }
            return 0;
        }
        ApplicationConfiguration.Initialize();
        if (args.Length == 0 || args[0] == AutoStart.Arg) { IconOnce(); _ = Task.Run(Wallpaper.Refresh); }
        // La tarea vieja de inicio abría Vigía como administrador desde una carpeta que tu usuario puede cambiar.
        // Si nos abrió ella (o cualquier camino con permisos), se quita ya y el inicio pasa a Run sin permisos:
        // desde el próximo inicio de sesión Vigía abre como tu usuario. Solo la tarea de Vigía (nombre, carpeta y .exe).
        if (Elevation.IsAdmin && !Elevation.InBroker) _ = Task.Run(() => { try { if (AutoStart.LegacyTask() is not null) AutoStart.Repair(); } catch { } });
        // Verificación visual: Vigia.exe --snap salida.png [storage] -> captura la UI real tras ~8 s y sale
        // Consumo real: Vigia.exe --bench salida.json -> 30 s abierta, 60 s minimizada, y sale
        Application.Run(args.Length >= 2 && args[0] == "--snap" ? new MainForm(args[1], args.ElementAtOrDefault(2))
            : args.Length >= 2 && args[0] == "--bench" ? new MainForm(null, null, args[1])
            : args.Length >= 1 && args[0] == AutoStart.Arg ? new MainForm(startHidden: true) : new MainForm());
        return 0;
    }
    // Tras instalar o actualizar, Windows puede seguir mostrando el icono viejo en el acceso directo
    // (lo guarda en caché). Una vez por versión se le avisa que lo vuelva a leer; no abre nada.
    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void SHChangeNotify(int ev, uint flags, IntPtr a, IntPtr b);
    static void IconOnce()
    {
        try
        {
            var v = typeof(Program).Assembly.GetName().Version?.ToString() ?? "";
            var mark = Path.Combine(Settings.Dir, "icono.txt");
            if (File.Exists(mark) && File.ReadAllText(mark) == v) return;
            SHChangeNotify(0x08000000, 0x2000, IntPtr.Zero, IntPtr.Zero);   // SHCNE_ASSOCCHANGED, SHCNF_FLUSHNOWAIT
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(mark, v);
        }
        catch { }
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

    // Al iniciar Windows (tarea oculta): directa a la bandeja, sin que la ventana llegue a verse ni salga en la barra de tareas.
    readonly bool _startHidden;
    public MainForm(bool startHidden) : this()
    {
        _startHidden = startHidden;
        // Nada de «minimizada»: Windows la activa al mostrarla y eso saca del juego que esté al frente.
        // Se crea invisible, fuera de la pantalla y sin activar; luego se esconde en la bandeja.
        if (startHidden) { StartPosition = FormStartPosition.Manual; Location = new Point(-32000, -32000); ShowInTaskbar = false; Opacity = 0; }
    }
    protected override bool ShowWithoutActivation => _startHidden && Opacity == 0;

    public MainForm()
    {
        Text = "Vigía";
        Width = 1360; Height = 860; MinimumSize = new Size(1024, 680);
        BackColor = Color.FromArgb(14, 10, 24);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "vigia.ico")); } catch { }
        Controls.Add(_web);
        Load += async (_, _) =>
        {
            if (_snapPath is null) InitTray();   /* --bench mide con el icono puesto */
            await InitAsync();
            if (_snapPath is null) _ = UndoRetiredAsync();
            if (_snapPath is null) _ = PawnAsync();
            if (_startHidden)
            {
                Hide(); ShowInTaskbar = true; Opacity = 1; CenterToScreen();
                await SetRestingAsync(true);
                _ = ReviewAfterLogonAsync();
            }
        };
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
        // la interfaz no usa objetos nativos expuestos a JavaScript, ni atajos del navegador (F5, Ctrl+P…), ni autocompletar
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = System.Diagnostics.Debugger.IsAttached;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        // Solo se navega a la UI local
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(UiMessage.Origin, StringComparison.Ordinal)) e.Cancel = true; };
        core.FrameNavigationStarting += (_, e) => { if (!e.Uri.StartsWith(UiMessage.Origin, StringComparison.Ordinal)) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.WebMessageReceived += OnMessage;
        // en capturas no sale la bienvenida (salvo que se pida con --snap … welcome / tour-N)
        if (_snapPath is not null) await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__vigiaSnap = true;");
        core.Navigate("https://app.vigia/index.html");
        _timer.Tick += async (_, _) => await PushAsync();
        _timer.Start();
        if (_snapPath is not null) _ = SnapAsync();
        else if (_benchPath is not null) _ = BenchAsync();
        // primera vez: foto de cómo estaba el sistema antes de que Vigía toque nada (una sola vez, en segundo plano)
        if (_snapPath is null && _benchPath is null)
        {
            _ = Task.Run(() => Backups.EnsureOriginal(withRestorePoint: true));
            if (_cfg.UpdateCheck) _ = UpdateLoopAsync();
        }
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

    /// <summary>
    /// Los ajustes con riesgo ya no están en Vigía. A quien los tenía puestos se le devuelven a como estaban,
    /// con copia de seguridad antes. Espera a que el PC esté tranquilo y nunca en mitad de un juego.
    /// </summary>
    CancellationTokenSource? _dsGuard;

    /// <summary>Driver para los grados del procesador: si falta, se pregunta en Monitor. Solo con un «sí» se instala, sin juego abierto.</summary>
    async Task PawnAsync()
    {
        try
        {
            if (Pawn.Installed()) return;
            await Task.Delay(TimeSpan.FromSeconds(20));
            if (_cfg.PawnConsent != true) { Send("pawnAsk", new { declined = _cfg.PawnConsent == false }); return; }
            await PawnInstallAsync();
        }
        catch { }
    }

    bool _pawnBusy;
    async Task PawnInstallAsync()
    {
        if (_pawnBusy || Pawn.Installed()) return;
        _pawnBusy = true;
        try
        {
            while (AutoStart.Busy()) { Send("pawnState", new { ok = false, waiting = true, error = (string?)null }); await Task.Delay(TimeSpan.FromMinutes(5)); }
            if (!Elevation.IsAdmin) { await ViaBrokerAsync("pawnInstall", new JsonObject()); return; }
            string? err = null;
            var ok = await Task.Run(() => Pawn.InstallIfMissing(out err, asked: true));
            if (ok) _tel.CpuDriverReady();
            Send("pawnState", new { ok = ok || Pawn.Installed(), waiting = false, error = err });
        }
        finally { _pawnBusy = false; }
    }

    async Task UndoRetiredAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            while (AutoStart.Busy()) await Task.Delay(TimeSpan.FromMinutes(5));
            if (Elevation.IsAdmin) await Task.Run(Tweaks.UndoRetired);
            else if (Tweaks.RetiredPending() && Elevation.Broker.Alive) await ViaBrokerAsync("undoRetired", new JsonObject());
        }
        catch { }
    }

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

    DateTime _updChecked = DateTime.MinValue;
    bool _procOn, _simpleBusy;

    async Task UpdateLoopAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(20));
        while (!IsDisposed)
        {
            await UpdateOnceAsync();
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    /// <summary>Una sola consulta a GitHub; también al volver de la bandeja si pasó más de una hora.</summary>
    internal async Task UpdateOnceAsync()
    {
        if (!_cfg.UpdateCheck || DateTime.UtcNow - _updChecked < TimeSpan.FromMinutes(5)) return;
        _updChecked = DateTime.UtcNow;
        var u = await Updates.CheckAsync();
        if (u.Available && !IsDisposed) Send("update", u);
    }

    internal void UpdateIfStale()
    {
        if (DateTime.UtcNow - _updChecked > TimeSpan.FromHours(1)) _ = UpdateOnceAsync();
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
                if (c.MemAuto) await MemAutoAsync(c);
                return;
            }
            // con un juego delante (Vigía abierta detrás o en otra pantalla): nada de sensores, solo la memoria
            if (!ContainsFocus && AutoStart.Busy())
            {
                var c = _cfg;
                if (c.MemAuto && await MemAutoAsync(c) is { } a) Send("memFreed", a);
                return;
            }
            if (!Elevation.IsAdmin && Elevation.Broker.Alive) await ViaBrokerAsync("telemetry", new JsonObject());   // sensores con el driver: solo con permisos
            else Send("telemetry", await Task.Run(_tel.Read));
            // cada 2 lecturas: estado de la memoria y, si toca, liberación automática
            if (_tick++ % 2 == 0)
            {
                var cfg = _cfg;
                var auto = await MemAutoAsync(cfg);
                if (auto is not null) Send("memFreed", auto);
                Send("memStatus", await Task.Run(Memory.Status));
            }
            // lo que más consume: solo con la vista Monitor delante, cada 2 s
            else if (_procOn && ContainsFocus) Send("procs", await Task.Run(Procs.Sample));
        }
        catch (Exception e) { Send("error", e.Message); }
        finally { _busy = false; }
    }

    /// <summary>Liberación automática: necesita permisos. Sin ellos, solo si el ayudante ya está abierto (nunca saca UAC sola).</summary>
    async Task<object?> MemAutoAsync(Settings c)
    {
        if (Elevation.IsAdmin) return await Task.Run(() => Memory.AutoTick(c));
        if (Elevation.Broker.Alive)
            await ViaBrokerAsync("memAuto", new JsonObject { ["on"] = c.MemAuto, ["threshold"] = c.MemAutoThreshold, ["interval"] = c.MemAutoIntervalMin });
        return null;   // lo que se libere llega como «memFreed» desde el ayudante
    }

    /// <summary>
    /// Manda una operación de la lista cerrada al ayudante con permisos (lo abre si hace falta: aviso de UAC).
    /// Lo que el ayudante va contando llega a la interfaz igual que antes; al final se vuelven a leer aquí (sin permisos)
    /// las listas que cambian. Si no se pudo, la pantalla recibe su «terminado» con el motivo.
    /// </summary>
    async Task ViaBrokerAsync(string cmd, JsonObject msg)
    {
        if (cmd == "simpleRun") { if (_simpleBusy) return; _simpleBusy = true; }
        var seen = new HashSet<string>();
        try
        {
            string? err;
            try
            {
                var b = await Elevation.Broker.EnsureAsync();
                err = await b.RunAsync(cmd, msg, (t, p) => { try { BeginInvoke(() => { seen.Add(t); OnBrokerEvent(t, p); }); } catch { } });
            }
            catch (Elevation.Denied d) { err = d.Message; }
            if (err is not null)
            {
                if (AdminOps.Failed(cmd, msg, err) is { } f && !seen.Contains(f.Type)) Send(f.Type, f.Payload);
                else if (cmd is not ("telemetry" or "memAuto" or "undoRetired")) Send("error", err);
                UiMessage.Note($"ayudante: {cmd}: {err}");
            }
            await AfterAdminAsync(cmd);
        }
        finally { if (cmd == "simpleRun") _simpleBusy = false; }
    }

    void OnBrokerEvent(string type, JsonElement p)
    {
        switch (type)
        {
            case "pong": break;
            case "_memAuto":
                var on = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("on", out var o) && o.ValueKind == JsonValueKind.True;
                _cfg.MemAuto = on; if (on) _cfg.MemAutoThreshold = 60; _cfg.Save();
                Send("settings", new { cfg = _cfg, lab = Tweaks.Lab, autoStart = AutoStart.Enabled() });
                break;
            case "_fpsFinished":
                _ = Task.Run(GamesPayload).ContinueWith(t => { try { BeginInvoke(() => Send("games", t.Result)); } catch { } }, TaskScheduler.Default);
                break;
            case "_pawn":
                Send("pawnState", new { ok = p.TryGetProperty("ok", out var k) && k.ValueKind == JsonValueKind.True, waiting = false,
                    error = p.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null });
                break;
            case "autoStartRepaired":
                Send("settings", new { cfg = _cfg, lab = Tweaks.Lab, autoStart = AutoStart.Enabled() });
                break;
            default:
                Send(type, p);
                break;
        }
    }

    /// <summary>Lo que la interfaz volvía a leer después de cada operación (todo de lectura, sin permisos).</summary>
    async Task AfterAdminAsync(string cmd)
    {
        switch (cmd)
        {
            case "secEnable" or "secScan": Send("sec", await Task.Run(Security.Status)); break;
            case "startupOffAll" or "startupRemove" or "startupSet": Send("startup", await Task.Run(Startup.List)); break;
            case "tweaksApply" or "tweaksRevert" or "undoRetired":
                Send("tweaks", new { items = await Task.Run(Tweaks.List), restorePoints = await Task.Run(RestorePoint.Count), laptop = Suggestions.HasBattery() });
                break;
            case "bloatRemove": Send("bloat", await Task.Run(Debloat.List)); break;
            case "backupRestore": Send("backups", await Task.Run(Backups.List)); break;
            case "sugFix": Send("suggest", await Task.Run(() => Suggestions.Run(_tel))); break;
        }
    }

    bool _fpsHooked;
    static object GamesPayload() => new { fortnite = Games.Fortnite(), fps = new { state = Fps.State, history = Fps.History() } };

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x00FF) Drift.Feed(m.WParam, m.LParam);   // WM_INPUT: solo llega mientras se miden los Hz del ratón
        base.WndProc(ref m);
    }

    /// <summary>Claves del inventario que manda la interfaz (nunca se pasan a PowerShell: se buscan en el inventario leído).</summary>
    static List<string> AppsIds(JsonNode? n) =>
        (n as JsonArray ?? []).Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
            .Where(s => s is { Length: > 0 and <= 300 } && !s.Any(char.IsControl))   // el plan solo acepta claves que existen en el inventario
            .Select(s => s!).Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToList();

    static PlanRequest AppsRequest(JsonNode? msg)
    {
        var mode = (string?)msg?["mode"] is { } m && AppPlanner.Modes.Contains(m) ? m : "safe";
        var deprov = new HashSet<string>(AppsIds(msg?["deprovision"]), StringComparer.OrdinalIgnoreCase);
        return new PlanRequest(mode, AppsIds(msg?["keys"]).Select(k => (k, mode == "advanced" && deprov.Contains(k))).ToList());
    }

    void Send(string type, object payload) =>
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, payload }, Program.Json));

    async void OnMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // origen real del documento, tamaño y forma: lo de app.js no basta
            var msg = UiMessage.Accept(e.Source, e.WebMessageAsJson, out var why);
            if (msg is null) { UiMessage.Note($"mensaje de la interfaz rechazado ({why})"); return; }
            // Lo que necesita administrador va al ayudante (lista cerrada). Las de lectura siguen aquí, sin permisos.
            var cmd0 = (string?)msg["cmd"] ?? "";
            if (!Elevation.IsAdmin && AdminOps.Names.Contains(cmd0) && !(cmd0 == "sugFix" && ((string?)msg["fix"] ?? "").StartsWith("hz:"))
                && !(AdminOps.Soft.Contains(cmd0) && !Elevation.Broker.Alive))
            {
                await ViaBrokerAsync(cmd0, msg);
                return;
            }
            switch (cmd0)
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
                case "suggest":
                    // la revisión sola (al abrir) solo con Vigía delante y sin juego: hace pings, WMI y lee discos.
                    // Si no, espera a la próxima vez que mires Vigía o a que pulses «Analizar».
                    if ((bool?)msg["auto"] == true && (AutoStart.Busy() || Form.ActiveForm != this)) break;
                    // a la vez que el resto: unos segundos mirando el ratón y los sticks, sin pedirle nada al usuario
                    var sense = Task.Run(() => Drift.Sense(this, 3000));
                    var srep = await Task.Run(() => Suggestions.Run(_tel));
                    Send("suggest", Suggestions.WithInput(srep, await sense));
                    break;
                case "diagRun":
                    Send("diagRun", await Task.Run(() => Diagnostics.Run(7)));
                    break;
                case "secGet":
                    Send("sec", await Task.Run(Security.Status));
                    Send("secPos", await Task.Run(Security.Posture));
                    break;
                case "secOpen":
                    // solo una clave de la lista cerrada de Security.Pages; nunca una ruta ni una URL del frontend
                    Security.OpenPage((string?)msg["page"] ?? "");
                    break;
                case "secEnable":
                case "secScan":
                    var secCmd = (string?)msg["cmd"];
                    Send("secDone", await Task.Run(() => secCmd == "secEnable"
                        ? Security.Enable(p => BeginInvoke(() => Send("secProgress", p)))
                        : Security.UpdateAndScan(p => BeginInvoke(() => Send("secProgress", p)))));
                    Send("sec", await Task.Run(Security.Status));
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
                    // si terminó bien, se vuelve a medir solo esa unidad (en SSD no hace nada)
                    Send("optStatus", await Task.Run(() => Optimize.Status(res.Ok, letter)));
                    break;
                case "optCancel":
                    Optimize.Cancel();
                    break;
                case "memFree":
                    var o = msg["opts"]?.Deserialize<MemOptions>(Program.Json) ?? MemOptions.Safe;
                    Send("memFreed", await Task.Run(() => Memory.Free(o)));
                    break;
                case "autoStartSet":
                    var (aok, amsg) = await Task.Run(() => AutoStart.Set((bool?)msg["on"] ?? false));
                    // queda la tarea antigua (con permisos de administrador): quitarla necesita permiso; lo pidió la persona al tocar el interruptor
                    if (aok && !Elevation.IsAdmin && await Task.Run(AutoStart.LegacyTask) is not null)
                        await ViaBrokerAsync("autoStartRepair", new JsonObject());
                    Send("autoStart", new { ok = aok, message = amsg == "legacy" ? null : amsg, on = AutoStart.Enabled() });
                    break;
                case "startupOffAll":
                    Send("startupAll", await Task.Run(Startup.OffAll));
                    Send("startup", await Task.Run(Startup.List));
                    break;
                case "settingsGet":
                    Send("settings", new { cfg = _cfg, lab = Tweaks.Lab, autoStart = AutoStart.Enabled() });
                    break;
                case "settingsSet":
                    var next = msg["cfg"].Deserialize<Settings>(Program.Json);
                    if (next is not null)
                    {
                        next.MemAutoThreshold = Math.Clamp(next.MemAutoThreshold, 50, 98);
                        next.MemAutoIntervalMin = Math.Clamp(next.MemAutoIntervalMin, 1, 120);
                        // el permiso para el driver solo sale del aviso de Monitor (pawnAnswer), no de guardar ajustes
                        next.PawnConsent = _cfg.PawnConsent;
                        _cfg = next; _cfg.Save();
                    }
                    Send("settings", new { cfg = _cfg, lab = Tweaks.Lab, autoStart = AutoStart.Enabled() });
                    break;
                case "tweaksList":
                    Send("tweaks", new { items = await Task.Run(Tweaks.List), restorePoints = await Task.Run(RestorePoint.Count), laptop = Suggestions.HasBattery() });
                    break;
                case "tweaksApply":
                case "tweaksRevert":
                    var tids = msg["ids"]!.AsArray().Select(x => (string)x!).ToList();
                    var batch = (string?)msg["cmd"] == "tweaksApply" ? await Task.Run(() => Tweaks.Apply(tids)) : await Task.Run(() => Tweaks.Revert(tids));
                    Send("tweaksDone", batch);
                    Send("tweaks", new { items = await Task.Run(Tweaks.List), restorePoints = await Task.Run(RestorePoint.Count), laptop = Suggestions.HasBattery() });
                    break;
                case "bloatList":
                    Send("bloat", await Task.Run(Debloat.List));
                    break;
                case "bloatRemove":
                    // lista corta antigua (modo seguro: solo tu usuario, mismas comprobaciones que la vista Apps)
                    var bids = AppsIds(msg["ids"]);
                    Send("bloatDone", await Task.Run(() => Debloat.Remove(bids)));
                    Send("bloat", await Task.Run(Debloat.List));
                    break;
                // Vista Apps: operaciones identificadas; la interfaz solo manda claves del inventario y el modo, nunca órdenes
                case "appsScan":
                    Send("apps", await Task.Run(() => Debloat.View(force: (bool?)msg["force"] == true)));
                    break;
                case "appsPreview":
                    Send("appsPlan", await Task.Run(() => Debloat.Preview(AppsRequest(msg))));
                    break;
                case "appsRun":
                    var areq = AppsRequest(msg);
                    var atok = (string?)msg["token"] ?? "";
                    if (areq.Items.Any(i => i.Deprovision) || areq.Mode != "safe")
                        await Task.Run(() => RestorePoint.Create("Vigía: antes de quitar apps"));
                    var ares = await Task.Run(() => Debloat.Run(areq, atok, st => BeginInvoke(() => Send("appsStep", st))));
                    Send("appsDone", ares);
                    Send("apps", await Task.Run(() => Debloat.View(force: true)));
                    break;
                case "appsCancel":
                    Debloat.Cancel();
                    break;
                case "appsRestore":
                    var hid = (string?)msg["id"] ?? "";
                    if (!System.Text.RegularExpressions.Regex.IsMatch(hid, "^[0-9a-f]{12}$")) { Send("appsRestored", new { ok = false, message = "id" }); break; }
                    var (rok, rmsg) = await Task.Run(() => Debloat.Restore(hid));
                    Send("appsRestored", new { ok = rok, message = rmsg });
                    Send("apps", await Task.Run(() => Debloat.View(force: true)));
                    break;
                case "appsExport":
                    try { Send("appsExported", new { ok = true, path = await Task.Run(Debloat.Export) }); }
                    catch (Exception ex) { Send("appsExported", new { ok = false, path = ex.Message }); }
                    break;
                case "appsSettings":
                    // programas clásicos: se abre la página de Windows; la desinstalación la confirma el usuario ahí
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true }); } catch { }
                    break;
                case "wallGet":
                    Send("wall", Wallpaper.Status());
                    break;
                case "wallSet":
                    Send("wallDone", await Task.Run(Wallpaper.Apply));
                    Send("wall", Wallpaper.Status());
                    break;
                case "toolsList":
                    Send("tools", new { items = await Task.Run(Toolbox.List) });
                    break;
                case "toolOpen":
                    var tid = (string?)msg["id"] ?? "";
                    Send("toolDone", tid == "restorepoint" ? await Task.Run(() => Toolbox.Open(tid)) : Toolbox.Open(tid));
                    break;
                case "toolInstall":
                    Send("toolDone", Toolbox.Install((string?)msg["id"] ?? ""));
                    break;
                case "startupList":
                    Send("startup", await Task.Run(Startup.List));
                    break;
                case "simplePlan":
                    var sa = msg["answers"].Deserialize<Simple.Answers>(Program.Json) ?? new();
                    Send("simplePlan", await Task.Run(() => Simple.Build(sa)));
                    break;
                case "simpleRun":
                    if (_simpleBusy) break;
                    _simpleBusy = true;
                    try
                    {
                        var ra = msg["answers"].Deserialize<Simple.Answers>(Program.Json) ?? new();
                        var rep = await Task.Run(() => Simple.Run(Simple.Build(ra), st => BeginInvoke(() => Send("simpleStep", st)),
                            on => BeginInvoke(() => { _cfg.MemAuto = on; if (on) _cfg.MemAutoThreshold = 60; _cfg.Save(); Send("settings", new { cfg = _cfg, lab = Tweaks.Lab, autoStart = AutoStart.Enabled() }); })));
                        Send("simpleDone", rep);
                    }
                    finally { _simpleBusy = false; }
                    break;
                case "procWatch":
                    _procOn = (bool)msg["on"]!;
                    if (_procOn) { Procs.Reset(); _ = Task.Run(Procs.Sample); }
                    break;
                case "procClose":
                    var pexe = (string)msg["exe"]!;
                    Send("procDone", await Task.Run(() => Procs.Close(pexe)));
                    break;
                case "procReveal":
                    var rexe = (string)msg["exe"]!;
                    await Task.Run(() => Procs.Reveal(rexe));
                    break;
                case "startupRemove":
                    Send("startupDone", await Task.Run(() => Startup.Remove((string)msg["id"]!)));
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
                    var dg = msg["games"]?.GetValue<bool>() ?? false;
                    var sdone = await Task.Run(() => Display.Stretch(dw, dh, dg));
                    Send("displayDone", sdone);
                    Send("display", Display.Status());
                    // red de seguridad aquí y no solo en la página: si la ventana está minimizada, WebView2 duerme
                    // su cuenta atrás y la pantalla podría quedarse en negro sin volver
                    _dsGuard?.Cancel(); _dsGuard = null;
                    if (sdone.Ok && sdone.Pending)
                    {
                        var g = _dsGuard = new CancellationTokenSource();
                        _ = Task.Delay(TimeSpan.FromSeconds(20), g.Token).ContinueWith(t =>
                        {
                            if (t.IsCanceled) return;
                            BeginInvoke(() => { if (_dsGuard != g) return; _dsGuard = null; Send("displayDone", Display.Restore()); Send("display", Display.Status()); });
                        }, TaskScheduler.Default);
                    }
                    break;
                case "displayKeep":
                    _dsGuard?.Cancel(); _dsGuard = null;
                    break;
                case "displayRestore":
                    _dsGuard?.Cancel(); _dsGuard = null;
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
                    if (await Updates.EnsurePuenteAsync(fresh: true) is string perr) { Send("update", (await Updates.CheckAsync()) with { Error = perr }); break; }
                    if (Updates.StartPuente()) { _quitting = true; Close(); }   // salir de verdad, no a la bandeja
                    else Send("update", (await Updates.CheckAsync()) with { Error = "No pude abrir Puente." });
                    break;
                case "gamesGet":
                    Send("games", await Task.Run(GamesPayload));
                    break;
                case "rigKeep":
                    Send("sugDone", Rig.Keep());
                    break;
                case "sugFix":
                    var sfix = (string?)msg["fix"] ?? "";
                    if (sfix.StartsWith("hz:"))
                    {
                        var parts = sfix.Split(':'); var shz = int.Parse(parts[^1]); var sdev = string.Join(':', parts[1..^1]);
                        Send("sugDone", await Task.Run(() => Rig.SetHz(sdev, shz, r => BeginInvoke(async () =>
                        {
                            Send("sugDone", r);
                            Send("suggest", await Task.Run(() => Suggestions.Run(_tel)));
                        }))));
                    }
                    else if (sfix.StartsWith("tweak:"))
                    {
                        var sb = await Task.Run(() => Tweaks.Apply(new[] { sfix[6..] }));
                        var sr = sb.Results.FirstOrDefault();
                        Send("sugDone", new Rig.HzDone(sb.Ok, sb.Ok ? (sb.NeedsReboot ? "Listo. Se nota después de reiniciar." : "Listo.") : $"No se pudo: {sr?.Error}", false));
                    }
                    else
                    {
                        var (sok, smsg) = await Task.Run(() => Suggestions.Fix(sfix));
                        Send("sugDone", new Rig.HzDone(sok, smsg, false));
                    }
                    Send("suggest", await Task.Run(() => Suggestions.Run(_tel)));
                    break;
                case "fortniteApply":
                    var fprof = (bool?)msg["profile"] == true; var fmode = (string?)msg["mode"];
                    Send("gameDone", await Task.Run(() => Games.ApplyFortnite(fprof, fmode)));
                    Send("games", await Task.Run(GamesPayload));
                    break;
                case "fpsStart":
                    var fsec = Math.Clamp((int?)msg["seconds"] ?? 180, 30, 1800);
                    if (!_fpsHooked)
                    {
                        _fpsHooked = true;
                        Fps.Changed += st => { try { BeginInvoke(() => Send("fpsState", st)); } catch { } };
                        Fps.Finished += _ => { try { BeginInvoke(async () => Send("games", await Task.Run(GamesPayload))); } catch { } };
                    }
                    Fps.Start(fsec, (string?)msg["label"] ?? "");
                    break;
                case "rates":
                    try { Send("rates", await Task.Run(() => Drift.Rates(5, this))); }
                    catch { Send("rates", new Drift.RateReport(5, [])); }
                    break;
                case "drift":
                    try { Send("drift", await Task.Run(() => Drift.Run(10))); }
                    catch { Send("drift", new DriftReport(10, 0, 0, [])); }
                    break;
                case "fpsCancel":
                    Fps.Cancel();
                    break;
                case "fpsDelete":
                    var fid = (string)msg["id"]!;
                    await Task.Run(() => Fps.Delete(fid));
                    Send("games", await Task.Run(GamesPayload));
                    break;
                case "biosGet":
                    Send("bios", await Task.Run(Bios.Get));
                    break;
                case "biosSupport":   // la URL la arma Vigía con el modelo de la placa; la interfaz no manda ninguna
                    var burl = (await Task.Run(Bios.Get)).Fw.SupportUrl;
                    if (burl is not null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(burl) { UseShellExecute = true });
                    break;
                case "rebootNow":   // solo desde el botón «Reiniciar ahora» del modo simple; da 20 s y se puede cancelar
                    Send("rebootState", new { on = Tweaks.Run("shutdown.exe", "/r /t 20 /c \"Vigía: reinicio para terminar la optimización\"").exit == 0 });
                    break;
                case "rebootCancel":
                    Tweaks.Run("shutdown.exe", "/a");
                    Send("rebootState", new { on = false });
                    break;
                case "biosReboot":
                    Send("biosDone", await Task.Run(Bios.RebootToFirmware));
                    break;
                case "isoPick":
                    using (var ofd = new OpenFileDialog { Filter = "Imagen de Windows (*.iso)|*.iso", Title = "Elige la ISO oficial de Windows" })
                        if (ofd.ShowDialog(this) == DialogResult.OK)
                        {
                            Send("isoSource", new { path = ofd.FileName, busy = true });
                            // montar la ISO pide administrador: el diálogo es de la ventana, el montaje lo hace el ayudante
                            if (Elevation.IsAdmin) Send("isoSource", await Task.Run(() => IsoBuild.Inspect(ofd.FileName)));
                            else await ViaBrokerAsync("isoInspect", new JsonObject { ["path"] = ofd.FileName });
                        }
                    break;
                case "isoSaveAs":
                    var isrc = (string?)msg["source"] ?? "";
                    using (var sfd = new SaveFileDialog { Filter = "Imagen de Windows (*.iso)|*.iso", Title = "Dónde guardar la ISO limpia", OverwritePrompt = true,
                        FileName = Path.GetFileNameWithoutExtension(isrc) + "-vigia.iso", InitialDirectory = Path.GetDirectoryName(isrc) ?? "" })
                        if (sfd.ShowDialog(this) == DialogResult.OK) Send("isoOutput", new { path = sfd.FileName });
                    break;
                case "isoBuild":
                    var iopt = msg["options"].Deserialize<IsoBuild.Options>(Program.Json)!;
                    string ilast = "";
                    Send("isoDone", await Task.Run(() => IsoBuild.Build(iopt, pr =>
                    {
                        var key = $"{pr.Num}{pr.Step}{(int)(pr.Pct ?? -1)}";
                        if (key == ilast) return; ilast = key;
                        BeginInvoke(() => Send("isoProgress", pr));
                    })));
                    break;
                case "isoCancel":
                case "usbCancel":
                    IsoBuild.Cancel();
                    break;
                case "usbList":
                    Send("usbList", await Task.Run(IsoBuild.UsbList));
                    break;
                case "usbWrite":
                    var uiso = (string?)msg["iso"] ?? ""; var unum = (int?)msg["number"] ?? -1; var userial = (string?)msg["serial"] ?? "";
                    string ulast = "";
                    Send("usbDone", await Task.Run(() => IsoBuild.ToUsb(uiso, unum, userial, pr =>
                    {
                        var key = $"{pr.Num}{pr.Step}{(int)(pr.Pct ?? -1)}";
                        if (key == ulast) return; ulast = key;
                        BeginInvoke(() => Send("usbProgress", pr));
                    })));
                    break;
                case "usbPickIso":
                    using (var ofd = new OpenFileDialog { Filter = "Imagen de Windows (*.iso)|*.iso", Title = "Elige la ISO que vas a pasar al USB" })
                        if (ofd.ShowDialog(this) == DialogResult.OK) Send("usbIso", new { path = ofd.FileName, bytes = new FileInfo(ofd.FileName).Length });
                    break;
                case "isoShow":
                    var ishow = (string?)msg["path"];
                    if (ishow is not null && File.Exists(ishow) && ishow.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{ishow}\"") { UseShellExecute = true });
                    break;
                case "isoGetWindows":
                    await OpenMsDownloadAsync(Tr.Lang(_cfg));
                    break;
                case "isoDlCancel":
                    CancelMsDownload();
                    break;
                case "supportGet":
                    Send("support", await Support.GetAsync());
                    break;
                case "pawnAnswer":
                    // respuesta del aviso de Monitor: solo true instala; se recuerda para no volver a preguntar
                    if (msg["yes"] is not System.Text.Json.Nodes.JsonValue yv || yv.GetValueKind() is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)) break;
                    _cfg.PawnConsent = (bool)yv;
                    _cfg.Save();
                    if (_cfg.PawnConsent == true) await PawnInstallAsync();
                    break;
                case "openSource":
                    // la fuente de un ajuste: el frontend solo manda el id; la URL sale del catálogo, nunca del mensaje
                    var srcId = UiMessage.Str(msg, "id", 64);
                    var srcUrl = srcId is { Length: > 0 } ? Tweaks.SourceOf(srcId) : null;
                    if (srcUrl is null || !Support.OpenSource(srcUrl)) Send("error", "Ese enlace no está permitido.");
                    break;
                case "openUrl":
                    var link = (string?)msg["url"] ?? "";
                    if (!Support.Open(link == "repo" ? Support.Repo : link)) Send("error", "Ese enlace no está permitido.");
                    break;
                default:
                    // lista cerrada: lo que no está arriba no se hace
                    UiMessage.Note($"comando desconocido de la interfaz: {(string?)msg["cmd"]}");
                    break;
            }
        }
        catch (Exception ex) { Send("error", ex.Message); }
    }
}
