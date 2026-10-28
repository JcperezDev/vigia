using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vigia;

public record BackupView(string Id, string At, string Kind, string Title, string[] Lines, string? RestorePoint, string? RestoredAt, Dictionary<string, int>? Counts = null);
public record BackupDone(string Id, bool Ok, string Message, string[] Errors);

/// <summary>
/// Una copia de seguridad por cada cambio que hace Vigía (ajustes, inicio de Windows, resolución, apps).
/// Cada copia guarda el estado de antes, completo, y se puede restaurar sola, aunque haya cambios después.
/// Se guardan en %LOCALAPPDATA%\Vigia\copias\*.json (una por archivo, escritura atómica). Se conservan las últimas 300.
/// Además hay una copia fija, «original» (copias\original.json): cómo estaba todo lo que Vigía puede tocar antes
/// del primer cambio. Se crea una sola vez y no entra en el límite de 300.
/// </summary>
public static class Backups
{
    static readonly object Gate = new();
    static readonly object OrigGate = new();
    public const string OriginalId = "original";
    static bool _haveOriginal;
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
        /// <summary>Un archivo de configuración entero (pequeño): su contenido exacto y si estaba en solo lectura.</summary>
        public static JsonObject File(string path, string label) =>
            new() { ["t"] = "file", ["path"] = path, ["label"] = label, ["existed"] = System.IO.File.Exists(path),
                    ["data"] = System.IO.File.Exists(path) ? Convert.ToBase64String(System.IO.File.ReadAllBytes(path)) : null,
                    ["readonly"] = System.IO.File.Exists(path) && (System.IO.File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 };
        public static JsonObject Appx(string name, string label) =>
            new() { ["t"] = "appx", ["name"] = name, ["label"] = label };
    }

    /// <summary>Guarda una copia y devuelve su id. Nunca lanza: si no se puede guardar, el cambio no debe seguir (devuelve null).</summary>
    public static string? Save(string kind, string title, IEnumerable<JsonObject> items, string? restorePoint = null)
    {
        // antes del primer cambio, la foto completa de cómo estaba el sistema; sin ella no se cambia nada
        if (!EnsureOriginal()) return null;
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
                foreach (var old in Directory.GetFiles(Dir, "*.json").Where(f => Path.GetFileNameWithoutExtension(f) != OriginalId)
                                                .OrderByDescending(f => f).Skip(Keep)) File.Delete(old);
                return id;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Crea la copia «original» si todavía no existe: todos los ajustes del catálogo, todo lo que arranca con Windows,
    /// la resolución y las apps que Vigía sabe quitar, tal como están ahora. Devuelve false solo si no se pudo guardar.
    /// </summary>
    public static bool EnsureOriginal(bool withRestorePoint = false)
    {
        if (_haveOriginal) return true;
        lock (OrigGate)
        {
            if (_haveOriginal || File.Exists(PathOf(OriginalId))) return _haveOriginal = true;
            try
            {
                var items = new List<JsonObject>();
                items.AddRange(Tweaks.SnapshotAll());
                items.AddRange(Startup.SnapshotAll());
                // sin pantalla real (sesión de servicio, escritorio remoto) Windows da un modo virtual de 0 Hz: ese no se guarda
                if (Display.Status().Current is { Hz: > 1 } c) items.Add(Item.Display(c.Width, c.Height, c.Hz));
                foreach (var a in Debloat.List().Where(a => a.Installed)) items.Add(Item.Appx(a.Id, a.Name));
                items.Add(Wallpaper.Capture(new JsonObject()));

                string? rp = null;
                if (withRestorePoint && Telemetry.Elevated && RestorePoint.Create("Vigía: estado original") is { Ok: true }) rp = "Vigía: estado original";

                Directory.CreateDirectory(Dir);
                var o = new JsonObject
                {
                    ["id"] = OriginalId, ["at"] = DateTime.Now.ToString("s"), ["kind"] = OriginalId,
                    ["title"] = "Cómo estaba tu PC antes de Vigía", ["version"] = Updates.Current.ToString(3),
                    ["restorePoint"] = rp, ["restoredAt"] = null, ["items"] = new JsonArray(items.Cast<JsonNode>().ToArray()),
                };
                lock (Gate) Write(OriginalId, o);
                return _haveOriginal = true;
            }
            catch { return false; }
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
                    var its = o["items"]!.AsArray();
                    var counts = its.GroupBy(i => (string?)i!["t"] ?? "").ToDictionary(g => g.Key, g => g.Count());
                    res.Add(new((string)o["id"]!, (string)o["at"]!, (string)o["kind"]!, (string)o["title"]!,
                        its.Select(i => (string?)i!["label"] ?? "").ToArray(),
                        (string?)o["restorePoint"], (string?)o["restoredAt"], counts));
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
        "file" => Item.File((string)it["path"]!, (string)it["label"]!),
        "wallpaper" => Wallpaper.Capture(it),
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
            case "hz":
                var hr = Rig.Restore((string)it["device"]!, (int)it["w"]!, (int)it["h"]!, (int)it["hz"]!);
                if (hr is not null) throw new InvalidOperationException(hr);
                break;
            case "file":
                var fp = (string)it["path"]!;
                if (fp.Contains("FortniteGame", StringComparison.OrdinalIgnoreCase) && Games.FortniteRunning())
                    throw new InvalidOperationException("cierra Fortnite primero.");
                if ((string?)it["data"] is not { } data)
                {
                    if ((bool?)it["existed"] == false && System.IO.File.Exists(fp)) System.IO.File.Delete(fp);
                    break;
                }
                if (System.IO.File.Exists(fp)) System.IO.File.SetAttributes(fp, System.IO.File.GetAttributes(fp) & ~FileAttributes.ReadOnly);
                System.IO.File.WriteAllBytes(fp, Convert.FromBase64String(data));
                if ((bool?)it["readonly"] == true) System.IO.File.SetAttributes(fp, System.IO.File.GetAttributes(fp) | FileAttributes.ReadOnly);
                break;
            case "wallpaper": Wallpaper.Restore(it); break;
            case "appx":
                // Vigía solo quita la app de tu usuario: el paquete de Windows sigue ahí y se vuelve a registrar
                var name = (string)it["name"]!;
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9.\-]+$")) throw new InvalidOperationException("Nombre de paquete no válido.");
                // si sigue instalada para tu usuario no hay nada que hacer
                var (exit, output) = Tweaks.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"if (Get-AppxPackage -Name '" + name + "') { exit 0 }; $p = Get-AppxPackage -AllUsers -Name '" + name +
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
