using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Vigia;

/// <summary>
/// «Apoya Vigía». Los enlaces de donación viven en support.json del repo (JcperezDev/vigia):
/// se cambian sin sacar versión nueva. Solo se lee al abrir la sección, nunca en segundo plano.
/// Nada de cuentas ni datos del usuario: Vigía solo abre el enlace en el navegador.
/// </summary>
public static class Support
{
    const string Url = "https://raw.githubusercontent.com/JcperezDev/vigia/main/support.json";
    public const string Repo = "https://github.com/JcperezDev/vigia";

    // Solo se abren enlaces https a estos sitios: aunque alguien tocara support.json, no puede mandar a otro lado.
    static readonly string[] Allowed = ["ko-fi.com", "paypal.me", "www.paypal.com", "github.com", "buymeacoffee.com", "www.buymeacoffee.com", "discord.gg"];

    public record Link(string Id, string Name, string Url);
    public record Info(List<Link> Links, string? Error);

    static Info? _cache;

    public static async Task<Info> GetAsync()
    {
        if (_cache is { Error: null }) return _cache;
        var links = new List<Link>();
        string? err = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Vigia");
            var j = JsonNode.Parse(await http.GetStringAsync(Url + "?t=" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerHour));
            foreach (var l in j?["links"]?.AsArray() ?? [])
            {
                var id = (string?)l?["id"]; var name = (string?)l?["name"]; var url = (string?)l?["url"];
                if (id is null || name is null || url is null || !IsAllowed(url)) continue;
                links.Add(new(id, name, url));
            }
        }
        // 404: todavía no hay support.json publicado → sin enlaces, sin error
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        catch (Exception e) { err = e is TaskCanceledException ? "sin respuesta" : e.Message; }
        return _cache = new(links, err);
    }

    public static bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && Allowed.Contains(u.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>Fuentes oficiales de los ajustes (documentación de Microsoft y de los navegadores). Solo se usan con
    /// URLs que vienen de tweaks.json, que va dentro del ejecutable; el frontend nunca manda la dirección.</summary>
    static readonly string[] SourceHosts = ["learn.microsoft.com", "devblogs.microsoft.com", "msrc.microsoft.com", "mozilla.github.io", "chromeenterprise.google"];
    public static bool IsSourceAllowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && SourceHosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase) && string.IsNullOrEmpty(u.UserInfo) && u.IsDefaultPort;
    public static bool OpenSource(string url)
    {
        if (!IsSourceAllowed(url)) return false;
        Process.Start(new ProcessStartInfo(new Uri(url).AbsoluteUri) { UseShellExecute = true });
        return true;
    }

    public static bool Open(string url)
    {
        if (!IsAllowed(url)) return false;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return true;
    }
}
