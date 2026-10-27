using System.Diagnostics;

namespace Vigia;

public record ToolItem(string Id, string Group, string Name, string Detail, string Path, bool Found, string? Warning, string Kind);
public record ToolDone(string Id, bool Ok, string? Message);

/// <summary>
/// Herramientas: programas de la carpeta del usuario (por defecto D:\pack externo), herramientas de Windows
/// y reparaciones con las órdenes de Microsoft. Vigía no copia ni modifica los programas: solo los abre.
/// Quedan fuera los que activan Windows, apagan Windows Update o Defender, o instalan drivers sin firma.
/// </summary>
public static class Toolbox
{
    // Kind: "folder" = ruta dentro de la carpeta; "win" = programa, panel o página de Configuración;
    //       "cmd" = orden de consola que se abre en su propia ventana; "action" = la hace Vigía;
    //       "app" = programa instalado (rutas separadas por |); si no está, se instala con winget (id en Winget).
    record Def(string Id, string Group, string Name, string Detail, string Target, string Kind = "folder", string? Warning = null, string? Winget = null);

    const string Folder = "folder", Win = "win", Cmd = "cmd", Act = "action", App = "app";

    static readonly Def[] Catalog =
    [
        // ---- de tu carpeta ----
        new("autoruns", "Inicio de Windows", "Autoruns", "Todo lo que arranca con Windows: programas, servicios, tareas y drivers.", @"5 - Programs\Autoruns.exe"),
        new("geek", "Programas", "Geek Uninstaller", "Desinstala programas y borra las carpetas y claves que dejan atrás.", @"12- Apps\geek.exe"),
        new("cpuz", "Información", "CPU-Z", "Modelo exacto de CPU, placa y RAM con sus tiempos.", @"12- Apps\cpu-z_2.13-en.exe"),
        new("fancontrol", "Ventiladores", "FanControl", "Curvas de ventiladores de CPU, GPU y caja según la temperatura que elijas. Gratis.",
            @"%ProgramFiles(x86)%\FanControl\FanControl.exe|%ProgramFiles%\FanControl\FanControl.exe|%LOCALAPPDATA%\Programs\FanControl\FanControl.exe", App,
            "No uses a la vez la curva de ventiladores de MSI Afterburner: los dos se pelean por la GPU.", "Rem0o.FanControl"),
        new("afterburner", "GPU", "MSI Afterburner (instalador)", "Curva de ventiladores, límite de energía y contador de FPS en pantalla.", @"12- Apps\MSIAfterburnerSetup466Beta3.exe"),
        new("inspector", "GPU", "NVIDIA Profile Inspector", "Ajustes ocultos del driver por juego. Tu perfil «Nvidia Perfil Optimizado.nip» está en la misma carpeta: se carga con «Import profile».", @"7- AMD OR NVIDIA\NVIDIA\Nvidia Inspector\nvidiaProfileInspector.exe"),
        new("nvcleanstall", "GPU", "NVCleanstall", "Instala el driver de NVIDIA sin telemetría ni extras.", @"7- AMD OR NVIDIA\NVIDIA\NVCleanstall_1.17.0.exe"),
        new("ddu", "GPU", "Display Driver Uninstaller", "Borra el driver de video por completo. Úsalo en modo seguro y con internet desconectado.",
            @"7- AMD OR NVIDIA\DDU\DDU v18.0.7.4\Display Driver Uninstaller.exe", Folder, "Te deja sin driver de video hasta que instales otro."),
        new("msiutil", "Latencia", "MSI Utility v3", "Pasa la GPU, la red y el USB al modo de interrupciones MSI, que responde antes que el modo clásico.",
            @"12- Apps\MSI Utility PRO.exe", Folder, "No toques el controlador de disco: si Windows no arranca, hay que entrar en modo seguro y desmarcarlo."),
        new("timerres", "Latencia", "TimerResolution", "Fija la resolución del temporizador de Windows a 0,5 ms.", @"12- Apps\TimerResolution.exe"),
        new("filterkeys", "Latencia", "FilterKeys Setter", "Repetición del teclado más rápida que el mínimo que deja Windows.", @"12- Apps\FilterKeysSetter v3.exe"),
        new("islc", "Memoria", "ISLC", "Limpia la caché en espera. Vigía ya lo hace en Memoria.", @"12- Apps\ISLC v1.0.3.4.exe"),
        new("memreduct", "Memoria", "Mem Reduct (instalador)", "Liberador de RAM con icono en la bandeja. Vigía ya lo hace en Memoria.", @"12- Apps\Mem Reduct Setup.exe"),
        new("lasso", "CPU", "Process Lasso (instalador)", "Prioridad y núcleos por programa, guardados para siempre.", @"12- Apps\processlassosetup64.exe"),
        new("parkcontrol", "CPU", "ParkControl (instalador)", "Aparcamiento de núcleos por plan de energía.", @"12- Apps\parkcontrolsetup64.exe"),
        new("quickcpu", "CPU", "Quick CPU (instalador)", "Aparcamiento de núcleos, frecuencia y límites de potencia con gráficas.", @"12- Apps\Quick-CPU\QuickCpuSetup-4.8.0.0-x64.msi"),
        new("devcleanup", "Dispositivos", "Device Cleanup", "Borra dispositivos que ya no están conectados.", @"12- Apps\DeviceCleanup.exe"),
        new("antimicro", "Dispositivos", "antimicro (instalador)", "Usa el mando como teclado y ratón.", @"12- Apps\antimicro Setup 64.msi"),
        new("macrogamer", "Dispositivos", "MacroGamer (instalador)", "Macros de teclado y ratón.", @"12- Apps\MacroGamer Setup.msi", Folder,
            "En Fortnite y otros juegos con antitrampas, las macros pueden costarte la cuenta."),
        new("tcpopt", "Red", "TCP Optimizer", "Ajustes de TCP con copia previa. Usa el perfil «Optimal».", @"8- Wifi\TCPOptimizer.exe"),
        new("wpd", "Privacidad", "WPD", "Telemetría, permisos de apps y bloqueo de servidores de Microsoft.", @"12- Apps\WPD.exe", Folder,
            "El apartado de bloqueo por firewall puede romper Windows Update y la Store."),
        new("adwcleaner", "Limpieza", "AdwCleaner", "Busca adware y barras de herramientas.", @"10- Uninstall\adwcleaner.exe"),
        new("cleanmgrplus", "Limpieza", "Cleanmgr+", "Liberador de espacio ampliado.", @"6 - Clear Temps Files And Disk\cleanmgr+\Cleanmgr+.exe"),
        new("vcredist", "Requisitos", "Visual C++ (todas las versiones)", "Instala de una vez las librerías de 2005 a 2022 que piden muchos juegos.", @"Microsoft Visual\install_all.bat"),
        new("directx", "Requisitos", "DirectX (runtime de junio 2010)", "Librerías antiguas que piden algunos juegos.", @"Microsoft Visual\DX12\DirectX_11_Setup\DXSETUP.exe"),
        new("dxweb", "Requisitos", "DirectX (instalador web)", "La misma instalación, descargando de Microsoft.", @"Microsoft Visual\DX12\Directx 12.exe"),
        new("runtimepack", "Requisitos", "RuntimePack", "Paquete de librerías comunes de jameszero.net.", @"Microsoft Visual\DX12\RuntimePack_x86_x64.exe"),
        new("crd", "Otros", "Escritorio remoto de Chrome (instalador)", "Para manejar este PC desde el móvil u otro ordenador.", @"chromeremotedesktophost.msi"),

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
            "La guía de Fortnite manda a apagar la integridad de memoria: da algunos FPS, pero quita una protección contra drivers maliciosos."),
        new("temp", "Configuración rápida", "Carpeta de temporales", "Tu carpeta %TEMP%. Vigía la limpia en Almacenamiento.", "%TEMP%", Win),

        // ---- reparar ----
        new("restorepoint", "Reparar Windows", "Crear punto de restauración", "Lo crea Vigía ahora, con la fecha de hoy.", "", Act),
        new("sfc", "Reparar Windows", "Comprobar archivos de Windows (SFC)", "Busca y repara archivos del sistema dañados. Tarda unos 10 minutos.", "sfc /scannow", Cmd),
        new("dism", "Reparar Windows", "Reparar la imagen de Windows (DISM)", "Descarga de Windows Update las piezas dañadas. Úsalo si SFC no pudo arreglar algo.", "DISM /Online /Cleanup-Image /RestoreHealth", Cmd),
        new("chkdsk", "Reparar Windows", "Revisar el disco C:", "Busca errores del sistema de archivos sin bloquear el disco.", "chkdsk C: /scan", Cmd),
        new("dns", "Reparar Windows", "Vaciar la caché DNS", "Cuando una web no carga o un juego no conecta a su servidor.", "ipconfig /flushdns", Cmd),
        new("netreset", "Reparar Windows", "Restablecer la red (Winsock e IP)", "Para cuando internet falla y nada más lo arregla.", "netsh winsock reset & netsh int ip reset", Cmd,
            "Pide reiniciar y borra la IP fija y las DNS que hayas puesto a mano."),
        new("wsreset", "Reparar Windows", "Reiniciar Microsoft Store", "Vacía la caché de la Store cuando no descarga.", "wsreset.exe", Win),
        new("mdsched", "Reparar Windows", "Diagnóstico de memoria", "Prueba la RAM al reiniciar. Útil si hay pantallazos azules.", "mdsched.exe", Win,
            "Te pregunta si reiniciar ahora; la prueba tarda unos 15 minutos."),
    ];

    // De la carpeta, lo que no se ofrece aunque aparezca al buscar programas nuevos.
    static readonly string[] Skip =
    [
        "optimizer-", "wub.exe", "iobit", @"\boost de usb\", "vcredist", @"\directx_11_setup\", @"\cleanmgr+\bin\",
        "ddu v18.0.7.4.exe", @"5 - programs\cpu z.exe", "msiafterburnersetup463", @"5 - programs\quickcpusetup", "punto de restaur",
        "choose a power plan", @"\3 basic windows\", "delete temp% files", "limpieza.lnk",
    ];

    public static List<ToolItem> List(string root)
    {
        var items = Catalog.Select(d =>
        {
            if (d.Kind == App)
            {
                var exe = AppPath(d);
                return new ToolItem(d.Id, d.Group, d.Name, d.Detail, exe ?? "", exe is not null, d.Warning, d.Kind);
            }
            var p = d.Kind == Folder ? Path.Combine(root, d.Target) : Environment.ExpandEnvironmentVariables(d.Target);
            return new ToolItem(d.Id, d.Group, d.Name, d.Detail, p, d.Kind != Folder || File.Exists(p), d.Warning, d.Kind);
        }).ToList();
        items.AddRange(Discover(root));
        return items;
    }

    /// <summary>Programas que el usuario añada a la carpeta después: aparecen en «Otros de tu carpeta».</summary>
    static IEnumerable<ToolItem> Discover(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var known = new HashSet<string>(Catalog.Where(d => d.Kind == Folder).Select(d => Path.GetFullPath(Path.Combine(root, d.Target))), StringComparer.OrdinalIgnoreCase);
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 5 })
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".exe" or ".msi" or ".lnk" or ".url")
                .ToList();
        }
        catch { yield break; }
        foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var low = f.ToLowerInvariant();
            if (known.Contains(Path.GetFullPath(f)) || Skip.Any(s => low.Contains(s))) continue;
            var rel = Path.GetRelativePath(root, f);
            yield return new ToolItem("x:" + rel, "Otros de tu carpeta", Path.GetFileNameWithoutExtension(f), rel, f, true, null, Folder);
        }
    }

    static string? AppPath(Def d) => d.Target.Split('|').Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);

    public static ToolDone Open(string root, string id)
    {
        try
        {
            if (id.StartsWith("x:"))
            {
                // solo se abre si sigue saliendo en la búsqueda (dentro de la carpeta y no excluido)
                var full = Path.GetFullPath(Path.Combine(root, id[2..]));
                if (!Discover(root).Any(t => t.Path.Equals(full, StringComparison.OrdinalIgnoreCase)))
                    return new(id, false, "Ese archivo no está en la lista.");
                Start(full, null, Path.GetDirectoryName(full));
                return new(id, true, null);
            }
            var d = Catalog.FirstOrDefault(x => x.Id == id);
            if (d is null) return new(id, false, "Herramienta desconocida.");
            switch (d.Kind)
            {
                case Folder:
                    var p = Path.Combine(root, d.Target);
                    if (!File.Exists(p)) return new(id, false, $"No encuentro {p}.");
                    Start(p, null, Path.GetDirectoryName(p));
                    return new(id, true, null);
                case Win:
                    Start(Environment.ExpandEnvironmentVariables(d.Target), null, null);
                    return new(id, true, null);
                case Cmd:
                    // ventana propia que queda abierta al terminar, para leer el resultado
                    // el eco lleva los & escapados: si no, cmd ejecutaría la orden dos veces
                    Start("cmd.exe", $"/k title {d.Name} & echo {d.Target.Replace("&", "^&")} & echo. & {d.Target}", null);
                    return new(id, true, $"{d.Name}: se abrió en otra ventana.");
                case App:
                    var exe = AppPath(d);
                    if (exe is not null) { Start(exe, null, Path.GetDirectoryName(exe)); return new(id, true, null); }
                    if (d.Winget is null) return new(id, false, $"{d.Name} no está instalado.");
                    // instalación oficial con winget en su propia ventana, para ver el progreso
                    Start("cmd.exe", $"/k title Instalando {d.Name} & winget install -e --id {d.Winget} --accept-package-agreements --accept-source-agreements", null);
                    return new(id, true, $"{d.Name} se está instalando en otra ventana. Cuando termine, vuelve a Herramientas.");
                case Act when id == "restorepoint":
                    var r = RestorePoint.Create("Vigía: punto manual");
                    return new(id, r.Ok, r.Message);
            }
            return new(id, false, "No sé abrir esto.");
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

    /// <summary>utilidad externa de externo en su propia ventana de PowerShell. Se descarga de internet en el momento:
    /// Vigía no lo incluye ni lo modifica. El usuario elige qué aplicar dentro de utilidad externa.</summary>
    public static string? Openutilidad externa()
    {
        Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -NoExit -Command \"irm https://externo.com/win | iex\"") { UseShellExecute = true });
        return null;
    }
}
