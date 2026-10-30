using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Vigia;

public record BloatApp(string Id, string Name, string Detail, string Risk, bool Installed, string? Version, string? Blocked, bool Useful = false);
public record BloatResult(string Id, bool Ok, string? Error);

/// <summary>
/// Apps de Windows. Este archivo solo junta las piezas:
/// AppInventory.cs (leer: AppX/MSIX de todos los usuarios, aprovisionadas y programas clásicos), AppCatalog.cs (catálogo
/// apps.json y clasificación), AppPlan.cs (plan, validación y ejecución de a una), AppHistory.cs (historial y recuperación).
/// Por defecto solo se quita de tu usuario; el aprovisionamiento y los programas clásicos son aparte y solo en modo Avanzado.
/// </summary>
public static class Debloat
{
    public static IAppHost Host { get; set; } = new WindowsAppHost();
    internal static bool BackupEnabled = true;
    static readonly object Gate = new();
    static AppInventoryData? _inv;
    static CancellationTokenSource _cts = new();

    /// <summary>Cancela el análisis o la ejecución en curso (la app que ya empezó termina; las demás no se inician).</summary>
    public static void Cancel() { lock (Gate) _cts.Cancel(); }
    static CancellationToken NewToken() { lock (Gate) { if (_cts.IsCancellationRequested) { _cts.Dispose(); _cts = new(); } return _cts.Token; } }

    /// <summary>El inventario, leído como mucho una vez por minuto (la búsqueda de la interfaz trabaja sobre esta copia).</summary>
    public static AppInventoryData Inventory(bool force = false)
    {
        lock (Gate) if (!force && _inv is { Ok: true } c && DateTime.Now - c.At < TimeSpan.FromSeconds(60)) return c;
        var inv = Host.Scan(NewToken());
        lock (Gate) _inv = inv;
        return inv;
    }

    public static void Forget() { lock (Gate) _inv = null; }

    /// <summary>Lo que necesita la vista Apps: todo clasificado, más cambios desde la última vez, avisos e historial.</summary>
    public static object View(bool force = false)
    {
        var sw = Stopwatch.StartNew();
        var inv = Inventory(force);
        if (!inv.Ok) return new { ok = false, error = inv.Error, apps = Array.Empty<AppView>(), win32 = Array.Empty<AppView>(), history = History(inv) };
        AppHistory.Reconcile(inv);
        var apps = AppClassifier.Classify(inv, AppHistory.RemovedNames(), LabGuard.Allowed);
        var (added, gone) = AppHistory.Compare(inv);
        return new
        {
            ok = true, apps, win32 = AppClassifier.ClassifyWin32(inv.Win32), allUsers = inv.AllUsers, provisionedOk = inv.ProvisionedOk,
            storeBlocked = inv.StoreBlocked, managed = inv.Managed, warnings = inv.Warnings, added, gone, ms = sw.ElapsedMilliseconds,
            history = History(inv),
        };
    }

    public static object History(AppInventoryData? inv = null)
    {
        inv ??= Inventory();
        return AppHistory.All().OrderByDescending(e => e.At).Take(100).Select(e =>
        {
            var o = inv.Ok && e.State == "removed" && e.Op.StartsWith("remove-user") ? AppRecovery.For(e, inv, Host) : null;
            return new { e.Id, e.At, e.Kind, e.Name, e.Display, e.Op, e.State, e.Error, e.Recovery, e.Source, restore = o?.Level, restoreText = o?.Text };
        }).ToList();
    }

    public static Plan Preview(PlanRequest req) => AppPlanner.Build(Host, Inventory(force: true), req, LabGuard.Allowed);

    public static ExecResult Run(PlanRequest req, string token, Action<ExecStep>? progress)
    {
        try { return AppPlanner.Execute(Host, req, token, progress, NewToken(), LabGuard.Allowed, BackupEnabled); }
        finally { Forget(); }
    }

    /// <summary>Recupera una app del historial con el método que haya hoy (A: volver a registrar; B: abrir su ficha en la Store).</summary>
    public static (bool Ok, string Message) Restore(string id)
    {
        var e = AppHistory.All().FirstOrDefault(x => x.Id == id);
        if (e is null) return (false, "No está en el historial.");
        var inv = Inventory(force: true);
        if (!inv.Ok) return (false, inv.Error ?? "No pude leer las apps.");
        var o = AppRecovery.For(e, inv, Host);
        var it = new PlanItem(e.Full ?? e.Name, "appx", e.Name, e.Display, "restore", true, null, "", [], o.Level, o.Text, false, [], e.Users, [],
            e.Full, null, null, e.Family, e.Version, e.Publisher, e.Arch, e.Provisioned, null);
        switch (o.Level)
        {
            case "A":
                var rec = AppHistory.Begin(it, "restore-register");
                var (st, err) = Host.Register(o.Manifest!, NewToken());
                AppHistory.End(rec, st == "registered" ? "registered" : "failed", err);
                Forget();
                return st == "registered" ? (true, $"{e.Display} volvió a tu usuario. Sus datos de antes no vuelven.") : (false, $"No se pudo: {err}");
            case "B":
                var r2 = AppHistory.Begin(it, "restore-store");
                var ok = AppRecovery.OpenStore(o.Family!);
                AppHistory.End(r2, ok ? "opened" : "failed", ok ? null : "No se pudo abrir Microsoft Store.");
                return ok ? (true, "Abrí su página en Microsoft Store: instálala desde ahí si sigue publicada.") : (false, "No se pudo abrir Microsoft Store.");
            default: return (false, o.Text);
        }
    }

    public static string Export()
    {
        var inv = Inventory();
        if (!inv.Ok) throw new InvalidOperationException(inv.Error ?? "No pude leer las apps.");
        return AppHistory.Export(AppClassifier.Classify(inv).Concat(AppClassifier.ClassifyWin32(inv.Win32)).ToList());
    }

    // ---------- lista corta del catálogo (Modo simple, ISO, copia del estado original y laboratorio) ----------

    /// <summary>Para la ISO: lo del catálogo que se puede quitar de una imagen (nunca lo protegido ni lo del fabricante).</summary>
    internal static IEnumerable<(string Id, string Name, string Risk)> All() =>
        AppCatalog.Catalog.Apps.Where(a => a.Iso && a.Category is "optional" or "useful").SelectMany(a => a.Packages.Select(p => (p, a.Name, a.Risk)));

    public static List<BloatApp> List()
    {
        var inv = Inventory();
        var mine = inv.Packages.Where(p => p.Me).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return AppCatalog.Catalog.Apps.Where(a => a.Iso && a.Category is "optional" or "useful").SelectMany(a => a.Packages.Select(id =>
        {
            mine.TryGetValue(id, out var p);
            return new BloatApp(id, a.Name, a.Detail, a.Risk, p is not null, p?.Version,
                a.Risk != "bajo" && !LabGuard.Allowed ? "Primero hay que probarlo en la máquina virtual." : null, a.Category == "useful");
        })).ToList();
    }

    /// <summary>Quitar por nombre de paquete en modo seguro (Modo simple y laboratorio): solo de tu usuario, sin aprovisionamiento,
    /// sin útiles ni protegidas, con las mismas comprobaciones, historial y copia que la vista Apps.</summary>
    public static List<BloatResult> Remove(IEnumerable<string> ids)
    {
        var want = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var res = new List<BloatResult>();
        if (want.Count == 0) return res;
        var inv = Inventory(force: true);
        if (!inv.Ok) return want.Select(id => new BloatResult(id, false, inv.Error)).ToList();
        var keys = new List<(string, bool)>();
        foreach (var id in want)
        {
            var p = inv.Packages.FirstOrDefault(x => x.Me && x.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (p is null) { var known = AppCatalog.Find(id) is not null; res.Add(new(id, known, known ? null : "No está en la lista de Vigía.")); continue; }
            keys.Add((p.FullName, false));
        }
        if (keys.Count == 0) return res;
        var plan = AppPlanner.Build(Host, inv, new PlanRequest("safe", keys), LabGuard.Allowed);
        foreach (var bad in plan.Items.Where(i => !i.Ok)) res.Add(new(bad.Name, false, bad.Problem));
        var good = plan.Items.Where(i => i.Ok).ToList();
        if (good.Count == 0) return res;
        var r = Run(new PlanRequest("safe", good.Select(i => (i.Key, false)).ToList()), AppPlanner.TokenOf("safe", good), null);
        if (r.Changed) res.AddRange(good.Select(i => new BloatResult(i.Name, false, "El inventario cambió mientras tanto; vuelve a revisar.")));
        else res.AddRange(r.Steps.Select(s => new BloatResult(good.FirstOrDefault(i => i.Key == s.Key)?.Name ?? s.Key, s.State is "removed" or "gone",
            s.State is "removed" or "gone" ? null : s.Error ?? s.State)));
        return res;
    }

    // ---------- pruebas (con un Windows falso: nada se quita de verdad) ----------

    sealed class FakeHost(AppInventoryData inv) : IAppHost
    {
        public AppInventoryData Inv = inv;
        public Dictionary<string, (string, string?)> Results = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Running = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Calls = [];
        public Action? OnRemove;
        public AppInventoryData Scan(CancellationToken ct) => Inv;
        public (string State, string? Error) RemoveForUser(string full, CancellationToken ct) { Calls.Add("rm " + full); OnRemove?.Invoke(); return Results.GetValueOrDefault(full, ("removed", null)); }
        public (string State, string? Error) Deprovision(string prov, CancellationToken ct) { Calls.Add("deprov " + prov); return ("removed", null); }
        public (string State, string? Error) Register(string m, CancellationToken ct) { Calls.Add("reg " + m); return ("registered", null); }
        public List<string> RunningUnder(string? folder) => folder is not null && Running.TryGetValue(folder, out var l) ? l : [];
        public bool FileExists(string path) => Files.Contains(path);
        public bool Win32Present(string key) => false;
    }

    static AppPkg Pkg(string name, string sig = "Store", bool fw = false, bool nr = false, bool me = true, int others = 0, bool prov = false, string[]? deps = null,
        string pub = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", string? pubName = null) =>
        new(name, $"{name}_1.0.0.0_x64__8wekyb3d8bbwe", $"{name}_8wekyb3d8bbwe", "1.0.0.0", "X64", pub, sig, fw, nr, false, false, "Ok",
            $@"C:\Program Files\WindowsApps\{name}_1.0.0.0_x64__8wekyb3d8bbwe", deps ?? [], me, others, prov, prov ? $"{name}_1.0.0.0_neutral_~_8wekyb3d8bbwe" : null, null, pubName);

    static AppInventoryData Inv(params AppPkg[] p) => new(true, null, true, true, p.ToList(), [], [], DateTime.Now, false, false);

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        var prevHost = Host;
        var hist = Path.Combine(Path.GetTempPath(), $"vigia-apps-test-{Environment.ProcessId}");
        try
        {
            AppHistory.DirOverride = hist; BackupEnabled = false;
            // --- catálogo
            var cat = AppCatalog.Catalog;
            Check(cat.Apps.Count > 40 && cat.Version >= 2, $"catálogo de apps dentro del ejecutable ({cat.Apps.Count} entradas)");
            Check(cat.Apps.SelectMany(a => a.Packages).Distinct(StringComparer.OrdinalIgnoreCase).Count() == cat.Apps.Sum(a => a.Packages.Length), "catálogo: sin paquetes repetidos");
            Check(cat.Apps.All(a => a.Category is "optional" or "useful" or "oem") && cat.Apps.All(a => cat.Sources.ContainsKey(a.Source)), "catálogo: categoría válida y fuente declarada en cada entrada");
            Check(cat.Apps.SelectMany(a => a.Packages).All(p => WindowsAppHost.PkgRx().IsMatch(p) && !p.Contains('_')), "catálogo: identificadores de paquete sin versión ni editor");
            Check(new[] { "Microsoft.Copilot", "Microsoft.WindowsAlarms", "Microsoft.WindowsSoundRecorder", "SpotifyAB.SpotifyMusic", "MSTeams", "Microsoft.OutlookForWindows", "Microsoft.YourPhone", "MicrosoftCorporationII.QuickAssist", "Microsoft.GamingApp" }
                .All(id => AppCatalog.Find(id)?.Category == "useful"), "útiles (Copilot, Alarmas, Grabadora, Spotify, Teams, Outlook, Vincular móvil, Asistencia rápida, Xbox) nunca son «opcionales»");
            Check(AppCatalog.Find("Microsoft.Advertising.Xaml") is null && cat.ProtectNames.Contains("Microsoft.Advertising.Xaml"), "Microsoft.Advertising.Xaml protegida (es un framework), no es «basura»");

            // --- lectura de PowerShell
            Check(Ps.Payload("") is null && Ps.Payload(null) is null && Ps.Payload("sin marca") is null, "respuesta vacía o sin marca = fallo de lectura, no lista vacía");
            Check(Ps.Payload("ADVERTENCIA: algo\r\nWARNING: x\r\n<<VIGIA-JSON>>{\"a\":1}") is { } pw && (int)pw["a"]! == 1, "avisos antes del JSON no lo rompen");
            Check(Ps.Payload("<<VIGIA-JSON>>{\"packages\":[{\"name\":") is null, "JSON cortado = fallo de lectura, sin excepción");
            var w = new List<string>();
            Check(AppParse.Packages(JsonNode.Parse("{\"packages\":[],\"provisioned\":[]}")!.AsObject(), w).Pkgs.Count == 0, "cero apps");
            Check(AppParse.Packages(JsonNode.Parse("{\"packages\":{\"name\":\"A.B\",\"full\":\"A.B_1_x64__abc\"}}")!.AsObject(), w).Pkgs.Count == 1, "una sola app (objeto, no lista)");
            var many = "{\"packages\":[" + string.Join(",", Enumerable.Range(0, 400).Select(i => $"{{\"name\":\"Vendor.App{i}\",\"full\":\"Vendor.App{i}_1.0_x64__abcdefghijklm\",\"me\":true}}")) + "]}";
            var sw = Stopwatch.StartNew();
            var bigInv = new AppInventoryData(true, null, true, true, AppParse.Packages(JsonNode.Parse(many)!.AsObject(), w).Pkgs, [], [], DateTime.Now, false, false);
            var bigViews = AppClassifier.Classify(bigInv);
            Check(bigViews.Count == 400 && sw.ElapsedMilliseconds < 3000, $"400 apps se leen y clasifican ({sw.ElapsedMilliseconds} ms)");
            Check(AppParse.Packages(JsonNode.Parse("{\"packages\":[{\"name\":\"A.B\",\"full\":\"A.B_1_x64__abc\",\"me\":true},{\"name\":\"A.B\",\"full\":\"A.B_1_x64__abc\",\"others\":2}]}")!.AsObject(), w).Pkgs is var dd
                && dd.Count == 1 && dd[0].Me && dd[0].Others == 2, "paquete repetido = una app (se juntan los usuarios)");
            Check(AppParse.Packages(JsonNode.Parse("{\"packages\":[{\"name\":\"A.B\",\"full\":\"A.B'; Remove-Item x\"},{\"name\":\"A'B\",\"full\":\"A.B_1_x64__abc\"},{\"name\":null,\"full\":5},{\"full\":\"X_1\"}]}")!.AsObject(), w).Pkgs.Count == 0,
                "identificadores inválidos o ausentes se saltan");
            Check(AppParse.Packages(JsonNode.Parse("{\"packages\":[{\"name\":\"A.B\",\"full\":\"A.B_1_x64__abc\",\"deps\":\"Microsoft.VCLibs.140.00\",\"ver\":{\"Major\":1}}]}")!.AsObject(), w).Pkgs is var odd
                && odd.Count == 1 && odd[0].Deps.Length == 1 && odd[0].Version == "", "campos raros o de otro tipo no rompen la lista");
            Check(AppParse.Packages(JsonNode.Parse("{\"packages\":[],\"provisioned\":[{\"name\":\"Microsoft.BingNews\",\"pkg\":\"Microsoft.BingNews_4.1.0.0_neutral_~_8wekyb3d8bbwe\"}]}")!.AsObject(), w).Pkgs is var pv
                && pv.Count == 1 && pv[0].Provisioned && !pv[0].Me, "aprovisionada sin instalar en nadie: sale aparte, no como instalada");
            w = [];
            Check(AppParse.Packages(JsonNode.Parse("{\"packages\":[{\"name\":\"A.B\",\"full\":\"A.B_1_x64__abc\"}],\"errors\":[\"provisioned: Acceso denegado\"]}")!.AsObject(), w).Pkgs.Count == 1 && w.Count == 1,
                "error de permisos en una parte: el resto se lista y el error se avisa");

            // PowerShell de verdad, sin tocar nada: Unicode, parámetros literales (sin inyección), errores, tiempo máximo y cancelar
            var u = Ps.Run("Write-Output ('<<VIGIA-JSON>>' + (@{ s = 'Ñandú ü 漢字 ' + $env:VIGIA_PKG } | ConvertTo-Json -Compress))",
                new Dictionary<string, string> { ["VIGIA_PKG"] = "a'; Remove-Item C:\\nada; '\"" }, TimeSpan.FromSeconds(60), CancellationToken.None);
            Check(AppParse.Str(Ps.Payload(u.Out), "s") == "Ñandú ü 漢字 a'; Remove-Item C:\\nada; '\"", "PowerShell: Unicode intacto y el parámetro llega como texto, no como orden");
            var er = Ps.Run("throw 'fallo de prueba'", null, TimeSpan.FromSeconds(60), CancellationToken.None);
            Check(Ps.Payload(er.Out) is null && er.Err.Contains("fallo de prueba"), "PowerShell: un error va aparte y no se toma como lista");
            Check(Ps.Run("Start-Sleep -Seconds 20", null, TimeSpan.FromSeconds(1), CancellationToken.None).TimedOut, "PowerShell: tiempo máximo respetado (se corta)");
            using (var cts = new CancellationTokenSource(300)) Check(Ps.Run("Start-Sleep -Seconds 20", null, TimeSpan.FromSeconds(60), cts.Token).Cancelled, "PowerShell: cancelar corta la lectura");

            // --- clasificación
            var inv = Inv(Pkg("Microsoft.BingNews"), Pkg("Microsoft.VCLibs.140.00", fw: true), Pkg("Microsoft.Windows.ShellExperienceHost", sig: "System"),
                Pkg("Microsoft.SecHealthUI", nr: true), Pkg("Microsoft.WindowsStore"), Pkg("Microsoft.Copilot"), Pkg("Contoso.Helper"),
                Pkg("Contoso.App", deps: ["Contoso.Helper"]), Pkg("Fabrikam.Thing", pub: "CN=1234", pubName: "Fabrikam"), Pkg("RealtekSemiconductorCorp.Audio", pubName: "Realtek Semiconductor Corp"),
                Pkg("Microsoft.Windows.Photos"), Pkg("Microsoft.HEVCVideoExtension"), Pkg("Microsoft.Advertising.Xaml"), Pkg("Microsoft.BingWeather", others: 2, prov: true),
                Pkg("Microsoft.GetHelp", me: false, prov: true), Pkg("Microsoft.Getstarted", pubName: "Microsoft Corporation"));
            var v = AppClassifier.Classify(inv).ToDictionary(x => x.Name);
            Check(v["Microsoft.VCLibs.140.00"].Category == "protected" && v["Microsoft.VCLibs.140.00"].MinMode == "never", "framework = protegido, no se puede marcar");
            Check(v["Microsoft.Windows.ShellExperienceHost"].Category == "protected", "app del sistema = protegida");
            Check(v["Microsoft.SecHealthUI"].Category == "protected", "no removible = protegida");
            Check(v["Microsoft.WindowsStore"].Category == "protected" && v["Microsoft.Advertising.Xaml"].Category == "protected", "Store y Advertising.Xaml protegidas por identidad");
            Check(v["Contoso.Helper"].Category == "protected" && v["Contoso.Helper"].Dependents.Length == 1, "otra app depende de ella = protegida");
            Check(v["Contoso.App"].Category == "unknown" && v["Contoso.App"].MinMode == "advanced" && !v["Contoso.App"].Recommended, "desconocida: no se recomienda, solo en Avanzado");
            Check(v["RealtekSemiconductorCorp.Audio"].Category == "oem" && !v["RealtekSemiconductorCorp.Audio"].Recommended, "software del fabricante = OEM, nunca recomendado");
            Check(v["Fabrikam.Thing"].Origin == "third" && v["Microsoft.Copilot"].Origin == "microsoft", "origen: Microsoft o terceros por el editor");
            Check(v["Microsoft.Copilot"].Category == "useful" && v["Microsoft.Copilot"].MinMode == "custom" && !v["Microsoft.Copilot"].Recommended, "Copilot: útil, no se marca sola");
            Check(v["Microsoft.Windows.Photos"].Category == "useful", "el catálogo manda sobre las reglas de prefijo (Fotos sigue siendo útil)");
            Check(v["Microsoft.HEVCVideoExtension"].Category == "protected", "extensiones de video e imagen protegidas");
            Check(v["Microsoft.BingNews"].Recommended && v["Microsoft.BingNews"].MinMode == "safe" && v["Microsoft.BingNews"].Recovery == "B", "Noticias: recomendada para revisar, recuperable desde la Store");
            Check(v["Microsoft.BingWeather"].Recovery == "A", "con otros usuarios o aprovisionada: se puede volver a registrar (nivel A)");
            Check(v["Microsoft.GetHelp"].Blocked is null && !v["Microsoft.GetHelp"].Me, "aprovisionada que no está en tu usuario: aparece, sin decir que está instalada");

            // --- plan
            var h = new FakeHost(inv); Host = h;
            string K(string n) => inv.Packages.First(p => p.Name == n).FullName;
            var p1 = AppPlanner.Build(h, inv, new("safe", [(K("Microsoft.BingNews"), false), (K("Microsoft.Copilot"), false), (K("Microsoft.VCLibs.140.00"), false), (K("Microsoft.BingWeather"), true), ("no.existe_1_x64__abc", false)]));
            var it = p1.Items.ToDictionary(i => i.Name);
            Check(it["Microsoft.BingNews"].Ok && it["Microsoft.BingNews"].Users == "Solo tu usuario", "modo seguro: una opcional entra, solo tu usuario");
            Check(!it["Microsoft.Copilot"].Ok, "modo seguro: una útil no entra (Copilot)");
            Check(!it["Microsoft.VCLibs.140.00"].Ok && p1.Summary.Protected == 1, "un framework nunca entra, aunque se pida");
            Check(!it["Microsoft.BingWeather"].Ok && it["Microsoft.BingWeather"].Keep.Contains("usuarios nuevos"), "modo seguro: no se quita el aprovisionamiento, y lo dice");
            Check(!it["no.existe_1_x64__abc"].Ok, "una app que ya no existe no entra");
            Check(p1.Summary is { Selected: 5, Ready: 1, Protected: 1, Unsupported: 3 }, "resumen: lo protegido no se cuenta otra vez como «no compatible»");
            Check(AppPlanner.Build(h, inv, new("custom", [(K("Microsoft.GetHelp"), false)])).Items[0] is { Ok: false, Action: "none", Users: "" },
                "no está en tu usuario y no se pide desaprovisionar: no se hace nada (ni se dice «tu usuario»)");
            Check(it["Microsoft.VCLibs.140.00"] is { Action: "none", Recovery: "-" }, "protegida: sin acción ni recuperación que mostrar");
            Check(AppPlanner.Build(h, inv, new("custom", [(K("Microsoft.Copilot"), false)])).Items[0].Ok, "modo personalizado: se puede elegir una útil");
            var adv = AppPlanner.Build(h, inv, new("advanced", [(K("Microsoft.BingWeather"), true), (K("Microsoft.GetHelp"), true), (K("Microsoft.SecHealthUI"), false)]));
            Check(adv.Items[0].Action == "remove-user+deprovision" && adv.Items[0].Admin && adv.Items[0].Recovery == "A" && adv.Summary.Deprovision == 2,
                "modo avanzado: quitar también el aprovisionamiento es aparte y pide administrador");
            Check(adv.Items[1].Action == "deprovision" && adv.Items[1].Users == "Usuarios nuevos", "solo aprovisionada: solo se desaprovisiona; otros usuarios no se tocan");
            Check(!adv.Items[2].Ok, "modo avanzado: lo protegido tampoco");
            h.Running[inv.Packages.First(p => p.Name == "Microsoft.Getstarted").Location!] = ["WinStore.App"];
            var pr = AppPlanner.Build(h, inv, new("safe", [(K("Microsoft.Getstarted"), false), (K("Microsoft.BingNews"), false)]));
            Check(pr.Items[0].Running.Length == 1 && pr.Summary.Running == 1 && pr.Items[0].Consequences.Any(c => c.Contains("ciérrala")), "app abierta: se avisa en la vista previa");

            // --- ejecución
            var req = new PlanRequest("safe", [(K("Microsoft.Getstarted"), false), (K("Microsoft.BingNews"), false)]);
            var r = AppPlanner.Execute(h, req, pr.Token, null, CancellationToken.None, backup: false);
            Check(!r.Changed && r.Removed == 1 && r.Skipped == 1 && h.Calls.SequenceEqual(["rm " + K("Microsoft.BingNews")]), "ejecución: la abierta no se toca (no se cierra nada), la otra sí");
            h.Calls.Clear();
            Check(AppPlanner.Execute(h, req, "otrahuella", null, CancellationToken.None, backup: false) is { Changed: true, NewPlan: not null } && h.Calls.Count == 0, "si el plan no coincide con lo confirmado no se ejecuta nada");
            var tok = AppPlanner.Build(h, inv, new("safe", [(K("Microsoft.BingNews"), false)])).Token;
            var gone = new FakeHost(inv with { Packages = inv.Packages.Where(p => p.Name != "Microsoft.BingNews").ToList() });
            Check(AppPlanner.Execute(gone, new("safe", [(K("Microsoft.BingNews"), false)]), tok, null, CancellationToken.None, backup: false) is { Changed: true } && gone.Calls.Count == 0,
                "la app desapareció entre la vista previa y el «Sí»: se vuelve a validar y no se ejecuta");
            var five = Inv(Pkg("Microsoft.BingNews"), Pkg("Microsoft.BingWeather"), Pkg("Microsoft.BingSearch"), Pkg("Microsoft.GetHelp"), Pkg("Microsoft.WindowsMaps"));
            var hf = new FakeHost(five); Host = hf;
            var rq = new PlanRequest("safe", five.Packages.Select(p => (p.FullName, false)).ToList());
            hf.Results[five.Packages[0].FullName] = ("failed", "falló");
            var r3 = AppPlanner.Execute(hf, rq, AppPlanner.Build(hf, five, rq).Token, null, CancellationToken.None, backup: false);
            Check(r3.Failed == 1 && r3.Removed == 4 && !r3.Stopped, "un fallo no para el resto (parcialmente completado)");
            foreach (var p in five.Packages.Take(3)) hf.Results[p.FullName] = ("failed", "falló");
            var r4 = AppPlanner.Execute(hf, rq, AppPlanner.Build(hf, five, rq).Token, null, CancellationToken.None, backup: false);
            Check(r4.Stopped && r4.Failed == 3 && r4.NotStarted == 2, "tres fallos seguidos: se para y lo demás queda sin empezar");
            hf.Results.Clear(); hf.Results[five.Packages[0].FullName] = ("failed", "Acceso denegado (0x80070005)");
            var r5 = AppPlanner.Execute(hf, rq, AppPlanner.Build(hf, five, rq).Token, null, CancellationToken.None, backup: false);
            Check(r5.Stopped && r5.NotStarted == 4, "error de permisos o de directiva: se para enseguida");
            hf.Results.Clear();
            using (var cts = new CancellationTokenSource())
            {
                hf.OnRemove = () => cts.Cancel();
                var r6 = AppPlanner.Execute(hf, rq, AppPlanner.Build(hf, five, rq).Token, null, cts.Token, backup: false);
                Check(r6.Removed == 1 && r6.NotStarted == 4, "cancelar: termina la que empezó y no inicia las demás");
                hf.OnRemove = null;
            }
            var hist1 = AppHistory.All();
            Check(hist1.Count > 0 && hist1.All(e => e.State != "pending") && hist1.Any(e => e.State == "failed" && e.Error == "falló"), "historial: cada operación con su resultado real");

            // --- recuperación
            var weather = inv.Packages.First(p => p.Name == "Microsoft.BingWeather");
            var e1 = hist1.First() with { Name = "Microsoft.BingWeather", Family = weather.Family, Kind = "appx" };
            var noMe = inv with { Packages = inv.Packages.Select(p => p.Name == "Microsoft.BingWeather" ? p with { Me = false } : p).ToList() };
            h.Files.Add(Path.Combine(weather.Location!, "AppxManifest.xml"));
            Check(AppRecovery.For(e1, noMe, h).Level == "A", "recuperable: los archivos siguen en Windows (nivel A)");
            h.Files.Clear();
            Check(AppRecovery.For(e1, noMe, h).Level == "B", "sin archivos locales: Microsoft Store (nivel B); Vigía no descarga nada");
            Check(AppRecovery.For(e1, noMe with { StoreBlocked = true }, h).Level == "C", "Store bloqueada por directiva: a mano (nivel C)");
            Check(AppRecovery.For(e1 with { Name = "Microsoft.SkypeApp" }, noMe, h).Level == "D", "app retirada por Microsoft: no recuperable (nivel D)");
            Check(AppRecovery.For(e1, inv, h).Level == "-", "si ya volvió a estar instalada, no se ofrece restaurar");
            Check(!AppRecovery.SafeManifest(@"C:\Program Files\WindowsApps\..\..\Windows\evil\AppxManifest.xml") && !AppRecovery.SafeManifest(@"C:\Users\x\AppxManifest.xml")
                && AppRecovery.SafeManifest(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"WindowsApps\A_1_x64__abc\AppxManifest.xml")), "solo se registran manifiestos de paquetes de Windows");
            Check(!AppRecovery.OpenStore("nada; calc"), "la ficha de la Store solo se abre con un Package Family Name válido");

            // --- programas clásicos
            object? G(Dictionary<string, object?> d, string n) => d.GetValueOrDefault(n);
            Check(Win32Reader.Entry("{11111111-2222-3333-4444-555555555555}", "machine", "x64", n => G(new() { ["DisplayName"] = "Juego X", ["WindowsInstaller"] = 1, ["UninstallString"] = "MsiExec.exe /X{...}" }, n))?.MsiCode is not null, "MSI con código de producto");
            Check(Win32Reader.Entry("k", "machine", "x64", n => G(new() { ["DisplayName"] = "Algo", ["SystemComponent"] = 1 }, n)) is null
                && Win32Reader.Entry("k", "machine", "x64", n => G(new() { ["DisplayName"] = "KB1", ["ParentKeyName"] = "Office" }, n)) is null
                && Win32Reader.Entry("k", "machine", "x64", n => G(new() { ["Publisher"] = "x" }, n)) is null, "componentes ocultos, actualizaciones y entradas sin nombre no salen");
            var wv = AppClassifier.ClassifyWin32([new("a", "Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Corporation", "14", "machine", "x64", null, true, null, 0),
                new("b", "Vigía", "JcperezDev", "0.20", "user", "", null, true, null, 0), new("c", "Programa Cualquiera", "Alguien", "1", "user", "", null, true, null, 0),
                new("d", "Sin desinstalador", "Alguien", "1", "user", "", null, false, null, 0), new("e", "Easy Anti-Cheat", "Epic", "1", "machine", "x64", null, true, null, 0)]);
            Check(wv[0].MinMode == "never" && wv[1].MinMode == "never" && wv[4].MinMode == "never", "runtimes, antitrampas y el propio Vigía: protegidos");
            Check(wv[2].MinMode == "advanced" && wv[3].MinMode == "never", "un programa normal solo en Avanzado; sin desinstalador, no");
            var w32inv = Inv(Pkg("Microsoft.BingNews")) with { Win32 = [new("c", "Programa Cualquiera", "Alguien", "1", "user", "", null, true, null, 0)] };
            Check(!AppPlanner.Build(h, w32inv, new("custom", [("c", false)])).Items[0].Ok && AppPlanner.Build(h, w32inv, new("advanced", [("c", false)])).Items[0] is { Ok: true, Action: "win32-settings" },
                "programa clásico: solo en Avanzado y por Configuración de Windows (nunca la cadena del registro)");

            // --- modo simple (Debloat.Remove = modo seguro)
            var hs = new FakeHost(Inv(Pkg("Microsoft.Copilot"), Pkg("Microsoft.VCLibs.140.00", fw: true), Pkg("Microsoft.BingNews"), Pkg("Microsoft.BingWeather", prov: true))); Host = hs; Forget();
            var rs = Remove(["Microsoft.Copilot", "Microsoft.VCLibs.140.00", "Microsoft.BingNews", "Microsoft.BingWeather"]);
            Check(!rs.Single(x => x.Id == "Microsoft.Copilot").Ok && !rs.Single(x => x.Id == "Microsoft.VCLibs.140.00").Ok, "modo simple: ni útiles ni frameworks, aunque lleguen en la lista");
            Check(rs.Single(x => x.Id == "Microsoft.BingNews").Ok && !hs.Calls.Any(c => c.StartsWith("deprov")), "modo simple: quita solo de tu usuario, nunca el aprovisionamiento");
            Check(Remove([]).Count == 0 && hs.Calls.Count(c => c.StartsWith("rm")) == 2, "modo simple: lista vacía = no se quita nada");
            hs.Inv = hs.Inv with { Ok = false, Error = "x" }; Forget();
            Check(Remove(["Microsoft.BingNews"]) is [{ Ok: false }], "si no se pudo leer el inventario no se quita nada (no es «lista vacía»)");
            hs.Inv = hs.Inv with { Ok = true }; Forget();
            Check(List().All(a => AppCatalog.Find(a.Id)?.Category is "optional" or "useful"), "lista corta (modo simple e ISO): nunca protegidas ni del fabricante");
        }
        catch (Exception ex) { Check(false, "excepción en las pruebas de apps: " + ex); }
        finally
        {
            Host = prevHost; Forget(); AppHistory.DirOverride = null; BackupEnabled = true;
            try { if (Directory.Exists(hist)) Directory.Delete(hist, true); } catch { }
        }
        return string.Join(Environment.NewLine, log);
    }
}
