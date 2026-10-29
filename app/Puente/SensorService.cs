using System.Diagnostics;

namespace Puente;

// «Sensores de Vigía»: servicio de solo lectura (Vigia.exe --sensor-service, como SYSTEM) que publica los grados del
// procesador para que Vigía los vea sin correr como administrador. Lo instala y lo quita solo Puente (ya elevado);
// apunta siempre a la versión activa bajo Archivos de programa, que solo SYSTEM y Administradores pueden cambiar.
static partial class Program
{
    const string SensorSvc = "VigiaSensores";

    static (int Code, string Out) Sc(params string[] args)
    {
        var psi = new ProcessStartInfo(Native.Sys("sc.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30_000)) { try { p.Kill(); } catch { } return (-1, o); }
        return (p.ExitCode, o);
    }

    static bool SensorSvcExists() => Sc("query", SensorSvc).Code == 0;

    static void StopSensors()
    {
        if (Layout.Test) return;
        try
        {
            if (!SensorSvcExists()) return;
            Sc("stop", SensorSvc);
            for (var i = 0; i < 40 && !Sc("query", SensorSvc).Out.Contains("STOPPED", StringComparison.Ordinal); i++) Thread.Sleep(250);
        }
        catch (Exception e) { Log("sensores: parar: " + e.Message); }
    }

    static void StartSensors()
    {
        if (Layout.Test) return;
        try { if (SensorSvcExists()) Sc("start", SensorSvc); } catch { }
    }

    /// <summary>Registra (o reapunta) el servicio a la versión activa y lo arranca. Nunca deja apuntando a otra carpeta.</summary>
    static void InstallSensors(string exe)
    {
        if (Layout.Test) return;
        try
        {
            if (!Layout.Under(exe, Layout.Root)) { Log("sensores: servicio rechazado, la ruta no es la instalación protegida"); return; }
            var bin = $"\"{exe}\" --sensor-service";
            StopSensors();
            var r = SensorSvcExists()
                ? Sc("config", SensorSvc, "binPath=", bin, "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "Sensores de Vigía")
                : Sc("create", SensorSvc, "binPath=", bin, "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "Sensores de Vigía");
            if (r.Code != 0) { Log($"sensores: registrar servicio falló ({r.Code})"); return; }
            Sc("description", SensorSvc, "Lee la temperatura del procesador para Vigía mientras Vigía está abierta. Solo lectura; no recibe órdenes.");
            // si se cae, vuelve a los 60 s; tras un día sin fallos el contador se reinicia
            Sc("failure", SensorSvc, "reset=", "86400", "actions=", "restart/60000/restart/60000//");
            var s = Sc("start", SensorSvc);
            Log(s.Code is 0 or 1056 ? "sensores: servicio activo" : $"sensores: el servicio no arrancó ({s.Code}); usar Reparar");
        }
        catch (Exception e) { Log("sensores: servicio: " + e.Message); }
    }

    static void RemoveSensors()
    {
        if (Layout.Test) return;
        try
        {
            if (!SensorSvcExists()) return;
            StopSensors();
            var r = Sc("delete", SensorSvc);
            Log(r.Code == 0 ? "sensores: servicio quitado" : $"sensores: quitar servicio falló ({r.Code})");
        }
        catch (Exception e) { Log("sensores: quitar: " + e.Message); }
    }
}
