using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vigia;

/// <summary>
/// Solo VigiaLab: Vigia --broker-hold &lt;segundos&gt; &lt;salida.jsonl&gt;.
/// Prueba de vida del ayudante con el canal real (UAC, tubería, lista cerrada):
///  1. abre el ayudante (aviso UAC de verdad) y anota su pid;
///  2. manda lecturas automáticas (telemetry) cada 5 s durante toda la prueba, como el Monitor;
///  3. pide labHold (tarea de la persona que dura &lt;segundos&gt;, sin tocar nada) y anota los latidos;
///  4. al terminar, mide cuánto tarda el ayudante en cerrarse solo (debería ser ~IdleLimit desde el final);
///  5. vuelve a pedir el ayudante (segundo aviso UAC) y comprueba que es otro proceso;
///  6. lo pide una tercera vez para cancelar el aviso: no debe quedar ningún ayudante.
/// Cada paso se escribe al momento en la salida (una línea JSON por evento).
/// </summary>
internal static class LabBrokerHold
{
    public static int Run(int seconds, string outPath)
    {
        if (!LabGuard.Allowed) { Console.Error.WriteLine(LabGuard.Refusal); return 3; }
        if (Elevation.IsAdmin) { Console.Error.WriteLine("Esta prueba se lanza sin permisos (como la interfaz)."); return 2; }
        var sw = new StreamWriter(outPath, append: false) { AutoFlush = true };
        void Log(string ev, object? data = null) =>
            sw.WriteLine(JsonSerializer.Serialize(new { t = DateTime.Now.ToString("HH:mm:ss"), ev, data }, Program.Json));
        try { return RunAsync(seconds, Log).GetAwaiter().GetResult(); }
        catch (Exception e) { Log("error", e.Message); return 1; }
        finally { sw.Dispose(); }
    }

    static async Task<int?> PingPid(Elevation.Broker b)
    {
        int? pid = null;
        var err = await b.RunAsync("ping", new JsonObject(), (t, p) =>
        {
            if (t == "pong" && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("pid", out var x)) pid = x.GetInt32();
        });
        return err is null ? pid : null;
    }

    static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch { return false; }
    }

    static int[] Brokers() =>
        Process.GetProcessesByName("Vigia").Where(p => p.Id != Environment.ProcessId).Select(p => p.Id).ToArray();

    static async Task<int> RunAsync(int seconds, Action<string, object?> Log)
    {
        Log("inicio", new { ui = Environment.ProcessId, uiElevated = Elevation.IsAdmin, seconds, idleLimitMin = Elevation.IdleLimit.TotalMinutes });

        // 1. primer aviso UAC
        Elevation.Broker b1;
        try { b1 = await Elevation.Broker.EnsureAsync(); }
        catch (Elevation.Denied d) { Log("uac1-negado", d.Message); return 4; }
        var pid1 = await PingPid(b1);
        if (pid1 is null) { Log("ping1-fallo", null); return 1; }
        Log("ayudante1", new { pid = pid1 });

        // 2. lecturas automáticas cada 5 s mientras haya ayudante (no deben renovar el plazo)
        using var stopPoll = new CancellationTokenSource();
        int reads = 0, readErrs = 0;
        var poll = Task.Run(async () =>
        {
            while (!stopPoll.IsCancellationRequested)
            {
                if (Elevation.Broker.Existing is { } b)
                {
                    var e = await b.RunAsync("telemetry", new JsonObject(), (_, _) => { });
                    if (e is null) Interlocked.Increment(ref reads); else Interlocked.Increment(ref readErrs);
                }
                try { await Task.Delay(5000, stopPoll.Token); } catch { }
            }
        });

        // 3. la tarea larga
        var holdStart = DateTime.UtcNow;
        int beats = 0, lastElapsed = -1; bool beatsElevated = true;
        var err = await b1.RunAsync("labHold", new JsonObject { ["seconds"] = seconds }, (t, p) =>
        {
            if (t != "labHold") return;
            beats++;
            lastElapsed = p.GetProperty("elapsed").GetInt32();
            beatsElevated &= p.GetProperty("elevated").GetBoolean();
            if (beats % 4 == 1 || p.TryGetProperty("done", out _)) Log("latido", new { elapsed = lastElapsed, pid = p.GetProperty("pid").GetInt32(), alive = Alive(pid1.Value) });
        });
        var holdEnd = DateTime.UtcNow;
        Log("tarea-fin", new { error = err, durS = (int)(holdEnd - holdStart).TotalSeconds, beats, lastElapsed, beatsElevated, aliveAtEnd = Alive(pid1.Value), reads, readErrs });

        // 4. esperar al cierre solo (hasta IdleLimit + 3 min), con las lecturas automáticas siguiendo
        var limit = holdEnd + Elevation.IdleLimit + TimeSpan.FromMinutes(3);
        DateTime? gone = null;
        while (DateTime.UtcNow < limit)
        {
            if (!Alive(pid1.Value)) { gone = DateTime.UtcNow; break; }
            await Task.Delay(5000);
        }
        stopPoll.Cancel(); await poll;
        var idleS = gone is { } g ? (int)(g - holdEnd).TotalSeconds : -1;
        Log("ayudante1-cierre", new { closed = gone is not null, idleS, reads, readErrs, uiElevated = Elevation.IsAdmin, existing = Elevation.Broker.Existing is not null });

        // 5. segundo aviso UAC: se puede volver a pedir
        int? pid2 = null;
        try
        {
            var b2 = await Elevation.Broker.EnsureAsync();
            pid2 = await PingPid(b2);
            Log("ayudante2", new { pid = pid2, distinto = pid2 != pid1 });
        }
        catch (Elevation.Denied d) { Log("uac2-negado", d.Message); }
        Elevation.Broker.Shutdown();
        for (var i = 0; i < 30 && pid2 is { } p2 && Alive(p2); i++) await Task.Delay(500);
        Log("ayudante2-cerrado", new { closed = pid2 is null || !Alive(pid2.Value) });

        // 6. tercer aviso: la persona dice que no
        Log("uac3-esperando-no", null);
        try { await Elevation.Broker.EnsureAsync(); Log("uac3-aceptado-inesperado", null); Elevation.Broker.Shutdown(); }
        catch (Elevation.Denied d) { Log("uac3-negado", d.Message); }
        await Task.Delay(3000);
        Log("fin", new { otrosVigia = Brokers(), uiElevated = Elevation.IsAdmin });

        var ok = err is null && beatsElevated && lastElapsed >= seconds - 2 && gone is not null
                 && idleS >= Elevation.IdleLimit.TotalSeconds - 5 && idleS <= Elevation.IdleLimit.TotalSeconds + 90
                 && pid2 is not null && pid2 != pid1;
        Log("veredicto", new { ok });
        return ok ? 0 : 1;
    }
}
