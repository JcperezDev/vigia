using System.Diagnostics;
using System.Management;

namespace Vigia;

/// <summary>
/// Cuánto gasta Vigía de verdad: su proceso y los de WebView2 que cuelgan de él (CPU acumulada y memoria privada).
/// Lo usa --bench para comparar ventana abierta contra minimizada.
/// </summary>
static class Bench
{
    public record Sample(DateTime At, double CpuSec, long PrivateMb, long WorkingMb, int Procs);

    public static Sample Take()
    {
        var me = Environment.ProcessId;
        var parent = new Dictionary<int, int>();
        using (var q = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process"))
            foreach (ManagementObject o in q.Get())
                using (o) parent[Convert.ToInt32(o["ProcessId"])] = Convert.ToInt32(o["ParentProcessId"]);
        var tree = new HashSet<int> { me };
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var (pid, pp) in parent)
                if (!tree.Contains(pid) && tree.Contains(pp) && pid != pp) { tree.Add(pid); grew = true; }
        }
        double cpu = 0; long priv = 0, ws = 0; int n = 0;
        foreach (var pid in tree)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                cpu += p.TotalProcessorTime.TotalSeconds; priv += p.PrivateMemorySize64; ws += p.WorkingSet64; n++;
            }
            catch { /* ya terminó */ }
        }
        return new Sample(DateTime.UtcNow, cpu, priv >> 20, ws >> 20, n);
    }

    /// <summary>% de UN núcleo usado entre dos muestras (100 % = un núcleo entero).</summary>
    public static double CorePct(Sample a, Sample b) => Math.Round((b.CpuSec - a.CpuSec) / Math.Max(0.001, (b.At - a.At).TotalSeconds) * 100, 2);
}
