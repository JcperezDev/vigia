using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Vigia;

public record ToolItem(string Id, string Group, string Name, string Detail, string Path, bool Found, string? Warning, string Kind,
    string? Publisher = null, string? Winget = null, string? Url = null, bool Confirm = false);
public record ToolDone(string Id, bool Ok, string? Message);

/// <summary>
/// Herramientas: las de Windows, reparaciones con las órdenes de Microsoft y un catálogo corto de programas conocidos
/// que se instalan con WinGet (repositorio oficial winget-pkgs) solo si el usuario lo pide.
/// - No hay carpetas personales ni rutas de ningún equipo concreto, ni se buscan .exe/.msi/.lnk sueltos en una carpeta.
/// - Los programas de otros se abren SIN el permiso de administrador de Vigía (con el token del Explorador);
///   si de verdad lo necesitan, Windows lo pedirá con su propia ventana.
/// - Los identificadores WinGet se comprobaron con «winget show --id … -e» (2026-10-29). Ver docs/TOOLS.md.
/// </summary>
public static class Toolbox
{
    // Kind: "win" = programa, panel o página de Configuración de Windows; "cmd" = orden de consola en su propia ventana;
    //       "action" = la hace Vigía; "app" = programa de otro editor, instalable con WinGet.
    record Def(string Id, string Group, string Name, string Detail, string Target, string Kind = "win", string? Warning = null);

    /// <summary>Programa de otro editor. <c>Paths</c>: dónde lo deja su instalador; <c>Uninstall</c>: inicio del nombre en
    /// «Aplicaciones instaladas»; <c>Needs</c>: "nvidia" si solo sirve con una gráfica NVIDIA.</summary>
    record AppDef(string Id, string Group, string Name, string Detail, string Publisher, string Winget, string Url,
        string[] Paths, string Uninstall, string? Warning = null, string? Needs = null);

    const string Win = "win", Cmd = "cmd", Act = "action", App = "app";

    static readonly AppDef[] Apps =
    [
        new("cpuz", "Información", "CPU-Z", "Modelo exacto de CPU, placa y RAM con sus tiempos.", "CPUID", "CPUID.CPU-Z", "https://www.cpuid.com/softwares/cpu-z.html",
            [@"%ProgramFiles%\CPUID\CPU-Z\cpuz.exe"], "CPUID CPU-Z"),
        new("gpuz", "Información", "GPU-Z", "Modelo de la gráfica, BIOS de la tarjeta, sensores y si Resizable BAR está activo según el driver.", "TechPowerUp", "TechPowerUp.GPU-Z", "https://www.techpowerup.com/gpuz/",
            [@"%ProgramFiles(x86)%\GPU-Z\GPU-Z.exe", @"%ProgramFiles%\GPU-Z\GPU-Z.exe"], "GPU-Z"),
        new("hwinfo", "Información", "HWiNFO", "Todos los sensores del equipo, con registro a archivo para ver qué pasó durante una partida.", "REALiX", "REALiX.HWiNFO", "https://www.hwinfo.com/",
            [@"%ProgramFiles%\HWiNFO64\HWiNFO64.exe"], "HWiNFO"),
        new("procexp", "Información", "Process Explorer", "Qué programa tiene abierto un archivo, qué carga cada proceso y quién usa la GPU.", "Microsoft Sysinternals", "Microsoft.Sysinternals.ProcessExplorer", "https://learn.microsoft.com/sysinternals/downloads/process-explorer",
            [@"%LOCALAPPDATA%\Microsoft\WinGet\Links\procexp64.exe", @"%LOCALAPPDATA%\Microsoft\WinGet\Links\procexp.exe"], "Process Explorer"),
        new("afterburner", "GPU", "MSI Afterburner", "Curva de ventiladores de la gráfica, límite de energía y FPS en pantalla. Sirve con cualquier marca de gráfica.", "MSI (paquete descargado del espejo de Guru3D)", "Guru3D.Afterburner", "https://www.msi.com/Landing/afterburner/graphics-cards",
            [@"%ProgramFiles(x86)%\MSI Afterburner\MSIAfterburner.exe"], "MSI Afterburner",
            "No uses a la vez la curva de ventiladores de Afterburner y la de FanControl: se pelean por la gráfica."),
        new("ddu", "GPU", "Display Driver Uninstaller", "Borra por completo el driver de la gráfica (NVIDIA, AMD o Intel) cuando uno nuevo da problemas.", "Wagnardsoft", "Wagnardsoft.DisplayDriverUninstaller", "https://www.wagnardsoft.com/",
            [@"%ProgramFiles(x86)%\Display Driver Uninstaller\Display Driver Uninstaller.exe", @"%ProgramFiles%\Display Driver Uninstaller\Display Driver Uninstaller.exe"], "Display Driver Uninstaller",
            "Te deja sin driver de video hasta que instales otro. Úsalo en modo seguro, con internet desconectado y con el driver nuevo ya descargado."),
        new("inspector", "GPU", "NVIDIA Profile Inspector", "Ajustes del driver de NVIDIA por juego que el panel no muestra.", "Orbmu2k (código abierto, MIT)", "Orbmu2k.nvidiaProfileInspector", "https://github.com/Orbmu2k/nvidiaProfileInspector",
            [@"%LOCALAPPDATA%\Microsoft\WinGet\Links\nvidiaProfileInspector.exe"], "NVIDIA Profile Inspector",
            "Cambia ajustes internos del driver. Antes de tocar nada, exporta tu perfil actual para poder volver.", Needs: "nvidia"),
        new("fancontrol", "Ventiladores", "FanControl", "Curvas de ventiladores de CPU, GPU y caja según la temperatura que elijas. Gratis.", "Rémi Mercier", "Rem0o.FanControl", "https://getfancontrol.com/",
            [@"%ProgramFiles(x86)%\FanControl\FanControl.exe", @"%ProgramFiles%\FanControl\FanControl.exe", @"%LOCALAPPDATA%\Programs\FanControl\FanControl.exe"], "FanControl",
            "No uses a la vez la curva de ventiladores de MSI Afterburner: los dos se pelean por la gráfica."),
        new("autoruns", "Inicio de Windows", "Autoruns", "Todo lo que arranca con Windows: programas, servicios, tareas y drivers.", "Microsoft Sysinternals", "Microsoft.Sysinternals.Autoruns", "https://learn.microsoft.com/sysinternals/downloads/autoruns",
            [@"%LOCALAPPDATA%\Microsoft\WinGet\Links\autoruns64.exe", @"%LOCALAPPDATA%\Microsoft\WinGet\Links\autoruns.exe"], "Autoruns",
            "Desmarcar lo que no conoces puede dejar sin arrancar un driver o el antivirus. Vigía ya cubre lo habitual en Inicio."),
    ];

    static readonly Def[] Catalog =
    [
        // ---- de Windows ----
        new("taskmgr", "Windows", "Administrador de tareas", "Procesos, rendimiento y apps de inicio.", "taskmgr.exe", Win),
        new("resmon", "Windows", "Monitor de recursos", "Qué programa usa el disco, la red o la memoria ahora mismo.", "resmon.exe", Win),
        new("devmgmt", "Windows", "Administrador de dispositivos", "Drivers y dispositivos con problemas.", "devmgmt.msc", Win),
        new("services", "Windows", "Servicios", "Todos los servicios de Windows y cómo arrancan.", "services.msc", Win),
        new("eventvwr", "Windows", "Visor de eventos", "El registro completo que resume Diagnóstico.", "eventvwr.msc", Win),
        new("msinfo", "Windows", "Información del sistema", "BIOS, placa, drivers y recursos.", "msinfo32.exe", Win),
        new("dxdiag", "Windows", "Diagnóstico de DirectX", "Versión de DirectX y del driver de video.", "dxdiag.exe", Win),
        new("msconfig", "Windows", "Configuración del sistema", "Arranque, modo seguro y servicios de inicio.", "msconfig.exe", Win),
        new("regedit", "Windows", "Editor del Registro", "Para revisar a mano lo que cambia Ajustes.", "regedit.exe", Win),
        new("appwiz", "Windows", "Programas y características", "Desinstalar programas a la manera clásica.", "appwiz.cpl", Win),
        new("perf", "Windows", "Opciones de rendimiento", "Efectos visuales y memoria virtual.", "SystemPropertiesPerformance.exe", Win),
        new("protect", "Windows", "Protección del sistema", "Puntos de restauración y su espacio en disco.", "SystemPropertiesProtection.exe", Win),
        new("power", "Configuración rápida", "Plan de energía", "Elegir o cambiar el plan de energía.", "powercfg.cpl", Win),
        new("hags", "Configuración rápida", "Gráficos: programación acelerada por GPU", "Activar HAGS y elegir la GPU de cada programa.", "ms-settings:display-advancedgraphics", Win),
        new("gamemode", "Configuración rápida", "Modo de juego", "Windows da prioridad al juego abierto.", "ms-settings:gaming-gamemode", Win),
        new("startupapps", "Configuración rápida", "Apps de inicio", "Encender o apagar lo que arranca con tu sesión.", "ms-settings:startupapps", Win),
        new("appsfeatures", "Configuración rápida", "Aplicaciones instaladas", "Lista de apps y programas para desinstalar.", "ms-settings:appsfeatures", Win),
        new("mouse", "Configuración rápida", "Ratón", "Velocidad del puntero y botones.", "main.cpl", Win),
        new("sound", "Configuración rápida", "Sonido", "Dispositivos de audio y frecuencia de muestreo.", "mmsys.cpl", Win),
        new("network", "Configuración rápida", "Conexiones de red", "Adaptadores de red y su configuración.", "ncpa.cpl", Win),
        new("wu", "Configuración rápida", "Windows Update", "Buscar, pausar o ver el historial de actualizaciones.", "ms-settings:windowsupdate", Win),
        new("coreiso", "Configuración rápida", "Seguridad de Windows", "Antivirus y aislamiento del núcleo.", "windowsdefender:", Win,
            "Algunas guías mandan a apagar la integridad de memoria para ganar FPS: quita una protección contra drivers maliciosos. Vigía no lo recomienda."),
        new("temp", "Configuración rápida", "Carpeta de temporales", "Tu carpeta %TEMP%. Vigía la limpia en Almacenamiento.", "%TEMP%", Win),

        // ---- reparar ----
        new("restorepoint", "Reparar Windows", "Crear punto de restauración", "Lo crea Vigía ahora, con la fecha de hoy.", "", Act),
        new("sfc", "Reparar Windows", "Comprobar archivos de Windows (SFC)", "Busca y repara archivos del sistema dañados. Tarda unos 10 minutos.", "sfc /scannow", Cmd, "Reemplaza archivos de Windows dañados por copias buenas. No toca tus archivos."),
        new("dism", "Reparar Windows", "Reparar la imagen de Windows (DISM)", "Descarga de Windows Update las piezas dañadas. Úsalo si SFC no pudo arreglar algo.", "DISM /Online /Cleanup-Image /RestoreHealth", Cmd, "Necesita internet y puede tardar 20 minutos o más. No toca tus archivos."),
        new("chkdsk", "Reparar Windows", "Revisar el disco C:", "Busca errores del sistema de archivos sin bloquear el disco.", "chkdsk C: /scan", Cmd, "Solo lee y avisa: no repara ni bloquea el disco. Para reparar hace falta reiniciar y lo decides tú."),
        new("dns", "Reparar Windows", "Vaciar la caché DNS", "Cuando una web no carga o un juego no conecta a su servidor.", "ipconfig /flushdns", Cmd, "Solo vacía la caché de nombres; se vuelve a llenar sola."),
        new("netreset", "Reparar Windows", "Restablecer la red (Winsock e IP)", "Para cuando internet falla y nada más lo arregla.", "netsh winsock reset & netsh int ip reset", Cmd,
            "Pide reiniciar y borra la IP fija y las DNS que hayas puesto a mano."),
        new("wsreset", "Reparar Windows", "Reiniciar Microsoft Store", "Vacía la caché de la Store cuando no descarga.", "wsreset.exe", Win),
        new("mdsched", "Reparar Windows", "Diagnóstico de memoria", "Prueba la RAM al reiniciar. Útil si hay pantallazos azules.", "mdsched.exe", Win,
            "Te pregunta si reiniciar ahora; la prueba tarda unos 15 minutos."),
    ];

    public static List<ToolItem> List()
    {
        var items = Catalog.Select(d => new ToolItem(d.Id, d.Group, d.Name, d.Detail, Environment.ExpandEnvironmentVariables(d.Target), true, d.Warning, d.Kind,
            Confirm: d.Kind == Cmd)).ToList();
        var nvidia = HasNvidia();
        foreach (var a in Apps)
        {
            if (a.Needs == "nvidia" && !nvidia) continue;
            var exe = AppPath(a);
            var installed = exe is not null || InUninstallList(a.Uninstall);
            items.Add(new ToolItem(a.Id, a.Group, a.Name, a.Detail, exe ?? "", installed, a.Warning, App, a.Publisher, a.Winget, a.Url, Confirm: true));
        }
        return items;
    }

    static string? AppPath(AppDef a) => a.Paths.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);

    /// <summary>¿Aparece en «Aplicaciones instaladas»? Se compara el inicio del nombre del producto (no cambia con el idioma).</summary>
    static bool InUninstallList(string name)
    {
        foreach (var (hive, path) in new[] { (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                     (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall") })
        {
            try
            {
                using var root = hive.OpenSubKey(path);
                if (root is null) continue;
                foreach (var sub in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is string dn && dn.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
        }
        return false;
    }

    /// <summary>¿Hay una gráfica NVIDIA? Se mira el proveedor del driver en la clase de adaptadores de pantalla.</summary>
    internal static bool HasNvidia()
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return false;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4) continue;
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if ((k?.GetValue("ProviderName") as string)?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) == true) return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    public static ToolDone Open(string id)
    {
        try
        {
            var a = Apps.FirstOrDefault(x => x.Id == id);
            if (a is not null)
            {
                var exe = AppPath(a);
                if (exe is not null) { Unelevated.Start(exe, null, Path.GetDirectoryName(exe)); return new(id, true, null); }
                if (InUninstallList(a.Uninstall)) return new(id, false, $"{a.Name} está instalado pero no sé dónde. Ábrelo desde el menú Inicio.");
                return new(id, false, $"{a.Name} no está instalado.");
            }
            var d = Catalog.FirstOrDefault(x => x.Id == id);
            if (d is null) return new(id, false, "Herramienta desconocida.");
            switch (d.Kind)
            {
                case Win:
                    Start(Environment.ExpandEnvironmentVariables(d.Target), null, null);
                    return new(id, true, null);
                case Cmd:
                    // ventana propia que queda abierta al terminar, para leer el resultado.
                    // Son órdenes de Windows que necesitan administrador (sfc, DISM, chkdsk): por eso sí heredan el permiso.
                    // el eco lleva los & escapados: si no, cmd ejecutaría la orden dos veces
                    Start("cmd.exe", $"/k title {d.Name} & echo {d.Target.Replace("&", "^&")} & echo. & {d.Target}", null);
                    return new(id, true, $"{d.Name}: se abrió en otra ventana.");
                case Act when id == "restorepoint":
                    var r = RestorePoint.Create("Vigía: punto manual");
                    return new(id, r.Ok, r.Message);
            }
            return new(id, false, "No sé abrir esto.");
        }
        catch (Exception e) { return new(id, false, e.Message); }
    }

    /// <summary>Instala con WinGet un programa del catálogo, en una ventana propia y sin permisos de Vigía.
    /// WinGet comprueba el hash del instalador que publica el manifiesto; si el instalador pide administrador, Windows lo pregunta.</summary>
    public static ToolDone Install(string id)
    {
        var a = Apps.FirstOrDefault(x => x.Id == id);
        if (a is null) return new(id, false, "Ese programa no está en el catálogo.");
        if (AppPath(a) is not null || InUninstallList(a.Uninstall)) return new(id, false, $"{a.Name} ya está instalado.");
        var winget = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe");
        if (!File.Exists(winget)) return new(id, false, "Falta WinGet («Instalador de aplicación» de Microsoft Store). Instálalo o descarga el programa desde su web oficial.");
        try
        {
            // el id sale del catálogo, nunca de la interfaz
            Unelevated.Start(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\cmd.exe"),
                $"/k title {a.Name} & \"{winget}\" install -e --id {a.Winget} --source winget", null, newConsole: true);
            return new(id, true, $"{a.Name} se está instalando en otra ventana. Cuando termine, vuelve a Herramientas.");
        }
        catch (Exception e) { return new(id, false, e.Message); }
    }

    static void Start(string file, string? args, string? dir)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = true };
        if (args is not null) psi.Arguments = args;
        if (dir is not null) psi.WorkingDirectory = dir;
        Process.Start(psi);
    }

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        var all = List();
        Check(all.All(t => t.Kind is Win or Cmd or Act or App), "ninguna herramienta apunta a una carpeta personal");
        Check(Apps.All(a => a.Winget.Contains('.') && a.Url.StartsWith("https://")), "todas las apps tienen id de WinGet y web https");
        Check(all.Select(t => t.Id).Distinct().Count() == all.Count, "ids sin repetir");
        Check(all.Where(t => t.Kind == Cmd).All(t => t.Confirm && t.Warning is not null), "las reparaciones de consola piden confirmación y explican qué hacen");
        Check(!Install("../../evil").Ok, "instalar un id fuera del catálogo se rechaza");
        Check(!Open("x:..\\evil.exe").Ok, "abrir un id desconocido se rechaza");
        return string.Join(Environment.NewLine, log);
    }
}

/// <summary>
/// Abre un programa con el permiso normal del usuario aunque Vigía corra como administrador: duplica el token del
/// Explorador (el proceso del escritorio) y lanza con CreateProcessWithTokenW. Si no hay Explorador o falla, NO se lanza
/// elevado como reserva: se devuelve el error.
/// </summary>
public static class Unelevated
{
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr tok);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool DuplicateTokenEx(IntPtr tok, uint access, IntPtr attrs, int level, int type, out IntPtr dup);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string? lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessWithTokenW(IntPtr tok, int logon, string? app, System.Text.StringBuilder cmd, uint flags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_DUPLICATE = 0x2, TOKEN_ALL = 0xF01FF;
    const uint CREATE_NEW_CONSOLE = 0x10, CREATE_NO_WINDOW = 0x08000000, CREATE_UNICODE_ENVIRONMENT = 0x400;

    /// <summary>Lanza y devuelve el PID. Con <paramref name="waitMs"/> &gt; 0 espera a que termine (solo para pruebas).</summary>
    public static int Start(string exe, string? args, string? dir, bool newConsole = false, bool hidden = false, uint waitMs = 0)
    {
        Privileges.Enable("SeImpersonatePrivilege");
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero) throw new InvalidOperationException("No encuentro el escritorio de Windows para abrirlo sin permisos de administrador.");
        GetWindowThreadProcessId(shell, out var pid);
        var hp = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hp == IntPtr.Zero) throw new Win32Exception();
        IntPtr tok = IntPtr.Zero, dup = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(hp, TOKEN_DUPLICATE, out tok)) throw new Win32Exception();
            if (!DuplicateTokenEx(tok, TOKEN_ALL, IntPtr.Zero, 2 /* Impersonation */, 1 /* Primary */, out dup)) throw new Win32Exception();
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            var cmd = new System.Text.StringBuilder($"\"{exe}\"" + (string.IsNullOrEmpty(args) ? "" : " " + args));
            var flags = CREATE_UNICODE_ENVIRONMENT | (hidden ? CREATE_NO_WINDOW : newConsole ? CREATE_NEW_CONSOLE : 0);
            if (!CreateProcessWithTokenW(dup, 0, exe, cmd, flags, IntPtr.Zero, dir, ref si, out var pi)) throw new Win32Exception();
            if (waitMs > 0) WaitForSingleObject(pi.hProcess, waitMs);
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }
        finally
        {
            if (dup != IntPtr.Zero) CloseHandle(dup);
            if (tok != IntPtr.Zero) CloseHandle(tok);
            CloseHandle(hp);
        }
    }
}
