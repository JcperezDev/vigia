using System.Management;

namespace Vigia;

/// <summary>Los comandos de consola que cambian el sistema sin preguntar (--lab, --simple-run, --startup-offall) son para la
/// máquina virtual de pruebas. Piden las dos cosas: VIGIA_LAB=1 (o lab.flag) y que el equipo sea de verdad una VM de Hyper-V.
/// Así un «Vigia --lab apply all» escrito por error en un PC normal no hace nada. Lo mismo para lo que el laboratorio
/// relaja en la interfaz (Apps de riesgo medio): VIGIA_LAB=1 o lab.flag solos, en un PC normal, no desbloquean nada.</summary>
static class LabGuard
{
    public static bool Allowed => Tweaks.Lab && OnVm.Value;

    // El fabricante y el modelo no cambian mientras Vigía está abierta: se leen una vez.
    static readonly Lazy<bool> OnVm = new(() => IsVm(Read("Manufacturer"), Read("Model")));

    public const string Refusal = "Solo en la máquina virtual de pruebas (VIGIA_LAB=1 y una VM de Hyper-V). No se cambió nada.";

    /// <summary>Hyper-V se presenta como fabricante «Microsoft Corporation» y modelo «Virtual Machine».</summary>
    public static bool IsVm(string? maker, string? model) =>
        string.Equals(maker?.Trim(), "Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
        && string.Equals(model?.Trim(), "Virtual Machine", StringComparison.OrdinalIgnoreCase);

    static string? Read(string prop)
    {
        try
        {
            using var s = new ManagementObjectSearcher($"SELECT {prop} FROM Win32_ComputerSystem");
            foreach (var o in s.Get()) return o[prop] as string;
        }
        catch { }
        return null;
    }

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        Check(IsVm("Microsoft Corporation", "Virtual Machine"), "una VM de Hyper-V cuenta como laboratorio");
        Check(!IsVm("Micro-Star International Co., Ltd.", "MS-7E12"), "un PC normal no cuenta como laboratorio");
        Check(!IsVm("Microsoft Corporation", "Surface Laptop 7"), "un Surface no es una VM");
        Check(!IsVm(null, null), "sin datos de WMI no se da por VM");
        if (!Allowed)
        {
            // labHold (prueba de vida del ayudante) la rechaza el propio ayudante fuera de la VM, aunque llegue por la tubería
            string? err = null;
            try { AdminOps.Run("labHold", new System.Text.Json.Nodes.JsonObject { ["seconds"] = 30 }, (_, _) => { }).GetAwaiter().GetResult(); }
            catch (Exception e) { err = e.Message; }
            Check(err == Refusal, "labHold no hace nada fuera de la VM de pruebas");
        }
        return string.Join(Environment.NewLine, log);
    }
}
