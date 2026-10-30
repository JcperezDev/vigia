using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Vigia;

/// <summary>
/// Vigía no necesita saber dónde está el equipo: ni el Monitor, ni la CPU, GPU, RAM, temperaturas, la red ni los
/// diagnósticos. Aquí está todo lo que evita que Windows le pida ubicación:
/// - WebView2: cualquier permiso que pida una página (ubicación incluida) se deniega sin preguntar.
/// - Wi-Fi: <see cref="Suggestions"/> solo usa consultas que no dan el BSSID (ver Suggestions.Wlan).
/// Las trazas van a «privacidad.log» en la carpeta de datos y nunca llevan la red, el BSSID, coordenadas ni la
/// frecuencia: solo qué se pidió y el código que devolvió Windows.
/// </summary>
public static class LocationGuard
{
    public static string LogPath => Path.Combine(Settings.Dir, "privacidad.log");

    /// <summary>Traza de diagnóstico. No escribe nada que sirva para ubicar el equipo.</summary>
    public static void Trace(string line)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var f = new FileInfo(LogPath);
            if (f.Exists && f.Length > 64 * 1024) f.Delete();   // solo diagnóstico: no crece sin límite
            File.AppendAllText(LogPath, $"{DateTime.Now:s} {line}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Deniega todos los permisos de página (ubicación, cámara, micrófono, notificaciones…) en un WebView2.</summary>
    public static void Harden(CoreWebView2 core, string where)
    {
        core.PermissionRequested += (_, e) =>
        {
            e.State = CoreWebView2PermissionState.Deny;
            Trace($"webview {where}: permiso {Decide(e.PermissionKind)} denegado ({Host(e.Uri)})");
        };
    }

    /// <summary>Nombre corto del permiso para la traza (y para la autoprueba).</summary>
    public static string Decide(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Geolocation => "ubicación",
        CoreWebView2PermissionKind.Microphone => "micrófono",
        CoreWebView2PermissionKind.Camera => "cámara",
        CoreWebView2PermissionKind.Notifications => "notificaciones",
        _ => kind.ToString()
    };

    static string Host(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var u) ? u.Host : "?";

    /// <summary>
    /// Prueba real (--geo-probe): abre la interfaz de Vigía en un WebView2 fuera de pantalla con la misma protección,
    /// le hace pedir la ubicación con navigator.geolocation y comprueba que se deniega sin preguntar (código 1).
    /// No lee ni guarda ninguna ubicación: si alguna vez llegara una posición, solo se anota «llegó posición».
    /// </summary>
    public static int Probe(string outPath)
    {
        var result = new Dictionary<string, object?> { ["ok"] = false };
        var asked = new List<string>();
        var form = new Form { Text = "Vigía probe", Size = new Size(640, 480), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
        var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);
        form.Shown += async (_, _) =>
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "vigia-geo-probe"));
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                core.PermissionRequested += (_, e) => { lock (asked) asked.Add(Decide(e.PermissionKind)); };
                Harden(core, "sonda");
                core.SetVirtualHostNameToFolderMapping("app.vigia", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
                var loaded = new TaskCompletionSource<bool>();
                core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
                core.Navigate("https://app.vigia/index.html");
                await Task.WhenAny(loaded.Task, Task.Delay(20000));
                result["secure"] = await core.ExecuteScriptAsync("window.isSecureContext");
                await core.ExecuteScriptAsync("window.__geo='esperando'; try { navigator.geolocation.getCurrentPosition(() => { window.__geo = 'llegó posición'; }, (e) => { window.__geo = 'error ' + e.code; }, { timeout: 8000 }); } catch (x) { window.__geo = 'excepción'; }");
                string geo = "";
                for (var i = 0; i < 40; i++) { geo = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("window.__geo")) ?? ""; if (geo != "esperando") break; await Task.Delay(250); }
                result["geolocation"] = geo;   // «error 1» = PERMISSION_DENIED
                lock (asked) result["permisos"] = asked.ToArray();
                // con la ubicación de Windows apagada WebView2 ya la niega sin preguntar; encendida, llega aquí y se deniega
                result["ok"] = geo == "error 1" && asked.All(k => k == "ubicación");
            }
            catch (Exception ex) { result["error"] = ex.Message; }
            finally
            {
                File.WriteAllText(outPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                form.Close();
            }
        };
        Application.Run(form);
        return result["ok"] is true ? 0 : 1;
    }

    /// <summary>
    /// --wifi-check: lee el Wi-Fi como lo hacen las Sugerencias, en cada adaptador inalámbrico (aunque esté desconectado),
    /// y dice solo si hubo datos. No imprime red, señal, frecuencia ni BSSID.
    /// </summary>
    public static int WifiCheck(string outPath)
    {
        var lines = new List<string>();
        var n = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(i => i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
        {
            n++;
            var f = Suggestions.Wlan.Read(ni.Id, 0);
            lines.Add($"adaptador {n}: estado={ni.OperationalStatus} datos={(f is null ? "no" : "sí")}");
        }
        lines.Add($"adaptadores inalámbricos: {n}");
        File.WriteAllLines(outPath, lines);
        return 0;
    }
}
