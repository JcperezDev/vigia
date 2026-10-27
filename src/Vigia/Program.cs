using System.Text.Json;
using System.Text.Json.Nodes;
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
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { snapshot = snap, clean = Cleanup.Scan(), sensors = t.Dump().ToList() }, new JsonSerializerOptions(Json) { WriteIndented = true }));
            return 0;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}

sealed class MainForm : Form
{
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(14, 10, 24) };
    readonly Telemetry _tel = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    bool _busy;

    public MainForm()
    {
        Text = "Vigía";
        Width = 1360; Height = 860; MinimumSize = new Size(1024, 680);
        BackColor = Color.FromArgb(14, 10, 24);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
        FormClosed += (_, _) => { _timer.Stop(); _tel.Dispose(); };
    }

    async Task InitAsync()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia", "WebView2");
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
    }

    async Task PushAsync()
    {
        if (_busy) return;
        _busy = true;
        try { Send("telemetry", await Task.Run(_tel.Read)); }
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
            }
        }
        catch (Exception ex) { Send("error", ex.Message); }
    }
}
