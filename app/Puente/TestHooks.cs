#if PUENTE_TEST
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Solo en la compilación de pruebas (tools/puente-test.js). Hace de «usuario sin permisos» sin crear cuentas ni
/// tocar UAC: pide a Windows un token SAFER de usuario normal (el grupo Administradores queda como solo-denegar)
/// y con él intenta escribir, crear, borrar y renombrar. Así se comprueba de verdad que tu usuario no puede cambiar
/// los .exe/.dll de la instalación, y que sí puede escribir en su propia carpeta (control).
/// </summary>
static class TestHooks
{
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool SaferCreateLevel(uint scopeId, uint levelId, uint openFlags, out IntPtr level, IntPtr reserved);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool SaferComputeTokenFromLevel(IntPtr level, IntPtr inToken, out IntPtr outToken, uint flags, IntPtr reserved);
    [DllImport("advapi32.dll")] static extern bool SaferCloseLevel(IntPtr level);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

    /// <summary>--as-user &lt;archivo&gt;: 4 intentos sobre el archivo y su carpeta. Imprime una línea por intento; exit = cuántos funcionaron.</summary>
    public static int AsUser(string file)
    {
        if (!SaferCreateLevel(2 /*USER*/, 0x20000 /*NORMALUSER*/, 1 /*OPEN*/, out var level, IntPtr.Zero)) { Console.WriteLine("ERROR SaferCreateLevel"); return 100; }
        try
        {
            if (!SaferComputeTokenFromLevel(level, IntPtr.Zero, out var tok, 0, IntPtr.Zero)) { Console.WriteLine("ERROR SaferComputeTokenFromLevel"); return 100; }
            using var h = new SafeAccessTokenHandle(tok);
            using (var id = new WindowsIdentity(tok))
            {
                var admin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                Console.WriteLine("token: administrador=" + admin);
                if (admin) return 100;   // el token no quedó limitado: la prueba no valdría
            }
            return WindowsIdentity.RunImpersonated(h, () =>
            {
                int ok = 0;
                var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
                void T(string what, Action a)
                {
                    try { a(); ok++; Console.WriteLine("PUEDE    " + what); }
                    catch (Exception e) when (e is UnauthorizedAccessException or IOException) { Console.WriteLine("NO PUEDE " + what); }
                }
                T("escribir " + Path.GetFileName(file), () => { using var f = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
                var nuevo = Path.Combine(dir, "plantado-" + Guid.NewGuid().ToString("N")[..6] + ".dll");
                T("crear un .dll al lado", () => { using (new FileStream(nuevo, FileMode.CreateNew, FileAccess.Write)) { } File.Delete(nuevo); });
                T("renombrar " + Path.GetFileName(file), () => { File.Move(file, file + ".x"); File.Move(file + ".x", file); });
                T("borrar " + Path.GetFileName(file), () =>
                {
                    // se pide el derecho de borrar (DELETE) sin borrar: si Windows da el permiso, se podría borrar
                    using var hf = CreateFileW(file, 0x00010000 /*DELETE*/, 7 /*compartir todo*/, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0, IntPtr.Zero);
                    if (hf.IsInvalid) throw new UnauthorizedAccessException();
                });
                return ok;
            });
        }
        finally { SaferCloseLevel(level); }
    }
}
#endif
