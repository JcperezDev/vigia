using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vigia;

public record BackupView(string Id, string At, string Kind, string Title, string[] Lines, string? RestorePoint, string? RestoredAt);
public record BackupDone(string Id, bool Ok, string Message, string[] Errors);

/// <summary>
/// Una copia de seguridad por cada cambio que hace Vigía (ajustes, inicio de Windows, resolución, apps).
/// Cada copia guarda el estado de antes, completo, y se puede restaurar sola, aunque haya cambios después.
/// Se guardan en %LOCALAPPDATA%\Vigia\copias\*.json (una por archivo, escritura atómica). Se conservan las últimas 300.
/// </summary>
public static class Backups
{
    static readonly object Gate = new();
    static string Dir => Path.Combine(Settings.Dir, "copias");
    const int Keep = 300;

    /// <summary>Cosas que se guardan en una copia. Cada una sabe volver a su estado.</summary>
    public static class Item
    {
        public static JsonObject Reg(string path, string name, string label) =>
            new() { ["t"] = "reg", ["path"] = path, ["name"] = name, ["label"] = label, ["snap"] = Tweaks.SnapValue(path, name) };
        public static JsonObject Task(string path, bool enabled, string label) =>
            new() { ["t"] = "task", ["path"] = path, ["enabled"] = enabled, ["label"] = label };
        public static JsonObject Display(int w, int h, int hz) =>
            new() { ["t"] = "display", ["w"] = w, ["h"] = h, ["hz"] = hz, ["label"] = $"Resolución {w} × {h} a {hz} Hz" };
        public static JsonObject Appx(string name, string label) =>
            new() { ["t"] = "appx", ["name"] = name, ["label"] = label };
    }

    /// <summary>Guarda una copia y devuelve su id. Nunca lanza: si no se puede guardar, el cambio no debe seguir (devuelve null).</summary>
    public static string? Save(string kind, string title, IEnumerable<JsonObject> items, string? restorePoint = null)
    {
        lock (Gate)
        {
            try
            {
                var list = new JsonArray(items.Cast<JsonNode>().ToArray());
                if (list.Count == 0) return null;
                Directory.CreateDirectory(Dir);
                var now = DateTime.Now;
                var id = $"{now:yyyyMMdd-HHmmss-fff}-{kind}";
                var o = new JsonObject
                {
                    ["id"] = id, ["at"] = now.ToString("s"), ["kind"] = kind, ["title"] = title,
                    ["restorePoint"] = restorePoint, ["restoredAt"] = null, ["items"] = list,
                };
                Write(id, o);
                foreach (var old in Directory.GetFiles(Dir, "*.json").OrderByDescending(f => f).Skip(Keep)) File.Delete(old);
                return id;
            }
            catch { return null; }
        }
    }

    public static List<BackupView> List()
    {
        lock (Gate)
        {
            if (!Directory.Exists(Dir)) return [];
            var res = new List<BackupView>();
            foreach (var f in Directory.GetFiles(Dir, "*.json").OrderByDescending(f => f))
            {
                try
                {
                    var o = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
                    res.Add(new((string)o["id"]!, (string)o["at"]!, (string)o["kind"]!, (string)o["title"]!,
                        o["items"]!.AsArray().Select(i => (string?)i!["label"] ?? "").ToArray(),
                        (string?)o["restorePoint"], (string?)o["restoredAt"]));
                }
                catch { /* archivo dañado: se ignora */ }
            }
            return res;
        }
    }

    /// <summary>Deja todo como estaba en esa copia. Antes guarda otra copia del estado actual, para poder deshacer la restauración.</summary>
    public static BackupDone Restore(string id)
    {
        JsonObject o;
        lock (Gate)
        {
            var path = PathOf(id);
            if (!File.Exists(path)) return new(id, false, "Esa copia ya no existe.", []);
            o = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        }
        var items = o["items"]!.AsArray().Select(i => i!.AsObject()).ToList();

        // copia del estado actual de las mismas cosas
        var now = items.Select(CaptureNow).Where(x => x is not null).Cast<JsonObject>().ToList();
        if (Save("restauracion", $"Antes de restaurar: {(string)o["title"]!}", now) is null && now.Count > 0)
            return new(id, false, "No pude guardar la copia del estado actual; no cambié nada.", []);

        var errors = new List<string>();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var it = items[i];
            try { RestoreItem(it); }
            catch (Exception e) { errors.Add($"{(string?)it["label"]}: {e.Message}"); }
        }
        lock (Gate)
        {
            o["restoredAt"] = DateTime.Now.ToString("s");
            Write(id, o);
        }
        var ok = errors.Count == 0;
        return new(id, ok, ok ? $"Restaurada: {items.Count} {(items.Count == 1 ? "cambio deshecho" : "cambios deshechos")}." : $"Restaurada en parte: {errors.Count} de {items.Count} no se pudieron deshacer.", errors.ToArray());
    }

    static JsonObject? CaptureNow(JsonObject it) => (string)it["t"]! switch
    {
        "reg" => Item.Reg((string)it["path"]!, (string)it["name"]!, (string)it["label"]!),
        "tweak" => Tweaks.SnapshotItem((string)it["id"]!),
        "task" => Startup.TaskEnabled((string)it["path"]!) is bool on ? Item.Task((string)it["path"]!, on, (string)it["label"]!) : null,
        "display" => Display.Status().Current is { } c ? Item.Display(c.Width, c.Height, c.Hz) : null,
        _ => null,   // apps quitadas: el estado actual es «quitada»; no hay nada que copiar
    };

    static void RestoreItem(JsonObject it)
    {
        switch ((string)it["t"]!)
        {
            case "reg": Tweaks.RestoreValue((string)it["path"]!, (string)it["name"]!, it["snap"]!.AsObject()); break;
            case "tweak": Tweaks.RestoreSnapshot(it); break;
            case "task": Startup.SetTask((string)it["path"]!, (bool)it["enabled"]!); break;
            case "display":
                var r = Display.SetMode((int)it["w"]!, (int)it["h"]!, (int)it["hz"]!);
                if (r is not null) throw new InvalidOperationException(r);
                break;
            case "appx":
                // Vigía solo quita la app de tu usuario: el paquete de Windows sigue ahí y se vuelve a registrar
                var name = (string)it["name"]!;
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9.\-]+$")) throw new InvalidOperationException("Nombre de paquete no válido.");
                var (exit, output) = Tweaks.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"$p = Get-AppxPackage -AllUsers -Name '" + name +
                    "' | Select-Object -First 1; if (-not $p) { exit 3 }; Add-AppxPackage -DisableDevelopmentMode -Register (Join-Path $p.InstallLocation 'AppxManifest.xml') -ErrorAction Stop\"");
                if (exit == 3) throw new InvalidOperationException("ya no está en Windows; instálala desde Microsoft Store.");
                if (exit != 0) throw new InvalidOperationException(output.Trim().Split('\n').FirstOrDefault() ?? $"código {exit}");
                break;
        }
    }

    static string PathOf(string id) =>
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^[0-9a-z\-]+$") ? Path.Combine(Dir, id + ".json") : throw new ArgumentException("id");

    static void Write(string id, JsonObject o)
    {
        var path = PathOf(id);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, true);
    }
}
