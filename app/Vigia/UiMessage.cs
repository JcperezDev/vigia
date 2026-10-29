using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>
/// Puerta de entrada de los mensajes de la interfaz (WebView2) al lado nativo.
/// La validación de app.js no cuenta: aquí se comprueba de nuevo el origen real del documento, el tamaño,
/// que sea un objeto JSON y que «cmd» sea un nombre corto. Los comandos que no existen se rechazan en el switch
/// de Program.OnMessage. Ningún mensaje lleva rutas o comandos para ejecutar: los parámetros se validan en cada caso.
/// </summary>
public static partial class UiMessage
{
    public const string Origin = "https://app.vigia/";
    /// <summary>El mensaje más grande que manda hoy la interfaz (lista de ids de limpieza o de apps) no llega a 8 KB.</summary>
    public const int MaxBytes = 64 * 1024;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,39}$")]
    private static partial Regex CmdRx();

    /// <summary>Devuelve el mensaje si se puede atender, o null con el motivo del rechazo.</summary>
    public static JsonObject? Accept(string? source, string? json, out string? why)
    {
        why = null;
        // el origen lo pone WebView2 a partir del documento que manda el mensaje, no el propio mensaje
        if (source is null || !source.StartsWith(Origin, StringComparison.Ordinal)) { why = "origen"; return null; }
        if (json is null || json.Length > MaxBytes) { why = "tamaño"; return null; }
        JsonNode? node;
        try { node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (JsonException) { why = "json"; return null; }
        if (node is not JsonObject o) { why = "no es un objeto"; return null; }
        if (o["cmd"] is not JsonValue v || v.GetValueKind() != JsonValueKind.String || !CmdRx().IsMatch((string)v!)) { why = "cmd"; return null; }
        return o;
    }

    /// <summary>Anota rechazos en ui.log (en la carpeta de datos). Si pasa de 256 KB se empieza de nuevo: no crece sin fin.</summary>
    public static void Note(string line)
    {
        try
        {
            var f = Path.Combine(Settings.Dir, "ui.log");
            if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024) File.Delete(f);
            File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Texto corto y de un solo tipo: si no es string o es más largo, null.</summary>
    public static string? Str(JsonObject msg, string key, int max = 256) =>
        msg[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String && ((string)v!).Length <= max ? (string)v! : null;

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        Check(Accept(Origin + "index.html", "{\"cmd\":\"cleanScan\"}", out _) is not null, "acepta un mensaje normal de la interfaz");
        Check(Accept("https://evil.example/", "{\"cmd\":\"cleanScan\"}", out var w1) is null && w1 == "origen", "rechaza otro origen");
        Check(Accept("https://app.vigia.evil.example/", "{\"cmd\":\"cleanScan\"}", out _) is null, "rechaza un host que solo empieza igual");
        Check(Accept(null, "{\"cmd\":\"cleanScan\"}", out _) is null, "rechaza sin origen");
        Check(Accept(Origin, "{\"cmd\":\"x\",\"pad\":\"" + new string('a', MaxBytes) + "\"}", out var w2) is null && w2 == "tamaño", "rechaza mensajes de más de 64 KB");
        Check(Accept(Origin, "[1,2]", out _) is null, "rechaza lo que no es un objeto");
        Check(Accept(Origin, "{\"cmd\":5}", out _) is null, "rechaza cmd que no es texto");
        Check(Accept(Origin, "{\"cmd\":\"rm -rf\"}", out _) is null, "rechaza cmd con espacios o símbolos");
        Check(Accept(Origin, "{\"cmd\":\"a\"" + string.Concat(Enumerable.Repeat(",\"b\":[", 40)) + string.Concat(Enumerable.Repeat("]", 40)) + "}", out _) is null, "rechaza JSON demasiado anidado");
        Check(Accept(Origin, "{oops", out _) is null, "rechaza JSON roto");
        var m = Accept(Origin, "{\"cmd\":\"openSource\",\"id\":7}", out _)!;
        Check(Str(m, "id") is null, "un id que no es texto se trata como ausente");
        return string.Join(Environment.NewLine, log);
    }
}
