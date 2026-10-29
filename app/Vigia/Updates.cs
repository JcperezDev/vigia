using System.Diagnostics;
using System.Text.Json;

namespace Vigia;

public record UpdateInfo(string Current, string? Latest, bool Available, string? Notes, bool PuenteFound, string? Error, long? Size = null);

/// <summary>
/// Mira si hay una versión nueva en JcperezDev/vigia. Vigía solo pregunta; quien descarga, comprueba la firma
/// e instala es Puente (%LOCALAPPDATA%\Programs\Vigia-Puente\Puente.exe).
/// </summary>
public static class Updates
{
    const string Api = "https://api.github.com/repos/JcperezDev/vigia/releases/latest";
    public static string PuentePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Vigia-Puente", "Puente.exe");

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
            return new(cur, latest.ToString(), latest > Current, notes, File.Exists(PuentePath), null, size);
        }
        catch (Exception e) { return new(cur, null, false, null, File.Exists(PuentePath), Friendly(e)); }
    }

    const string PuenteUrl = "https://github.com/JcperezDev/vigia/releases/latest/download/Puente.exe";
    // la misma clave pública que lleva Puente: solo se instala un Puente firmado por nosotros
    const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2sn60BucOhX4a3HZt59BBg3gWbZ77dyDNEdmP0/+cFAaUg7jSEjaywk5q54fPIZKzpuUtQlsS01dcTNdbv0muA==";

    /// <summary>Si falta Puente, lo baja de GitHub y comprueba la firma antes de dejarlo en su carpeta. Devuelve null si está listo.</summary>
    /// <param name="fresh">Para actualizar: se baja y se comprueba otra vez aunque ya exista, así nunca se abre un Puente
    /// que alguien haya cambiado en su carpeta (es del usuario y Vigía corre como administrador).</param>
    public static async Task<string?> EnsurePuenteAsync(string? dest = null, bool fresh = false)
    {
        dest ??= PuentePath;
        if (!fresh && File.Exists(dest)) return null;
        var dir = Path.GetDirectoryName(dest)!;
        var tmp = Path.Combine(Path.GetTempPath(), $"vigia-puente-{Guid.NewGuid():N}.exe");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Vigia/" + Current);
            string sig = "";
            for (int i = 1; ; i++)   // si se corta la conexión, se intenta hasta 3 veces
            {
                try
                {
                    await File.WriteAllBytesAsync(tmp, await http.GetByteArrayAsync(PuenteUrl));
                    sig = await http.GetStringAsync(PuenteUrl + ".sig");
                    break;
                }
                catch (Exception e) when (i < 3 && Friendly(e) != e.Message) { await Task.Delay(TimeSpan.FromSeconds(3 * i)); }
            }
            using (var ec = System.Security.Cryptography.ECDsa.Create())
            {
                ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
                byte[] s;
                try { s = Convert.FromBase64String(sig.Trim()); } catch (FormatException) { return "La firma de Puente no es válida."; }
                using var fs = File.OpenRead(tmp);
                if (!ec.VerifyData(fs, s, System.Security.Cryptography.HashAlgorithmName.SHA256)) return "La firma de Puente no coincide: no lo instalé.";
            }
            Directory.CreateDirectory(dir);
            File.Move(tmp, dest, true);
            return null;
        }
        catch (Exception e) { return $"No pude bajar Puente: {Friendly(e)}"; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    /// <summary>Abre Puente para que espere a que Vigía se cierre, la actualice y la vuelva a abrir.</summary>
    public static bool StartPuente()
    {
        if (!File.Exists(PuentePath)) return false;
        Process.Start(new ProcessStartInfo(PuentePath, $"--update --wait {Environment.ProcessId}")
        { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(PuentePath)! });
        return true;
    }
}
