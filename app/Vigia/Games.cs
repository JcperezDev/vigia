using System.Diagnostics;
using System.Text;

namespace Vigia;

/// <summary>
/// Perfiles de juego. Solo edita el archivo de configuración que el propio juego guarda en tu usuario
/// (nada de inyectar ni tocar el antitrampas). Siempre con el juego cerrado y con copia antes.
/// </summary>
public static class Games
{
    public record Setting(string Key, string Label, string Value, string? Want, bool Ok);
    public record FortniteView(bool Found, string Path, bool ReadOnly, bool Running, string Mode, bool ProfileApplied, List<Setting> Settings);
    public record Done(bool Ok, string Message);

    public static string FortnitePath => !Elevation.InBroker && Environment.GetEnvironmentVariable("VIGIA_FORTNITE_INI") is { Length: > 0 } p ? p
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini");

    /// <summary>
    /// El perfil competitivo de Vigía: quita lo que cuesta FPS y no ayuda a ver rivales.
    /// No toca distancia de visión, texturas, resolución ni el límite de FPS: eso es gusto de cada uno.
    /// </summary>
    static readonly (string Key, string Want, string Label)[] Profile =
    [
        ("bMotionBlur", "False", "Desenfoque de movimiento"),
        ("bShowGrass", "False", "Hierba"),
        ("bUseVSync", "False", "Sincronización vertical"),
        ("bRayTracing", "False", "Trazado de rayos"),
        ("bUseNanite", "False", "Nanite (geometría detallada)"),
        ("LowInputLatencyModeIsEnabled", "True", "Baja latencia (NVIDIA Reflex)"),
        ("sg.ShadowQuality", "0", "Sombras"),
        ("sg.GlobalIlluminationQuality", "0", "Iluminación global"),
        ("sg.ReflectionQuality", "0", "Reflejos"),
        ("sg.PostProcessQuality", "0", "Posprocesado"),
        ("sg.EffectsQuality", "0", "Efectos"),
        ("sg.FoliageQuality", "0", "Vegetación"),
        ("sg.AntiAliasingQuality", "0", "Suavizado"),
    ];

    /// <summary>Solo para mostrar: lo que el usuario elige y Vigía no cambia.</summary>
    static readonly (string Key, string Label)[] Info =
    [
        ("FrameRateLimit", "Límite de FPS"),
        ("FrontendFrameRateLimit", "Límite de FPS en menús"),
        ("ResolutionSizeX", "Resolución (ancho)"),
        ("ResolutionSizeY", "Resolución (alto)"),
        ("sg.ViewDistanceQuality", "Distancia de visión"),
        ("sg.TextureQuality", "Texturas"),
        ("sg.ResolutionQuality", "Escala de resolución 3D"),
    ];

    public static bool FortniteRunning()
    {
        foreach (var n in new[] { "FortniteClient-Win64-Shipping", "FortniteClient-Win64-Shipping_EAC", "FortniteClient-Win64-Shipping_BE", "FortniteClient-Win64-Shipping_EAC_EOS" })
        {
            var ps = Process.GetProcessesByName(n);
            var any = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            if (any) return true;
        }
        return false;
    }

    // ---------- ini por líneas: se respeta todo lo demás tal cual ----------
    sealed class Ini
    {
        public readonly List<string> Lines;
        public readonly Encoding Enc;
        public readonly bool Bom;
        public Ini(byte[] bytes)
        {
            Bom = bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF));
            Enc = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? new UnicodeEncoding(false, true) : new UTF8Encoding(Bom);
            var text = Enc.GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
            Lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        }
        int Find(string key, string? section = null)
        {
            string? cur = null;
            for (int i = 0; i < Lines.Count; i++)
            {
                var l = Lines[i].Trim();
                if (l.StartsWith('[') && l.EndsWith(']')) { cur = l[1..^1]; continue; }
                if ((section is null || cur == section) && l.StartsWith(key + "=", StringComparison.Ordinal)) return i;
            }
            return -1;
        }
        public string? Get(string key, string? section = null) { var i = Find(key, section); return i < 0 ? null : Lines[i].Trim()[(key.Length + 1)..]; }
        /// <summary>Cambia una clave que ya existe. Devuelve false si no está (no se inventan claves).</summary>
        public bool Set(string key, string value, string? section = null)
        {
            var i = Find(key, section);
            if (i < 0) return false;
            Lines[i] = $"{key}={value}";
            return true;
        }
        /// <summary>Cambia o añade la clave dentro de su sección (la crea al final si no existe).</summary>
        public void Put(string section, string key, string value)
        {
            if (Set(key, value, section)) return;
            var h = Lines.FindIndex(l => l.Trim() == $"[{section}]");
            if (h < 0)
            {
                while (Lines.Count > 0 && Lines[^1].Trim() == "") Lines.RemoveAt(Lines.Count - 1);
                Lines.Add(""); Lines.Add($"[{section}]"); Lines.Add($"{key}={value}"); Lines.Add("");
                return;
            }
            Lines.Insert(h + 1, $"{key}={value}");
        }
        public byte[] Bytes()
        {
            var body = Enc.GetBytes(string.Join("\r\n", Lines));
            var pre = Bom ? Enc.GetPreamble() : [];
            return [.. pre, .. body];
        }
    }

    const string RhiSection = "D3DRHIPreference";

    static string ModeOf(Ini ini)
    {
        var rhi = ini.Get("PreferredRHI", RhiSection)?.ToLowerInvariant();
        var fl = ini.Get("PreferredFeatureLevel", RhiSection)?.ToLowerInvariant();
        if (fl == "es31") return rhi == "dx11" ? "Rendimiento (antiguo, DX11)" : "Rendimiento";
        return rhi switch { "dx11" => "DirectX 11", "dx12" => "DirectX 12", null or "" => "Sin elegir", _ => rhi };
    }

    public static FortniteView Fortnite()
    {
        var path = FortnitePath;
        if (!File.Exists(path)) return new(false, path, false, FortniteRunning(), "", false, []);
        var ini = new Ini(File.ReadAllBytes(path));
        var list = new List<Setting>();
        foreach (var (k, want, label) in Profile)
        {
            var v = ini.Get(k);
            if (v is null) continue;   // esta versión del juego no la tiene
            list.Add(new(k, label, v, want, Same(v, want)));
        }
        foreach (var (k, label) in Info)
            if (ini.Get(k) is { } v) list.Add(new(k, label, v, null, true));
        var ro = (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        // «aplicado» solo si reconoce al menos una clave del perfil y todas están: una versión sin ninguna no cuenta como hecha
        var mine = list.Where(s => s.Want is not null).ToList();
        return new(true, path, ro, FortniteRunning(), ModeOf(ini), mine.Count > 0 && mine.All(s => s.Ok), list);
    }

    static bool Same(string v, string want) =>
        string.Equals(v, want, StringComparison.OrdinalIgnoreCase) ||
        (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
         double.TryParse(want, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) && a == b);

    /// <summary>
    /// Aplica el perfil competitivo y/o el modo de render ("perf" = Rendimiento, "dx12" = DirectX 12, null = no cambiar).
    /// Con el juego cerrado y copia antes. Además quita el «solo lectura» para que el juego pueda guardar lo que cambies dentro.
    /// </summary>
    public static Done ApplyFortnite(bool profile, string? mode)
    {
        var path = FortnitePath;
        if (!File.Exists(path)) return new(false, "No encontré la configuración de Fortnite. Abre el juego una vez y vuelve.");
        if (FortniteRunning()) return new(false, "Cierra Fortnite primero: si está abierto, al salir guarda encima y se pierde el cambio.");
        var ini = new Ini(File.ReadAllBytes(path));
        var changed = new List<string>();
        if (profile)
            foreach (var (k, want, label) in Profile)
                if (ini.Get(k) is { } v && !Same(v, want) && ini.Set(k, want)) changed.Add(label);
        if (mode is "perf" or "dx12")
        {
            var before = ModeOf(ini);
            ini.Put(RhiSection, "PreferredRHI", "dx12");
            ini.Put(RhiSection, "PreferredFeatureLevel", mode == "perf" ? "es31" : "sm6");
            if (ModeOf(ini) != before) changed.Add(mode == "perf" ? "Modo Rendimiento" : "Modo DirectX 12");
        }
        var ro = (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        if (changed.Count == 0 && !ro) return new(true, "Ya estaba así: no hubo nada que cambiar.");
        if (Backups.Save("juegos", changed.Count > 0 ? $"Fortnite: {string.Join(", ", changed)}" : "Fortnite: quitar solo lectura",
                [Backups.Item.File(path, "Configuración de Fortnite")]) is null)
            return new(false, "No pude guardar la copia de seguridad; no cambié nada.");
        try
        {
            if (ro) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            if (changed.Count > 0)
            {
                var tmp = path + ".vigia.tmp";
                File.WriteAllBytes(tmp, ini.Bytes());
                File.Move(tmp, path, true);
            }
        }
        catch (Exception e) { return new(false, $"No pude escribir la configuración: {e.Message}"); }
        var what = changed.Count == 0 ? "Listo: Fortnite ya puede guardar tus cambios (quité el «solo lectura»)."
            : $"Listo: {changed.Count} {(changed.Count == 1 ? "cambio" : "cambios")}. Se nota al abrir Fortnite.";
        return new(true, what);
    }

    static string WindowMark => System.IO.Path.Combine(Settings.Dir, "fortnite-ventana.txt");

    /// <summary>
    /// Deja Fortnite en «Ventana a pantalla completa» a w × h (la de la pantalla estirada), para que abra igual que el
    /// escritorio y no pelee con la resolución. Solo el archivo de configuración, con el juego cerrado y copia antes.
    /// Devuelve null si no hay Fortnite. Con onlyIfOurs solo actúa si fue Vigía quien lo dejó así.
    /// </summary>
    public static Done? FortniteWindow(int w, int h, bool onlyIfOurs = false)
    {
        try
        {
            if (onlyIfOurs && !File.Exists(WindowMark)) return null;
            var path = FortnitePath;
            if (!File.Exists(path)) return null;
            if (FortniteRunning()) return new(false, "estaba abierto y no lo toqué; ciérralo y vuelve a aplicar para dejarlo en ventana a pantalla completa.");
            var ini = new Ini(File.ReadAllBytes(path));
            var changed = false;
            foreach (var (k, v) in new[]
            {
                ("FullscreenMode", "1"), ("LastConfirmedFullscreenMode", "1"), ("PreferredFullscreenMode", "1"),
                ("ResolutionSizeX", $"{w}"), ("ResolutionSizeY", $"{h}"),
                ("LastUserConfirmedResolutionSizeX", $"{w}"), ("LastUserConfirmedResolutionSizeY", $"{h}"),
                ("DesiredScreenWidth", $"{w}"), ("DesiredScreenHeight", $"{h}"),
                ("LastUserConfirmedDesiredScreenWidth", $"{w}"), ("LastUserConfirmedDesiredScreenHeight", $"{h}"),
            })
                if (ini.Get(k) is { } cur && cur != v && ini.Set(k, v)) changed = true;
            if (!changed)
            {
                if (onlyIfOurs) File.Delete(WindowMark);
                return new(true, $"Fortnite ya estaba en ventana a pantalla completa a {w} × {h}.");
            }
            if (Backups.Save("juegos", $"Fortnite: ventana a pantalla completa {w} × {h}", [Backups.Item.File(path, "Configuración de Fortnite")]) is null)
                return new(false, "no pude guardar la copia de seguridad; no lo toqué.");
            var ro = (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
            if (ro) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            var tmp = path + ".vigia.tmp";
            File.WriteAllBytes(tmp, ini.Bytes());
            File.Move(tmp, path, true);
            if (ro) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);   // se deja como estaba
            if (onlyIfOurs) File.Delete(WindowMark);
            else { Directory.CreateDirectory(Settings.Dir); File.WriteAllText(WindowMark, $"{w}x{h}"); }
            return new(true, onlyIfOurs ? $"Fortnite también vuelve a {w} × {h}." : $"Fortnite queda en ventana a pantalla completa a {w} × {h}.");
        }
        catch (Exception e) { return new(false, $"no pude cambiar su configuración ({e.Message})."); }
    }

    // ---------- pruebas (sobre una copia, nunca el archivo real) ----------
    public static string SelfTest()
    {
        var o = new List<string>();
        void Check(bool ok, string what) => o.Add($"{(ok ? "OK  " : "FAIL")} juegos: {what}");
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vigia-games-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "GameUserSettings.ini");
        var old = Environment.GetEnvironmentVariable("VIGIA_FORTNITE_INI");
        try
        {
            Environment.SetEnvironmentVariable("VIGIA_FORTNITE_INI", path);
            var src = "[/Script/FortniteGame.FortGameUserSettings]\r\nbMotionBlur=True\r\nbShowGrass=True\r\nFrameRateLimit=240.000000\r\nCustomThing=Keep me\r\n" +
                      "[ScalabilityGroups]\r\nsg.ShadowQuality=3\r\nsg.ViewDistanceQuality=2\r\n\r\n[D3DRHIPreference]\r\nPreferredRHI=dx12\r\nPreferredFeatureLevel=sm6\r\n";
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(src));
            File.SetAttributes(path, FileAttributes.ReadOnly);
            var v = Fortnite();
            Check(v.Found && v.ReadOnly && v.Mode == "DirectX 12" && !v.ProfileApplied, $"leer: modo {v.Mode}, solo lectura {v.ReadOnly}");
            var r = ApplyFortnite(true, "perf");
            var t = File.ReadAllText(path);
            Check(r.Ok, $"aplicar -> {r.Message}");
            Check(t.Contains("bMotionBlur=False") && t.Contains("bShowGrass=False") && t.Contains("sg.ShadowQuality=0"), "perfil escrito");
            Check(t.Contains("sg.ViewDistanceQuality=2") && t.Contains("CustomThing=Keep me") && t.Contains("FrameRateLimit=240.000000"), "lo demás intacto");
            Check(!t.Contains("bUseVSync"), "no inventa claves");
            Check(t.Contains("PreferredFeatureLevel=es31") && Fortnite().Mode == "Rendimiento", "modo Rendimiento");
            Check((File.GetAttributes(path) & FileAttributes.ReadOnly) == 0, "quita solo lectura");
            Check(Fortnite().ProfileApplied, "perfil reconocido como aplicado");
            var again = ApplyFortnite(true, "perf");
            Check(again.Ok && again.Message.StartsWith("Ya estaba"), "aplicar dos veces no cambia nada");
            // volver con la copia
            var bk = Backups.List().FirstOrDefault(b => b.Kind == "juegos");
            var rs = bk is null ? null : Backups.Restore(bk.Id);
            Check(rs is { Ok: true } && File.ReadAllText(path).Contains("PreferredFeatureLevel=sm6") && File.ReadAllText(path).Contains("bMotionBlur=True"), $"restaurar copia -> {rs?.Message}");
        }
        catch (Exception e) { Check(false, "excepción " + e.Message); }
        finally
        {
            Environment.SetEnvironmentVariable("VIGIA_FORTNITE_INI", old);
            try { foreach (var f in Directory.GetFiles(dir)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(dir, true); } catch { }
        }
        return string.Join(Environment.NewLine, o);
    }
}
