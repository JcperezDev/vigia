using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vigia;

/// <summary>
/// PawnIO es el driver con el que LibreHardwareMonitor lee los grados del procesador. Sin él sale 0 °C.
/// Vigía trae el instalador oficial firmado (drivers\PawnIO_setup.exe, sin tocar). Puente prepara la dependencia
/// durante instalación/actualización elevada, nunca al abrir Monitor ni con un juego abierto.
/// Al desinstalar Vigía se quita únicamente si lo puso Vigía; un PawnIO de otra aplicación no se toca.
/// Sin PawnIO todo funciona igual salvo los grados del procesador.
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

    /// <summary>
    /// Lo pone si falta. true si quedó instalado ahora (para empezar a leer los grados).
    /// <paramref name="asked"/>: el usuario acaba de pulsar «Instalar», así que se intenta aunque haya fallado hace poco.
    /// </summary>
    public static bool InstallIfMissing(out string? error, bool asked = false)
    {
        error = null;
        if (Installed()) return false;
        if (AutoStart.Busy()) { error = "Hay un juego abierto: la preparación de sensores queda aplazada."; return false; }
        if (!File.Exists(Setup)) { error = "Falta el componente oficial de sensores en la instalación. Usa Reparar."; return false; }
        // si ya falló una vez, no insistir en cada arranque: una vez por semana como mucho
        if (!asked && Load() is { Ok: false } last && DateTimeOffset.UtcNow - last.At < TimeSpan.FromDays(7)) return false;
        try
        {
            var code = RunPinned(Setup, "-install -silent", Sha256, TimeSpan.FromMinutes(2), out var why);
            if (code is null) { error = why; Save(false, error); return false; }
            var ok = code == 0 && Installed();
            if (!ok) error = $"La preparación de sensores terminó con código {code}; dependencia no confirmada. Usa Reparar.";
            Save(ok, error);
            return ok;
        }
        catch (Exception e) { error = e.Message; Save(false, error); return false; }
    }

    /// <summary>
    /// Ejecuta <paramref name="exe"/> solo si su SHA-256 es <paramref name="sha256"/>. El archivo queda abierto sin permiso
    /// de escritura, renombrado ni borrado para nadie desde que se calcula el hash hasta que termina el proceso: así nadie
    /// puede cambiarlo entre la comprobación y la ejecución (la carpeta de instalación es del usuario).
    /// Devuelve el código de salida, o null con el motivo si no se ejecutó o no terminó a tiempo.
    /// </summary>
    internal static int? RunPinned(string exe, string args, string sha256, TimeSpan timeout, out string? error)
    {
        error = null;
        using var f = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!Convert.ToHexString(SHA256.HashData(f)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
        {
            error = "El instalador de PawnIO no es el original.";
            return null;
        }
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        if (p is null || !p.WaitForExit(timeout))
        {
            try { p?.Kill(); } catch { }
            error = "El instalador de PawnIO no terminó.";
            return null;
        }
        return p.ExitCode;
    }

    static void Save(bool ok, string? error)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(Mark)!); File.WriteAllText(Mark, JsonSerializer.Serialize(new State(ok || Load()?.ByVigia == true, ok, DateTimeOffset.UtcNow, error))); } catch { }
    }

    /// <summary>
    /// Prueba de RunPinned con una copia de whoami.exe (no instala nada): se ejecuta con el hash bueno, no con uno malo,
    /// y mientras está abierto nadie puede reemplazarlo. Instalar el driver de verdad necesita un PC sin PawnIO.
    /// </summary>
    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        var dir = Path.Combine(Path.GetTempPath(), "vigia-pawn-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "probe.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), exe);
            string hash;
            using (var s = File.OpenRead(exe)) hash = Convert.ToHexString(SHA256.HashData(s));
            Check(RunPinned(exe, "", hash, TimeSpan.FromSeconds(20), out _) == 0, "ejecuta el archivo cuando el hash coincide");
            Check(RunPinned(exe, "", new string('0', 64), TimeSpan.FromSeconds(20), out var e2) is null && e2 is not null, "no ejecuta nada si el hash no coincide");
            using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool blocked;
                try { File.Move(exe, exe + ".x"); blocked = false; } catch (IOException) { blocked = true; } catch (UnauthorizedAccessException) { blocked = true; }
                Check(blocked, "con el archivo abierto para comprobarlo, nadie puede reemplazarlo");
            }
        }
        catch (Exception e) { Check(false, "prueba de PawnIO: " + e.Message); }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return string.Join(Environment.NewLine, log);
    }
}
