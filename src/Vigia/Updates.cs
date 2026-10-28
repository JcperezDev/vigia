using System.Diagnostics;
using System.Text.Json;

namespace Vigia;

public record UpdateInfo(string Current, string? Latest, bool Available, string? Notes, bool PuenteFound, string? Error);

/// <summary>
/// Mira si hay una versión nueva en JcperezDev/vigia-releases. Vigía solo pregunta; quien descarga, comprueba la firma
/// e instala es Puente (%LOCALAPPDATA%\Programs\Vigia-Puente\Puente.exe).
/// </summary>
public static class Updates
{
    const string Api = "https://api.github.com/repos/JcperezDev/vigia-releases/releases/latest";
    public static string PuentePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Vigia-Puente", "Puente.exe");

    public static Version Current
    {
        get
        {
            var v = typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public static async Task<UpdateInfo> CheckAsync()
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
            var tag = doc.RootElement.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');
            var notes = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() : null;
            var latest = Version.Parse(tag);
            latest = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));
            return new(cur, latest.ToString(), latest > Current, notes, File.Exists(PuentePath), null);
        }
        catch (Exception e) { return new(cur, null, false, null, File.Exists(PuentePath), e.Message); }
    }

    const string PuenteUrl = "https://github.com/JcperezDev/vigia-releases/releases/latest/download/Puente.exe";
    // la misma clave pública que lleva Puente: solo se instala un Puente firmado por nosotros
    const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2sn60BucOhX4a3HZt59BBg3gWbZ77dyDNEdmP0/+cFAaUg7jSEjaywk5q54fPIZKzpuUtQlsS01dcTNdbv0muA==";

    /// <summary>Si falta Puente, lo baja de GitHub y comprueba la firma antes de dejarlo en su carpeta. Devuelve null si está listo.</summary>
    public static async Task<string?> EnsurePuenteAsync(string? dest = null)
    {
        dest ??= PuentePath;
        if (File.Exists(dest)) return null;
        var dir = Path.GetDirectoryName(dest)!;
        var tmp = Path.Combine(Path.GetTempPath(), $"vigia-puente-{Guid.NewGuid():N}.exe");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Vigia/" + Current);
            await File.WriteAllBytesAsync(tmp, await http.GetByteArrayAsync(PuenteUrl));
            var sig = await http.GetStringAsync(PuenteUrl + ".sig");
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
        catch (Exception e) { return $"No pude bajar Puente: {e.Message}"; }
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
