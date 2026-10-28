using System.Runtime.InteropServices;
using System.Text.Json;

namespace Vigia;

public record DisplayMode(int Width, int Height, int Hz);
public record DisplayStatus(DisplayMode? Native, DisplayMode? Current, bool Stretched, List<DisplayMode> Options, DisplayMode? Saved);
public record DisplayDone(bool Ok, string Message, bool Pending);

/// <summary>
/// Resolución estirada para juegos: cambia la pantalla principal a una resolución más estrecha (4:3, 5:4, 16:10)
/// con escalado «estirar». Guarda la que tenías y la devuelve con un clic.
/// Al aplicar hay 15 s para confirmar; si no, vuelve sola (por si la pantalla se queda negra).
/// </summary>
public static class Display
{
    static string BackupPath => Path.Combine(Settings.Dir, "display.json");

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
                if (dm.dmPelsHeight > native.Height || dm.dmPelsHeight < 720 || r > ratio - 0.05) continue;
                var m = new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
                if (!opts.Contains(m)) opts.Add(m);
            }
        }
        opts = opts.OrderByDescending(m => m.Height).ThenByDescending(m => m.Width).ToList();
        var stretched = saved is not null && cur is not null && (cur.Width != saved.Width || cur.Height != saved.Height);
        return new DisplayStatus(native, cur, stretched, opts, saved);
    }

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

    public static DisplayDone Stretch(int w, int h)
    {
        var st = Status();
        var want = st.Options.FirstOrDefault(m => m.Width == w && m.Height == h);
        if (want is null) return new(false, $"La pantalla no ofrece {w} × {h} a {st.Native?.Hz} Hz.", false);
        var cur = st.Current ?? throw new InvalidOperationException("No pude leer la resolución actual.");
        // se guarda la de antes solo la primera vez: si ya está estirada, la original sigue siendo la guardada
        if (st.Saved is null) Save(new DisplayMode(cur.Width, cur.Height, cur.Hz));
        if (Backups.Save("pantalla", $"Resolución estirada {w} × {h}", [Backups.Item.Display(cur.Width, cur.Height, cur.Hz)]) is null)
            return new(false, "No pude guardar la copia de seguridad; no cambié nada.", false);
        var r = Change(want.Width, want.Height, want.Hz, DMDFO_STRETCH);
        if (r != 0)
        {
            if (st.Saved is null) File.Delete(BackupPath);
            return new(false, $"Windows no aceptó {w} × {h} ({Why(r)}).", false);
        }
        return new(true, $"Pantalla en {w} × {h} estirada a {want.Hz} Hz.", true);
    }

    public static DisplayDone Restore()
    {
        var saved = LoadSaved();
        if (saved is null) return new(false, "No hay una resolución guardada.", false);
        if (Status().Current is { } now) Backups.Save("pantalla", "Volver a la resolución normal", [Backups.Item.Display(now.Width, now.Height, now.Hz)]);
        var r = Change(saved.Width, saved.Height, saved.Hz, DMDFO_DEFAULT);
        if (r != 0) return new(false, $"Windows no aceptó volver a {saved.Width} × {saved.Height} ({Why(r)}).", false);
        File.Delete(BackupPath);
        return new(true, $"Pantalla de vuelta en {saved.Width} × {saved.Height} a {saved.Hz} Hz.", false);
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

    /// <summary>Solo pregunta al driver (CDS_TEST); no cambia la pantalla.</summary>
    public static string Test(int w, int h)
    {
        var st = Status();
        var m = st.Options.FirstOrDefault(o => o.Width == w && o.Height == h);
        if (m is null) return $"NO: {w}x{h} no está en la lista";
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
