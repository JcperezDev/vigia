using System.Management;

namespace Vigia;

/// <summary>Los comandos de consola que cambian el sistema sin preguntar (--lab, --simple-run, --startup-offall) son para la
/// máquina virtual de pruebas. Piden las dos cosas: VIGIA_LAB=1 (o lab.flag) y que el equipo sea de verdad una VM de Hyper-V.
/// Así un «Vigia --lab apply all» escrito por error en un PC normal no hace nada.</summary>
static class LabGuard
{
    public static bool Allowed => Tweaks.Lab && IsVm(Read("Manufacturer"), Read("Model"));

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
        return string.Join(Environment.NewLine, log);
    }
}
