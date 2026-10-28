using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Vigia;

/// <summary>
/// Prueba de laboratorio (VIGIA_LAB=1) de «Bajar de Microsoft»: abre la página real, elige edición e idioma
/// y pulsa «Descargar» como lo haría una persona, comprueba que la descarga llega como .iso desde microsoft.com
/// y la cancela enseguida. No guarda la ISO.
/// </summary>
static class IsoDownloadProbe
{
    public static int Run(string outPath)
    {
        var log = new List<string>();
        var result = new Dictionary<string, object?> { ["ok"] = false };
        var form = new Form { Text = "Vigía probe", Size = new Size(1100, 820), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
        var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);
        void L(string m) { lock (log) log.Add($"{DateTime.Now:HH:mm:ss} {m}"); }

        form.Shown += async (_, _) =>
        {
            try
            {
                var data = Path.Combine(Path.GetTempPath(), "vigia-msdl-probe");
                var env = await CoreWebView2Environment.CreateAsync(null, data);
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                var got = new TaskCompletionSource<bool>();
                core.NavigationStarting += (_, e) => L("nav " + e.Uri[..Math.Min(e.Uri.Length, 120)]);
                core.DownloadStarting += (_, e) =>
                {
                    var op = e.DownloadOperation;
                    var u = new Uri(op.Uri);
                    result["host"] = u.Host;
                    result["file"] = Path.GetFileName(e.ResultFilePath);
                    result["totalGb"] = Math.Round((op.TotalBytesToReceive ?? 0) / 1e9, 2);
                    result["fromMicrosoft"] = u.Scheme == "https" && (u.Host == "microsoft.com" || u.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase));
                    e.Handled = true; e.Cancel = true;   // no se baja nada
                    L("download capturada y cancelada");
                    got.TrySetResult(true);
                };
                async Task<string> Js(string s) => await core.ExecuteScriptAsync(s);
                async Task<bool> WaitFor(string cond, int secs)
                {
                    for (int i = 0; i < secs * 2; i++) { if (await Js($"!!({cond})") == "true") return true; await Task.Delay(500); }
                    return false;
                }
                core.Navigate("https://www.microsoft.com/es-es/software-download/windows11");
                if (!await WaitFor("document.getElementById('product-edition')", 40)) throw new Exception("no cargó la página");
                L("página cargada; ediciones: " + await Js("[...document.getElementById('product-edition').options].map(o => o.value + '=' + o.text).join(' | ')"));
                await Js("(() => { const s = document.getElementById('product-edition'); s.value = [...s.options].find(o => /^\\d+$/.test(o.value))?.value; s.dispatchEvent(new Event('change', {bubbles:true})); document.getElementById('submit-product-edition').click(); })()");
                if (!await WaitFor("(() => { const s = document.getElementById('product-languages'); return s && s.options.length > 1; })()", 40)) throw new Exception("no salió la lista de idiomas: " + await Js("JSON.stringify({ err: document.getElementById('product-edition-error')?.innerText, langs: document.getElementById('product-languages')?.options.length, val: document.getElementById('product-edition').value, txt: (document.querySelector('#SoftwareDownload_LanguageSelectionByProductEdition, main') || document.body).innerText.slice(-600) })"));
                L("idiomas listos");
                await Js("(() => { const s = document.getElementById('product-languages'); const o = [...s.options].find(o => { try { return JSON.parse(o.value).language === 'Spanish'; } catch { return false; } }) || [...s.options].find(o => o.value && o.value !== 'null' && o.value !== ''); s.selectedIndex = [...s.options].indexOf(o); s.dispatchEvent(new Event('input', {bubbles:true})); s.dispatchEvent(new Event('change', {bubbles:true})); setTimeout(() => document.getElementById('submit-sku').click(), 800); })()");
                if (!await WaitFor("document.querySelector('#download-links a[href]')", 60)) throw new Exception("no salió el botón de descarga: " + await Js("JSON.stringify({ err: document.getElementById('product-languages-error')?.innerText, val: document.getElementById('product-languages').value, opts: [...document.getElementById('product-languages').options].slice(0,4).map(o => o.value.slice(0,60) + '=' + o.text), dl: document.getElementById('download-links')?.outerHTML.slice(0,300), tail: document.body.innerText.slice(-900) })"));
                L("botón de descarga listo");
                await Js("(() => { const a = [...document.querySelectorAll('#download-links a[href]')].find(a => /x64|64/.test(a.textContent)) || document.querySelector('#download-links a[href]'); a.click(); })()");
                var done = await Task.WhenAny(got.Task, Task.Delay(30000));
                result["ok"] = done == got.Task && result.GetValueOrDefault("fromMicrosoft") is true && (result["file"] as string)?.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) == true;
                if (done != got.Task) L("no arrancó la descarga");
            }
            catch (Exception ex) { L("error " + ex.Message); }
            finally
            {
                result["log"] = log;
                File.WriteAllText(outPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                form.Close();
            }
        };
        Application.Run(form);
        return result["ok"] is true ? 0 : 1;
    }
}
