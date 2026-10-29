using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vigia;

/// <summary>Registro de cada operación con apps (apps-history.json en la carpeta de datos). Se escribe antes de tocar nada
/// (estado «pending») y se completa con el resultado real. Es un manifiesto: no guarda los archivos ni los datos de la app.</summary>
public static class AppHistory
{
    public sealed record Entry(
        string Id, DateTime At, string Kind, string Name, string Display, string Family, string? Full, string Version, string Publisher,
        string Arch, string Users, bool Provisioned, string Op, string State, string? Error, string Recovery, string Source, string? Msi);

    static readonly object Gate = new();
    /// <summary>Carpeta aparte para las pruebas (así nunca escriben en el historial real).</summary>
    internal static string? DirOverride;
    static string Dir => DirOverride ?? Settings.Dir;
    public static string PathOf => Path.Combine(Dir, "apps-history.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static List<Entry> All()
    {
        lock (Gate)
        {
            try { return File.Exists(PathOf) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(PathOf), Opt) ?? [] : []; }
            catch { return []; }   // un historial roto no impide usar Vigía; se empieza otro
        }
    }

    static void Save(List<Entry> list)
    {
        Directory.CreateDirectory(Dir);
        var tmp = PathOf + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list.TakeLast(500).ToList(), Opt));
        File.Move(tmp, PathOf, true);
    }

    static void Put(Entry e)
    {
        lock (Gate)
        {
            var list = All();
            var i = list.FindIndex(x => x.Id == e.Id);
            if (i >= 0) list[i] = e; else list.Add(e);
            Save(list);
        }
    }

    public static Entry Begin(PlanItem it, string? op = null)
    {
        var e = new Entry(Guid.NewGuid().ToString("N")[..12], DateTime.Now, it.Kind, it.Name, it.Display, it.Family, it.Kind == "win32" ? it.Key : it.Full, it.Version, it.Publisher,
            it.Arch, it.Users, it.Provisioned, op ?? it.Action, "pending", null, it.Recovery,
            it.Kind == "win32" ? (it.Msi is not null ? "Windows Installer" : "Configuración de Windows") : "Paquete de Windows (AppX/MSIX)", it.Msi);
        Put(e);
        return e;
    }

    public static Entry End(Entry e, string state, string? error)
    {
        var done = e with { State = state, Error = error };
        Put(done);
        return done;
    }

    /// <summary>Apps que Vigía quitó de tu usuario y no se restauraron después (para avisar si Windows las vuelve a poner).</summary>
    public static HashSet<string> RemovedNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in All().Where(e => e.Kind == "appx").OrderBy(e => e.At))
        {
            if (e.State == "removed" && e.Op.StartsWith("remove-user")) set.Add(e.Name);
            else if (e.State == "registered" || e.Op.StartsWith("restore")) set.Remove(e.Name);
        }
        return set;
    }

    /// <summary>Programas clásicos mandados a Configuración: si ya no están registrados, se anota que se quitaron.</summary>
    public static void Reconcile(AppInventoryData inv)
    {
        if (!inv.Ok) return;
        lock (Gate)
        {
            var list = All(); var changed = false;
            for (var i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.Kind == "win32" && e.State == "opened" && !inv.Win32.Any(w => w.Key == e.Full && w.Name == e.Name))
                { list[i] = e with { State = "uninstalled", Error = "Comprobado después: ya no está registrado." }; changed = true; }
            }
            if (changed) Save(list);
        }
    }

    /// <summary>Lo que cambió desde el último análisis (solo nombres de paquete y de programa; nada personal).</summary>
    public static (string[] Added, string[] Gone) Compare(AppInventoryData inv)
    {
        if (!inv.Ok) return ([], []);
        var now = inv.Packages.Where(p => p.Me && !p.Framework && !p.Resource).Select(p => "appx:" + p.Name)
            .Concat(inv.Win32.Select(w => "win32:" + w.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var f = Path.Combine(Dir, "apps-snapshot.json");
        HashSet<string>? before = null;
        try { if (File.Exists(f)) before = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(f)); } catch { }
        try { Directory.CreateDirectory(Dir); File.WriteAllText(f, JsonSerializer.Serialize(now.Order(StringComparer.OrdinalIgnoreCase))); } catch { }
        if (before is null) return ([], []);
        before = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        return (now.Except(before).Order().ToArray(), before.Except(now).Order().ToArray());
    }

    /// <summary>Exporta el inventario (JSON y CSV) a Documentos\Vigía. Sin rutas, usuarios ni identificadores de usuario.</summary>
    public static string Export(List<AppView> views)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vigía");
        Directory.CreateDirectory(dir);
        var stem = Path.Combine(dir, $"apps-{DateTime.Now:yyyyMMdd-HHmm}");
        var rows = views.Select(v => new { v.Kind, package = v.Name, name = v.Display, v.Publisher, v.Version, v.Arch, v.Category, v.Origin, v.Me, otherUsers = v.Others, v.Provisioned, v.Recovery }).ToList();
        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(rows, Opt));
        static string C(object? x) { var s = Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture) ?? ""; return s.IndexOfAny([',', '"', '\n', ';']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        var sb = new StringBuilder("kind,package,name,publisher,version,arch,category,origin,me,otherUsers,provisioned,recovery\r\n");
        foreach (var r in rows) sb.AppendJoin(',', C(r.Kind), C(r.package), C(r.name), C(r.Publisher), C(r.Version), C(r.Arch), C(r.Category), C(r.Origin), C(r.Me), C(r.otherUsers), C(r.Provisioned), C(r.Recovery)).Append("\r\n");
        File.WriteAllText(stem + ".csv", sb.ToString(), new UTF8Encoding(true));
        return stem + ".json";
    }
}

/// <summary>Qué se puede recuperar y cómo. Nunca promete más de lo que hay: un manifiesto no es una copia de la app.</summary>
public static class AppRecovery
{
    public static (string Level, string Text) Estimate(AppPkg p, CatalogApp? cat, bool storeBlocked, bool deprovision)
    {
        if (p.Others > 0 || (p.Provisioned && !deprovision))
            return ("A", "Windows sigue guardando sus archivos (para otros usuarios o los nuevos): se puede volver a registrar desde el historial. Sus datos no vuelven.");
        if (cat?.Recovery == "none") return ("D", "Microsoft la retiró: Vigía no puede volver a instalarla.");
        if (p.Signature.Equals("Store", StringComparison.OrdinalIgnoreCase) && p.Family.Length > 0 && !storeBlocked)
            return ("B", "Se reinstala desde Microsoft Store si sigue publicada. Sus datos no vuelven.");
        if (storeBlocked) return ("C", "La Store está bloqueada por una directiva: habría que reinstalarla a mano.");
        return ("C", "Hay que volver a instalarla a mano; Vigía no puede hacerlo por ti.");
    }

    /// <summary>Solo manifiestos de paquetes de Windows (WindowsApps o SystemApps), sin rodeos con «..».</summary>
    public static bool SafeManifest(string path)
    {
        try
        {
            if (path.Contains("..") || !Path.GetFileName(path).Equals("AppxManifest.xml", StringComparison.OrdinalIgnoreCase)) return false;
            var full = Path.GetFullPath(path);
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + "\\",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps") + "\\",
            };
            return roots.Any(r => full.StartsWith(r, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public sealed record Option(string Level, string Text, string? Manifest, string? Family);

    /// <summary>Qué se puede hacer hoy con una app del historial (se vuelve a comprobar en el momento, no se fía del registro).</summary>
    public static Option For(AppHistory.Entry e, AppInventoryData inv, IAppHost host)
    {
        if (e.Kind != "appx") return new("C", "Para volver a tenerlo hay que instalarlo otra vez desde su web oficial.", null, null);
        if (inv.Packages.Any(p => p.Me && p.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase)))
            return new("-", "Ya está instalada otra vez en tu usuario.", null, null);
        var copy = inv.Packages.FirstOrDefault(p => p.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase) && p.Location is { Length: > 0 } && !p.Me);
        var man = copy?.Location is { } l ? Path.Combine(l, "AppxManifest.xml") : null;
        if (man is not null && SafeManifest(man) && host.FileExists(man))
            return new("A", "Windows aún guarda sus archivos: se puede volver a registrar. Sus datos no vuelven.", man, null);
        if (AppCatalog.Find(e.Name)?.Recovery == "none") return new("D", "Microsoft la retiró: Vigía no puede volver a instalarla.", null, null);
        if (e.Family.Length > 0 && !inv.StoreBlocked)
            return new("B", "Sus archivos ya no están en el equipo. Se puede abrir su página en Microsoft Store para reinstalarla.", null, e.Family);
        return new("C", "Sus archivos ya no están en el equipo y la Store no está disponible: hay que reinstalarla a mano.", null, null);
    }

    /// <summary>Abre la ficha de la app en Microsoft Store por su Package Family Name (enlace documentado por Microsoft).
    /// La instalación la confirma el usuario en la Store; Vigía no descarga nada.</summary>
    public static bool OpenStore(string family)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(family, @"^[A-Za-z0-9.\-]+_[a-z0-9]{13}$")) return false;
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?PFN=" + Uri.EscapeDataString(family)) { UseShellExecute = true }); return true; }
        catch { return false; }
    }
}
