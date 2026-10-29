using System.Text.Json;

namespace Vigia;

/// <summary>
/// Comparación offline de la BIOS: Vigía guarda lo que leyó la última vez (versión y si RAM, Secure Boot, TPM…
/// estaban bien) y, al volver a mirar, dice qué cambió. Al actualizar o restablecer la BIOS los ajustes vuelven a
/// los de fábrica (por ejemplo, el perfil de la RAM) y nadie avisa. Solo lectura: no toca la BIOS ni Windows.
/// El archivo vive en los datos del usuario: no se confía en él para nada más que para este aviso.
/// </summary>
public static class BiosWatch
{
    public record Snap(string Version, string? Date, Dictionary<string, string> Levels);

    static readonly string[] Watched = ["ram", "rebar", "uefi", "secureboot", "tpm"];
    const int MaxText = 64;

    static string FilePath => Path.Combine(Settings.Dir, "bios-ultima.json");

    /// <summary>Lo que se guarda ahora: una lectura fallida («info») conserva lo que había, para no perder el «estaba bien».</summary>
    internal static Snap Next(Snap? prev, Bios.Firmware fw, IEnumerable<Rig.Check> checks)
    {
        var levels = new Dictionary<string, string>();
        foreach (var c in checks)
        {
            if (!Watched.Contains(c.Key)) continue;
            levels[c.Key] = c.Level is "ok" or "warn" ? c.Level : prev?.Levels.GetValueOrDefault(c.Key) ?? c.Level;
        }
        return new(Cut(fw.Version), fw.Date is null ? null : Cut(fw.Date), levels);
    }

    /// <summary>Avisos por diferencia con la foto anterior. Solo «bien → mal» cuenta; nada si no hay foto.</summary>
    internal static List<Rig.Check> Compare(Snap? prev, Bios.Firmware fw, IEnumerable<Rig.Check> checks)
    {
        var list = new List<Rig.Check>();
        if (prev is null) return list;
        var ver = Cut(fw.Version);
        if (prev.Version.Length > 0 && ver.Length > 0 && prev.Version != ver)
            list.Add(new("bios-changed", "info", "La BIOS cambió",
                $"Antes tenías la {prev.Version}, ahora la {ver}. Al actualizarla, la BIOS vuelve a los valores de fábrica: revisa abajo la RAM, Secure Boot y TPM.", null, null, null));
        foreach (var c in checks)
        {
            if (!Watched.Contains(c.Key) || c.Level != "warn") continue;
            if (prev.Levels.GetValueOrDefault(c.Key) != "ok") continue;
            list.Add(new($"was-{c.Key}", "warn", "Cambió desde la última vez",
                $"{c.Title}: estaba bien y ahora no. Suele pasar al actualizar o restablecer la BIOS.", null, null, null));
        }
        return list;
    }

    /// <summary>Compara con la última foto, guarda la nueva y devuelve los avisos. Cualquier fallo = sin avisos.</summary>
    public static List<Rig.Check> Note(Bios.Firmware fw, List<Rig.Check> checks)
    {
        try
        {
            var prev = Load();
            var diff = Compare(prev, fw, checks);
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Next(prev, fw, checks)));
            return diff;
        }
        catch { return []; }
    }

    internal static Snap? Load()
    {
        try
        {
            var fi = new FileInfo(FilePath);
            if (!fi.Exists || fi.Length > 4096) return null;
            return Parse(File.ReadAllText(FilePath));
        }
        catch { return null; }
    }

    /// <summary>Lee la foto guardada sin fiarse: textos cortos, solo claves conocidas y niveles válidos.</summary>
    internal static Snap? Parse(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Snap>(json);
            if (s?.Version is null || s.Levels is null) return null;
            var levels = s.Levels.Where(kv => Watched.Contains(kv.Key) && kv.Value is "ok" or "warn" or "info")
                                 .ToDictionary(kv => kv.Key, kv => kv.Value);
            return new(Cut(s.Version), s.Date is null ? null : Cut(s.Date), levels);
        }
        catch { return null; }
    }

    static string Cut(string s)
    {
        s = new string(s.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return s.Length > MaxText ? s[..MaxText] : s;
    }

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        Bios.Firmware Fw(string v) => new("B650", "MSI", v, "AMI", "2026-01-01", 9, true, null);
        Rig.Check C(string k, string lv) => new(k, lv, k == "ram" ? "Memoria RAM" : k, "x", null, null, null);
        var good = new List<Rig.Check> { C("ram", "ok"), C("secureboot", "ok"), C("tpm", "ok"), C("virt", "info") };
        var snap = Next(null, Fw("1.A0"), good);

        Check(Compare(null, Fw("1.A0"), good).Count == 0, "primera vez: sin foto anterior no se avisa nada");
        Check(Compare(snap, Fw("1.A0"), good).Count == 0, "nada cambió: sin avisos");
        var upd = Compare(snap, Fw("1.B0"), [C("ram", "warn"), C("secureboot", "ok"), C("tpm", "ok")]);
        Check(upd.Count == 2 && upd[0].Key == "bios-changed" && upd[0].Detail.Contains("1.A0") && upd[0].Detail.Contains("1.B0")
              && upd[1].Key == "was-ram" && upd[1].Level == "warn", "BIOS actualizada y RAM a velocidad de fábrica: se dicen las dos cosas");
        Check(Compare(snap, Fw("1.A0"), [C("ram", "info")]).Count == 0, "RAM sin leer (info): no cuenta como cambio");
        var kept = Next(snap, Fw("1.A0"), [C("ram", "info"), C("tpm", "ok")]);
        Check(kept.Levels["ram"] == "ok", "una lectura fallida no borra el «estaba bien» guardado");
        Check(Compare(Next(null, Fw("1.A0"), [C("ram", "warn")]), Fw("1.A0"), [C("ram", "warn")]).Count == 0, "si ya estaba mal antes, no se repite como cambio");
        Check(Compare(snap, Fw("1.A0"), [C("virt", "warn")]).Count == 0, "la virtualización no se vigila (es opcional)");
        Check(Parse("{no es json") is null && Parse("null") is null && Parse("{\"Version\":null}") is null, "foto rota o vacía = como si no hubiera");
        var p = Parse(JsonSerializer.Serialize(new Snap(new string('x', 500) + "\u0007", null,
                  new() { ["ram"] = "ok", ["otra"] = "ok", ["tpm"] = "rojo" })));
        Check(p is not null && p.Version.Length == MaxText && !p.Version.Contains('\u0007') && p.Levels.Count == 1 && p.Levels["ram"] == "ok",
              "foto manipulada: texto recortado, sin caracteres de control, solo claves y niveles conocidos");
        Check(Compare(snap, Fw(""), good).Count == 0, "versión sin leer: no se anuncia cambio de BIOS");
        return string.Join(Environment.NewLine, log);
    }
}
