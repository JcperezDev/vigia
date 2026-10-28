using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Vigia;

public record WallInfo(bool Ours, string? Current, bool Slideshow, string? BackupId);
public record WallDone(bool Ok, string Message);

/// <summary>
/// Fondo de pantalla de VigíaOS (opcional, desde Apps de Windows). Antes de ponerlo guarda el fondo de ahora:
/// los valores de registro y una copia de la imagen, por si el archivo original se borra o es el
/// TranscodedWallpaper que Windows sobrescribe. «Volver a como estaba» lo devuelve.
/// </summary>
public static class Wallpaper
{
    const string Desk = @"HKCU\Control Panel\Desktop";
    static string Dir => Path.Combine(Settings.Dir, "fondo");
    static string Ours => Path.Combine(Dir, "vigiaos.jpg");
    static string Asset => Path.Combine(AppContext.BaseDirectory, "assets", "vigiaos.jpg");
    static string Transcoded => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(int action, int param, string? value, int flags);
    const int SPI_SETDESKWALLPAPER = 20, SPIF_UPDATEINIFILE = 1, SPIF_SENDCHANGE = 2;

    static string? Current()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return k?.GetValue("WallPaper") as string;
    }

    static bool SlideshowOn()
    {
        // 1 = presentación de imágenes o Windows Spotlight
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
        return k?.GetValue("BackgroundType") is int t && t != 0;
    }

    public static WallInfo Status()
    {
        var cur = Current();
        return new(cur is not null && string.Equals(Path.GetFullPath(cur), Path.GetFullPath(Ours), StringComparison.OrdinalIgnoreCase),
                   string.IsNullOrEmpty(cur) ? null : Path.GetFileName(cur), SlideshowOn(),
                   Backups.List().FirstOrDefault(b => b.Kind == "fondo" && b.RestoredAt is null)?.Id);
    }

    public static WallDone Apply()
    {
        if (!File.Exists(Asset)) return new(false, "Falta la imagen del fondo en la carpeta de Vigía; reinstala Vigía.");
        if (Status().Ours) return new(true, "Ya tienes puesto el fondo de VigíaOS.");

        var cur = Current();
        var saved = SaveCopy(cur);

        // la imagen va primera: al restaurar se deshace en orden inverso y el registro tiene que estar antes de aplicar la imagen
        var items = new List<JsonObject>
        {
            new() { ["t"] = "wallpaper", ["path"] = cur, ["saved"] = saved, ["label"] = "Fondo de pantalla" + (string.IsNullOrEmpty(cur) ? "" : $" ({Path.GetFileName(cur)})") },
            Backups.Item.Reg(Desk, "WallpaperStyle", "Ajuste del fondo"),
            Backups.Item.Reg(Desk, "TileWallpaper", "Fondo en mosaico"),
        };
        if (Backups.Save("fondo", "Poner el fondo de VigíaOS", items) is null)
            return new(false, "No pude guardar la copia de seguridad; no cambié el fondo.");

        try
        {
            Directory.CreateDirectory(Dir);
            File.Copy(Asset, Ours, true);
            using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
            {
                k.SetValue("WallpaperStyle", "10");   // rellenar: sin bandas en pantallas que no son 16:9
                k.SetValue("TileWallpaper", "0");
            }
            if (!Set(Ours)) return new(false, $"Windows no aceptó el fondo (código {Marshal.GetLastWin32Error()}).");
        }
        catch (Exception e) { return new(false, e.Message); }
        return new(true, SlideshowOn()
            ? "Fondo de VigíaOS puesto. Tenías presentación o Spotlight: si vuelves al de antes queda tu última imagen, y la presentación se activa otra vez en Configuración → Personalización."
            : "Fondo de VigíaOS puesto. Si quieres el de antes, dale a «Volver al fondo de antes».");
    }

    /// <summary>Copia la imagen de fondo actual (o la caché de Windows si el archivo ya no está). Null si no hay nada que copiar.</summary>
    static string? SaveCopy(string? cur)
    {
        var src = !string.IsNullOrEmpty(cur) && File.Exists(cur) ? cur : File.Exists(Transcoded) ? Transcoded : null;
        if (src is null) return null;
        try
        {
            Directory.CreateDirectory(Path.Combine(Dir, "antes"));
            var dst = Path.Combine(Dir, "antes", $"{DateTime.Now:yyyyMMdd-HHmmss-fff}{(Path.GetExtension(src) is { Length: > 0 } e ? e : ".jpg")}");
            File.Copy(src, dst, true);
            // solo las 30 últimas: cada copia del fondo puede pesar varios MB
            foreach (var old in Directory.GetFiles(Path.GetDirectoryName(dst)!).OrderByDescending(f => f).Skip(30)) File.Delete(old);
            return dst;
        }
        catch { return null; }
    }

    static bool Set(string path) => SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

    /// <summary>Usado por Backups.Restore: vuelve al archivo de antes; si ya no existe, a la copia guardada.</summary>
    public static void Restore(JsonObject it)
    {
        var path = (string?)it["path"];
        var saved = (string?)it["saved"];
        string target;
        if (!string.IsNullOrEmpty(path) && File.Exists(path)
            && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(Transcoded), StringComparison.OrdinalIgnoreCase)) target = path;
        else if (saved is not null && File.Exists(saved)) target = saved;
        else if (string.IsNullOrEmpty(path)) target = "";   // no había fondo: color sólido
        else throw new InvalidOperationException("ya no existe la imagen de antes ni su copia.");
        if (!Set(target)) throw new InvalidOperationException($"Windows no aceptó el fondo (código {Marshal.GetLastWin32Error()}).");
    }

    public static JsonObject Capture(JsonObject it)
    {
        var cur = Current();
        return new() { ["t"] = "wallpaper", ["path"] = cur, ["saved"] = SaveCopy(cur), ["label"] = (string?)it["label"] ?? "Fondo de pantalla" };
    }
}
