using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Puente;

/// <summary>
/// Llamadas a Windows que Puente necesita y .NET no trae:
///  - quién es la persona que usa el escritorio (aunque Puente corra con permisos de administrador),
///  - abrir Vigía como esa persona, sin permisos (Puente no le pasa su elevación),
///  - qué programas tienen abiertos los archivos de Vigía (Restart Manager), para no reemplazar a medias.
/// </summary>
static class Native
{
    public static bool IsAdmin
    {
        get
        {
            try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------------- la cuenta del escritorio

    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attrs, int level, int type, out IntPtr dup);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string? app, StringBuilder cmd, uint flags, IntPtr env, string? dir, ref StartupInfo si, out ProcessInformation pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfo
    {
        public int cb; public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    const uint QueryLimited = 0x1000, TokenQuery = 0x0008, TokenDuplicate = 0x0002, TokenAssign = 0x0001, TokenAll = 0xF01FF;

    /// <summary>Token de la persona que usa el escritorio (el del Explorador). IntPtr.Zero si no hay escritorio.</summary>
    static IntPtr ShellToken()
    {
        var w = GetShellWindow();
        if (w == IntPtr.Zero || GetWindowThreadProcessId(w, out var pid) == 0 || pid == 0) return IntPtr.Zero;
        var p = OpenProcess(QueryLimited, false, pid);
        if (p == IntPtr.Zero) return IntPtr.Zero;
        try { return OpenProcessToken(p, TokenQuery | TokenDuplicate | TokenAssign, out var t) ? t : IntPtr.Zero; }
        finally { CloseHandle(p); }
    }

    /// <summary>SID de la persona del escritorio (si se dio permiso con otra cuenta, sigue siendo la persona que usa el PC).</summary>
    public static SecurityIdentifier? ShellUser()
    {
        var t = ShellToken();
        if (t == IntPtr.Zero) return null;
        try { using var id = new WindowsIdentity(t); return id.User; }
        catch { return null; }
        finally { CloseHandle(t); }
    }

    /// <summary>%LOCALAPPDATA% de la persona del escritorio. null si no se puede saber.</summary>
    public static string? ShellLocalAppData()
    {
        var t = ShellToken();
        if (t == IntPtr.Zero) return null;
        try
        {
            var id = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");   // FOLDERID_LocalAppData
            if (SHGetKnownFolderPath(ref id, 0, t, out var p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); } finally { CoTaskMemFree(p); }
        }
        finally { CloseHandle(t); }
    }

    public static readonly Guid FolderDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    public static readonly Guid FolderPrograms = new("A77F5D77-2E2B-44C3-A6A2-ABA601054A51");

    /// <summary>Una carpeta conocida (Escritorio, Programas…) de la persona del escritorio. null si no se puede saber.</summary>
    public static string? ShellFolder(Guid folder)
    {
        var t = ShellToken();
        if (t == IntPtr.Zero) return null;
        try
        {
            if (SHGetKnownFolderPath(ref folder, 0, t, out var p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); } finally { CoTaskMemFree(p); }
        }
        finally { CloseHandle(t); }
    }

    /// <summary>
    /// Abre un programa como la persona del escritorio, sin los permisos de Puente. Devuelve el código de salida si
    /// <paramref name="waitMs"/> &gt; 0 y terminó a tiempo; -1 si quedó abierto; null si no se pudo abrir así.
    /// </summary>
    public static int? RunAsShellUser(string exe, string args, string dir, uint waitMs = 0)
    {
        var t = ShellToken();
        if (t == IntPtr.Zero) return null;
        IntPtr dup = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(t, TokenAll, IntPtr.Zero, 2 /*SecurityImpersonation*/, 1 /*TokenPrimary*/, out dup)) return null;
            var si = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            var cmd = new StringBuilder($"\"{exe}\" {args}".TrimEnd());
            if (!CreateProcessWithTokenW(dup, 0, exe, cmd, 0, IntPtr.Zero, dir, ref si, out var pi)) return null;
            try
            {
                if (waitMs == 0) return -1;
                if (WaitForSingleObject(pi.hProcess, waitMs) != 0) return -1;
                return GetExitCodeProcess(pi.hProcess, out var code) ? (int)code : null;
            }
            finally { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
        }
        finally { if (dup != IntPtr.Zero) CloseHandle(dup); CloseHandle(t); }
    }

    // ---------------------------------------------------------------- quién tiene abiertos los archivos

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint session, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint session, uint nFiles, string[] files, uint nApps, IntPtr apps, uint nServices, IntPtr services);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] RmProcessInfo[]? info, ref uint reasons);

    [StructLayout(LayoutKind.Sequential)] struct RmUniqueProcess { public int dwProcessId; public long ProcessStartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public int ApplicationType; public uint AppStatus; public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    /// <summary>Programas que tienen abierto algún .exe o .dll de esa carpeta (pid y nombre). Vacío si nadie o si no se pudo saber.</summary>
    public static List<(int Pid, string Name)> Lockers(string dir)
    {
        var res = new List<(int, string)>();
        if (!Directory.Exists(dir)) return res;
        var files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Take(1000).ToArray();
        if (files.Length == 0) return res;
        var key = new StringBuilder(64);
        if (RmStartSession(out var s, 0, key) != 0) return res;
        try
        {
            if (RmRegisterResources(s, (uint)files.Length, files, 0, IntPtr.Zero, 0, IntPtr.Zero) != 0) return res;
            uint n = 0, reasons = 0;
            int r = RmGetList(s, out var need, ref n, null, ref reasons);
            if (r == 234 /*ERROR_MORE_DATA*/ && need > 0)
            {
                var arr = new RmProcessInfo[need]; n = need;
                if (RmGetList(s, out _, ref n, arr, ref reasons) == 0)
                    for (int i = 0; i < n; i++) res.Add((arr[i].Process.dwProcessId, arr[i].strAppName));
            }
        }
        finally { RmEndSession(s); }
        return res;
    }

    // ---------------------------------------------------------------- clave de firma protegida con DPAPI (cuenta actual)

    [StructLayout(LayoutKind.Sequential)] struct DataBlob { public int cbData; public IntPtr pbData; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DataBlob data, string? desc, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref DataBlob data, IntPtr desc, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    static readonly byte[] Entropy = "Vigia.release-key.v1"u8.ToArray();

    public static byte[] Protect(byte[] plain) => Dpapi(plain, protect: true);
    public static byte[] Unprotect(byte[] blob) => Dpapi(blob, protect: false);

    static byte[] Dpapi(byte[] input, bool protect)
    {
        var hIn = GCHandle.Alloc(input, GCHandleType.Pinned);
        var hEnt = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var inB = new DataBlob { cbData = input.Length, pbData = hIn.AddrOfPinnedObject() };
            var ent = new DataBlob { cbData = Entropy.Length, pbData = hEnt.AddrOfPinnedObject() };
            const int UiForbidden = 0x1;
            bool ok = protect ? CryptProtectData(ref inB, "Vigia release key", ref ent, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var outB)
                              : CryptUnprotectData(ref inB, IntPtr.Zero, ref ent, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outB);
            if (!ok) throw new InvalidOperationException("Windows no pudo " + (protect ? "proteger" : "abrir") + " la clave (error " + Marshal.GetLastWin32Error() + ").");
            try { var r = new byte[outB.cbData]; Marshal.Copy(outB.pbData, r, 0, outB.cbData); return r; }
            finally
            {
                // lo descifrado no se queda en la memoria de Windows
                for (int i = 0; i < outB.cbData; i++) Marshal.WriteByte(outB.pbData, i, 0);
                LocalFree(outB.pbData);
            }
        }
        finally { hIn.Free(); hEnt.Free(); }
    }

    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SetTokenInformation(IntPtr token, int cls, IntPtr info, int len);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Que todo lo que cree este proceso tenga como dueño a Administradores, no a tu usuario. Según la directiva
    /// «Propietario predeterminado de objetos creados por administradores» (y con UAC apagado) el dueño sería la
    /// cuenta, y el dueño de un archivo siempre puede reescribir sus permisos: eso abriría la carpeta protegida.
    /// Solo con permisos de administrador (el token trae el grupo con derecho a ser dueño).
    /// </summary>
    public static bool UseAdminsAsOwner()
    {
        if (!IsAdmin) return false;
        var sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var bytes = new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes, 0);
        IntPtr pSid = Marshal.AllocHGlobal(bytes.Length), pOwner = Marshal.AllocHGlobal(IntPtr.Size), tok = IntPtr.Zero;
        try
        {
            Marshal.Copy(bytes, 0, pSid, bytes.Length);
            Marshal.WriteIntPtr(pOwner, pSid);
            if (!OpenProcessToken(GetCurrentProcess(), 0x0080 /*TOKEN_ADJUST_DEFAULT*/, out tok)) return false;
            return SetTokenInformation(tok, 4 /*TokenOwner*/, pOwner, IntPtr.Size);
        }
        finally { if (tok != IntPtr.Zero) CloseHandle(tok); Marshal.FreeHGlobal(pOwner); Marshal.FreeHGlobal(pSid); }
    }
}
