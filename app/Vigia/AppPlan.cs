using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Vigia;

/// <summary>Lo que pidió la interfaz: claves del inventario y, por cada una, si además se quita el aprovisionamiento.</summary>
public sealed record PlanRequest(string Mode, List<(string Key, bool Deprovision)> Items);

/// <summary>Una app del plan. Ok = false: no se hará, y Problem dice por qué.</summary>
public sealed record PlanItem(
    string Key, string Kind, string Name, string Display, string Action, bool Ok, string? Problem, string Keep,
    string[] Consequences, string Recovery, string RecoveryText, bool Admin, string[] Running, string Users, string[] Dependents,
    string? Full, string? ProvName, string? Location, string Family, string Version, string Publisher, string Arch, bool Provisioned, string? Msi);

public sealed record Plan(string Token, string Mode, List<PlanItem> Items, PlanSummary Summary, bool Changed = false);

public sealed record PlanSummary(int Selected, int Ready, int Protected, int Unsupported, int Admin, string Users, int Deprovision,
    int NoAutoRestore, int Running, string Restart);

public sealed record ExecStep(string Key, string Display, string State, string? Error, int Index, int Total);
public sealed record ExecResult(bool Changed, Plan? NewPlan, List<ExecStep> Steps, int Removed, int Failed, int Skipped, int NotStarted, bool Stopped, string? StopReason);

public static class AppPlanner
{
    public static readonly string[] Modes = ["safe", "custom", "advanced"];
    static int Level(string m) => Array.IndexOf(Modes, m);

    /// <summary>Arma el plan sobre un inventario recién leído. No ejecuta nada.</summary>
    public static Plan Build(IAppHost host, AppInventoryData inv, PlanRequest req, bool lab = false)
    {
        var mode = Modes.Contains(req.Mode) ? req.Mode : "safe";
        var views = AppClassifier.Classify(inv, null, lab).ToDictionary(v => v.Key, StringComparer.OrdinalIgnoreCase);
        var w32 = AppClassifier.ClassifyWin32(inv.Win32).ToDictionary(v => v.Key, StringComparer.Ordinal);
        var pkgs = inv.Packages.GroupBy(p => p.FullName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var items = new List<PlanItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prot = 0;
        foreach (var (key, deprov) in req.Items)
        {
            if (!seen.Add(key)) continue;
            if (w32.TryGetValue(key, out var w)) { if (w.Category == "protected") prot++; items.Add(Win32Item(w, mode, inv)); continue; }
            if (!views.TryGetValue(key, out var v) || !pkgs.TryGetValue(key, out var p))
            {
                items.Add(new PlanItem(key, "appx", key, key, "none", false, "Ya no está en el equipo: el inventario cambió.", "", [], "C", "", false, [], "", [], null, null, null, "", "", "", "", false, null));
                continue;
            }
            string? problem = null;
            if (v.MinMode == "never") problem = v.Why;
            else if (Level(v.MinMode) > Level(mode)) problem = v.MinMode == "custom" ? "Solo en modo Personalizado o Avanzado." : "Solo en modo Avanzado.";
            else if (v.Blocked is not null) problem = v.Blocked;
            var doDeprov = deprov && p.Provisioned && p.ProvName is not null;
            if (deprov && mode != "advanced") { problem ??= "Quitar el aprovisionamiento solo se puede en modo Avanzado."; doDeprov = false; }
            var doUser = p.Me;
            if (!doUser && !doDeprov) problem ??= "No está en tu usuario. Solo se puede quitar el aprovisionamiento, en modo Avanzado.";
            // frameworks y no removibles: aunque alguien fuerce la clave, nunca entran
            if (p.Framework || p.NonRemovable || p.Signature.Equals("System", StringComparison.OrdinalIgnoreCase)) problem = v.Why;
            var isProt = v.Category == "protected" || v.MinMode == "never";
            if (isProt) prot++;
            var running = problem is null && doUser ? host.RunningUnder(p.Location).ToArray() : [];
            var action = doUser && doDeprov ? "remove-user+deprovision" : doDeprov ? "deprovision" : doUser ? "remove-user" : "none";
            var (rec, recText) = AppRecovery.Estimate(p, AppCatalog.Find(p.Name), inv.StoreBlocked, doDeprov);
            var cons = new List<string>();
            if (isProt) { doUser = false; doDeprov = false; action = "none"; (rec, recText) = ("-", ""); }
            if (doUser) cons.Add("Se borran sus datos locales en tu usuario (ajustes, archivos sin sincronizar).");
            if (doDeprov) cons.Add("Los usuarios que se creen desde ahora ya no la tendrán.");
            if (v.Dependents.Length > 0) cons.Add($"Otras apps dependen de ella: {string.Join(", ", v.Dependents)}.");
            if (running.Length > 0) cons.Add("Está abierta: ciérrala antes. Vigía no la cierra por ti.");
            var keep = (p.Others, doDeprov, p.Provisioned) switch
            {
                ( > 0, _, _) when !doDeprov && p.Provisioned => $"Se queda para los otros {p.Others} usuarios y para los usuarios nuevos.",
                ( > 0, _, _) => $"Se queda para los otros {p.Others} usuarios.",
                (_, false, true) => "Se queda aprovisionada: los usuarios nuevos la tendrán.",
                _ => "",
            };
            var users = doUser && doDeprov ? "Tu usuario y los usuarios nuevos" : doDeprov ? "Usuarios nuevos" : doUser ? "Solo tu usuario" : "";
            items.Add(new PlanItem(key, "appx", p.Name, v.Display, action, problem is null, problem, keep, cons.ToArray(), rec, recText,
                Admin: doDeprov, running, users, v.Dependents, p.FullName, doDeprov ? p.ProvName : null, p.Location, p.Family, p.Version,
                v.Publisher, p.Arch, p.Provisioned, null));
        }
        return Finish(mode, items, prot);
    }

    static PlanItem Win32Item(AppView w, string mode, AppInventoryData inv)
    {
        string? problem = w.MinMode == "never" ? (w.Blocked ?? w.Why) : mode != "advanced" ? "Los programas clásicos solo en modo Avanzado." : null;
        var src = inv.Win32.First(x => x.Key == w.Key);
        return new PlanItem(w.Key, "win32", w.Name, w.Display, w.Msi ? "win32-msi" : "win32-settings", problem is null, problem, "",
            [w.Msi ? "Se abre el desinstalador de Windows Installer: confirma tú en su ventana." : "Se abre Configuración > Apps instaladas: búscalo y desinstálalo desde ahí.",
             "Sus archivos de usuario pueden quedarse; depende del programa."],
            "C", w.RecoveryText, src.Scope == "machine", [], src.Scope == "machine" ? "Todo el equipo" : "Solo tu usuario", [], null, null, src.Location, "",
            w.Version, w.Publisher, w.Arch, false, src.MsiCode);
    }

    static Plan Finish(string mode, List<PlanItem> items, int prot)
    {
        var ok = items.Where(i => i.Ok).ToList();
        var users = ok.Any(i => i.Action.Contains("deprovision")) ? (ok.Any(i => i.Action.StartsWith("remove-user")) ? "Tu usuario y los usuarios nuevos" : "Usuarios nuevos")
            : ok.Any(i => i.Users == "Todo el equipo") ? "Todo el equipo" : ok.Count > 0 ? "Solo tu usuario" : "Nadie";
        var sum = new PlanSummary(items.Count, ok.Count(i => i.Running.Length == 0), prot,
            Math.Max(0, items.Count(i => !i.Ok) - prot), ok.Count(i => i.Admin), users, ok.Count(i => i.Action.Contains("deprovision")),
            ok.Count(i => i.Recovery is "C" or "D"), ok.Count(i => i.Running.Length > 0),
            ok.Any(i => i.Kind == "win32") ? "Algunos desinstaladores de programas piden reiniciar al terminar." : "No hace falta reiniciar.");
        return new Plan(TokenOf(mode, items), mode, items, sum);
    }

    /// <summary>Huella del plan: si entre la vista previa y el «Sí» cambia cualquier app del plan, la huella no coincide.</summary>
    public static string TokenOf(string mode, IEnumerable<PlanItem> items)
    {
        var sb = new StringBuilder(mode);
        foreach (var i in items.OrderBy(i => i.Key, StringComparer.Ordinal))
            sb.Append('|').Append(i.Key).Append(';').Append(i.Action).Append(';').Append(i.Ok).Append(';').Append(i.Version).Append(';').Append(i.Provisioned).Append(';').Append(i.Running.Length);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    static readonly string[] StopCodes = ["0x80070005", "0x80073D19", "0x80073CFA"];

    /// <summary>Ejecuta el plan confirmado. Vuelve a leer el inventario: si cambió, no hace nada y devuelve el plan nuevo.
    /// Una app a la vez, con copia y registro antes; se para con errores de permisos o de directiva, o tras 3 fallos seguidos.</summary>
    public static ExecResult Execute(IAppHost host, PlanRequest req, string token, Action<ExecStep>? progress, CancellationToken ct, bool lab = false, bool backup = true)
    {
        var inv = host.Scan(ct);
        if (!inv.Ok) return new(false, null, [new ExecStep("", "", "failed", inv.Error, 0, 0)], 0, 1, 0, 0, true, inv.Error);
        var plan = Build(host, inv, req, lab);
        if (plan.Token != token) return new(true, plan with { Changed = true }, [], 0, 0, 0, 0, false, null);
        var todo = plan.Items.Where(i => i.Ok).ToList();
        var steps = new List<ExecStep>();
        int removed = 0, failed = 0, skipped = 0, row = 0, inARow = 0; bool stopped = false; string? why = null;
        foreach (var it in todo)
        {
            row++;
            if (ct.IsCancellationRequested || stopped)
            {
                steps.Add(new(it.Key, it.Display, "not-started", stopped ? why : "Cancelado antes de empezar.", row, todo.Count)); continue;
            }
            var running = it.Kind == "appx" && it.Action.StartsWith("remove-user") ? host.RunningUnder(it.Location) : [];
            if (running.Count > 0)
            {
                var s0 = new ExecStep(it.Key, it.Display, "skipped", "Está abierta: ciérrala y vuelve a intentarlo.", row, todo.Count);
                steps.Add(s0); progress?.Invoke(s0); skipped++; continue;
            }
            var rec = AppHistory.Begin(it);
            if (backup && it.Kind == "appx" && it.Action.StartsWith("remove-user")
                && Backups.Save("apps", $"Quitar app: {it.Name}", [Backups.Item.Appx(it.Name, it.Display)]) is null)
            {
                AppHistory.End(rec, "failed", "No pude guardar la copia de seguridad; no la quité.");
                var sb = new ExecStep(it.Key, it.Display, "failed", "No pude guardar la copia de seguridad; no la quité.", row, todo.Count);
                steps.Add(sb); progress?.Invoke(sb); failed++; continue;
            }
            var (state, err) = Run(host, it, ct);
            AppHistory.End(rec, state, err);
            var step = new ExecStep(it.Key, it.Display, state, err, row, todo.Count);
            steps.Add(step); progress?.Invoke(step);
            if (state is "removed" or "gone" or "uninstalled") { removed++; inARow = 0; }
            else if (state is "opened" or "cancelled-by-user") { skipped++; inARow = 0; }   // lo termina (o lo cancela) la persona en la ventana de Windows
            else if (state == "cancelled") { stopped = true; why = "Cancelado."; }
            else
            {
                failed++; inARow++;
                if (err is not null && StopCodes.Any(c => err.Contains(c, StringComparison.OrdinalIgnoreCase))) { stopped = true; why = "Windows no da permiso (o una directiva de la organización lo impide): paro aquí."; }
                else if (inARow >= 3) { stopped = true; why = "Tres fallos seguidos: paro para no seguir a ciegas."; }
            }
        }
        return new(false, null, steps, removed, failed, skipped, steps.Count(s => s.State == "not-started"), stopped, why);
    }

    static (string, string?) Run(IAppHost host, PlanItem it, CancellationToken ct)
    {
        switch (it.Action)
        {
            case "remove-user": return host.RemoveForUser(it.Full!, ct);
            case "deprovision": return host.Deprovision(it.ProvName!, ct);
            case "remove-user+deprovision":
                var a = host.RemoveForUser(it.Full!, ct);
                if (a.State is not ("removed" or "gone")) return a;
                var b = host.Deprovision(it.ProvName!, ct);
                return b.State is "removed" or "gone" ? ("removed", null) : ("failed", "Se quitó de tu usuario, pero no el aprovisionamiento: " + b.Error);
            case "win32-msi": return Win32Uninstall.Msi(it.Msi!, it.Key, host);
            case "win32-settings": return Win32Uninstall.Settings();
        }
        return ("failed", "Operación desconocida.");
    }
}

/// <summary>Programas clásicos: Vigía nunca ejecuta la cadena UninstallString del registro. Los MSI se quitan con
/// msiexec /x {código} (el asistente del propio instalador pide confirmar); el resto, desde Configuración de Windows.</summary>
static class Win32Uninstall
{
    public static (string, string?) Msi(string code, string key, IAppHost host)
    {
        if (!Win32Reader.GuidRx().IsMatch(code)) return ("failed", "Código de producto no válido.");
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe")) { UseShellExecute = false };
        psi.ArgumentList.Add("/x"); psi.ArgumentList.Add(code);
        using var p = Process.Start(psi)!;
        if (!p.WaitForExit(TimeSpan.FromMinutes(60))) return ("failed", "El desinstalador sigue abierto; revisa su ventana.");
        if (p.ExitCode == 1602) return ("cancelled-by-user", "Lo cancelaste en el asistente.");
        var gone = !host.Win32Present(key);
        return gone ? ("uninstalled", p.ExitCode == 3010 ? "Pide reiniciar para terminar." : null)
            : ("failed", $"El desinstalador terminó (código {p.ExitCode}), pero el programa sigue registrado.");
    }

    public static (string, string?) Settings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true }); return ("opened", null); }
        catch (Exception ex) { return ("failed", ex.Message); }
    }
}
