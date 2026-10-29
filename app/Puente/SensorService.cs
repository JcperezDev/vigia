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
        // las dos salidas a la vez y un límite total de 30 s desde el arranque: sc.exe colgado no cuelga a Puente
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(30_000))
        {
            try { p.Kill(); p.WaitForExit(5_000); } catch { }
            return (-1, "sc.exe no terminó a tiempo");
        }
        p.WaitForExit();
        return (p.ExitCode, o.Result + e.Result);
    }

    static bool SensorSvcExists() => Sc("query", SensorSvc).Code == 0;

    // sc query escribe «STATE : 4  RUNNING»; la etiqueta se traduce («ESTADO»), el número y el nombre en mayúsculas no
    static int SvcState(string output)
    {
        var m = System.Text.RegularExpressions.Regex.Match(output,
            @":\s*([1-7])\s+(STOPPED|START_PENDING|STOP_PENDING|RUNNING|CONTINUE_PENDING|PAUSE_PENDING|PAUSED)\b");
        return m.Success ? m.Groups[1].Value[0] - '0' : 0;
    }

    /// <summary>Para el servicio y espera a verlo parado (hasta 10 s). true si no existe o quedó parado.</summary>
    static bool StopSensors()
    {
        if (Layout.Test) return true;
        try
        {
            var q = Sc("query", SensorSvc);
            if (q.Code != 0) return true;
            if (SvcState(q.Out) == 1) return true;
            Sc("stop", SensorSvc);
            for (var i = 0; i < 40; i++)
            {
                if (SvcState(Sc("query", SensorSvc).Out) == 1) return true;
                Thread.Sleep(250);
            }
            Log("sensores: el servicio no se paró a tiempo");
            return false;
        }
        catch (Exception e) { Log("sensores: parar: " + e.Message); return false; }
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
            // si la copia anterior sigue corriendo, cambiar la ruta no la cambia a ella: mejor no tocar y avisar
            if (!StopSensors()) { Log("sensores: no se reapuntó el servicio porque no se paró; usar Reparar"); return; }
            var r = SensorSvcExists()
                ? Sc("config", SensorSvc, "binPath=", bin, "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "Sensores de Vigía")
                : Sc("create", SensorSvc, "binPath=", bin, "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "Sensores de Vigía");
            if (r.Code != 0) { Log($"sensores: registrar servicio falló ({r.Code})"); return; }
            Sc("description", SensorSvc, "Lee la temperatura del procesador para Vigía mientras Vigía está abierta. Solo lectura; no recibe órdenes.");
            // si se cae, vuelve a los 60 s; tras un día sin fallos el contador se reinicia
            Sc("failure", SensorSvc, "reset=", "86400", "actions=", "restart/60000/restart/60000//");
            Sc("start", SensorSvc);
            // «activo» solo si Windows lo ve en marcha: el servicio avisa RUNNING cuando ya puede publicar
            var running = false;
            for (var i = 0; i < 40 && !running; i++) { running = SvcState(Sc("query", SensorSvc).Out) == 4; if (!running) Thread.Sleep(250); }
            Log(running ? "sensores: servicio activo" : "sensores: el servicio no arrancó; usar Reparar");
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
