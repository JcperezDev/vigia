using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Vigia;

public record MemStatus(double TotalGb, double UsedGb, double AvailGb, int LoadPct,
    double StandbyGb, double StandbyLowGb, double ModifiedGb, double FreeGb, double CommitGb, double CommitLimitGb);
public record MemStep(string Name, bool Ok, string? Error);
/// <summary>Resultado medido antes y después. <see cref="FreedMb"/> es solo el cambio de «disponible», que casi nunca sube al soltar
/// caché en espera (Windows ya la cuenta como disponible). Por eso se separan: caché descartada, libre ganada y disponible.
/// Nada de esto es una mejora de FPS.</summary>
public record MemResult(double FreedMb, MemStatus Before, MemStatus After, MemStep[] Steps, bool Auto,
    double CacheDroppedMb = 0, double FreeGainedMb = 0, double ModifiedWrittenMb = 0, bool Deep = false);

/// <summary>Opciones de liberación. Son funciones públicas de Windows (NtSetSystemInformation).
/// Por defecto (y en bandeja, modo simple y automático) solo se suelta la caché en espera de baja prioridad:
/// no saca páginas de los programas ni obliga a releer del disco lo que se está usando. Lo demás son opciones avanzadas
/// que el usuario marca a mano en la vista Memoria.</summary>
public record MemOptions(bool WorkingSets = false, bool Modified = false, bool Standby = true, bool StandbyLowOnly = true,
    bool FileCache = false, bool Combine = false)
{
    public static readonly MemOptions Safe = new();
    /// <summary>Alguna operación que puede dar tirones (sacar memoria de procesos, escribir modificadas, toda la caché…).</summary>
    public bool IsDeep => WorkingSets || Modified || FileCache || Combine || (Standby && !StandbyLowOnly);
}

/// <summary>
/// Liberación de RAM con las API del kernel (NtSetSystemInformation / SystemMemoryListInformation).
/// No mata procesos ni toca archivos: vacía cachés que Windows vuelve a llenar cuando hace falta.
/// </summary>
public static class Memory
{
    const int SystemMemoryListInformation = 80, SystemCombinePhysicalMemoryInformation = 130;
    const int MemoryEmptyWorkingSets = 2, MemoryFlushModifiedList = 3, MemoryPurgeStandbyList = 4, MemoryPurgeLowPriorityStandbyList = 5;
    static readonly object Gate = new();
    static DateTime _lastAuto = DateTime.MinValue;
    public static MemResult? LastResult { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORY_COMBINE_INFORMATION_EX { public IntPtr Handle; public UIntPtr PagesCombined; public uint Flags; }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetSystemFileCacheSize(IntPtr min, IntPtr max, uint flags);
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref int cmd, int len);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref MEMORY_COMBINE_INFORMATION_EX info, int len);
    [DllImport("ntdll.dll")] static extern int RtlNtStatusToDosError(int status);

    const double GB = 1024d * 1024 * 1024;

    public static MemStatus Status()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        double standby = 0, standbyLow = 0, modified = 0, free = 0;
        // SYSTEM_MEMORY_LIST_INFORMATION: 5 contadores + 8 standby por prioridad + 8 reutilizadas + 1 = 22 ULONG_PTR
        var buf = Marshal.AllocHGlobal(22 * 8);
        try
        {
            if (NtQuerySystemInformation(SystemMemoryListInformation, buf, 22 * 8, out _) == 0)
            {
                long ps = Environment.SystemPageSize;
                long At(int i) => Marshal.ReadInt64(buf, i * 8);
                free = (At(0) + At(1)) * ps / GB;
                modified = At(2) * ps / GB;
                for (int p = 0; p < 8; p++)
                {
                    var g = At(5 + p) * ps / GB;
                    standby += g;
                    if (p <= 4) standbyLow += g;   // prioridades 0-4: caché de baja prioridad (lo que se puede soltar sin tirones)
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return new MemStatus(m.ullTotalPhys / GB, (m.ullTotalPhys - m.ullAvailPhys) / GB, m.ullAvailPhys / GB, (int)m.dwMemoryLoad,
            standby, standbyLow, modified, free, (m.ullTotalPageFile - m.ullAvailPageFile) / GB, m.ullTotalPageFile / GB);
    }

    public static MemResult Free(MemOptions o, bool auto = false)
    {
        lock (Gate)
        {
            Privileges.Enable("SeProfileSingleProcessPrivilege", "SeIncreaseQuotaPrivilege");
            var before = Status();
            var steps = new List<MemStep>();
            void Do(string name, Func<int> call)
            {
                try
                {
                    var st = call();
                    if (st == unchecked((int)0xC00000BB)) steps.Add(new(name, true, "no disponible en este Windows"));   // STATUS_NOT_SUPPORTED: combinación de páginas apagada
                    else steps.Add(st == 0 ? new(name, true, null) : new(name, false, new Win32Exception(RtlNtStatusToDosError(st)).Message));
                }
                catch (Exception e) { steps.Add(new(name, false, e.Message)); }
            }
            // Orden: primero sacar páginas de los procesos, luego escribir las modificadas, luego vaciar la caché.
            if (o.WorkingSets) Do("Memoria de los procesos", () => { int c = MemoryEmptyWorkingSets; return NtSetSystemInformation(SystemMemoryListInformation, ref c, 4); });
            if (o.FileCache) Do("Caché de archivos del sistema", () => SetSystemFileCacheSize(new IntPtr(-1), new IntPtr(-1), 0) ? 0 : unchecked((int)0xC0000001));
            if (o.Modified) Do("Páginas modificadas", () => { int c = MemoryFlushModifiedList; return NtSetSystemInformation(SystemMemoryListInformation, ref c, 4); });
            if (o.Standby) Do(o.StandbyLowOnly ? "Caché en espera (baja prioridad)" : "Caché en espera",
                () => { int c = o.StandbyLowOnly ? MemoryPurgeLowPriorityStandbyList : MemoryPurgeStandbyList; return NtSetSystemInformation(SystemMemoryListInformation, ref c, 4); });
            if (o.Combine) Do("Combinar páginas repetidas", () =>
            {
                var info = new MEMORY_COMBINE_INFORMATION_EX();
                return NtSetSystemInformation(SystemCombinePhysicalMemoryInformation, ref info, Marshal.SizeOf<MEMORY_COMBINE_INFORMATION_EX>());
            });
            Thread.Sleep(400);
            var after = Status();
            LastResult = new MemResult(Math.Max(0, (after.AvailGb - before.AvailGb) * 1024), before, after, steps.ToArray(), auto,
                CacheDroppedMb: Math.Max(0, (before.StandbyGb - after.StandbyGb) * 1024),
                FreeGainedMb: Math.Max(0, (after.FreeGb - before.FreeGb) * 1024),
                ModifiedWrittenMb: Math.Max(0, (before.ModifiedGb - after.ModifiedGb) * 1024),
                Deep: o.IsDeep);
            return LastResult;
        }
    }

    /// <summary>Modo automático: solo la caché en espera de baja prioridad nunca la memoria de los procesos
    /// mientras se juega, para no provocar tirones.</summary>
    public static MemResult? AutoTick(Settings s)
    {
        if (!s.MemAuto) return null;
        var st = Status();
        if (st.LoadPct < s.MemAutoThreshold || DateTime.Now - _lastAuto < TimeSpan.FromMinutes(Math.Max(1, s.MemAutoIntervalMin))) return null;
        // con un juego abierto no se toca nada: soltar caché en plena partida obliga a leer de disco otra vez
        if (AutoStart.Busy()) { AutoLog($"saltada: juego abierto (uso {st.LoadPct} %, libre {st.FreeGb:0.0} GB)"); _lastAuto = DateTime.Now; return null; }
        _lastAuto = DateTime.Now;
        // Nunca se vacía la memoria de los procesos en automático, juegues o no: todo lo que se saca vuelve del disco
        // al momento, y con poca RAM o disco lento eso deja el PC congelado varios segundos cada vez.
        // Solo se suelta la caché en espera de baja prioridad, y solo cuando de verdad queda poca libre.
        if (!(st.FreeGb < 2 && st.StandbyLowGb > 0.5)) return null;
        var res = Free(MemOptions.Safe, auto: true);
        AutoLog($"liberada caché en espera de baja prioridad: uso {st.LoadPct} %, libre {st.FreeGb:0.0} GB -> {res.After.AvailGb:0.0} GB disponibles");
        return res;
    }

    /// <summary>Comprueba la política sin liberar nada.</summary>
    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        Check(!MemOptions.Safe.IsDeep && MemOptions.Safe.Standby && MemOptions.Safe.StandbyLowOnly, "por defecto solo la caché en espera de baja prioridad");
        Check(!new MemOptions().WorkingSets && !new MemOptions().Combine && !new MemOptions().FileCache && !new MemOptions().Modified, "new MemOptions() ya no vacía procesos, caché de archivos, modificadas ni combina");
        Check(new MemOptions(WorkingSets: true).IsDeep && new MemOptions(StandbyLowOnly: false).IsDeep, "lo agresivo se marca como avanzado");
        var j = System.Text.Json.JsonSerializer.Deserialize<MemOptions>("{}", Program.Json);
        Check(j is not null && !j.IsDeep, "un mensaje sin opciones usa la política segura");
        var st = Status();
        Check(st.TotalGb > 0 && st.AvailGb >= 0 && st.StandbyGb >= st.StandbyLowGb, "lectura de listas de memoria coherente");
        return string.Join(Environment.NewLine, log);
    }

    /// <summary>Cada intervención automática queda con su motivo y resultado en memoria.log (máx. ~256 KB).</summary>
    static void AutoLog(string line)
    {
        try
        {
            var p = Path.Combine(Settings.Dir, "memoria.log");
            if (File.Exists(p) && new FileInfo(p).Length > 256 * 1024) File.Delete(p);
            File.AppendAllText(p, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }
}

/// <summary>Activa privilegios del token del proceso (requiere ejecutar como administrador).</summary>
static class Privileges
{
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct TOKEN_PRIVILEGES { public int Count; public LUID Luid; public int Attr; }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr h, int access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string? sys, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES p, int len, IntPtr prev, IntPtr ret);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

    public static void Enable(params string[] names)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var tok)) return;   // ADJUST_PRIVILEGES | QUERY
        try
        {
            foreach (var n in names)
                if (LookupPrivilegeValue(null, n, out var luid))
                {
                    var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attr = 2 };   // SE_PRIVILEGE_ENABLED
                    AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
        }
        finally { CloseHandle(tok); }
    }
}
