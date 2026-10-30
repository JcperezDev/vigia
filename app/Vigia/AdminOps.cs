using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>
/// Lo único que hace Vigía con permisos de administrador. Lista cerrada: cada nombre tiene su función y sus
/// parámetros se comprueban aquí (otra vez, aunque la interfaz ya los haya mirado). Nada de aquí acepta una orden,
/// una ruta de programa, una clave de registro ni un texto para PowerShell que venga de fuera: solo identificadores
/// que luego se buscan en los catálogos, el inventario o la lista que ve el usuario.
///
/// Si Vigía ya corre como administrador (UAC apagado, o abierta así a propósito) se ejecuta en el mismo proceso;
/// si no, en el ayudante (<see cref="Elevation.Broker"/>).
/// </summary>
public static class AdminOps
{
    /// <summary>Necesitan administrador. Ver docs/PRIVILEGES.md para la clasificación completa.</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "cleanRun", "secEnable", "secScan", "optRun", "optAnalyze", "optCancel", "memFree", "memAuto",
        "startupOffAll", "startupRemove", "startupSet", "autoStartRepair",
        "tweaksApply", "tweaksRevert", "undoRetired", "bloatRemove",
        "appsScan", "appsPreview", "appsRun", "appsCancel", "appsRestore",
        "simpleRun", "procClose", "backupRestore", "sugFix", "biosReboot",
        "isoBuild", "isoCancel", "usbList", "usbWrite", "fpsStart", "fpsCancel",
        "pawnInstall", "restorePoint", "telemetry", "ping",
        "labHold",   // solo VigiaLab: prueba de vida del ayudante (espera sin tocar nada); el ayudante la rechaza fuera
        // lecturas que sin permisos salen incompletas (arranque, TPM, SMART) o no se pueden hacer (montar la ISO)
        "healthScan", "diagRun", "secGet", "isoInspect", "cleanScan",
    };

    /// <summary>
    /// Estas solo van al ayudante si ya está abierto; si no, se hacen aquí sin permisos (lectura, o cancelar algo
    /// que no está corriendo). Así abrir una vista nunca saca el aviso de UAC.
    /// </summary>
    public static readonly IReadOnlySet<string> Soft = new HashSet<string>(StringComparer.Ordinal)
    {
        "appsScan", "appsPreview", "telemetry", "optCancel", "appsCancel", "isoCancel", "fpsCancel", "memAuto", "undoRetired",
        "healthScan", "diagRun", "secGet", "cleanScan",
    };

    static readonly Regex IdRx = new(@"^[a-z0-9][a-z0-9\-]{0,63}$", RegexOptions.CultureInvariant);
    static readonly Regex CleanRx = new(@"^[a-z0-9][a-z0-9_\-]{0,63}$", RegexOptions.CultureInvariant);
    static readonly Regex LetterRx = new(@"^[A-Z]:$", RegexOptions.CultureInvariant);
    static readonly Regex HistRx = new(@"^[0-9a-f]{12}$", RegexOptions.CultureInvariant);
    static readonly Regex BackupRx = new(@"^[0-9a-z\-]{1,80}$", RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------- lectura estricta de parámetros
    static string S(JsonObject m, string k, int max)
    {
        if (m[k] is not JsonValue v || !v.TryGetValue<string>(out var s) || s.Length == 0 || s.Length > max || s.Any(char.IsControl))
            throw new ArgumentException(k);
        return s;
    }
    static string S(JsonObject m, string k, Regex rx) => S(m, k, 200) is var s && rx.IsMatch(s) ? s : throw new ArgumentException(k);
    static bool B(JsonObject m, string k, bool? def = null) =>
        m[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : def ?? throw new ArgumentException(k);
    static int I(JsonObject m, string k, int min, int max, int? def = null) =>
        m[k] is JsonValue v && v.TryGetValue<int>(out var i) ? (i >= min && i <= max ? i : throw new ArgumentException(k)) : def ?? throw new ArgumentException(k);
    static List<string> L(JsonObject m, string k, int maxCount, Regex rx)
    {
        if (m[k] is not JsonArray a || a.Count > maxCount) throw new ArgumentException(k);
        return a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) && rx.IsMatch(s) ? s : throw new ArgumentException(k)).Distinct().ToList();
    }

    /// <summary>Claves del inventario de apps (se buscan en el inventario; nunca van a PowerShell).</summary>
    internal static List<string> AppKeys(JsonNode? n) =>
        (n as JsonArray ?? []).Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
            .Where(s => s is { Length: > 0 and <= 300 } && !s.Any(char.IsControl))
            .Select(s => s!).Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToList();

    internal static PlanRequest AppsRequest(JsonObject msg)
    {
        var mode = (string?)msg["mode"] is { } m && AppPlanner.Modes.Contains(m) ? m : "safe";
        var deprov = new HashSet<string>(AppKeys(msg["deprovision"]), StringComparer.OrdinalIgnoreCase);
        return new PlanRequest(mode, AppKeys(msg["keys"]).Select(k => (k, mode == "advanced" && deprov.Contains(k))).ToList());
    }

    static Telemetry? _tel;
    static readonly object TelGate = new();

    /// <summary>Ejecuta una operación de la lista. <paramref name="emit"/> manda a la interfaz (tipo, contenido).</summary>
    public static async Task Run(string cmd, JsonObject msg, Action<string, object?> emit)
    {
        if (!Names.Contains(cmd)) throw new InvalidOperationException("Esa operación no está permitida.");
        switch (cmd)
        {
            case "labHold":
                // Prueba de laboratorio del ciclo de vida del ayudante: una tarea larga que no cambia nada.
                // La comprobación va aquí, en el proceso elevado: fuera de la VM de pruebas no hace nada.
                if (!LabGuard.Allowed) throw new InvalidOperationException(LabGuard.Refusal);
                {
                    var secs = I(msg, "seconds", 30, 1200);
                    var t0 = DateTime.UtcNow;
                    for (var done = 0; done < secs; done += 30)
                    {
                        emit("labHold", new { pid = Environment.ProcessId, elevated = Elevation.IsAdmin, elapsed = (int)(DateTime.UtcNow - t0).TotalSeconds });
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, secs - done)));
                    }
                    emit("labHold", new { pid = Environment.ProcessId, elevated = Elevation.IsAdmin, elapsed = (int)(DateTime.UtcNow - t0).TotalSeconds, done = true });
                }
                break;
            case "ping":
                emit("pong", new { elevated = Elevation.IsAdmin, broker = Elevation.InBroker, pid = Environment.ProcessId });
                break;
            case "telemetry":
                Telemetry t;
                lock (TelGate) t = _tel ??= new Telemetry();
                emit("telemetry", await Task.Run(t.Read));
                break;
            case "healthScan":
                Telemetry ht;
                lock (TelGate) ht = _tel ??= new Telemetry();
                emit("healthScan", await Task.Run(() => Health.Scan(ht)));
                break;
            case "cleanScan":
                emit("cleanScan", await Task.Run(Cleanup.Scan));
                break;
            case "diagRun":
                emit("diagRun", await Task.Run(() => Diagnostics.Run(7)));
                break;
            case "secGet":
                emit("sec", await Task.Run(Security.Status));
                emit("secPos", await Task.Run(Security.Posture));
                break;
            case "isoInspect":
                // solo se monta (de solo lectura) un .iso local que el usuario eligió en el diálogo; nunca se ejecuta nada de dentro
                var ipath = S(msg, "path", 1024);
                if (PathGuard.IsoProblem(ipath, mustExist: true) is { } ie0) throw new InvalidOperationException(ie0);
                emit("isoSource", await Task.Run(() => IsoBuild.Inspect(ipath)));
                break;
            case "cleanRun":
                var cids = L(msg, "ids", 60, CleanRx);
                emit("cleanRun", await Task.Run(() => Cleanup.Run(cids)));
                break;
            case "secEnable":
                emit("secDone", await Task.Run(() => Security.Enable(p => emit("secProgress", p))));
                break;
            case "secScan":
                emit("secDone", await Task.Run(() => Security.UpdateAndScan(p => emit("secProgress", p))));
                break;
            case "optRun":
                var letter = S(msg, "letter", LetterRx);
                var ores = await Task.Run(() => Optimize.RunAsync(letter, p => emit("optProgress", p)));
                emit("optDone", ores);
                emit("optStatus", await Task.Run(() => Optimize.Status(ores.Ok, letter)));
                break;
            case "optAnalyze":
                emit("optStatus", await Task.Run(() => Optimize.Status(true)));
                break;
            case "optCancel": Optimize.Cancel(); break;
            case "memFree":
                // mismas opciones que la interfaz; lo que no venga, la opción segura
                var mo = msg["opts"] is JsonObject oo
                    ? new MemOptions(B(oo, "workingSets", false), B(oo, "modified", false), B(oo, "standby", true), B(oo, "standbyLowOnly", true), B(oo, "fileCache", false), B(oo, "combine", false))
                    : MemOptions.Safe;
                emit("memFreed", await Task.Run(() => Memory.Free(mo)));
                break;
            case "memAuto":
                var ms = new Settings { MemAuto = B(msg, "on"), MemAutoThreshold = I(msg, "threshold", 50, 98), MemAutoIntervalMin = I(msg, "interval", 1, 120) };
                if (await Task.Run(() => Memory.AutoTick(ms)) is { } freed) emit("memFreed", freed);
                break;
            case "startupOffAll":
                emit("startupAll", await Task.Run(Startup.OffAll));
                break;
            case "startupRemove":
                var rid = S(msg, "id", 1024);
                emit("startupDone", await Task.Run(() => Startup.Remove(rid)));
                break;
            case "startupSet":
                var sid = S(msg, "id", 1024); var son = B(msg, "on");
                emit("startupDone", await Task.Run(() => Startup.Set(sid, son)));
                break;
            case "autoStartRepair":
                emit("autoStartRepaired", await Task.Run(AutoStart.Repair));
                break;
            case "tweaksApply":
            case "tweaksRevert":
                var tids = L(msg, "ids", 300, IdRx);
                emit("tweaksDone", await Task.Run(() => cmd == "tweaksApply" ? Tweaks.Apply(tids) : Tweaks.Revert(tids)));
                break;
            case "undoRetired":
                await Task.Run(Tweaks.UndoRetired);
                break;
            case "bloatRemove":
                var bids = AppKeys(msg["ids"]);
                emit("bloatDone", await Task.Run(() => Debloat.Remove(bids)));
                break;
            case "appsScan":
                var force = B(msg, "force", false);
                emit("apps", await Task.Run(() => Debloat.View(force)));
                break;
            case "appsPreview":
                var preq = AppsRequest(msg);
                emit("appsPlan", await Task.Run(() => Debloat.Preview(preq)));
                break;
            case "appsRun":
                var areq = AppsRequest(msg);
                var atok = S(msg, "token", 64);
                if (areq.Items.Any(i => i.Deprovision) || areq.Mode != "safe")
                    await Task.Run(() => RestorePoint.Create("Vigía: antes de quitar apps"));
                emit("appsDone", await Task.Run(() => Debloat.Run(areq, atok, st => emit("appsStep", st))));
                emit("apps", await Task.Run(() => Debloat.View(force: true)));
                break;
            case "appsCancel": Debloat.Cancel(); break;
            case "appsRestore":
                var hid = S(msg, "id", HistRx);
                var (rok, rmsg) = await Task.Run(() => Debloat.Restore(hid));
                emit("appsRestored", new { ok = rok, message = rmsg });
                emit("apps", await Task.Run(() => Debloat.View(force: true)));
                break;
            case "simpleRun":
                var ans = msg["answers"].Deserialize<Simple.Answers>(Program.Json) ?? throw new ArgumentException("answers");
                emit("simpleDone", await Task.Run(() => Simple.Run(Simple.Build(ans), st => emit("simpleStep", st), on => emit("_memAuto", new { on }))));
                break;
            case "procClose":
                var exe = S(msg, "exe", 260);
                // en el ayudante no hay lista vista todavía: se mira ahora y se cierra solo si sale como cerrable
                if (Elevation.InBroker) await Task.Run(Procs.Sample);
                emit("procDone", await Task.Run(() => Procs.Close(exe)));
                break;
            case "backupRestore":
                var bkid = S(msg, "id", BackupRx);
                emit("backupDone", await Task.Run(() => Backups.Restore(bkid)));
                break;
            case "sugFix":
                var fix = S(msg, "fix", 200);
                if (fix.StartsWith("tweak:"))
                {
                    var tid = fix[6..];
                    if (!IdRx.IsMatch(tid)) throw new ArgumentException("fix");
                    var sb = await Task.Run(() => Tweaks.Apply(new[] { tid }));
                    var sr = sb.Results.FirstOrDefault();
                    emit("sugDone", new Rig.HzDone(sb.Ok, sb.Ok ? (sb.NeedsReboot ? "Listo. Se nota después de reiniciar." : "Listo.") : $"No se pudo: {sr?.Error}", false));
                }
                else
                {
                    var (sok, smsg) = await Task.Run(() => Suggestions.Fix(fix));
                    emit("sugDone", new Rig.HzDone(sok, smsg, false));
                }
                break;
            case "biosReboot":
                emit("biosDone", await Task.Run(Bios.RebootToFirmware));
                break;
            case "isoBuild":
                var iopt = msg["options"].Deserialize<IsoBuild.Options>(Program.Json) ?? throw new ArgumentException("options");
                if (PathGuard.IsoProblem(iopt.Source, mustExist: true) is { } ie1) throw new InvalidOperationException(ie1);
                if (PathGuard.IsoProblem(iopt.Output, mustExist: false) is { } ie2) throw new InvalidOperationException(ie2);
                string ilast = "";
                emit("isoDone", await Task.Run(() => IsoBuild.Build(iopt, pr =>
                {
                    var key = $"{pr.Num}{pr.Step}{(int)(pr.Pct ?? -1)}";
                    if (key == ilast) return; ilast = key;
                    emit("isoProgress", pr);
                })));
                break;
            case "isoCancel": IsoBuild.Cancel(); break;
            case "usbList":
                emit("usbList", await Task.Run(IsoBuild.UsbList));
                break;
            case "usbWrite":
                var uiso = S(msg, "iso", 1024); var unum = I(msg, "number", 0, 128); var userial = S(msg, "serial", 200);
                if (PathGuard.IsoProblem(uiso, mustExist: true) is { } ue) throw new InvalidOperationException(ue);
                string ulast = "";
                emit("usbDone", await Task.Run(() => IsoBuild.ToUsb(uiso, unum, userial, pr =>
                {
                    var key = $"{pr.Num}{pr.Step}{(int)(pr.Pct ?? -1)}";
                    if (key == ulast) return; ulast = key;
                    emit("usbProgress", pr);
                })));
                break;
            case "fpsStart":
                var fsec = I(msg, "seconds", 30, 1800, 180);
                var flabel = msg["label"] is JsonValue lv && lv.TryGetValue<string>(out var ls) ? new string(ls.Where(c => !char.IsControl(c)).Take(80).ToArray()) : "";
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(Fps.WatchState st) { emit("fpsState", st); if (!st.Active) finished.TrySetResult(); }
                Fps.Changed += Changed;
                try
                {
                    if (!Fps.Start(fsec, flabel)) { emit("fpsState", Fps.State); break; }
                    await finished.Task.WaitAsync(TimeSpan.FromHours(5));
                }
                catch (TimeoutException) { Fps.Cancel(); }
                finally { Fps.Changed -= Changed; }
                emit("_fpsFinished", new { });
                break;
            case "fpsCancel": Fps.Cancel(); break;
            case "pawnInstall":
                string? perr = null;
                var pok = await Task.Run(() => Pawn.InstallIfMissing(out perr, asked: true));
                emit("_pawn", new { ok = pok || Pawn.Installed(), error = perr });
                break;
            case "restorePoint":
                emit("toolDone", await Task.Run(() => Toolbox.Open("restorepoint")));
                break;
        }
    }

    /// <summary>Lo que se le dice a la interfaz si la operación no llegó a hacerse (sin permiso, ayudante caído…),
    /// con la forma que espera cada pantalla para no quedarse «trabajando» para siempre.</summary>
    public static (string Type, object Payload)? Failed(string cmd, JsonObject msg, string message) => cmd switch
    {
        "tweaksApply" or "tweaksRevert" => ("tweaksDone", new TweakBatch(false, null, [new TweakResult("—", false, "error", message)], false)),
        "simpleRun" => ("simpleDone", new Simple.Report(false, 0, 0, 1, 0, 0, 0, false, [message])),
        "appsRun" => ("appsDone", new ExecResult(false, null, [], 0, 0, 0, 0, true, message)),
        "appsRestore" => ("appsRestored", new { ok = false, message }),
        "bloatRemove" => ("bloatDone", new[] { new { id = "—", ok = false, error = message } }),
        "cleanRun" => ("cleanRun", new CleanResult(0, 0, 0)),
        "startupSet" or "startupRemove" => ("startupDone", new StartDone("", false, message)),
        "startupOffAll" => ("startupAll", new Startup.AllDone(0, 0, [], [message])),
        "backupRestore" => ("backupDone", new BackupDone((string?)msg["id"] ?? "", false, message, [])),
        "secEnable" or "secScan" => ("secDone", new Security.Done(false, message, false)),
        "optRun" => ("optDone", new OptResult((string?)msg["letter"] ?? "", false, -1, 0, message)),
        "procClose" => ("procDone", new Procs.Done(false, message)),
        "sugFix" => ("sugDone", new Rig.HzDone(false, message, false)),
        "biosReboot" => ("biosDone", new { ok = false, message }),
        "isoInspect" => ("isoSource", new IsoBuild.Source((string?)msg["path"] ?? "", 0, [], message)),
        "isoBuild" => ("isoDone", new IsoBuild.Done(false, message, null, 0, 0)),
        "usbWrite" => ("usbDone", new IsoBuild.UsbDone(false, message, null, 0)),
        "usbList" => ("usbList", new { error = message }),   // la página de ISO enseña el motivo (antes: lista vacía sin explicación)
        "restorePoint" => ("toolDone", new ToolDone("restorepoint", false, message)),
        "pawnInstall" => ("pawnState", new { ok = false, waiting = false, error = message }),
        _ => null,
    };

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        Check(!Names.Contains("powershell") && !Names.Contains("run") && !Names.Contains("exec") && !Names.Contains("openUrl"), "ninguna operación genérica de ejecutar");
        Check(Soft.All(Names.Contains), "las suaves también están en la lista cerrada");
        string? Err(string cmd, string json)
        {
            try { Run(cmd, JsonNode.Parse(json)!.AsObject(), (_, _) => { }).GetAwaiter().GetResult(); return null; }
            catch (Exception e) { return e.GetType().Name; }
        }
        Check(Err("cmd.exe /c calc", "{}") == nameof(InvalidOperationException), "un comando fuera de la lista se rechaza");
        Check(Err("tweaksApply", "{\"ids\":[\"x; rm -rf\"]}") == nameof(ArgumentException), "ajuste con id raro: rechazado antes de tocar nada");
        Check(Err("tweaksApply", "{\"ids\":\"todo\"}") == nameof(ArgumentException), "ajustes: la lista tiene que ser una lista");
        Check(Err("optRun", "{\"letter\":\"C:\\\\Windows\"}") == nameof(ArgumentException), "optimizar: solo una letra de unidad");
        Check(Err("backupRestore", "{\"id\":\"..\\\\..\\\\x\"}") == nameof(ArgumentException), "copias: el id no puede ser una ruta");
        Check(Err("appsRestore", "{\"id\":\"zz\"}") == nameof(ArgumentException), "apps: el id del historial tiene forma fija");
        Check(Err("startupSet", "{\"id\":\"a\\u0000b\",\"on\":true}") == nameof(ArgumentException), "inicio: sin caracteres de control");
        Check(Err("memAuto", "{\"on\":true,\"threshold\":5,\"interval\":1}") == nameof(ArgumentException), "memoria automática: umbral fuera de rango");
        Check(Err("usbWrite", "{\"iso\":\"C:\\\\Windows\\\\System32\\\\config\\\\SAM\",\"number\":1,\"serial\":\"x\"}") == nameof(InvalidOperationException), "USB: solo desde un archivo .iso");
        Check(Err("isoBuild", "{\"options\":{\"source\":\"C:\\\\no\\\\existe.iso\",\"output\":\"C:\\\\Windows\\\\x.iso\",\"index\":1,\"apps\":[],\"noAds\":false,\"localAccount\":false,\"skipChecks\":false}}") == nameof(InvalidOperationException),
            "ISO: no se escribe dentro de Windows ni se lee algo que no existe");
        Check(Err("ping", "{}") is null, "ping responde");
        Check(Err("isoInspect", "{\"path\":\"C:\\\\Windows\\\\System32\\\\config\\\\SAM\"}") == nameof(InvalidOperationException)
            && Err("isoInspect", "{\"path\":\"\\\\\\\\servidor\\\\x.iso\"}") == nameof(InvalidOperationException), "ISO: solo se monta un .iso local elegido por el usuario");
        Check(Failed("isoInspect", JsonNode.Parse("{\"path\":\"C:\\\\a.iso\"}")!.AsObject(), "x") is { Type: "isoSource" }, "ISO: si no se pudo abrir, la pantalla deja de esperar");
        Check(Failed("tweaksApply", new JsonObject(), "x") is { Type: "tweaksDone" } && Failed("appsRun", new JsonObject(), "x") is { Type: "appsDone" }, "si no se pudo, la pantalla recibe su «terminado»");
        return string.Join(Environment.NewLine, log);
    }
}

/// <summary>Rutas que el ayudante acepta para la ISO y el USB: archivos .iso fuera de las carpetas del sistema y de otros usuarios, sin enlaces.</summary>
public static class PathGuard
{
    static IEnumerable<string> Forbidden()
    {
        foreach (var f in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
            if (Environment.GetFolderPath(f) is { Length: > 0 } p) yield return p.TrimEnd('\\') + '\\';
    }

    /// <summary>null si vale; si no, el motivo para el usuario.</summary>
    public static string? IsoProblem(string? path, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Any(char.IsControl)) return "Ruta no válida.";
        string full;
        try { full = Path.GetFullPath(path); } catch { return "Ruta no válida."; }
        if (!Path.IsPathFullyQualified(path) || full.StartsWith(@"\\", StringComparison.Ordinal)) return "Usa una ruta local completa (C:\\…).";
        if (!full.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return "Solo archivos .iso.";
        if (Forbidden().Any(f => full.StartsWith(f, StringComparison.OrdinalIgnoreCase))) return "Esa carpeta es de Windows o de programas; elige otra.";
        // dentro de C:\Users solo la carpeta de tu cuenta
        var users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var mine = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\') + '\\';
        if (users is { Length: > 0 } && full.StartsWith(users.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase) && !full.StartsWith(mine, StringComparison.OrdinalIgnoreCase))
            return "Esa carpeta es de otra cuenta.";
        if (mustExist && !File.Exists(full)) return "Ese archivo no existe.";
        try
        {
            // ni el archivo ni ninguna carpeta del camino puede ser un enlace
            for (var d = new DirectoryInfo(Path.GetDirectoryName(full)!); d is not null; d = d.Parent)
                if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) return "Esa carpeta es un enlace a otro sitio; elige una carpeta normal.";
            if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return "Ese archivo es un enlace; elige el archivo de verdad.";
        }
        catch { return "No pude revisar esa carpeta."; }
        return null;
    }
}
