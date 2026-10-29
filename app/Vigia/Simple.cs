namespace Vigia;

/// <summary>
/// Modo simple: unas pocas preguntas y Vigía hace el resto con lo que ya tiene (ajustes, apps, limpieza, memoria).
/// No trae ajustes propios: solo elige del catálogo, así cada cambio pasa por la misma copia de seguridad y se
/// puede volver atrás desde «Volver a como estaba». Lo de riesgo medio solo entra si ya se probó en la VM.
/// </summary>
public static class Simple
{
    public record Answers(bool Games = true, bool Printer = true, bool Xbox = false, bool Search = true, bool Laptop = false,
        bool Visual = true, bool Privacy = true, bool MemAuto = true, bool Clean = true, string[]? KeepApps = null,
        bool Wallpaper = false, string[]? Skip = null);

    public record Stage(string Id, string Title, List<string> Tweaks, List<string> Apps);
    /// <summary>Un ajuste que el modo simple propone: sale en pantalla con su casilla para que el usuario decida.</summary>
    public record Choice(string Id, string Title, string Detail, string Stage, bool Reboot);
    public record Plan(List<Stage> Stages, bool Clean, bool MemAuto, int Changes, List<string>? Names = null, List<Choice>? Choices = null);
    public record Step(int Index, string Id, string State, string? Detail);
    public record Report(bool Ok, int Applied, int Already, int Failed, int AppsRemoved, double CleanedMb, double FreedMb, bool NeedsReboot, List<string> Problems);

    static readonly string[] XboxApps = ["Microsoft.GamingApp", "Microsoft.XboxGamingOverlay", "Microsoft.XboxApp"];

    public static Plan Build(Answers a)
    {
        var perf = new List<string> { "priority-separation", "browsers-bg", "startup-delay", "end-task" };
        if (!a.Laptop) perf.AddRange(["power-ultimate", "hibernate-off"]);
        if (a.Games) perf.AddRange(["mmcss-games", "gpu-high", "windowed-opt", "hags", "game-mode", "mouse-raw", "accessibility-keys"]);
        if (a.Games && !a.Xbox) perf.Add("game-dvr-off");
        if (a.Visual) perf.AddRange(["transparency-off", "animations-off"]);

        var priv = new List<string> { "suggestions-off", "background-apps" };
        if (a.Privacy) priv.AddRange(["telemetry-off", "telemetry-tasks", "activity-off", "ad-id-off", "firefox-telemetry", "wer-off"]);

        var svc = new List<string> { "services-unused", "smb1-off", "update-control" };
        if (!a.Printer) svc.Add("print-off");

        var net = new List<string> { "responsiveness", "p2p-updates-off" };

        var keep = new HashSet<string>(a.KeepApps ?? [], StringComparer.OrdinalIgnoreCase);
        var apps = Debloat.List().Where(x => x.Installed && x.Blocked is null)
            .Where(x => XboxApps.Contains(x.Id) ? !a.Xbox : !keep.Contains(x.Id))
            .Select(x => x.Id).ToList();

        // solo lo que existe en el catálogo, no está bloqueado y aún no está puesto
        var view = Tweaks.List().ToDictionary(t => t.Id);
        List<string> Ok(List<string> ids) => ids.Distinct().Where(id => view.TryGetValue(id, out var t) && t.Blocked is null && t.State is not ("applied" or "na")).ToList();

        var stages = new List<Stage>
        {
            new("backup", "Copia de seguridad", [], []),
            new("perf", "Rendimiento", Ok(perf), []),
            new("privacy", "Privacidad y anuncios", Ok(priv), []),
            new("services", "Servicios que no usas", Ok(svc), []),
            new("network", "Red", Ok(net), []),
            new("apps", "Apps de Windows", [], apps),
            new("clean", "Limpieza", [], []),
            new("memory", "Memoria", [], []),
            new("wallpaper", "Fondo de VigíaOS", [], []),
        };
        // todo lo propuesto, con nombre y explicación de Ajustes, antes de quitar lo que el usuario desmarcó
        var choices = stages.SelectMany(s => s.Tweaks.Select(id => new Choice(id, view[id].Title, view[id].Detail, s.Title, view[id].Reboot))).ToList();
        var skip = new HashSet<string>(a.Skip ?? [], StringComparer.OrdinalIgnoreCase);
        stages = stages.Select(s => s with { Tweaks = s.Tweaks.Where(id => !skip.Contains(id)).ToList() }).ToList();
        try { if (!a.Wallpaper || Wallpaper.Status().Ours) stages.RemoveAll(s => s.Id == "wallpaper"); } catch { }   // el fondo solo si dijo que sí
        if (!a.Clean) stages.RemoveAll(s => s.Id == "clean");
        // nombres de lo que se va a tocar, iguales a los de Ajustes, para decirlo en la pantalla
        var names = stages.SelectMany(s => s.Tweaks.Select(id => Tweaks.TitleOf(id) ?? s.Title)
            .Concat(s.Apps.Count > 0 ? [s.Title] : Array.Empty<string>())).Distinct().ToList();
        return new(stages, a.Clean, a.MemAuto, stages.Sum(s => s.Tweaks.Count + s.Apps.Count), names, choices);
    }

    /// <summary>Corre el plan en orden. Una etapa que falla no frena las demás; todo queda en el informe.</summary>
    public static Report Run(Plan plan, Action<Step> progress, Action<bool> setMemAuto)
    {
        int applied = 0, already = 0, failed = 0, removed = 0; double cleaned = 0, freed = 0; bool reboot = false;
        var problems = new List<string>();
        var titles = Tweaks.List().ToDictionary(t => t.Id, t => t.Title);
        for (int i = 0; i < plan.Stages.Count; i++)
        {
            var s = plan.Stages[i];
            progress(new(i, s.Id, "run", null));
            string state = "ok"; string? detail = null;
            try
            {
                switch (s.Id)
                {
                    case "backup":
                        Backups.EnsureOriginal(withRestorePoint: false);
                        var rp = RestorePoint.Create("Vigía: antes del modo simple");
                        detail = rp.Ok ? "Punto de restauración creado." : $"Sin punto de restauración ({rp.Message}). Igual se guarda copia de cada cambio.";
                        if (!rp.Ok) state = "warn";
                        break;
                    case "apps":
                        if (s.Apps.Count == 0) { detail = "Nada que quitar."; break; }
                        var res = Debloat.Remove(s.Apps);
                        removed += res.Count(r => r.Ok);
                        var bad = res.Where(r => !r.Ok).ToList();
                        detail = $"{res.Count(r => r.Ok)} quitadas" + (bad.Count > 0 ? $", {bad.Count} no se dejaron" : "") + ".";
                        if (bad.Count > 0) { state = "warn"; failed += bad.Count; problems.AddRange(bad.Select(b => $"{b.Id}: {b.Error}")); }
                        break;
                    case "clean":
                        var cr = Cleanup.Run(Cleanup.TempIds.Append("startup_left"));   // temporales + restos del inicio; la Papelera no: eso lo decide cada uno
                        cleaned = Math.Round(cr.FreedBytes / 1048576.0, 1);
                        detail = cleaned >= 1024 ? $"{cleaned / 1024:0.0} GB liberados." : $"{cleaned:0} MB liberados.";
                        break;
                    case "memory":
                        setMemAuto(plan.MemAuto);
                        var mr = Memory.Free(new MemOptions());
                        freed = mr.FreedMb;
                        detail = (plan.MemAuto ? "Se liberará sola al llegar al 60 %. " : "") + $"{mr.FreedMb:0} MB liberados ahora.";
                        break;
                    case "wallpaper":
                        var wd = Wallpaper.Apply();
                        detail = wd.Ok ? "Puesto. El de antes vuelve desde Apps de Windows." : wd.Message;
                        if (!wd.Ok) state = "warn";
                        break;
                    default:
                        if (s.Tweaks.Count == 0) { detail = "Ya estaba todo puesto."; break; }
                        var tb = Tweaks.Apply(s.Tweaks, restorePoint: false);
                        foreach (var r in tb.Results)
                        {
                            if (!r.Ok) { failed++; problems.Add($"{(titles.TryGetValue(r.Id, out var t) ? t : r.Id)}: {r.Error}"); }
                            else if (r.State == "na") already++;
                            else applied++;
                        }
                        reboot |= tb.NeedsReboot;
                        if (s.Id == "network") FlushDns();
                        var okc = tb.Results.Count(r => r.Ok);
                        detail = $"{okc} de {tb.Results.Length} listos.";
                        if (okc < tb.Results.Length) state = "warn";
                        break;
                }
            }
            catch (Exception e) { state = "warn"; failed++; detail = e.Message; problems.Add($"{s.Title}: {e.Message}"); }
            progress(new(i, s.Id, state, detail));
        }
        return new(failed == 0, applied, already, failed, removed, cleaned, freed, reboot, problems);
    }

    static void FlushDns()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig.exe", "/flushdns")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
            p?.WaitForExit(5000);
        }
        catch { }
    }
}
