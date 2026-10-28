using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Vigia;

/// <summary>
/// Bajar la ISO oficial: la página real de Microsoft en una ventana propia (su verificación anti-bots
/// corre como en cualquier navegador; Vigía no la imita ni la salta). Cuando pulsas «Descargar»,
/// Vigía toma ese enlace directo, guarda la ISO donde elijas, muestra el progreso y la carga sola.
/// </summary>
sealed partial class MainForm
{
    Form? _msWin;
    CoreWebView2DownloadOperation? _msDl;

    static bool IsMicrosoft(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" &&
        (u.Host == "microsoft.com" || u.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase));

    async Task OpenMsDownloadAsync(string lang)
    {
        if (_msDl is not null) { Send("isoDl", new { state = "busy" }); return; }
        if (_msWin is not null && !_msWin.IsDisposed) { _msWin.Show(); _msWin.Activate(); return; }

        var locale = lang switch { "en" => "en-us", "pt" => "pt-br", "fr" => "fr-fr", "de" => "de-de", _ => "es-es" };
        var win = new Form
        {
            Text = Tr.T("msWinTitle", lang),
            Icon = this.Icon, StartPosition = FormStartPosition.CenterParent,
            Size = new Size(1100, 820), MinimumSize = new Size(700, 500), ShowInTaskbar = true,
        };
        var web = new WebView2 { Dock = DockStyle.Fill };
        win.Controls.Add(web);
        // mientras baja, cerrar la ventana solo la esconde: si se destruye, la descarga se corta
        win.FormClosing += (_, e) => { if (_msDl is not null && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; win.Hide(); } };
        win.FormClosed += (_, _) => { _msWin = null; };
        _msWin = win;
        win.Show(this);

        // perfil aparte: las cookies de Microsoft no se mezclan con las de Vigía
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia", "WebView2-ms");
        var env = await CoreWebView2Environment.CreateAsync(null, data);
        await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.NavigationStarting += (_, e) => { if (!IsMicrosoft(e.Uri)) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => { e.Handled = true; if (IsMicrosoft(e.Uri)) core.Navigate(e.Uri); };
        core.DownloadStarting += (_, e) => OnMsDownload(win, e);
        core.Navigate($"https://www.microsoft.com/{locale}/software-download/windows11");
    }

    void OnMsDownload(Form win, CoreWebView2DownloadStartingEventArgs e)
    {
        var op = e.DownloadOperation;
        var name = Path.GetFileName(e.ResultFilePath);
        e.Handled = true;   // sin la barra de descargas del navegador: el progreso sale en Vigía
        if (!IsMicrosoft(op.Uri) || !name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || _msDl is not null) { e.Cancel = true; return; }

        var deferral = e.GetDeferral();
        BeginInvoke(() =>
        {
            try
            {
                using var sfd = new SaveFileDialog
                {
                    Filter = "Imagen de Windows (*.iso)|*.iso", Title = "Dónde guardar la ISO de Windows", FileName = name, OverwritePrompt = true,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
                };
                if (sfd.ShowDialog(win) != DialogResult.OK) { e.Cancel = true; return; }
                var total = (long)(op.TotalBytesToReceive ?? 0);
                var free = new DriveInfo(Path.GetPathRoot(sfd.FileName)!).AvailableFreeSpace;
                if (total > 0 && free < total + (256L << 20))
                {
                    e.Cancel = true;
                    Send("isoDl", new { state = "error", message = string.Format("Hace falta {0} GB libres en {1} y hay {2} GB.", Math.Ceiling((total + (256L << 20)) / 1e9), Path.GetPathRoot(sfd.FileName), Math.Round(free / 1e9, 1)) });
                    return;
                }
                e.ResultFilePath = sfd.FileName;
                _msDl = op;
                win.Hide();
                Send("isoDl", new { state = "start", path = sfd.FileName, total });
                int last = -1;
                op.BytesReceivedChanged += (_, _) =>
                {
                    var t = (long)(op.TotalBytesToReceive ?? 0);
                    var pct = t > 0 ? (int)(op.BytesReceived * 100 / t) : -1;
                    if (pct == last) return; last = pct;
                    Send("isoDl", new { state = "progress", pct, received = op.BytesReceived, total = t });
                };
                op.StateChanged += async (_, _) =>
                {
                    if (op.State == CoreWebView2DownloadState.InProgress) return;
                    var path = op.ResultFilePath;
                    _msDl = null;
                    var ok = op.State == CoreWebView2DownloadState.Completed;
                    Send("isoDl", ok ? new { state = "done", path } : new { state = "error", message = op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled ? "Descarga cancelada." : $"La descarga se cortó ({op.InterruptReason})." });
                    try { win.Close(); } catch { }
                    if (!ok) return;
                    Send("isoSource", new { path, busy = true });
                    Send("isoSource", await Task.Run(() => IsoBuild.Inspect(path)));
                };
            }
            catch (Exception ex) { e.Cancel = true; Send("isoDl", new { state = "error", message = ex.Message }); }
            finally { deferral.Complete(); }
        });
    }

    void CancelMsDownload() => _msDl?.Cancel();
}
