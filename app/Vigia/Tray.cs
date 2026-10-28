using System.Globalization;
using System.Text.Json;

namespace Vigia;

/// <summary>
/// Icono en la bandeja («Mostrar iconos ocultos») con acciones rápidas en el clic derecho.
/// No tiene reloj propio ni sondea nada: el menú se arma cuando se abre y cada acción corre solo al hacer clic.
/// Cerrar la ventana la manda aquí en reposo (WebView2 suspendido); «Salir de Vigía» la cierra de verdad.
/// </summary>
sealed partial class MainForm
{
    NotifyIcon? _tray;
    readonly ContextMenuStrip _menu = new() { ShowImageMargin = false, ShowCheckMargin = true };
    bool _quitting;
    bool _trayBusy;

    internal const string ShowEventName = @"Local\Vigia.Show";
    EventWaitHandle? _showEvent;
    RegisteredWaitHandle? _showWait;

    void InitTray()
    {
        // abrir Vigía otra vez (acceso directo, menú Inicio) estando en la bandeja: la otra instancia avisa por este evento.
        // RegisterWaitForSingleObject no gasta un hilo propio: espera en el pool del sistema.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => { if (!IsDisposed) BeginInvoke(() => ShowFromTray()); }, null, -1, false);
        _tray = new NotifyIcon { Text = "Vigía", Icon = Icon ?? SystemIcons.Application, ContextMenuStrip = _menu, Visible = true };
        _menu.Opening += (_, _) => BuildMenu(_menu, Tr.Lang(_cfg));
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        FormClosing += (_, e) =>
        {
            if (_quitting || e.CloseReason != CloseReason.UserClosing || !_cfg.TrayOnClose) return;
            e.Cancel = true;
            WindowState = FormWindowState.Minimized;   // entra en reposo (ver SetRestingAsync)
            Hide();
            if (!_cfg.TrayHintShown)
            {
                _cfg.TrayHintShown = true; _cfg.Save();
                var l = Tr.Lang(_cfg);
                _tray!.ShowBalloonTip(6000, "Vigía", Tr.T("hint", l), ToolTipIcon.Info);
            }
        };
        FormClosed += (_, _) => { _showWait?.Unregister(null); _showEvent?.Dispose(); if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); } };
    }

    void ShowFromTray(string? view = null)
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        if (view is not null)
            _ = _web.CoreWebView2?.ExecuteScriptAsync($"document.querySelector('.nav[data-view=\"{view}\"]')?.click()");
    }

    void Notify(string text, bool ok = true) =>
        _tray?.ShowBalloonTip(5000, "Vigía", text, ok ? ToolTipIcon.Info : ToolTipIcon.Warning);

    /// <summary>Arma el menú en el momento de abrirlo: así la resolución y la RAM salen como están ahora.</summary>
    internal void BuildMenu(ContextMenuStrip m, string l)
    {
        m.Items.Clear();
        var c = Tr.Culture(l);
        string head = "Vigía";
        try { var ms = Memory.Status(); head = $"Vigía  ·  RAM {ms.UsedGb.ToString("0.0", c)} / {ms.TotalGb.ToString("0", c)} GB ({ms.LoadPct} %)"; } catch { }
        m.Items.Add(new ToolStripMenuItem(head) { Enabled = false });
        m.Items.Add(new ToolStripSeparator());

        var open = new ToolStripMenuItem(Tr.T("open", l), null, (_, _) => ShowFromTray());
        open.Font = new Font(open.Font, FontStyle.Bold);
        m.Items.Add(open);
        m.Items.Add(new ToolStripSeparator());

        m.Items.Add(new ToolStripMenuItem(Tr.T("optimize", l), null, async (_, _) => await QuickOptimizeAsync(l)) { Enabled = !_trayBusy, ToolTipText = Tr.T("optimizeTip", l) });
        m.Items.Add(new ToolStripMenuItem(Tr.T("freeRam", l), null, async (_, _) => await QuickFreeAsync(l)) { Enabled = !_trayBusy });

        // resolución estirada: las que ofrece la pantalla a su frecuencia, más «volver a la normal»
        var res = new ToolStripMenuItem(Tr.T("stretch", l));
        try
        {
            var st = Display.Status();
            foreach (var o in st.Options.Take(8))
            {
                var mode = o;
                var on = st.Stretched && st.Current is { } cur && cur.Width == o.Width && cur.Height == o.Height;
                res.DropDownItems.Add(new ToolStripMenuItem($"{o.Width} × {o.Height}  ·  {o.Hz} Hz", null, (_, _) => QuickStretch(mode, l)) { Checked = on, Enabled = !on });
            }
            if (st.Options.Count == 0) res.DropDownItems.Add(new ToolStripMenuItem(Tr.T("noModes", l)) { Enabled = false });
            res.DropDownItems.Add(new ToolStripSeparator());
            var back = st.Saved ?? st.Native;
            res.DropDownItems.Add(new ToolStripMenuItem(Tr.T("normal", l) + (back is null ? "" : $"  ({back.Width} × {back.Height})"), null, (_, _) => QuickRestoreDisplay(l)) { Enabled = st.Stretched });
            if (st.Stretched) res.Text += $"  ({st.Current?.Width} × {st.Current?.Height})";
        }
        catch { res.Enabled = false; }
        m.Items.Add(res);

        m.Items.Add(new ToolStripMenuItem(Tr.T("undo", l), null, async (_, _) => await QuickUndoAsync(l)) { Enabled = !_trayBusy });
        m.Items.Add(new ToolStripSeparator());

        var go = new ToolStripMenuItem(Tr.T("goTo", l));
        foreach (var (view, key) in new[] { ("monitor", "vMonitor"), ("mem", "vMem"), ("tweaks", "vTweaks"), ("startup", "vStartup"), ("tools", "vTools"), ("backups", "vBackups"), ("settings", "vSettings") })
            go.DropDownItems.Add(new ToolStripMenuItem(Tr.T(key, l), null, (_, _) => ShowFromTray(view)));
        m.Items.Add(go);

        var auto = new ToolStripMenuItem(string.Format(Tr.T("auto", l), _cfg.MemAutoThreshold)) { Checked = _cfg.MemAuto };
        auto.Click += (_, _) =>
        {
            _cfg.MemAuto = !_cfg.MemAuto; _cfg.Save();
            Send("settings", new { cfg = _cfg, lab = Tweaks.Lab });
        };
        m.Items.Add(auto);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem(Tr.T("quit", l), null, (_, _) => { _quitting = true; Close(); }));
    }

    async Task QuickFreeAsync(string l)
    {
        _trayBusy = true;
        try
        {
            var r = await Task.Run(() => Memory.Free(new MemOptions()));
            Send("memFreed", r);
            Notify(string.Format(Tr.T("freed", l), Gb(r.FreedMb, l), r.Before.LoadPct, r.After.LoadPct));
        }
        catch (Exception e) { Notify(e.Message, false); }
        finally { _trayBusy = false; }
    }

    /// <summary>Optimizar rápido: liberar la RAM y pasar TRIM a los SSD. No borra archivos ni toca ajustes de Windows.</summary>
    async Task QuickOptimizeAsync(string l)
    {
        _trayBusy = true;
        try
        {
            Notify(Tr.T("optimizing", l));
            var r = await Task.Run(() => Memory.Free(new MemOptions()));
            Send("memFreed", r);
            var ssd = (await Task.Run(() => Optimize.Status(false))).Volumes.Where(v => v.Media == "SSD").Select(v => v.Letter).ToList();
            var ok = new List<string>();
            foreach (var letter in ssd)
                if ((await Optimize.RunAsync(letter, _ => { })).Ok) ok.Add(letter);
            var trim = ok.Count > 0 ? string.Format(Tr.T("trimmed", l), string.Join(" ", ok)) : "";
            Notify(string.Format(Tr.T("optimized", l), Gb(r.FreedMb, l), r.Before.LoadPct, r.After.LoadPct) + trim);
        }
        catch (Exception e) { Notify(e.Message, false); }
        finally { _trayBusy = false; }
    }

    void QuickStretch(DisplayMode mode, string l)
    {
        var r = Display.Stretch(mode.Width, mode.Height);
        Send("display", Display.Status());
        if (!r.Ok) { Notify(r.Message, false); return; }
        // igual que en la ventana: 15 s para confirmar o vuelve sola (por si la pantalla se queda en negro)
        if (!KeepDialog.Ask(Tr.T("keepQ", l), Tr.T("keep", l), Tr.T("revert", l), 15))
        {
            var b = Display.Restore();
            Send("display", Display.Status());
            if (!b.Ok) Notify(b.Message, false);
        }
    }

    void QuickRestoreDisplay(string l)
    {
        var r = Display.Restore();
        Send("display", Display.Status());
        if (!r.Ok) Notify(r.Message, false);
    }

    async Task QuickUndoAsync(string l)
    {
        var last = (await Task.Run(Backups.List)).FirstOrDefault(b => b.RestoredAt is null);
        if (last is null) { Notify(Tr.T("noBackup", l)); return; }
        var q = string.Format(Tr.T("undoQ", l), last.Title, last.At);
        if (MessageBox.Show(q, "Vigía", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _trayBusy = true;
        try
        {
            var r = await Task.Run(() => Backups.Restore(last.Id));
            Send("backupDone", r);
            Send("backups", await Task.Run(Backups.List));
            Notify(r.Ok ? string.Format(Tr.T("undone", l), last.Title) : r.Message, r.Ok);
        }
        finally { _trayBusy = false; }
    }

    static string Gb(double mb, string l) => mb >= 1024
        ? (mb / 1024).ToString("0.0", Tr.Culture(l)) + " GB"
        : mb.ToString("0", Tr.Culture(l)) + " MB";

    /// <summary>Para --tray-dump: el menú tal como sale en cada idioma, sin mostrar nada.</summary>
    internal static string Dump(MainForm f)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var l in Tr.Langs)
        {
            using var m = new ContextMenuStrip();
            f.BuildMenu(m, l);
            sb.AppendLine($"[{l}]");
            void Walk(ToolStripItemCollection items, string pad)
            {
                foreach (ToolStripItem i in items)
                {
                    if (i is ToolStripSeparator) { sb.AppendLine(pad + "----"); continue; }
                    var mi = i as ToolStripMenuItem;
                    sb.AppendLine($"{pad}{(mi?.Checked == true ? "[x] " : "")}{i.Text}{(i.Enabled ? "" : "  (gris)")}");
                    if (mi is { HasDropDownItems: true }) Walk(mi.DropDownItems, pad + "    ");
                }
            }
            Walk(m.Items, "  ");
        }
        return sb.ToString();
    }
}

/// <summary>Ventanita «¿Se ve bien?» con cuenta atrás: si nadie contesta, devuelve false y se vuelve a la resolución de antes.</summary>
sealed class KeepDialog : Form
{
    public static bool Ask(string question, string keep, string revert, int seconds)
    {
        using var f = new KeepDialog(question, keep, revert, seconds);
        return f.ShowDialog() == DialogResult.OK;
    }

    KeepDialog(string question, string keep, string revert, int seconds)
    {
        Text = "Vigía"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = true;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(18);
        BackColor = Color.FromArgb(22, 24, 30); ForeColor = Color.FromArgb(228, 230, 236);
        Font = new Font("Segoe UI", 10f);
        var lbl = new Label { Text = question, AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 0, 0, 14) };
        var bKeep = new Button { Text = keep, DialogResult = DialogResult.OK, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(56, 104, 214), ForeColor = Color.White };
        var bBack = new Button { Text = $"{revert} ({seconds})", DialogResult = DialogResult.Cancel, AutoSize = true, FlatStyle = FlatStyle.Flat };
        bKeep.FlatAppearance.BorderSize = 0;
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.AddRange([bKeep, bBack]);
        var col = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        col.Controls.AddRange([lbl, row]);
        Controls.Add(col);
        AcceptButton = bKeep; CancelButton = bBack;
        var left = seconds;
        var t = new System.Windows.Forms.Timer { Interval = 1000 };
        t.Tick += (_, _) =>
        {
            left--; bBack.Text = $"{revert} ({left})";
            if (left <= 0) { t.Stop(); DialogResult = DialogResult.Cancel; }
        };
        Shown += (_, _) => { t.Start(); Activate(); };
        FormClosed += (_, _) => t.Dispose();
    }
}

/// <summary>Textos del menú de la bandeja (ES/EN/PT/FR/DE). Los de la ventana viven en wwwroot/i18n.</summary>
static class Tr
{
    public static readonly string[] Langs = ["es", "en", "pt", "fr", "de"];

    public static string Lang(Settings s)
    {
        var l = s.Lang is { Length: 2 } x && Langs.Contains(x) ? x : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Langs.Contains(l) ? l : "en";
    }

    public static CultureInfo Culture(string l) => CultureInfo.GetCultureInfo(l switch
    {
        "es" => "es-ES", "pt" => "pt-BR", "fr" => "fr-FR", "de" => "de-DE", _ => "en-US",
    });

    public static string T(string key, string l)
    {
        if (!S.TryGetValue(key, out var v)) return key;
        var i = Array.IndexOf(Langs, l);
        return v[i < 0 ? 1 : i];
    }

    static readonly Dictionary<string, string[]> S = new()
    {
        ["open"] = ["Abrir Vigía", "Open Vigía", "Abrir o Vigía", "Ouvrir Vigía", "Vigía öffnen"],
        ["optimize"] = ["Optimizar ahora", "Optimize now", "Otimizar agora", "Optimiser maintenant", "Jetzt optimieren"],
        ["optimizeTip"] = ["Libera la RAM y pasa TRIM a los SSD. No borra archivos.", "Frees RAM and runs TRIM on SSDs. Deletes no files.", "Libera a RAM e faz TRIM nos SSD. Não apaga arquivos.", "Libère la RAM et lance TRIM sur les SSD. Ne supprime aucun fichier.", "Gibt RAM frei und führt TRIM auf SSDs aus. Löscht keine Dateien."],
        ["freeRam"] = ["Liberar memoria", "Free memory", "Liberar memória", "Libérer la mémoire", "Speicher freigeben"],
        ["stretch"] = ["Resolución estirada", "Stretched resolution", "Resolução esticada", "Résolution étirée", "Gestreckte Auflösung"],
        ["noModes"] = ["La pantalla no ofrece ninguna", "Your display offers none", "A tela não oferece nenhuma", "L'écran n'en propose aucune", "Der Bildschirm bietet keine an"],
        ["normal"] = ["Volver a la normal", "Back to normal", "Voltar ao normal", "Revenir à la normale", "Zurück zur normalen"],
        ["undo"] = ["Deshacer último cambio…", "Undo last change…", "Desfazer última alteração…", "Annuler la dernière modification…", "Letzte Änderung rückgängig…"],
        ["goTo"] = ["Ir a", "Go to", "Ir para", "Aller à", "Gehe zu"],
        ["vMonitor"] = ["Monitor", "Monitor", "Monitor", "Moniteur", "Monitor"],
        ["vMem"] = ["Memoria", "Memory", "Memória", "Mémoire", "Speicher"],
        ["vTweaks"] = ["Ajustes", "Tweaks", "Ajustes", "Réglages", "Optimierungen"],
        ["vStartup"] = ["Inicio de Windows", "Windows startup", "Inicialização do Windows", "Démarrage de Windows", "Windows-Autostart"],
        ["vTools"] = ["Herramientas", "Tools", "Ferramentas", "Outils", "Werkzeuge"],
        ["vBackups"] = ["Copias de seguridad", "Backups", "Backups", "Sauvegardes", "Sicherungen"],
        ["vSettings"] = ["Configuración", "Settings", "Configurações", "Paramètres", "Einstellungen"],
        ["auto"] = ["Liberar sola al {0} %", "Auto-free at {0} %", "Liberar sozinha em {0} %", "Libérer seule à {0} %", "Automatisch freigeben bei {0} %"],
        ["quit"] = ["Salir de Vigía", "Quit Vigía", "Sair do Vigía", "Quitter Vigía", "Vigía beenden"],
        ["hint"] = ["Vigía sigue aquí, en reposo. Clic derecho en el icono para las acciones rápidas.", "Vigía is still here, resting. Right-click the icon for quick actions.", "O Vigía continua aqui, em repouso. Clique com o botão direito no ícone para ações rápidas.", "Vigía est toujours là, au repos. Clic droit sur l'icône pour les actions rapides.", "Vigía läuft im Ruhezustand weiter. Rechtsklick auf das Symbol für Schnellaktionen."],
        ["freed"] = ["Liberados {0}. RAM {1} % → {2} %.", "Freed {0}. RAM {1} % → {2} %.", "Liberados {0}. RAM {1} % → {2} %.", "{0} libérés. RAM {1} % → {2} %.", "{0} freigegeben. RAM {1} % → {2} %."],
        ["optimizing"] = ["Optimizando: memoria y TRIM de los SSD…", "Optimizing: memory and SSD TRIM…", "Otimizando: memória e TRIM dos SSD…", "Optimisation : mémoire et TRIM des SSD…", "Optimiere: Speicher und SSD-TRIM…"],
        ["optimized"] = ["Listo. Liberados {0}, RAM {1} % → {2} %.", "Done. Freed {0}, RAM {1} % → {2} %.", "Pronto. Liberados {0}, RAM {1} % → {2} %.", "Terminé. {0} libérés, RAM {1} % → {2} %.", "Fertig. {0} freigegeben, RAM {1} % → {2} %."],
        ["trimmed"] = [" TRIM en {0}.", " TRIM on {0}.", " TRIM em {0}.", " TRIM sur {0}.", " TRIM auf {0}."],
        ["keepQ"] = ["¿Se ve bien? Si no contestas, la pantalla vuelve sola a como estaba.", "Does it look right? If you don't answer, the display goes back on its own.", "Está tudo certo? Se você não responder, a tela volta sozinha.", "L'affichage est correct ? Sans réponse, l'écran revient tout seul.", "Sieht es gut aus? Ohne Antwort stellt sich der Bildschirm selbst zurück."],
        ["keep"] = ["Mantener", "Keep", "Manter", "Garder", "Behalten"],
        ["revert"] = ["Volver", "Revert", "Voltar", "Revenir", "Zurück"],
        ["noBackup"] = ["No hay ningún cambio que deshacer.", "There's nothing to undo.", "Não há nada para desfazer.", "Rien à annuler.", "Nichts rückgängig zu machen."],
        ["undoQ"] = ["¿Deshacer «{0}» ({1})?", "Undo \"{0}\" ({1})?", "Desfazer \"{0}\" ({1})?", "Annuler « {0} » ({1}) ?", "„{0}“ ({1}) rückgängig machen?"],
        ["undone"] = ["Deshecho: {0}.", "Undone: {0}.", "Desfeito: {0}.", "Annulé : {0}.", "Rückgängig gemacht: {0}."],
    };
}
