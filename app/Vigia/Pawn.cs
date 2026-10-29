using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vigia;

/// <summary>
/// PawnIO es el driver con el que LibreHardwareMonitor lee los grados del procesador. Sin él sale 0 °C.
/// Vigía trae el instalador oficial firmado (drivers\PawnIO_setup.exe, sin tocar) y lo pone una sola vez,
/// en silencio y nunca con un juego abierto. Solo si no estaba ya; al desinstalar Vigía se quita si lo puso Vigía.
/// </summary>
static class Pawn
{
    // PawnIO 2.2.0 oficial: el hash publicado en su release de GitHub. Otro archivo no se ejecuta.
    const string Sha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
    static string Setup => Path.Combine(AppContext.BaseDirectory, "drivers", "PawnIO_setup.exe");
    static string Mark => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia", "pawnio.json");

    public static bool Installed()
    {
        try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"); return k is not null; } catch { return false; }
    }

    sealed record State(bool ByVigia, bool Ok, DateTimeOffset At, string? Error);

    static State? Load() { try { return JsonSerializer.Deserialize<State>(File.ReadAllText(Mark)); } catch { return null; } }

    /// <summary>Lo pone si falta. true si quedó instalado ahora (para empezar a leer los grados).</summary>
    public static bool InstallIfMissing(out string? error)
    {
        error = null;
        if (Installed() || !File.Exists(Setup)) return false;
        // si ya falló una vez, no insistir en cada arranque: una vez por semana como mucho
        if (Load() is { Ok: false } last && DateTimeOffset.UtcNow - last.At < TimeSpan.FromDays(7)) return false;
        try
        {
            using (var f = File.OpenRead(Setup))
                if (Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant() != Sha256) { error = "El instalador de PawnIO no es el original."; Save(false, error); return false; }
            using var p = Process.Start(new ProcessStartInfo(Setup, "-install -silent") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (p is null || !p.WaitForExit(120_000)) { try { p?.Kill(); } catch { } error = "El instalador de PawnIO no terminó."; Save(false, error); return false; }
            var ok = Installed();
            if (!ok) error = $"El instalador de PawnIO terminó con código {p.ExitCode}.";
            Save(ok, error);
            return ok;
        }
        catch (Exception e) { error = e.Message; Save(false, error); return false; }
    }

    static void Save(bool ok, string? error)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(Mark)!); File.WriteAllText(Mark, JsonSerializer.Serialize(new State(true, ok, DateTimeOffset.UtcNow, error))); } catch { }
    }
}
