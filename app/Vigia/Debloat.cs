using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vigia;

public record BloatApp(string Id, string Name, string Detail, string Risk, bool Installed, string? Version, string? Blocked);
public record BloatResult(string Id, bool Ok, string? Error);

/// <summary>
/// Quita apps preinstaladas de Windows para el usuario actual (Remove-AppxPackage). No toca el paquete aprovisionado,
/// así que cualquiera se vuelve a instalar desde Microsoft Store. Lista propia de apps de relleno.
/// </summary>
public static class Debloat
{
    record Def(string Id, string Name, string Detail, string Risk);

    static readonly Def[] Catalog =
    [
        new("Microsoft.BingNews", "Noticias", "Noticias de Microsoft Start.", "bajo"),
        new("Microsoft.BingWeather", "El Tiempo", "App del clima.", "bajo"),
        new("Microsoft.BingSearch", "Búsqueda de Bing", "Resultados web en el menú Inicio.", "bajo"),
        new("Microsoft.GetHelp", "Obtener ayuda", "Asistente de soporte.", "bajo"),
        new("Microsoft.Getstarted", "Recomendaciones", "Consejos de Windows.", "bajo"),
        new("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", "Juegos de cartas con anuncios.", "bajo"),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office)", "Lanzador de Office; no borra Office si lo tienes instalado.", "bajo"),
        new("Microsoft.WindowsFeedbackHub", "Centro de opiniones", "Enviar comentarios a Microsoft.", "bajo"),
        new("Microsoft.WindowsMaps", "Mapas", "Mapas sin conexión.", "bajo"),
        new("Microsoft.People", "Contactos", "Agenda de contactos.", "bajo"),
        new("Microsoft.Todos", "Microsoft To Do", "Lista de tareas.", "bajo"),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", "Automatización de escritorio.", "bajo"),
        new("Clipchamp.Clipchamp", "Clipchamp", "Editor de video online.", "bajo"),
        new("Microsoft.ZuneMusic", "Reproductor multimedia", "Reproductor de música y video nuevo.", "medio"),
        new("Microsoft.ZuneVideo", "Películas y TV", "Reproductor de video antiguo.", "bajo"),
        new("MSTeams", "Microsoft Teams (personal)", "Chat de Teams integrado.", "bajo"),
        new("Microsoft.OutlookForWindows", "Outlook (nuevo)", "Correo nuevo de Windows.", "medio"),
        new("Microsoft.YourPhone", "Vincular móvil", "Conexión con el teléfono.", "medio"),
        new("Microsoft.Windows.DevHome", "Dev Home", "Panel para desarrolladores.", "bajo"),
        new("Microsoft.549981C3F5F10", "Cortana", "Asistente antiguo.", "bajo"),
        new("Microsoft.XboxApp", "Xbox (antigua)", "App de Xbox de Windows 10.", "medio"),
        new("Microsoft.Copilot", "Copilot", "Chat de IA de Microsoft. Si lo quieres de vuelta, está en Microsoft Store.", "bajo"),
        new("MicrosoftWindows.Client.WebExperience", "Widgets", "Panel de noticias y tiempo de la barra de tareas. Se reinstala con «Windows Web Experience Pack» de la Store.", "bajo"),
        new("Microsoft.BingFinance", "Finanzas", "Cotizaciones de MSN.", "bajo"),
        new("Microsoft.BingSports", "Deportes", "Resultados de MSN.", "bajo"),
        new("Microsoft.BingTravel", "Viajes", "Guías de MSN.", "bajo"),
        new("Microsoft.MixedReality.Portal", "Portal de realidad mixta", "Solo sirve con visores de realidad mixta de Windows, ya descontinuados.", "bajo"),
        new("Microsoft.Microsoft3DViewer", "Visor 3D", "Abre modelos 3D.", "bajo"),
        new("Microsoft.Print3D", "Print 3D", "Preparar piezas para impresora 3D.", "bajo"),
        new("Microsoft.MSPaint", "Paint 3D", "No es el Paint normal: ese no se toca.", "bajo"),
        new("Microsoft.SkypeApp", "Skype", "Microsoft cerró Skype en 2025.", "bajo"),
        new("Microsoft.Office.OneNote", "OneNote para Windows 10", "Versión vieja de OneNote; no toca la de Office.", "bajo"),
        new("Microsoft.Messaging", "Mensajes", "App de mensajes antigua.", "bajo"),
        new("Microsoft.OneConnect", "Planes móviles", "Datos móviles para PCs con SIM.", "bajo"),
        new("Microsoft.Wallet", "Microsoft Pay", "Cartera antigua.", "bajo"),
        new("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family", "Control parental con cuenta de Microsoft.", "bajo"),
        new("Microsoft.WindowsAlarms", "Alarmas y reloj", "Alarmas, temporizador y cronómetro.", "bajo"),
        new("Microsoft.WindowsSoundRecorder", "Grabadora de sonido", "Grabar audio del micrófono.", "bajo"),
        new("Microsoft.MicrosoftStickyNotes", "Notas rápidas", "Notas adhesivas del escritorio. Si las usas, no la quites: se pierden las que no estén sincronizadas.", "medio"),
        new("SpotifyAB.SpotifyMusic", "Spotify", "Viene preinstalado en muchos PCs. Si lo usas, déjalo.", "bajo"),
        new("Disney.37853FC22B2CE", "Disney+", "Viene preinstalado en algunos PCs.", "bajo"),
        new("AmazonVideo.PrimeVideo", "Prime Video", "Viene preinstalado en algunos PCs.", "bajo"),
        new("king.com.CandyCrushSaga", "Candy Crush Saga", "Juego con anuncios preinstalado.", "bajo"),
        new("king.com.CandyCrushSodaSaga", "Candy Crush Soda Saga", "Juego con anuncios preinstalado.", "bajo"),
        new("BytedancePte.Ltd.TikTok", "TikTok", "Viene preinstalado en algunos PCs.", "bajo"),
        new("Facebook.InstagramBeta", "Instagram", "Viene preinstalado en algunos PCs.", "bajo"),
        new("Microsoft.GamingApp", "Xbox", "Rompe Game Pass para PC.", "medio"),
        new("Microsoft.XboxGamingOverlay", "Barra de juegos Xbox", "Win+G, capturas y grabación. Afecta también al overlay de FPS de Xbox.", "medio"),
    ];

    /// <summary>El mismo catálogo, para quitar las apps de una ISO antes de instalar Windows.</summary>
    internal static IEnumerable<(string Id, string Name, string Risk)> All() => Catalog.Select(d => (d.Id, d.Name, d.Risk));

    public static List<BloatApp> List()
    {
        var installed = Installed();
        return Catalog.Select(d =>
        {
            installed.TryGetValue(d.Id, out var ver);
            return new BloatApp(d.Id, d.Name, d.Detail, d.Risk, ver is not null, ver,
                d.Risk != "bajo" && !Tweaks.Lab ? "Primero hay que probarlo en la máquina virtual." : null);
        }).ToList();
    }

    static Dictionary<string, string> Installed()
    {
        var (exit, output) = Tweaks.Run("powershell.exe",
            "-NoProfile -NonInteractive -Command \"Get-AppxPackage | Select-Object Name,Version | ConvertTo-Json -Compress\"");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (exit != 0) return map;
        var node = JsonNode.Parse(output.Trim() is { Length: > 0 } s ? s : "[]");
        var arr = node is JsonArray a ? a : new JsonArray(node);
        foreach (var p in arr.OfType<JsonObject>())
            if ((string?)p["Name"] is { } n) map[n] = (string?)p["Version"] ?? "";
        return map;
    }

    public static List<BloatResult> Remove(IEnumerable<string> ids)
    {
        var res = new List<BloatResult>();
        var list = List().ToDictionary(a => a.Id);
        foreach (var id in ids)
        {
            if (!list.TryGetValue(id, out var app)) { res.Add(new(id, false, "No está en la lista de Vigía.")); continue; }
            if (app.Blocked is not null) { res.Add(new(id, false, app.Blocked)); continue; }
            if (!app.Installed) { res.Add(new(id, true, null)); continue; }
            if (Backups.Save("apps", $"Quitar app: {id}", [Backups.Item.Appx(id, id)]) is null) { res.Add(new(id, false, "No pude guardar la copia de seguridad; no la quité.")); continue; }
            // El Id viene del catálogo fijo de arriba, nunca de la interfaz: no hay inyección en la orden de PowerShell
            var (exit, output) = Tweaks.Run("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"Get-AppxPackage -Name '{id}' | Remove-AppxPackage -ErrorAction Stop\"");
            res.Add(exit == 0 ? new(id, true, null) : new(id, false, output.Trim().Split('\n').FirstOrDefault()));
        }
        return res;
    }
}
