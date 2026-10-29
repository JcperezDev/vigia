using System.Runtime.InteropServices;
using System.Text.Json;

namespace Vigia;

public record DisplayMode(int Width, int Height, int Hz);
public record DisplayStatus(DisplayMode? Native, DisplayMode? Current, bool Stretched, List<DisplayMode> Options, DisplayMode? Saved)
{
    /// <summary>Las de la lista de Vigía que tu tarjeta aún no tiene: se crean en su panel al aplicarlas.</summary>
    public List<DisplayMode> Extra { get; init; } = [];
    /// <summary>«nvidia», «amd» u «other»: si es otra, las de Extra hay que crearlas a mano.</summary>
    public string Gpu { get; init; } = "other";
    /// <summary>Hay Fortnite en este usuario (para ofrecer dejarlo en ventana a pantalla completa).</summary>
    public bool Fortnite { get; init; }
}
public record DisplayDone(bool Ok, string Message, bool Pending);

/// <summary>
/// Resolución estirada para juegos: cambia la pantalla principal a una resolución más estrecha (4:3, 5:4, 16:10)
/// con escalado «estirar». Guarda la que tenías y la devuelve con un clic.
/// Al aplicar hay 15 s para confirmar; si no, vuelve sola (por si la pantalla se queda negra).
/// </summary>
public static class Display
{
    static string BackupPath => Path.Combine(Settings.Dir, "display.json");

    /// <summary>
    /// Las estiradas que más se usan en juegos competitivos. Solo se ofrecen las que caben en tu pantalla
    /// y son más estrechas que ella (si no, no se «estira» nada).
    /// </summary>
    static readonly (int W, int H)[] Presets =
    [
        // 4:3
        (1920, 1440), (1856, 1392), (1792, 1344), (1600, 1200), (1440, 1080), (1400, 1050), (1280, 960),
        (1152, 864), (1024, 768), (960, 720), (800, 600), (640, 480),
        // 5:4
        (1800, 1440), (1350, 1080), (1280, 1024), (1200, 960), (1080, 864),
        // 16:10
        (2304, 1440), (1920, 1200), (1728, 1080), (1680, 1050), (1440, 900), (1280, 800),
        // 3:2
        (2160, 1440), (1620, 1080), (1440, 960),
        // a medida (de lo más usado en Fortnite y CS2)
        (1750, 1080), (1700, 1080), (1650, 1080), (1600, 1080), (1568, 1080), (1500, 1080), (1280, 1080),
        (1080, 1080), (2048, 1440), (1440, 1440), (1154, 864), (1280, 880), (1176, 664),
    ];

    public static DisplayStatus Status()
    {
        // la del escritorio guardada en el registro, no la del momento: un juego a pantalla completa
        // cambia la actual mientras está abierto y no queremos tomar esa como «la normal»
        var cur = Read(ENUM_REGISTRY_SETTINGS) ?? Read(ENUM_CURRENT_SETTINGS);
        var saved = LoadSaved();
        var native = saved ?? NativeGuess(cur);
        var opts = new List<DisplayMode>();
        if (native is not null)
        {
            var ratio = (double)native.Width / native.Height;
            for (var i = 0; ; i++)
            {
                var dm = NewDevMode();
                if (!EnumDisplaySettingsW(null, i, ref dm)) break;
                if (dm.dmBitsPerPel < 32 || dm.dmDisplayFrequency != native.Hz) continue;
                var r = (double)dm.dmPelsWidth / dm.dmPelsHeight;
                if (dm.dmPelsHeight < 720) continue;
                // las normales (misma forma que tu pantalla: 1920 × 1080, 2K, 4K si la tarjeta la ofrece) y las estiradas (más estrechas)
                var same = Math.Abs(r - ratio) < 0.02;
                if (!same && (dm.dmPelsHeight > native.Height || r > ratio - 0.05)) continue;
                var m = new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
                if (!opts.Contains(m)) opts.Add(m);
            }
        }
        opts = opts.OrderByDescending(m => m.Height).ThenByDescending(m => m.Width).ToList();
        var extra = new List<DisplayMode>();
        if (native is not null)
        {
            var ratio = (double)native.Width / native.Height;
            var have = new HashSet<(int, int)>();
            for (var i = 0; ; i++)
            {
                var dm = NewDevMode();
                if (!EnumDisplaySettingsW(null, i, ref dm)) break;
                if (dm.dmBitsPerPel >= 32 && dm.dmDisplayFrequency == native.Hz) have.Add((dm.dmPelsWidth, dm.dmPelsHeight));
            }
            foreach (var (w, h) in Presets)
            {
                if (w > native.Width || h > native.Height || (double)w / h > ratio - 0.05) continue;
                if (opts.Any(o => o.Width == w && o.Height == h) || extra.Any(o => o.Width == w && o.Height == h)) continue;
                // la tarjeta ya la tiene a tu frecuencia: va con las listas; si no, se crea al aplicarla
                (have.Contains((w, h)) ? opts : extra).Add(new DisplayMode(w, h, native.Hz));
            }
            // las normales de siempre con la forma de tu pantalla (en una 16:9: 720p, 900p, 1080p) y la tuya
            foreach (var h in new[] { 720, 768, 900, 1080, 1200, 1440, 1600, 2160 })
            {
                // solo si sale exacta (en 16:9, 1200 o 768 darían anchos raros)
                var wx = h * native.Width / (double)native.Height;
                var w = (int)Math.Round(wx);
                if (Math.Abs(wx - w) > 0.01 || w % 8 != 0 || h > native.Height || w > native.Width) continue;
                if (opts.Any(o => o.Width == w && o.Height == h) || extra.Any(o => o.Width == w && o.Height == h)) continue;
                (have.Contains((w, h)) || (w == native.Width && h == native.Height) ? opts : extra).Add(new DisplayMode(w, h, native.Hz));
            }
            opts = opts.OrderByDescending(m => m.Height).ThenByDescending(m => m.Width).ToList();
            extra = extra.OrderByDescending(m => m.Height).ThenByDescending(m => m.Width).ToList();
        }
        var stretched = saved is not null && cur is not null && (cur.Width != saved.Width || cur.Height != saved.Height);
        return new DisplayStatus(native, cur, stretched, opts, saved)
        {
            Extra = extra,
            Fortnite = File.Exists(Games.FortnitePath),
            Gpu = GpuModes.Detect() switch { GpuModes.Vendor.Nvidia => "nvidia", GpuModes.Vendor.Amd => "amd", _ => "other" },
        };
    }

    /// <summary>Misma forma que la pantalla (16:9 en una 16:9): no se estira nada.</summary>
    public static bool SameShape(DisplayMode a, DisplayMode b) => Math.Abs((double)a.Width / a.Height - (double)b.Width / b.Height) < 0.02;

    /// <summary>La frecuencia más alta que ofrece la pantalla a esa resolución.</summary>
    public static int MaxHz(int w, int h)
    {
        var max = 0;
        for (var i = 0; ; i++)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettingsW(null, i, ref dm)) break;
            if (dm.dmPelsWidth == w && dm.dmPelsHeight == h && dm.dmBitsPerPel >= 32) max = Math.Max(max, dm.dmDisplayFrequency);
        }
        return max;
    }

    /// <summary>La resolución nativa: la más grande que ofrece la pantalla a la frecuencia actual.</summary>
    static DisplayMode? NativeGuess(DisplayMode? cur)
    {
        if (cur is null) return null;
        DisplayMode best = cur;
        for (var i = 0; ; i++)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettingsW(null, i, ref dm)) break;
            if (dm.dmDisplayFrequency == cur.Hz && dm.dmPelsWidth * dm.dmPelsHeight > best.Width * best.Height)
                best = new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
        }
        return best;
    }

    /// <param name="games">además deja Fortnite en «Ventana a pantalla completa» a esa resolución (con el juego cerrado)</param>
    public static DisplayDone Stretch(int w, int h, bool games = false)
    {
        var st = Status();
        // elegir la normal es lo mismo que «Volver a la normal»
        if (st.Saved is { } sv && sv.Width == w && sv.Height == h) return Restore();
        if (st.Saved is null && st.Current is { } now0 && now0.Width == w && now0.Height == h)
            return new(true, $"Ya estás en {w} × {h}.", false);
        var want = Want(st, w, h, out var why, out var missing);
        var created = "";
        if (want is null && missing && (st.Saved ?? st.Native) is { } normal)
        {
            // la tarjeta no la tiene: se crea en su panel (NVIDIA › Cambiar resolución › Personalizar, o AMD › Resoluciones personalizadas)
            var err = GpuModes.Create(w, h, normal.Hz, normal.Width, normal.Height);
            if (err is null)
            {
                st = Status();
                want = Want(st, w, h, out why, out _);
                created = st.Gpu == "amd" ? " La creé en AMD Software (Resoluciones personalizadas)." : " La creé en el Panel de control de NVIDIA (Cambiar resolución › Personalizar).";
            }
            else why = $"No pude crear {w} × {h} en tu tarjeta gráfica: {err}. {Manual(w, h, normal.Hz)}";
        }
        if (want is null) return new(false, why!, false);
        var cur = st.Current ?? throw new InvalidOperationException("No pude leer la resolución actual.");
        // se guarda la de antes solo la primera vez: si ya está estirada, la original sigue siendo la guardada
        if (st.Saved is null) Save(new DisplayMode(cur.Width, cur.Height, cur.Hz));
        if (Backups.Save("pantalla", $"Resolución {w} × {h}", [Backups.Item.Display(cur.Width, cur.Height, cur.Hz)]) is null)
            return new(false, "No pude guardar la copia de seguridad; no cambié nada.", false);
        var r = Change(want.Width, want.Height, want.Hz, DMDFO_STRETCH);
        if (r != 0)
        {
            if (st.Saved is null) File.Delete(BackupPath);
            return new(false, $"Windows no aceptó {w} × {h} ({Why(r)}).", false);
        }
        var game = games ? GameNote(Games.FortniteWindow(w, h)) : "";
        var normal0 = st.Saved ?? st.Native;
        return normal0 is not null && SameShape(new DisplayMode(w, h, 0), normal0)
            ? new(true, $"Pantalla en {w} × {h} a {want.Hz} Hz.{created}{game}", true)
            : new(true, $"Pantalla en {w} × {h} estirada a {want.Hz} Hz.{created}{game}", true);
    }

    static string GameNote(Games.Done? d) => d is null ? "" : d.Ok ? $" {d.Message}" : $" Fortnite: {d.Message}";

    static string Manual(int w, int h, int hz) =>
        $"Créala a mano: Panel de control de NVIDIA › Cambiar resolución › Personalizar › Crear resolución personalizada ({w} × {h}, {hz} Hz, sincronización Automática, Probar), " +
        "o AMD Software › Pantalla › Resoluciones personalizadas, o el panel de Intel. Luego vuelve aquí.";

    public static DisplayDone Restore()
    {
        var saved = LoadSaved();
        if (saved is null) return new(false, "No hay una resolución guardada.", false);
        if (Status().Current is { } now) Backups.Save("pantalla", "Volver a la resolución normal", [Backups.Item.Display(now.Width, now.Height, now.Hz)]);
        var r = Change(saved.Width, saved.Height, saved.Hz, DMDFO_DEFAULT);
        if (r != 0) return new(false, $"Windows no aceptó volver a {saved.Width} × {saved.Height} ({Why(r)}).", false);
        File.Delete(BackupPath);
        // si Vigía dejó Fortnite en la estirada, vuelve a la normal también ahí (si no lo tocó, no hace nada)
        var game = Games.FortniteWindow(saved.Width, saved.Height, onlyIfOurs: true);
        return new(true, $"Pantalla de vuelta en {saved.Width} × {saved.Height} a {saved.Hz} Hz.{(game is { Ok: true } ? " " + game.Message : "")}", false);
    }

    /// <summary>Para restaurar una copia: pone ese modo (sin estirar si es la nativa). Devuelve null si salió bien.</summary>
    internal static string? SetMode(int w, int h, int hz)
    {
        var native = Status().Native;
        var stretch = native is not null && (native.Width != w || native.Height != h);
        var r = Change(w, h, hz, stretch ? DMDFO_STRETCH : DMDFO_DEFAULT);
        if (r != 0) return $"Windows no aceptó {w} × {h} ({Why(r)}).";
        if (!stretch && File.Exists(BackupPath)) File.Delete(BackupPath);
        return null;
    }

    /// <summary>
    /// La de la lista, o una que escribe el usuario: entre 640 × 480 y la normal, a la frecuencia normal.
    /// Si el driver no la tiene, Windows la rechaza y se explica cómo crearla en el panel de la tarjeta.
    /// </summary>
    static DisplayMode? Want(DisplayStatus st, int w, int h, out string? why, out bool missing)
    {
        why = null; missing = false;
        var m = st.Options.FirstOrDefault(o => o.Width == w && o.Height == h);
        if (m is not null) return m;
        var normal = st.Saved ?? st.Native;
        if (normal is null) { why = "No pude leer la pantalla principal."; return null; }
        if (w < 640 || h < 480 || w > normal.Width || h > normal.Height)
        { why = $"Pon un tamaño entre 640 × 480 y {normal.Width} × {normal.Height}."; return null; }
        m = new DisplayMode(w, h, normal.Hz);
        if (Change(w, h, normal.Hz, DMDFO_STRETCH, testOnly: true) == 0) return m;
        missing = true;
        why = $"Tu tarjeta gráfica no ofrece {w} × {h} a {normal.Hz} Hz. {Manual(w, h, normal.Hz)}";
        return null;
    }

    public static string Test(int w, int h)
    {
        var st = Status();
        var m = Want(st, w, h, out var why, out var missing);
        if (m is null) return missing ? $"FALTA (se crearía en la gráfica: {GpuModes.Probe(w, h, (st.Saved ?? st.Native)!.Hz)})" : $"NO: {why}";
        var r = Change(m.Width, m.Height, m.Hz, DMDFO_STRETCH, testOnly: true);
        return r == 0 ? $"OK {w}x{h}@{m.Hz} estirado" : $"NO {Why(r)}";
    }

    static int Change(int w, int h, int hz, int fixedOutput, bool testOnly = false)
    {
        var dm = NewDevMode();
        dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmDisplayFrequency = hz; dm.dmBitsPerPel = 32;
        dm.dmDisplayFixedOutput = fixedOutput;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL | DM_DISPLAYFIXEDOUTPUT;
        var t = ChangeDisplaySettingsExW(null, ref dm, 0, CDS_TEST, 0);
        if (t != 0)
        {
            // algunos drivers no aceptan el modo de escalado: se prueba sin él
            dm.dmFields &= ~DM_DISPLAYFIXEDOUTPUT;
            t = ChangeDisplaySettingsExW(null, ref dm, 0, CDS_TEST, 0);
            if (t != 0) return t;
        }
        if (testOnly) return 0;
        return ChangeDisplaySettingsExW(null, ref dm, 0, CDS_UPDATEREGISTRY, 0);
    }

    static string Why(int r) => r switch
    {
        1 => "pide reiniciar", -1 => "el driver falló", -2 => "modo no compatible", -3 => "no se pudo guardar en el registro",
        -4 => "parámetros no válidos", -5 => "opciones no válidas", -6 => "otro programa tiene la pantalla", _ => $"código {r}",
    };

    static DisplayMode? Read(int which)
    {
        var dm = NewDevMode();
        return EnumDisplaySettingsW(null, which, ref dm) ? new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency) : null;
    }

    static DisplayMode? LoadSaved()
    {
        try { return JsonSerializer.Deserialize<DisplayMode>(File.ReadAllText(BackupPath), Program.Json); }
        catch { return null; }
    }

    static void Save(DisplayMode m)
    {
        Directory.CreateDirectory(Settings.Dir);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(m, Program.Json));
    }

    static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };

    const int ENUM_CURRENT_SETTINGS = -1, ENUM_REGISTRY_SETTINGS = -2;
    const int DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000, DM_DISPLAYFIXEDOUTPUT = 0x20000000;
    const int DMDFO_DEFAULT = 0, DMDFO_STRETCH = 1;
    const int CDS_UPDATEREGISTRY = 1, CDS_TEST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string? device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
}
