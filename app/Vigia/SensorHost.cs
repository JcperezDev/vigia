using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace Vigia;

/// <summary>
/// «Sensores de Vigía»: servicio de Windows de solo lectura que instala Puente. Corre como SYSTEM desde la carpeta
/// protegida de Archivos de programa, lee los grados del procesador (con el driver oficial PawnIO, que exige permisos)
/// y los deja en una memoria compartida que los demás procesos solo pueden leer. No escucha órdenes ni lee datos de
/// nadie: no hay tubería, puerto ni archivo de entrada. Así Vigía puede correr sin administrador y ver los grados.
/// Solo trabaja mientras hay una ventana de Vigía abierta; sin Vigía abierta no toca el hardware.
/// </summary>
public static class SensorHost
{
    public const string ServiceName = "VigiaSensores";
    public const string MapName = @"Global\VigiaSensores";
    const string TestPrefix = @"Local\VigiaSensoresTest";
    const uint Magic = 0x31534756;            // "VGS1"
    const int Ver = 1, Size = 64;
    // 0 magic u32 · 4 versión i32 · 8 secuencia i64 (impar = escribiendo) · 16 hora UTC (FILETIME) i64 · 24 grados double (NaN = sin dato)
    static readonly TimeSpan Fresh = TimeSpan.FromSeconds(15);

    public enum Share { Ok, Missing, Untrusted, Stale, NoValue }

    /// <summary>Nombre que se lee. VIGIA_SENSOR_MAP solo puede apuntar a un nombre de prueba (Local\VigiaSensoresTest…).</summary>
    static string ReadName()
    {
        var env = Environment.GetEnvironmentVariable("VIGIA_SENSOR_MAP");
        return env is not null && env.StartsWith(TestPrefix, StringComparison.Ordinal) ? env : MapName;
    }

    // ---------------------------------------------------------------- lectura (Vigía sin permisos)

    public static (Share State, double? Temp) ReadCpu() => ReadCpu(ReadName(), DateTime.UtcNow);

    internal static (Share, double?) ReadCpu(string name, DateTime now)
    {
        var h = OpenFileMappingW(FILE_MAP_READ | READ_CONTROL, false, name);
        if (h == IntPtr.Zero) return (Share.Missing, null);
        try
        {
            // Solo vale si lo creó SYSTEM o Administradores: un usuario normal no puede crear nombres Global\,
            // y aunque lo hiciera en su sesión, su objeto tendría otro dueño y se ignora.
            if (!TrustedOwner(h)) return (Share.Untrusted, null);
            var v = MapViewOfFile(h, FILE_MAP_READ, 0, 0, (UIntPtr)Size);
            if (v == IntPtr.Zero) return (Share.Missing, null);
            try
            {
                if ((uint)Marshal.ReadInt32(v, 0) != Magic || Marshal.ReadInt32(v, 4) != Ver) return (Share.Untrusted, null);
                for (var i = 0; i < 5; i++)
                {
                    var s1 = Marshal.ReadInt64(v, 8);
                    if ((s1 & 1) != 0) { Thread.Sleep(1); continue; }
                    Thread.MemoryBarrier();
                    var ft = Marshal.ReadInt64(v, 16);
                    var t = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(v, 24));
                    Thread.MemoryBarrier();
                    if (Marshal.ReadInt64(v, 8) != s1) continue;
                    DateTime at;
                    try { at = DateTime.FromFileTimeUtc(ft); } catch { return (Share.Stale, null); }
                    if (now - at > Fresh || at - now > TimeSpan.FromSeconds(5)) return (Share.Stale, null);
                    return double.IsFinite(t) && t > 1 && t < 150 ? (Share.Ok, Math.Round(t, 2)) : (Share.NoValue, null);
                }
                return (Share.Stale, null);
            }
            finally { UnmapViewOfFile(v); }
        }
        finally { CloseHandle(h); }
    }

    static bool TrustedOwner(IntPtr h) => Owner(h) is { } sid
        && (sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));

    static SecurityIdentifier? Owner(IntPtr h)
    {
        if (GetSecurityInfo(h, 6 /*SE_KERNEL_OBJECT*/, 1 /*OWNER*/, out var owner, out _, out _, out _, out var sd) != 0) return null;
        try { return new SecurityIdentifier(owner); }
        catch { return null; }
        finally { LocalFree(sd); }
    }

    /// <summary>Texto para Monitor cuando no hay grados y Vigía no tiene permisos.</summary>
    public static string? Explain(Share s) => s switch
    {
        Share.Missing => "Sensores de Vigía no está activo, así que sin permisos no se ven los grados del procesador. Abre Puente y usa Reparar.",
        Share.Stale => "Sensores de Vigía no está respondiendo. Si sigue así, abre Puente y usa Reparar.",
        Share.Untrusted => "Se ignoró una fuente de sensores que no es de Vigía.",
        Share.NoValue => "Sensores de Vigía está activo, pero este procesador no entrega una temperatura válida.",
        _ => null,
    };

    // ---------------------------------------------------------------- escritura (servicio)

    sealed class Publisher : IDisposable
    {
        readonly IntPtr _h, _v;
        long _seq;
        public Publisher(IntPtr h, IntPtr v)
        {
            _h = h; _v = v;
            _seq = Marshal.ReadInt64(_v, 8);   // reutilizada: sigue la secuencia (par = dato completo) para no confundir a un lector
            if ((_seq & 1) != 0) _seq++;
            Marshal.WriteInt32(_v, 0, (int)Magic); Marshal.WriteInt32(_v, 4, Ver);
        }

        public void Write(double? temp, DateTime utc)
        {
            Marshal.WriteInt64(_v, 8, ++_seq);   // impar: escribiendo
            Thread.MemoryBarrier();
            Marshal.WriteInt64(_v, 16, utc.ToFileTimeUtc());
            Marshal.WriteInt64(_v, 24, BitConverter.DoubleToInt64Bits(temp ?? double.NaN));
            Thread.MemoryBarrier();
            Marshal.WriteInt64(_v, 8, ++_seq);
        }

        public void Dispose() { UnmapViewOfFile(_v); CloseHandle(_h); }
    }

    /// <summary>Crea la memoria compartida: escribe solo SYSTEM; leen Administradores y usuarios autenticados.
    /// Si el nombre ya existía se reutiliza solo si su dueño es el mismo que pondría este proceso (la dejó la copia
    /// anterior y un lector la mantenía abierta); con otro dueño se rechaza.</summary>
    static Publisher? Create(string name, out string? error)
    {
        error = null;
        var system = WindowsIdentity.GetCurrent().IsSystem;
        var owner = system ? "SY" : "BA";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW($"O:{owner}D:P(A;;GA;;;SY)(A;;GR;;;BA)(A;;GR;;;AU)", 1, out var sd, IntPtr.Zero))
        { error = "permisos: " + Marshal.GetLastWin32Error(); return null; }
        try
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            var h = CreateFileMappingW(new IntPtr(-1), ref sa, 0x04 /*PAGE_READWRITE*/, 0, Size, name);
            var err = Marshal.GetLastWin32Error();
            if (h == IntPtr.Zero) { error = "crear: " + err; return null; }
            if (err == 183 /*ERROR_ALREADY_EXISTS*/ && Owner(h)?.IsWellKnown(system ? WellKnownSidType.LocalSystemSid : WellKnownSidType.BuiltinAdministratorsSid) != true)
            { CloseHandle(h); error = "el nombre ya existía con otro dueño"; return null; }
            var v = MapViewOfFile(h, 0x02 /*FILE_MAP_WRITE*/, 0, 0, (UIntPtr)Size);
            if (v == IntPtr.Zero) { error = "mapear: " + Marshal.GetLastWin32Error(); CloseHandle(h); return null; }
            return new Publisher(h, v);
        }
        finally { LocalFree(sd); }
    }

    /// <summary>¿Hay una Vigía de esta instalación abierta? Un proceso que solo se llame «Vigia» desde otra carpeta no cuenta.</summary>
    static bool VigiaOpen()
    {
        var me = Environment.ProcessId;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")).TrimEnd('\\') + "\\";
        var all = Process.GetProcessesByName("Vigia");
        try
        {
            foreach (var p in all)
            {
                if (p.Id == me) continue;
                string? exe = null;
                try { exe = p.MainModule?.FileName; } catch { }
                if (exe is not null && exe.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        finally { foreach (var p in all) p.Dispose(); }
    }

    /// <summary>Bucle del servicio. Sin una ventana de Vigía abierta no lee el hardware.</summary>
    static int Loop(string name, WaitHandle stop, TimeSpan? limit, bool always, Action? ready = null)
    {
        Publisher? made = null; string? error = null;
        for (var i = 0; i < 5 && made is null; i++)
            if ((made = Create(name, out error)) is null && stop.WaitOne(TimeSpan.FromSeconds(2))) break;
        using var pub = made;
        if (pub is null) { Log("no se pudo publicar: " + error); return 5; }
        ready?.Invoke();   // «en marcha» solo cuando ya se puede publicar
        Computer? pc = null;
        var misses = 0;
        var until = limit is { } l ? DateTime.UtcNow + l : DateTime.MaxValue;
        try
        {
            while (!stop.WaitOne(TimeSpan.FromSeconds(2)) && DateTime.UtcNow < until)
            {
                if (!always && !VigiaOpen()) { if (pc is not null) { try { pc.Close(); } catch { } pc = null; } continue; }
                if (pc is null)
                {
                    var c = new Computer { IsCpuEnabled = true };
                    try { c.Open(); pc = c; }
                    catch (Exception e) { Log("abrir sensores: " + e.Message); try { c.Close(); } catch { } }
                }
                double? t = null;
                if (pc is not null)
                    foreach (var h in pc.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
                        try { h.Update(); t ??= Telemetry.CpuTemp(h); } catch { }
                pub.Write(t, DateTime.UtcNow);
                // si el driver llegó después de arrancar, LHM hay que reabrirlo para que lo use
                if (t is null && ++misses >= 30 && pc is not null) { try { pc.Close(); } catch { } pc = null; misses = 0; }
                if (t is not null) misses = 0;
            }
        }
        finally { try { pc?.Close(); } catch { } }
        return 0;
    }

    static void Log(string s)
    {
        try { EventLog.WriteEntry("Application", "Sensores de Vigía: " + s, EventLogEntryType.Warning); } catch { }
    }

    /// <summary>Vigia.exe --sensor-service: solo como SYSTEM y solo desde la carpeta protegida.</summary>
    public static int RunService()
    {
        if (!WindowsIdentity.GetCurrent().IsSystem) return 3;
        if (InstallTrust.Problem(AppContext.BaseDirectory) is { } why) { Log("carpeta sin proteger, no arranco: " + why); return 3; }
        _main = ServiceMain; _handler = Handler;
        var table = new[] { new TableEntry { Name = ServiceName, Proc = _main }, new TableEntry() };
        return StartServiceCtrlDispatcherW(table) ? 0 : 2;
    }

    /// <summary>Prueba sin servicio: Vigia.exe --sensor-publish SEGUNDOS (administrador; nombre Local\VigiaSensoresTest).</summary>
    public static int RunTest(int seconds)
    {
        if (!Elevation.IsAdmin) return 3;
        var name = Environment.GetEnvironmentVariable("VIGIA_SENSOR_MAP") is { } n && n.StartsWith(TestPrefix, StringComparison.Ordinal) ? n : TestPrefix;
        using var stop = new ManualResetEvent(false);
        return Loop(name, stop, TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300)), always: true);
    }

    static ServiceMainFn? _main;
    static HandlerEx? _handler;
    static IntPtr _status;
    static readonly ManualResetEvent Stop = new(false);

    static void ServiceMain(int argc, IntPtr argv)
    {
        _status = RegisterServiceCtrlHandlerExW(ServiceName, _handler!, IntPtr.Zero);
        if (_status == IntPtr.Zero) return;
        Report(2 /*START_PENDING*/, 0);
        var rc = 0;
        try { rc = Loop(MapName, Stop, null, always: false, ready: () => Report(4 /*RUNNING*/, 0)); } catch (Exception e) { Log(e.Message); rc = 1; }
        Report(1 /*STOPPED*/, rc);
    }

    static int Handler(int control, int type, IntPtr data, IntPtr ctx)
    {
        switch (control)
        {
            case 1: case 5: Report(3 /*STOP_PENDING*/, 0); Stop.Set(); return 0;   // parar / apagar
            case 4: return 0;                                                        // interrogar
            default: return 120;                                                     // ERROR_CALL_NOT_IMPLEMENTED
        }
    }

    static void Report(int state, int exit)
    {
        var s = new SERVICE_STATUS
        {
            dwServiceType = 0x10, dwCurrentState = state,
            dwControlsAccepted = state == 4 ? 1 | 4 : 0,
            dwWin32ExitCode = exit == 0 ? 0 : 1066 /*ERROR_SERVICE_SPECIFIC_ERROR*/, dwServiceSpecificExitCode = exit,
            dwWaitHint = state == 3 ? 5000 : state == 2 ? 15000 : 0,
        };
        SetServiceStatus(_status, ref s);
    }

    // ---------------------------------------------------------------- pruebas

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        var name = $"{TestPrefix}Self{Environment.ProcessId}";
        Check(ReadCpu(name, DateTime.UtcNow).Item1 == Share.Missing, "sin servicio: «no activo», nunca 0 °C");
        if (!Elevation.IsAdmin) { Check(true, "(publicar se prueba solo con administrador)"); return string.Join(Environment.NewLine, log); }
        using (var pub = Create(name, out var err))
        {
            Check(pub is not null, "se crea la memoria compartida " + (err ?? ""));
            if (pub is null) return string.Join(Environment.NewLine, log);
            // Reabrir para escribir solo lo puede SYSTEM (la DACL da a Administradores solo lectura): el servicio que se reinicia
            // reutiliza la memoria que dejó su copia anterior; un administrador u otro proceso, no.
            using (var again = Create(name, out var againErr))
                if (WindowsIdentity.GetCurrent().IsSystem) Check(again is not null, "SYSTEM reutiliza la memoria que dejó su copia anterior");
                else Check(again is null && againErr is not null, "sin ser SYSTEM no se puede reabrir para escribir (" + againErr + ")");
            var squat = $"{TestPrefix}Ajena{Environment.ProcessId}";
            var me = WindowsIdentity.GetCurrent().User!.Value;
            if (ConvertStringSecurityDescriptorToSecurityDescriptorW($"O:{me}D:P(A;;GA;;;WD)", 1, out var ssd, IntPtr.Zero))
            {
                var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = ssd };
                var sh = CreateFileMappingW(new IntPtr(-1), ref sa, 0x04, 0, Size, squat);
                LocalFree(ssd);
                try
                {
                    using (var taken = Create(squat, out var why)) Check(sh != IntPtr.Zero && taken is null && why!.Contains("otro dueño"), "un nombre creado por otro dueño (cualquiera puede escribir) no se usa");
                    Check(ReadCpu(squat, DateTime.UtcNow).Item1 == Share.Untrusted, "y el lector tampoco se fía de él");
                }
                finally { if (sh != IntPtr.Zero) CloseHandle(sh); }
            }
            else Check(false, "no se pudo preparar el objeto ajeno de prueba");
            var now = DateTime.UtcNow;
            pub.Write(54.37, now);
            var (s, t) = ReadCpu(name, now);
            Check(s == Share.Ok && t == 54.37, "se leen los grados publicados");
            Check(ReadCpu(name, now + TimeSpan.FromSeconds(30)).Item1 == Share.Stale, "un dato viejo (servicio colgado) no se muestra");
            pub.Write(null, now);
            Check(ReadCpu(name, now).Item1 == Share.NoValue, "sin temperatura válida se dice, no sale 0 °C");
            pub.Write(0, now);
            Check(ReadCpu(name, now).Item1 == Share.NoValue, "0 °C no cuenta como lectura");
            pub.Write(200, now);
            Check(ReadCpu(name, now).Item1 == Share.NoValue, "200 °C no cuenta como lectura");
        }
        Check(ReadCpu(name, DateTime.UtcNow).Item1 == Share.Missing, "al cerrar el servicio desaparece");
        Environment.SetEnvironmentVariable("VIGIA_SENSOR_MAP", @"Global\Otra");
        Check(ReadName() == MapName, "VIGIA_SENSOR_MAP no puede apuntar a otro nombre");
        Environment.SetEnvironmentVariable("VIGIA_SENSOR_MAP", null);
        Check(Explain(Share.Missing)!.Contains("Reparar"), "si falta el servicio se dice qué hacer");
        return string.Join(Environment.NewLine, log);
    }

    // ---------------------------------------------------------------- Win32

    const uint FILE_MAP_READ = 0x04, READ_CONTROL = 0x20000;
    [StructLayout(LayoutKind.Sequential)] struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }
    [StructLayout(LayoutKind.Sequential)]
    struct SERVICE_STATUS { public int dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TableEntry { public string? Name; public ServiceMainFn? Proc; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ServiceMainFn(int argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int HandlerEx(int control, int type, IntPtr data, IntPtr ctx);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateFileMappingW(IntPtr file, ref SECURITY_ATTRIBUTES sa, uint protect, uint hi, uint lo, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint hi, uint lo, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern bool UnmapViewOfFile(IntPtr v);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    [DllImport("advapi32.dll")] static extern int GetSecurityInfo(IntPtr h, int type, int info, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, int rev, out IntPtr sd, IntPtr size);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool StartServiceCtrlDispatcherW(TableEntry[] table);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr RegisterServiceCtrlHandlerExW(string name, HandlerEx handler, IntPtr ctx);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SetServiceStatus(IntPtr h, ref SERVICE_STATUS s);
}
