using System.Diagnostics;
using System.Text.Json;

namespace Vigia;

public record UpdateInfo(string Current, string? Latest, bool Available, string? Notes, bool PuenteFound, string? Error, long? Size = null);

/// <summary>
/// Mira si hay una versión nueva en JcperezDev/vigia. Vigía solo pregunta; quien descarga, comprueba la firma
/// e instala es Puente. Instalada con el Puente nuevo, Vigía vive en %ProgramFiles%\Vigia\versiones\x.y.z y Puente
/// en %ProgramFiles%\Vigia\Puente.exe (solo administradores lo cambian). La instalación vieja usaba
/// %LOCALAPPDATA%\Programs\Vigia-Puente\Puente.exe.
/// </summary>
public static class Updates
{
    const string Api = "https://api.github.com/repos/JcperezDev/vigia/releases/latest";

    /// <summary>%ProgramFiles%\Vigia si este Vigia.exe corre desde ahí (versiones\x.y.z); si no, null.</summary>
    public static string? InstallRoot
    {
        get
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Vigia");
            var me = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
            var parent = Path.GetDirectoryName(me);
            return parent is not null && Path.GetFileName(parent).Equals("versiones", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(parent), root, StringComparison.OrdinalIgnoreCase) ? root : null;
        }
    }

    /// <summary>¿Corre desde la instalación vieja de %LOCALAPPDATA%\Programs\Vigia?</summary>
    public static bool Legacy => InstallRoot is null && string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Vigia"), StringComparison.OrdinalIgnoreCase);

    static readonly string LegacyPuente = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Vigia-Puente", "Puente.exe");
    public static string PuentePath => InstallRoot is { } r ? Path.Combine(r, "Puente.exe") : LegacyPuente;
    /// <summary>Con la instalación nueva, Puente está en la carpeta protegida y no se baja ni se reemplaza desde Vigía.</summary>
    public static bool Protected => InstallRoot is { } r && InstallTrust.Problem(r, 200) is null;

    /// <summary>
    /// Puente abre la versión nueva así antes de activarla: tienen que estar la interfaz, las traducciones y los
    /// catálogos, y la carpeta tiene que estar protegida si es la de Archivos de programa. 0 = bien.
    /// </summary>
    public static int InstallCheck()
    {
        try
        {
            var ui = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!File.Exists(Path.Combine(ui, "index.html")) || !File.Exists(Path.Combine(ui, "app.js"))) return 10;
            foreach (var l in new[] { "es", "en", "pt", "fr", "de" })
                if (!File.Exists(Path.Combine(ui, "i18n", l + ".json"))) return 11;
            if (Tweaks.List().Count == 0) return 12;
            if (AppCatalog.Catalog is null) return 13;
            if (InstallRoot is { } r && InstallTrust.Problem(AppContext.BaseDirectory, 2000) is not null) return 14;
            return 0;
        }
        catch { return 1; }
    }

    public static Version Current
    {
        get
        {
            var v = typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>Lo que ve el usuario cuando falla la red: nunca el texto técnico de Windows (va al log de .NET, no a la pantalla).</summary>
    public static string Friendly(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
            if (x is HttpRequestException or System.Net.Sockets.SocketException or TimeoutException or TaskCanceledException or IOException)
                return "no hay conexión con GitHub ahora mismo";
        return e.Message;
    }

    public static async Task<UpdateInfo> CheckAsync()
    {
        var first = await CheckOnceAsync();
        if (first.Error is null) return first;
        await Task.Delay(3000);   // un corte de un segundo no debería verse: se mira otra vez antes de avisar
        return await CheckOnceAsync();
    }

    /// <summary>Lee «v1.2.3», «1.2.3», «1.2», «1.2.3-beta» o «1.2.3-rc.1». Devuelve false si no tiene esa forma (sin excepción).
    /// pre = true si trae sufijo de prueba. No usa la versión del ensamblado (4 números) para nada de esto.</summary>
    internal static bool TryParseTag(string? tag, out Version v, out bool pre)
    {
        v = new Version(0, 0, 0); pre = false;
        var m = System.Text.RegularExpressions.Regex.Match(tag?.Trim() ?? "", @"^[vV]?(\d{1,5})\.(\d{1,5})(?:\.(\d{1,5}))?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$");
        if (!m.Success) return false;
        v = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        pre = m.Groups[4].Success;
        return true;
    }

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        Check(TryParseTag("v1.2.3", out var a, out var p1) && a == new Version(1, 2, 3) && !p1, "v1.2.3");
        Check(TryParseTag("1.2.3", out var b, out var p2) && b == new Version(1, 2, 3) && !p2, "1.2.3");
        Check(TryParseTag("0.20", out var c, out _) && c == new Version(0, 20, 0), "0.20 = 0.20.0");
        Check(TryParseTag("1.2.3-beta", out var d, out var p3) && d == new Version(1, 2, 3) && p3, "1.2.3-beta es de prueba");
        Check(TryParseTag("1.2.3-rc.1", out _, out var p4) && p4, "1.2.3-rc.1 es de prueba");
        Check(!TryParseTag("latest", out _, out _) && !TryParseTag("", out _, out _) && !TryParseTag(null, out _, out _) && !TryParseTag("1.2.3.4.5", out _, out _), "versiones raras se rechazan sin excepción");
        Check(TryParseTag("0.20.18", out var n, out _) && TryParseTag("0.20.9", out var o, out _) && n > o, "0.20.18 > 0.20.9 (no por texto)");
        // Puente: la firma se comprueba sobre bytes en memoria; nada sin firma válida, vacío o gigante
        var fake = System.Text.Encoding.UTF8.GetBytes("MZ no es Puente");
        Check(!PuenteSigOk(fake, ""), "Puente sin firma: no");
        Check(!PuenteSigOk(fake, "esto no es base64!"), "Puente con firma rota: no");
        Check(!PuenteSigOk(fake, Convert.ToBase64String(new byte[64])), "Puente con firma de otra cosa: no");
        Check(!PuenteSigOk([], Convert.ToBase64String(new byte[64])), "Puente vacío: no");
        Check(!PuenteSigOk(new byte[MaxPuenteBytes + 1], Convert.ToBase64String(new byte[64])), "Puente demasiado grande: no");
        using (var k = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))
        {   // clave de usar y tirar, solo para ver que una firma buena sí pasa y que cambiar un byte la rompe
            var pub = Convert.ToBase64String(k.ExportSubjectPublicKeyInfo());
            var good = Convert.ToBase64String(k.SignData(fake, System.Security.Cryptography.HashAlgorithmName.SHA256));
            Check(PuenteSigOk(fake, good, pub), "Puente con firma buena: sí");
            var bad = (byte[])fake.Clone(); bad[^1] ^= 1;
            Check(!PuenteSigOk(bad, good, pub), "Puente con un byte cambiado: no");
            Check(!PuenteSigOk(fake, good), "firma de otra clave contra la clave de Vigía: no");
        }
        return string.Join(Environment.NewLine, log);
    }

    static async Task<UpdateInfo> CheckOnceAsync()
    {
        var cur = Current.ToString();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Vigia/" + cur);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var res = await http.GetAsync(Api);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return new(cur, null, false, null, File.Exists(PuentePath), null);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var tag = doc.RootElement.TryGetProperty("tag_name", out var tn) ? tn.GetString() ?? "" : "";
            var pre = doc.RootElement.TryGetProperty("prerelease", out var pr) && pr.ValueKind == JsonValueKind.True;
            var notes = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() : null;
            // versión con forma rara o de prueba (-beta, -rc.1): en el canal estable no se ofrece
            if (!TryParseTag(tag, out var latest, out var tagPre) || pre || tagPre)
                return new(cur, null, false, null, File.Exists(PuentePath), null);
            long? size = null;   // lo que baja Puente: el zip de la app
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                    if (a.TryGetProperty("name", out var n) && n.GetString() is string nm && nm.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var bytes)) { size = bytes; break; }
            // en la carpeta vieja (la ponía un Puente anterior) se ofrece «actualizar» aunque sea la misma versión:
            // Puente la pasa a Archivos de programa sin tocar tus ajustes
            return new(cur, latest.ToString(), latest > Current || (Legacy && latest >= Current), notes, File.Exists(PuentePath), null, size);
        }
        catch (Exception e) { return new(cur, null, false, null, File.Exists(PuentePath), Friendly(e)); }
    }

    const string PuenteUrl = "https://github.com/JcperezDev/vigia/releases/latest/download/Puente.exe";
    // la misma clave pública que lleva Puente: solo se instala un Puente firmado por nosotros
    const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2sn60BucOhX4a3HZt59BBg3gWbZ77dyDNEdmP0/+cFAaUg7jSEjaywk5q54fPIZKzpuUtQlsS01dcTNdbv0muA==";

    /// <summary>Si falta Puente, lo baja de GitHub y comprueba la firma antes de dejarlo en su carpeta. Devuelve null si está listo.</summary>
    /// <param name="fresh">Para actualizar: se baja y se comprueba otra vez aunque ya exista, así nunca se abre un Puente
    /// que alguien haya cambiado en su carpeta (en la carpeta antigua de AppData cualquier programa tuyo puede escribir).</param>
    /// <remarks>La firma se comprueba sobre los bytes en memoria y a disco solo van esos mismos bytes: entre comprobar y
    /// usar no hay un archivo en %TEMP% que otro programa pueda cambiar. Con tope de tamaño para no llenar la memoria.</remarks>
    public static async Task<string?> EnsurePuenteAsync(string? dest = null, bool fresh = false)
    {
        if (dest is null && Protected) return File.Exists(PuentePath) ? null : "Falta Puente en la carpeta de Vigía. Vuelve a instalar Vigía con Puente.";
        dest ??= PuentePath;
        if (!fresh && File.Exists(dest)) return null;
        var dir = Path.GetDirectoryName(dest)!;
        var tmp = dest + ".nuevo";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5), MaxResponseContentBufferSize = MaxPuenteBytes };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Vigia/" + Current);
            byte[] exe = [];
            string sig = "";
            for (int i = 1; ; i++)   // si se corta la conexión, se intenta hasta 3 veces
            {
                try
                {
                    exe = await http.GetByteArrayAsync(PuenteUrl);
                    sig = await http.GetStringAsync(PuenteUrl + ".sig");
                    break;
                }
                catch (Exception e) when (i < 3 && Friendly(e) != e.Message) { await Task.Delay(TimeSpan.FromSeconds(3 * i)); }
            }
            if (!PuenteSigOk(exe, sig)) return "La firma de Puente no coincide: no lo instalé.";
            // nunca se cambia un Puente por uno más viejo, aunque esté firmado (una versión vieja puede tener fallos ya arreglados)
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(tmp, exe);
            if (File.Exists(dest) && FileVer(tmp) is { } got && FileVer(dest) is { } have && got < have)
                return $"El Puente publicado ({got}) es más viejo que el que tienes ({have}); no lo cambié.";
            // lo que queda en disco tiene que ser exactamente lo que se comprobó
            if (!File.ReadAllBytes(tmp).AsSpan().SequenceEqual(exe)) return "Puente cambió en disco antes de usarlo: no lo instalé.";
            File.Move(tmp, dest, true);
            return null;
        }
        catch (Exception e) { return $"No pude bajar Puente: {Friendly(e)}"; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    /// <summary>Puente pesa unos pocos MB; nada de lo que se publique debería pasar de esto.</summary>
    internal const long MaxPuenteBytes = 64L * 1024 * 1024;

    internal static bool PuenteSigOk(byte[] exe, string sig, string? publicKey = null)
    {
        if (exe.Length == 0 || exe.Length > MaxPuenteBytes) return false;
        byte[] s;
        try { s = Convert.FromBase64String(sig.Trim()); } catch (FormatException) { return false; }
        using var ec = System.Security.Cryptography.ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey ?? PublicKey), out _);
        return ec.VerifyData(exe, s, System.Security.Cryptography.HashAlgorithmName.SHA256);
    }

    static Version? FileVer(string path)
    {
        var v = FileVersionInfo.GetVersionInfo(path);
        return v.FileMajorPart + v.FileMinorPart + v.FileBuildPart > 0 ? new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart) : null;
    }

    /// <summary>Abre Puente para que espere a que Vigía se cierre, la actualice y la vuelva a abrir.</summary>
    public static bool StartPuente()
    {
        var p = PuentePath;
        if (!File.Exists(p)) return false;
        try
        {
            // Puente abre sin permisos y pide el de administrador (UAC) él mismo, solo para instalar
            Process.Start(new ProcessStartInfo(p, $"--update --wait {Environment.ProcessId}")
            { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(p)! })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
