using System.Diagnostics;

namespace Vigia;

public record ToolItem(string Id, string Group, string Name, string Detail, string Path, bool Found, string? Warning);

/// <summary>
/// Atajos a los programas de la carpeta del usuario (por defecto D:\pack externo). Vigía no los copia ni los modifica:
/// solo los abre. Quedan fuera los que activan Windows, apagan Windows Update o Defender, o instalan drivers sin firma.
/// </summary>
public static class Toolbox
{
    record Def(string Id, string Group, string Name, string Detail, string Rel, string? Warning = null);

    static readonly Def[] Catalog =
    [
        new("autoruns", "Inicio de Windows", "Autoruns", "Todo lo que arranca con Windows: programas, servicios, tareas y drivers.", @"5 - Programs\Autoruns.exe"),
        new("cpuz", "Información", "CPU-Z", "Modelo exacto de CPU, placa y RAM con sus tiempos.", @"12- Apps\cpu-z_2.13-en.exe"),
        new("afterburner", "GPU", "MSI Afterburner (instalador)", "Curva de ventiladores, límite de energía y overlay de FPS.", @"12- Apps\MSIAfterburnerSetup466Beta3.exe"),
        new("inspector", "GPU", "NVIDIA Profile Inspector", "Ajustes ocultos del driver por juego. Tu perfil está en la misma carpeta.", @"7- AMD OR NVIDIA\NVIDIA\Nvidia Inspector\nvidiaProfileInspector.exe"),
        new("nvcleanstall", "GPU", "NVCleanstall", "Instala el driver de NVIDIA sin telemetría ni extras.", @"7- AMD OR NVIDIA\NVIDIA\NVCleanstall_1.17.0.exe"),
        new("ddu", "GPU", "Display Driver Uninstaller", "Borra el driver de video por completo. Úsalo en modo seguro y con internet desconectado.",
            @"7- AMD OR NVIDIA\DDU\DDU v18.0.7.4\Display Driver Uninstaller.exe", "Te deja sin driver de video hasta que instales otro."),
        new("islc", "Memoria", "ISLC", "Limpia la caché en espera. Vigía ya lo hace en Memoria.", @"12- Apps\ISLC v1.0.3.4.exe"),
        new("timerres", "Latencia", "TimerResolution", "Fija la resolución del temporizador de Windows a 0,5 ms.", @"12- Apps\TimerResolution.exe"),
        new("lasso", "CPU", "Process Lasso (instalador)", "Prioridad y núcleos por programa, permanente.", @"12- Apps\processlassosetup64.exe"),
        new("parkcontrol", "CPU", "ParkControl (instalador)", "Aparcamiento de núcleos por plan de energía.", @"12- Apps\parkcontrolsetup64.exe"),
        new("devcleanup", "Dispositivos", "Device Cleanup", "Borra dispositivos que ya no están conectados.", @"12- Apps\DeviceCleanup.exe"),
        new("tcpopt", "Red", "TCP Optimizer", "Ajustes de TCP con copia previa. Usa el perfil «Optimal».", @"8- Wifi\TCPOptimizer.exe"),
        new("adwcleaner", "Limpieza", "AdwCleaner", "Busca adware y barras de herramientas.", @"10- Uninstall\adwcleaner.exe"),
        new("cleanmgrplus", "Limpieza", "Cleanmgr+", "Liberador de espacio ampliado.", @"6 - Clear Temps Files And Disk\cleanmgr+\Cleanmgr+.exe"),
        new("vcredist", "Requisitos", "Visual C++ (todas las versiones)", "Instala las librerías que piden muchos juegos.", @"Microsoft Visual\install_all.bat"),
        new("directx", "Requisitos", "DirectX (runtime de junio 2010)", "Librerías antiguas que piden algunos juegos.", @"Microsoft Visual\DX12\DirectX_11_Setup\DXSETUP.exe"),
    ];

    public static List<ToolItem> List(string root) => Catalog.Select(d =>
    {
        var p = Path.Combine(root, d.Rel);
        return new ToolItem(d.Id, d.Group, d.Name, d.Detail, p, File.Exists(p), d.Warning);
    }).ToList();

    public static string? Open(string root, string id)
    {
        var d = Catalog.FirstOrDefault(x => x.Id == id);
        if (d is null) return "Herramienta desconocida.";
        var p = Path.Combine(root, d.Rel);
        if (!File.Exists(p)) return $"No encuentro {p}.";
        Process.Start(new ProcessStartInfo(p) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(p)! });
        return null;
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
