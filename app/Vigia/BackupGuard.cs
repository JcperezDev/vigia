using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>
/// Las copias viven en %LOCALAPPDATA%\Vigia\copias: tu usuario (y cualquier programa tuyo) puede editarlas.
/// Restaurar lo hace el ayudante con permisos de administrador, así que cada copia se trata como dato NO confiable:
///
///  1. Esquema: tipos conocidos, campos con el tipo correcto, tamaños acotados. Lo desconocido se rechaza.
///  2. Destinos en lista cerrada: claves de registro, servicios, archivos y tareas solo de los sitios donde Vigía
///     guarda cosas. Nada de rutas libres, «..», enlaces (junction o simbólicos) ni raíces de registro raras.
///  3. Sello (HMAC-SHA256): el ayudante sella cada copia que crea con una clave que solo leen SYSTEM y Administradores
///     (%ProgramData%\Vigia\claves\copias.key). El sello solo amplía lo permitido en tres casos de riesgo (volver a
///     poner una entrada de Run para todos los usuarios, un acceso en Inicio común o el bloqueo de SmartScreen);
///     el resto se valida igual con sello o sin él. Una copia vieja o editada pierde el sello, no la restauración.
///  4. Si algo no pasa, no se restaura NADA y se explica qué ítem falla.
/// Lo que ya está como en la copia no se toca (y no hace falta que pase la lista: no se escribe).
/// </summary>
public static class BackupGuard
{
    // ---------------------------------------------------------------- sello
    static string KeyDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vigia", "claves");
    internal static string? KeyDirOverride;   // pruebas
    static string KeyPath => Path.Combine(KeyDirOverride ?? KeyDir, "copias.key");
    static byte[]? _key;
    static readonly object KeyGate = new();

    /// <summary>La clave del sello. Solo con permisos de administrador; la crea con ACL cerrada si no existe.</summary>
    static byte[]? Key()
    {
        if (!Elevation.IsAdmin && KeyDirOverride is null) return null;
        lock (KeyGate)
        {
            if (_key is not null) return _key;
            try
            {
                var dir = new DirectoryInfo(KeyDirOverride ?? KeyDir);
                // la carpeta la pudo crear antes un usuario sin permisos (ProgramData deja crear carpetas): se rehace
                if (KeyDirOverride is null && (!dir.Exists || !LockedDown(dir)))
                {
                    if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) dir.Delete();
                    else if (dir.Exists) { TakeOver(dir); foreach (var f in dir.GetFiles()) f.Delete(); }
                    dir.Create();
                    Lock(dir);
                }
                else if (!dir.Exists) dir.Create();
                var f2 = new FileInfo(KeyPath);
                if (f2.Exists && (f2.Attributes & FileAttributes.ReparsePoint) == 0 && f2.Length == 32) return _key = File.ReadAllBytes(KeyPath);
                if (f2.Exists) f2.Delete();
                var k = RandomNumberGenerator.GetBytes(32);
                using (var fs = new FileStream(KeyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) fs.Write(k);
                return _key = k;
            }
            catch (Exception e) { UiMessage.Note("copias: sin clave de sello: " + e.Message); return null; }
        }
    }

    static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);

    static void Lock(DirectoryInfo dir)
    {
        var sec = new DirectorySecurity();
        sec.SetOwner(Admins);
        sec.SetAccessRuleProtection(true, false);
        const InheritanceFlags inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(sec);
    }

    static void TakeOver(DirectoryInfo dir)
    {
        var sec = dir.GetAccessControl();
        sec.SetOwner(Admins);
        dir.SetAccessControl(sec);
        Lock(dir);
    }

    /// <summary>Solo SYSTEM y Administradores, sin herencia, dueño de confianza, no es un enlace.</summary>
    static bool LockedDown(DirectoryInfo dir)
    {
        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        var sec = dir.GetAccessControl();
        if (!sec.AreAccessRulesProtected) return false;
        if (sec.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier o || !(o.Equals(Admins) || o.Equals(SystemSid))) return false;
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (r.AccessControlType == AccessControlType.Allow && !(r.IdentityReference.Equals(Admins) || r.IdentityReference.Equals(SystemSid))) return false;
        return true;
    }

    /// <summary>Lo que entra en el sello: la cuenta, el id, el tipo y los ítems (no «restoredAt», que cambia).</summary>
    static byte[] Material(JsonObject o)
    {
        string sid;
        using (var id = WindowsIdentity.GetCurrent()) sid = id.User?.Value ?? "";
        var sb = new StringBuilder();
        sb.Append("vigia-copia-v1\n").Append(sid).Append('\n')
          .Append((string?)o["id"]).Append('\n').Append((string?)o["kind"]).Append('\n')
          .Append(o["items"]?.ToJsonString() ?? "[]");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Sella la copia (solo el ayudante o Vigía con permisos). Sin clave, la copia queda sin sello.</summary>
    public static void Seal(JsonObject o)
    {
        o.Remove("seal");
        if (Key() is not { } k) return;
        o["seal"] = Convert.ToHexString(HMACSHA256.HashData(k, Material(o))).ToLowerInvariant();
    }

    public static bool Sealed(JsonObject o)
    {
        if ((string?)o["seal"] is not { Length: 64 } s || Key() is not { } k) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(s), HMACSHA256.HashData(k, Material(o))); }
        catch { return false; }
    }

    // ---------------------------------------------------------------- validación
    static readonly HashSet<string> Types = ["reg", "tweak", "task", "display", "hz", "file", "wallpaper", "appx"];
    static readonly Regex AppxRx = new(@"^[A-Za-z0-9][A-Za-z0-9.\-]{0,127}$", RegexOptions.CultureInvariant);
    static readonly Regex DeviceRx = new(@"^\\\\\.\\DISPLAY[0-9]{1,2}$", RegexOptions.CultureInvariant);
    static readonly Regex TaskRx = new(@"^\\[^\x00-\x1f""<>|*?:]{1,255}$", RegexOptions.CultureInvariant);
    static readonly Regex ValueNameRx = new(@"^[^\x00-\x1f\\]{0,255}$", RegexOptions.CultureInvariant);
    static readonly Regex SvcRx = new(@"^[A-Za-z0-9_.\-]{1,80}$", RegexOptions.CultureInvariant);
    const string Approved = @"\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    const string Run = @"\Software\Microsoft\Windows\CurrentVersion\Run";
    const string Run32 = @"\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    const string SvcRoot = @"HKLM\SYSTEM\CurrentControlSet\Services\";
    const long MaxFile = 4 << 20;

    /// <summary>null si la copia se puede restaurar; si no, el motivo de cada ítem que falla (para enseñarlo).</summary>
    public static List<string> Check(JsonObject o, bool isSealed)
    {
        var errs = new List<string>();
        if (o["items"] is not JsonArray items) { errs.Add("la copia no tiene la lista de cambios."); return errs; }
        if (items.Count > 5000) { errs.Add("la copia tiene demasiados cambios."); return errs; }
        foreach (var n in items)
        {
            if (n is not JsonObject it) { errs.Add("un cambio de la copia está dañado."); continue; }
            var label = (string?)it["label"] is { } l ? (l.Length > 80 ? l[..80] : l) : "(sin nombre)";
            string? why;
            try { why = Item(it, isSealed); }
            catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException or InvalidCastException) { why = "datos con forma rara"; }
            if (why is not null) errs.Add($"{label}: {why}.");
        }
        return errs;
    }

    static string? Str(JsonObject it, string k, int max = 1024) =>
        it[k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length <= max && !s.Any(char.IsControl) ? s : null;

    static string? Item(JsonObject it, bool isSealed)
    {
        var t = Str(it, "t", 20);
        if (t is null || !Types.Contains(t)) return "tipo de cambio desconocido";
        if (it["label"] is not null && Str(it, "label", 500) is null) return "nombre no válido";
        switch (t)
        {
            case "reg":
            {
                var path = Str(it, "path", 512); var name = Str(it, "name", 255);
                if (path is null || name is null || !ValueNameRx.IsMatch(name) || path.Contains("..") || path.EndsWith('\\')) return "clave de registro no válida";
                if (!Tweaks.ValidSnap(it["snap"])) return "valor guardado no válido";
                var snap = it["snap"]!.AsObject();
                if (!Tweaks.WouldChange(path, name, snap)) return null;   // ya está así: no se escribe nada
                return Reg(path, name, snap, isSealed);
            }
            case "tweak":
                return Tweaks.CheckSnapshot(it);
            case "task":
                var tp = Str(it, "path", 260);
                if (tp is null || !TaskRx.IsMatch(tp) || tp.Contains("..")) return "tarea no válida";
                if (it["enabled"] is not JsonValue ev || !ev.TryGetValue<bool>(out _)) return "estado de la tarea no válido";
                return null;
            case "display":
                return Int(it, "w", 320, 16384) && Int(it, "h", 200, 16384) && Int(it, "hz", 1, 1000) ? null : "resolución no válida";
            case "hz":
                return Str(it, "device", 40) is { } dv && DeviceRx.IsMatch(dv) && Int(it, "w", 320, 16384) && Int(it, "h", 200, 16384) && Int(it, "hz", 1, 1000)
                    ? null : "pantalla no válida";
            case "appx":
                return Str(it, "name", 128) is { } an && AppxRx.IsMatch(an) ? null : "nombre de paquete no válido";
            case "wallpaper":
                foreach (var k in new[] { "path", "saved" })
                    if (it[k] is not null && (Str(it, k, 520) is not { } wp || (wp.Length > 0 && FileTarget(wp, wallpaper: true) is not null))) return "imagen de fondo no válida";
                return null;
            case "file":
            {
                var fp = Str(it, "path", 520);
                if (fp is null) return "ruta no válida";
                if (FileTarget(fp, wallpaper: false, isSealed) is { } fw) return fw;
                if (it["data"] is JsonValue dv2 && (!dv2.TryGetValue<string>(out var data) || data.Length > MaxFile * 4 / 3 + 4)) return "contenido demasiado grande";
                if (it["data"] is { } dd && dd is not JsonValue) return "contenido no válido";
                return null;
            }
        }
        return "tipo de cambio desconocido";
    }

    static bool Int(JsonObject it, string k, int min, int max) => it[k] is JsonValue v && v.TryGetValue<int>(out var i) && i >= min && i <= max;

    /// <summary>Claves de registro que una copia puede restaurar.</summary>
    static string? Reg(string path, string name, JsonObject snap, bool isSealed)
    {
        var root = path.Split('\\', 2)[0];
        var rest = path.Length > root.Length ? path[root.Length..] : "";
        var gone = (bool?)snap["existed"] != true;

        // Activar Defender: solo esos valores, y sin sello solo de vuelta a como viene Windows
        var (known, safe) = Security.RestoreTarget(path, name, snap);
        if (known) return safe || (isSealed && Eq(path, @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\smartscreen.exe"))
            ? null : "esa copia volvería a apagar una protección de Windows; sin el sello de Vigía no se hace";

        // tu propio registro: lo mismo que puede escribir tu usuario sin permisos
        if (root == "HKCU") return null;
        if (root != "HKLM") return "raíz de registro no permitida";

        // inicio de Windows: aprobado/apagado (binario) en StartupApproved
        if (rest.StartsWith(Approved, StringComparison.OrdinalIgnoreCase) && rest[Approved.Length..] is "Run" or "Run32" or "StartupFolder")
            return gone || (string?)snap["type"] == "binary" ? null : "valor de inicio no válido";

        // entradas Run de todos los usuarios: quitar siempre; volver a ponerlas solo con sello
        if (Eq(rest, Run) || Eq(rest, Run32))
            return gone || isSealed ? null : "volver a poner un programa que arranca para todos los usuarios necesita una copia sellada por Vigía";

        // arranque de servicios existentes: automático, manual o deshabilitado (nunca arranque o sistema)
        if (path.StartsWith(SvcRoot, StringComparison.OrdinalIgnoreCase) && path[SvcRoot.Length..] is var svc && SvcRx.IsMatch(svc))
        {
            if (!Startup.ServiceExists(svc)) return "ese servicio no existe";
            if (Eq(name, "Start")) return (string?)snap["type"] == "dword" && (string?)snap["value"] is "2" or "3" or "4" ? null : "arranque de servicio no permitido";
            if (Eq(name, "DelayedAutostart")) return gone || ((string?)snap["type"] == "dword" && (string?)snap["value"] is "0" or "1") ? null : "valor no válido";
            return "valor de servicio no permitido";
        }

        // TRIM
        if (Eq(path, @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem") && Eq(name, "DisableDeleteNotification"))
            return gone || ((string?)snap["type"] == "dword" && (string?)snap["value"] is "0" or "1") ? null : "valor no válido";

        return "esa clave de registro no es de las que guarda Vigía";
    }

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Archivos que una copia puede reescribir: configuración de juegos en tu perfil y accesos de la carpeta Inicio.
    /// Ruta completa, sin «..», sin enlaces en ningún tramo, extensión de la lista.
    /// </summary>
    internal static string? FileTarget(string path, bool wallpaper, bool isSealed = false)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains("..") || path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0) return "ruta no válida";
        string full;
        try { full = Path.GetFullPath(path); } catch { return "ruta no válida"; }
        if (!Eq(full, path)) return "ruta no válida";
        var ext = Path.GetExtension(full).ToLowerInvariant();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

        bool Under(string root) => root.Length > 0 && full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        bool ok;
        // el fondo solo se lee (lo pone Windows para tu usuario): basta con que sea una imagen en un disco local
        // el archivo de configuración del juego que conoce Vigía (en pruebas, el de la carpeta de pruebas)
        if (!wallpaper && Eq(full, Path.GetFullPath(Games.FortnitePath))) ok = true;
        else if (wallpaper) return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".jfif" or ".webp" or ".heic" ? null : "no es una imagen";
        else if (Under(startup)) ok = ext is ".lnk" or ".url" && Path.GetDirectoryName(full)!.Equals(startup.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        else if (Under(common))
        {
            if (!isSealed) return "un acceso en el Inicio de todos los usuarios solo se repone desde una copia sellada por Vigía";
            ok = ext is ".lnk" or ".url" && Path.GetDirectoryName(full)!.Equals(common.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        else ok = Under(local) && ext is ".ini" or ".cfg" or ".xml" or ".json" && full.Contains(@"\Saved\Config\", StringComparison.OrdinalIgnoreCase);
        if (!ok) return "ese archivo no es de los que guarda Vigía";

        // ningún tramo puede ser un enlace (junction o simbólico): llevaría la escritura a otro sitio
        for (var d = new DirectoryInfo(Path.GetDirectoryName(full)!); d is not null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) return "la ruta pasa por un enlace";
        if (System.IO.File.Exists(full) && (System.IO.File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return "el archivo es un enlace";
        return null;
    }

    // ---------------------------------------------------------------- pruebas
    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        JsonObject One(JsonObject it) => new() { ["id"] = "20260101-000000-000-x", ["kind"] = "x", ["items"] = new JsonArray(it) };
        JsonObject Reg(string path, string name, string type, string value) =>
            new() { ["t"] = "reg", ["path"] = path, ["name"] = name, ["label"] = "x", ["snap"] = new JsonObject { ["existed"] = true, ["type"] = type, ["value"] = value } };
        bool Bad(JsonObject it, bool seal = false) => BackupGuard.Check(One(it), seal).Count > 0;

        Check(Bad(new JsonObject { ["t"] = "exec", ["cmd"] = "calc.exe" }), "copias: tipo desconocido (p. ej. «ejecutar») se rechaza");
        Check(Bad(Reg(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\notepad.exe", "Debugger", "string", @"C:\x.exe"), true),
            "copias: un depurador en IFEO se rechaza aunque esté sellada");
        Check(Bad(Reg(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\smartscreen.exe", "Debugger", "string", @"C:\x.exe")),
            "copias: sin sello no se vuelve a bloquear SmartScreen");
        Check(Bad(Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "VigiaPruebaNoExiste", "string", @"C:\Users\Public\x.exe")),
            "copias: sin sello no se pone un programa en el Run de todos los usuarios");
        Check(!Bad(new JsonObject { ["t"] = "reg", ["path"] = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", ["name"] = "VigiaPruebaNoExiste", ["label"] = "x", ["snap"] = new JsonObject { ["existed"] = false } }),
            "copias: quitar una entrada de Run sí se permite (ya no estaba: nada que escribir)");
        Check(Bad(Reg(@"HKLM\SYSTEM\CurrentControlSet\Services\VigiaNoExiste", "ImagePath", "string", @"C:\x.exe")), "copias: ImagePath de un servicio se rechaza");
        Check(Bad(Reg(@"HKLM\SYSTEM\CurrentControlSet\Services\VigiaNoExiste", "Start", "dword", "2")), "copias: servicio que no existe se rechaza");
        Check(Bad(Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", "dword", "1")), "copias: no se puede volver a apagar Defender desde una copia editada");
        Check(Bad(Reg(@"HKLM\SOFTWARE\Classes\exefile\shell\open\command", "", "string", "x")), "copias: una clave fuera de la lista se rechaza");
        Check(Bad(Reg(@"HKCR\exefile", "x", "string", "x")), "copias: raíz de registro rara se rechaza");
        Check(Bad(Reg(@"HKCU\Software\VigiaPrueba", "x", "tipo-raro", "x")), "copias: tipo de valor desconocido se rechaza");
        Check(Bad(Reg(@"HKCU\Software\VigiaPrueba", "x", "dword", "abc")), "copias: número no válido se rechaza");
        Check(!Bad(Reg(@"HKCU\Software\VigiaPruebaNoExiste", "x", "dword", "1")), "copias: un valor de tu propio registro se permite");
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        JsonObject F(string p) => new() { ["t"] = "file", ["path"] = p, ["label"] = "x", ["existed"] = true, ["data"] = "eA==" };
        Check(Bad(F(Path.Combine(win, "System32", "drivers", "etc", "hosts"))), "copias: escribir en Windows se rechaza");
        Check(Bad(F(Path.Combine(local, "FortniteGame", "Saved", "Config", "..", "..", "..", "..", "x.exe"))), "copias: «..» en la ruta se rechaza");
        Check(Bad(F(Path.Combine(local, "FortniteGame", "Saved", "Config", "WindowsClient", "x.exe"))), "copias: extensión fuera de la lista se rechaza");
        Check(Bad(F(@"\\servidor\recurso\x.ini")), "copias: rutas de red se rechazan");
        Check(Bad(F(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "x.lnk"))), "copias: Inicio de todos los usuarios sin sello se rechaza");
        Check(!Bad(F(Path.Combine(local, "VigiaPruebaNoExiste", "Saved", "Config", "WindowsClient", "GameUserSettings.ini"))), "copias: la configuración de un juego en tu perfil se permite");
        Check(Bad(new JsonObject { ["t"] = "appx", ["name"] = "x'; calc; '", ["label"] = "x" }), "copias: nombre de app con comillas se rechaza");
        Check(Bad(new JsonObject { ["t"] = "hz", ["device"] = @"\\.\DISPLAY1"" & calc", ["w"] = 1920, ["h"] = 1080, ["hz"] = 60, ["label"] = "x" }), "copias: pantalla no válida se rechaza");
        Check(Bad(new JsonObject { ["t"] = "display", ["w"] = 99999, ["h"] = 1, ["hz"] = 60, ["label"] = "x" }), "copias: resolución fuera de rango se rechaza");
        Check(Bad(new JsonObject { ["t"] = "tweak", ["id"] = "no-existe-este-ajuste", ["saved"] = new JsonObject(), ["label"] = "x" }), "copias: ajuste fuera del catálogo se rechaza");

        // junction dentro de la carpeta del juego
        var tmp = Path.Combine(local, "VigiaPruebaEnlace-" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            Directory.CreateDirectory(tmp);
            var cfg = Path.Combine(tmp, "Saved");
            Directory.CreateSymbolicLink(cfg, win);
            Check(Bad(F(Path.Combine(cfg, "Config", "x.ini"))), "copias: una ruta que pasa por un enlace se rechaza");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.Add("OK   copias: enlaces sin permiso para crearlos aquí (se prueba en la VM)"); }
        finally { try { foreach (var d in Directory.GetDirectories(tmp)) Directory.Delete(d); Directory.Delete(tmp); } catch { } }

        // sello: con una clave de pruebas en una carpeta temporal
        var kd = Path.Combine(Path.GetTempPath(), "vigia-seal-" + Guid.NewGuid().ToString("N")[..6]);
        var prevKey = _key; var prevDir = KeyDirOverride;
        try
        {
            KeyDirOverride = kd; _key = null;
            var o = One(Reg(@"HKCU\Software\VigiaPrueba", "x", "dword", "1"));
            Seal(o);
            Check(Sealed(o), "sello: una copia sellada se reconoce");
            o["items"]![0]!["snap"]!["value"] = "2";
            Check(!Sealed(o), "sello: si se edita un valor, el sello deja de valer");
            Seal(o); o["id"] = "otra";
            Check(!Sealed(o), "sello: no se puede mover a otro id");
            Seal(o); o["restoredAt"] = "2026-01-01T00:00:00";
            Check(Sealed(o), "sello: marcar como restaurada no rompe el sello");
            o["seal"] = new string('0', 64);
            Check(!Sealed(o), "sello: un sello inventado no vale");
        }
        finally { KeyDirOverride = prevDir; _key = prevKey; try { Directory.Delete(kd, true); } catch { } }
        return string.Join(Environment.NewLine, log);
    }
}
