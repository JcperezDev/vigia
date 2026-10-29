using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>Una entrada del catálogo (apps.json, dentro del ejecutable).</summary>
public sealed record CatalogApp(
    string Id, string[] Packages, string Name, string Detail, string Category, string Advice, string Risk, string Vendor,
    string Windows, string Method, string Recovery, string Source, bool Iso, string[] Deps);

/// <summary>Cómo ve Vigía cada app. Category: protected, useful, optional, oem, unknown. Origin: microsoft, oem, third.
/// MinMode: el modo desde el que se puede marcar (safe, custom, advanced) o never. Recovery: A (volver a registrar con los
/// archivos que Windows aún guarda), B (Microsoft Store), C (a mano), D (no se puede recuperar).</summary>
public sealed record AppView(
    string Key, string Kind, string Name, string Display, string Publisher, string Version, string Arch,
    string Category, string Origin, string Why, string Detail, string Risk, bool Me, int Others, bool Provisioned,
    string[] Deps, string[] Dependents, string Recovery, string RecoveryText, string MinMode, string? Blocked,
    bool Recommended, string? CatalogId, string Family, bool Returned = false, bool Msi = false, string Scope = "user", long SizeKb = 0);

public static class AppCatalog
{
    public sealed record Data(int Version, string[] ProtectNames, string[] ProtectPrefixes, Regex[] ProtectPatterns, Regex Oem,
        Regex[] Win32Protect, List<CatalogApp> Apps, Dictionary<string, string> Sources);

    static Data? _data;
    public static Data Catalog => _data ??= Load();

    static Data Load()
    {
        using var res = typeof(AppCatalog).Assembly.GetManifestResourceStream("Vigia.apps.json") ?? throw new InvalidOperationException("Falta el catálogo de apps dentro de Vigía.");
        return Parse(JsonNode.Parse(res)!.AsObject());
    }

    internal static Data Parse(JsonObject o)
    {
        string[] Arr(JsonNode? n) => n is JsonArray a ? a.Select(x => (string?)x).OfType<string>().ToArray() : [];
        var p = o["protect"] as JsonObject ?? [];
        var words = Arr(o["oem"]).Select(w => w.Length <= 4 && w.ToUpperInvariant() == w ? $"(?-i:\\b{Regex.Escape(w)}\\b)" : $"\\b{Regex.Escape(w)}");
        var sources = (o["sources"] as JsonObject ?? []).ToDictionary(kv => kv.Key, kv => (string?)kv.Value ?? "");
        var apps = new List<CatalogApp>();
        foreach (var a in (o["apps"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string S(string k, string d) => (string?)a[k] ?? d;
            apps.Add(new CatalogApp(S("id", ""), Arr(a["packages"]), S("name", ""), S("detail", ""), S("category", "unknown"), S("advice", "keep"),
                S("risk", "bajo"), S("vendor", "Microsoft"), S("windows", "10/11"), S("method", "appx-user"), S("recovery", "auto"),
                S("source", ""), (bool?)a["iso"] ?? true, Arr(a["deps"])));
        }
        return new Data((int?)o["version"] ?? 0, Arr(p["names"]), Arr(p["prefixes"]),
            Arr(p["patterns"]).Select(x => new Regex(x, RegexOptions.CultureInvariant)).ToArray(),
            new Regex(string.Join("|", words), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            Arr(o["win32Protect"]).Select(x => new Regex(x, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray(), apps, sources);
    }

    /// <summary>La entrada del catálogo para ese paquete, por nombre exacto (nunca por parecido).</summary>
    public static CatalogApp? Find(string pkgName) =>
        Catalog.Apps.FirstOrDefault(a => a.Packages.Any(x => x.Equals(pkgName, StringComparison.OrdinalIgnoreCase)));

    public static string SourceText(CatalogApp c) => Catalog.Sources.GetValueOrDefault(c.Source, c.Source);
}

/// <summary>Decide la categoría de cada app. Lo duro primero (framework, no removible, del sistema, otras dependen de ella);
/// luego el catálogo por nombre exacto; luego el fabricante; y lo que queda es desconocido.</summary>
public static class AppClassifier
{
    public static bool IsMicrosoft(AppPkg p) => p.Publisher.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
        || p.Publisher.StartsWith("CN=Microsoft", StringComparison.OrdinalIgnoreCase) || (p.PublisherName ?? "").StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);

    static string Origin(AppPkg p, AppCatalog.Data d)
    {
        var who = $"{p.PublisherName} {p.Name.Split('.')[0]}";
        if (IsMicrosoft(p) && !d.Oem.IsMatch(p.PublisherName ?? "")) return "microsoft";
        return d.Oem.IsMatch(who) ? "oem" : "third";
    }

    /// <summary>Clasifica todo el inventario de paquetes. dependents: quién depende de cada paquete (por nombre).</summary>
    public static List<AppView> Classify(AppInventoryData inv, IReadOnlySet<string>? removedBefore = null, bool lab = false)
    {
        var d = AppCatalog.Catalog;
        var installed = inv.Packages.Where(p => p.Me || p.Others > 0).ToList();
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in installed.Where(p => !p.Framework && !p.Resource))
            foreach (var dep in p.Deps.Where(x => !x.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                (dependents.TryGetValue(dep, out var s) ? s : dependents[dep] = new(StringComparer.OrdinalIgnoreCase)).Add(p.Display ?? p.Name);
        var res = new List<AppView>();
        // un paquete puede venir en varias arquitecturas (x64 y neutral): se muestra una vez, la que está en tu usuario
        foreach (var g in inv.Packages.Where(p => !p.Resource).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var p = g.OrderByDescending(x => x.Me).ThenByDescending(x => x.Framework ? 0 : 1).First();
            var deps = dependents.GetValueOrDefault(p.Name)?.ToArray() ?? [];
            res.Add(One(p, d, deps, inv, removedBefore?.Contains(p.Name) == true && p.Me, lab));
        }
        return res.OrderBy(v => Rank(v.Category)).ThenBy(v => v.Display, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static int Rank(string c) => c switch { "optional" => 0, "useful" => 1, "oem" => 2, "unknown" => 3, _ => 4 };

    internal static AppView One(AppPkg p, AppCatalog.Data d, string[] dependents, AppInventoryData inv, bool returned, bool lab)
    {
        var cat = AppCatalog.Find(p.Name);
        string category, why;
        if (p.Framework) (category, why) = ("protected", "Framework: otras apps lo necesitan para funcionar.");
        else if (p.NonRemovable) (category, why) = ("protected", "Windows la marca como no removible.");
        else if (p.Signature.Equals("System", StringComparison.OrdinalIgnoreCase)) (category, why) = ("protected", "Viene dentro de Windows (app del sistema).");
        else if (dependents.Length > 0) (category, why) = ("protected", $"Otras apps instaladas dependen de ella: {string.Join(", ", dependents.Take(3))}{(dependents.Length > 3 ? "…" : "")}.");
        else if (d.ProtectNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
                 || (cat is null && (d.ProtectPrefixes.Any(x => p.Name.StartsWith(x, StringComparison.OrdinalIgnoreCase)) || d.ProtectPatterns.Any(r => r.IsMatch(p.Name)))))
            (category, why) = ("protected", "Componente de Windows, de la Store o de los juegos.");
        else if (cat is not null) (category, why) = (cat.Category, cat.Category switch
        {
            "optional" => "Preinstalada o promocional: se puede quitar si no la usas.",
            "useful" => "Útil para mucha gente: no se marca sola.",
            "oem" => "Software del fabricante: puede controlar ventiladores, teclas, audio o energía.",
            _ => "Vigía no la conoce: no se recomienda quitarla.",
        });
        else if (Origin(p, d) == "oem") (category, why) = ("oem", "Software del fabricante: puede controlar ventiladores, teclas, audio o energía.");
        else (category, why) = ("unknown", "Vigía no la conoce: no se recomienda quitarla.");

        var origin = cat is not null && cat.Vendor != "Microsoft" && category != "oem" ? "third" : category == "oem" ? "oem" : Origin(p, d);
        var risk = category == "protected" ? "alto" : cat?.Risk ?? (category is "oem" or "unknown" ? "medio" : "bajo");
        var minMode = category switch { "protected" => "never", "optional" => "safe", "useful" => "custom", _ => "advanced" };
        // las de riesgo medio del catálogo siguen la regla de siempre: primero se prueban en la máquina virtual
        string? blocked = cat is not null && cat.Risk != "bajo" && !lab ? "Primero hay que probarlo en la máquina virtual." : null;
        if (!p.Me && !p.Provisioned) blocked ??= "No está instalada en tu usuario: Vigía no toca otros usuarios.";
        var (rec, recText) = AppRecovery.Estimate(p, cat, inv.StoreBlocked, deprovision: false);
        var publisher = p.PublisherName ?? Cn(p.Publisher) ?? cat?.Vendor ?? "";
        return new AppView(p.FullName, "appx", p.Name, cat?.Name is { Length: > 0 } n ? n : p.Display ?? p.Name, publisher, p.Version, p.Arch,
            category, origin, why, cat?.Detail ?? "", risk, p.Me, p.Others, p.Provisioned, cat?.Deps ?? [], dependents, rec, recText, minMode, blocked,
            category == "optional" && cat?.Advice == "review" && p.Me && risk == "bajo", cat?.Id, p.Family, returned);
    }

    static string? Cn(string dn)
    {
        var m = Regex.Match(dn, @"(?:^|,\s*)O=([^,]+)");
        if (m.Success) return m.Groups[1].Value.Trim('"');
        m = Regex.Match(dn, @"^CN=([^,]+)");
        return m.Success && !Regex.IsMatch(m.Groups[1].Value, "^[0-9A-F-]{36}$", RegexOptions.IgnoreCase) ? m.Groups[1].Value : null;
    }

    /// <summary>Programas clásicos. Solo en modo avanzado; runtimes, drivers, antitrampas y el propio Vigía quedan protegidos.</summary>
    public static List<AppView> ClassifyWin32(IEnumerable<Win32App> list)
    {
        var d = AppCatalog.Catalog;
        return list.Select(w =>
        {
            string category, why;
            if (d.Win32Protect.Any(r => r.IsMatch(w.Name))) (category, why) = ("protected", "Runtime, driver o componente del que dependen otros programas o los juegos.");
            else if (d.Oem.IsMatch($"{w.Publisher} {w.Name}")) (category, why) = ("oem", "Software del fabricante: puede controlar ventiladores, teclas, audio o energía.");
            else (category, why) = ("unknown", "Programa instalado: solo tú sabes si lo usas.");
            var origin = (w.Publisher ?? "").StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ? "microsoft" : category == "oem" ? "oem" : "third";
            var how = w.MsiCode is not null ? "Se desinstala con Windows Installer: verás su propio asistente."
                : "Se desinstala desde Configuración de Windows: Vigía abre la página y tú eliges el programa.";
            return new AppView(w.Key, "win32", w.Name, w.Name, w.Publisher ?? "", w.Version ?? "", w.Arch, category, origin, why, how,
                category == "protected" ? "alto" : "medio", w.Scope == "user", 0, false, [], [], "C",
                "Para volver a tenerlo hay que instalarlo otra vez desde su web oficial.", category == "protected" || !w.HasUninstaller ? "never" : "advanced",
                w.HasUninstaller ? null : "No tiene desinstalador registrado.", false, null, "", false, w.MsiCode is not null, w.Scope, w.SizeKb);
        }).ToList();
    }
}
