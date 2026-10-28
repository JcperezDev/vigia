using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Vigia;

/// <summary>
/// Microsoft Defender: ver si protege y volver a encenderlo cuando alguna herramienta lo apagó.
/// Vigía no trae antivirus propio ni apaga Defender: solo quita los bloqueos que otros dejaron
/// (políticas, servicios deshabilitados, SmartScreen bloqueado) y usa las herramientas de Windows.
/// Todo pasa por una copia antes de tocar nada.
/// </summary>
public static class Security
{
    public record Block(string Key, string Label);
    public record SecView(bool Running, bool RealTime, int? SigAgeDays, string? SigVersion, bool CanScan,
                          bool NeedsReboot, IReadOnlyList<Block> Blocks, IReadOnlyList<string> Others);
    public record Done(bool Ok, string Message, bool Reboot);

    const string Pol = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender";
    const string Svc = @"HKLM\SYSTEM\CurrentControlSet\Services\";
    const string Ifeo = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";

    // Valores de política que apagan Defender. Ninguno existe en un Windows recién instalado.
    static readonly (string Sub, string Name)[] Policies =
    {
        ("", "DisableAntiSpyware"), ("", "DisableAntiVirus"), ("", "PassiveMode"), ("", "ServiceKeepAlive"),
        ("", "IsServiceRunning"), ("", "PreviousRunningMode"), ("", "DisableRoutinelyTakingAction"),
        (@"\Real-Time Protection", "DisableRealtimeMonitoring"), (@"\Real-Time Protection", "DisableBehaviorMonitoring"),
        (@"\Real-Time Protection", "DisableOnAccessProtection"), (@"\Real-Time Protection", "DisableScanOnRealtimeEnable"),
        (@"\Real-Time Protection", "DisableIOAVProtection"),
    };

    // Arranque de fábrica de Windows 11 (leído en una instalación limpia). 0 arranque, 2 automático, 3 manual.
    static readonly (string Name, int Start)[] Services =
    {
        ("WinDefend", 2), ("WdFilter", 0), ("WdBoot", 0), ("WdNisSvc", 3), ("WdNisDrv", 3), ("MDCoreSvc", 2),
        ("SecurityHealthService", 3), ("wscsvc", 2), ("Sense", 3), ("webthreatdefsvc", 3), ("webthreatdefusersvc", 2),
    };

    static string DefenderDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender");
    static string? MpCmdRun()
    {
        // la plataforma actualizada vive en ProgramData; la de fábrica en Archivos de programa
        var plat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
        try
        {
            var p = Directory.Exists(plat) ? Directory.GetDirectories(plat).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                        .Select(d => Path.Combine(d, "MpCmdRun.exe")).FirstOrDefault(File.Exists) : null;
            if (p is not null) return p;
        }
        catch { }
        var f = Path.Combine(DefenderDir, "MpCmdRun.exe");
        return File.Exists(f) ? f : null;
    }

    static object? Get(string path, string name)
    {
        var (root, sub) = Split(path);
        using (root) using (var k = root.OpenSubKey(sub)) return k?.GetValue(name);
    }

    const string DefKey = @"HKLM\SOFTWARE\Microsoft\Windows Defender";
    static readonly string[] Own = { "DisableAntiSpyware", "DisableAntiVirus" };

    // Claves donde los administradores solo leen (la configuración propia de Defender, a veces sus servicios).
    // Se activa SeRestorePrivilege, el que usan los programas de copias, y se abre la clave en modo restauración.
    // No crea tareas ni procesos.
    static void WithRestoreKey(string path, Action<IntPtr> act)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x20 | 0x8, out var tok)) throw new InvalidOperationException("sin token");
        try
        {
            if (!LookupPrivilegeValue(null, "SeRestorePrivilege", out var luid)) throw new InvalidOperationException("sin privilegio");
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attr = 2 };
            if (!AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
                throw new InvalidOperationException("Windows no dio el permiso de restaurar");
            var sub = path[(path.IndexOf('\\') + 1)..];
            var rc = RegCreateKeyEx(new IntPtr(unchecked((int)0x80000002)), sub, 0, null, 4 /*BACKUP_RESTORE*/, 0x0002 | 0x0100, IntPtr.Zero, out var hk, out _);
            if (rc != 0) throw new InvalidOperationException($"código {rc}");
            try { act(hk); } finally { RegCloseKey(hk); }
        }
        finally { CloseHandle(tok); }
    }

    static void DeleteProtected(string path, string name) => WithRestoreKey(path, hk =>
    {
        var d = RegDeleteValue(hk, name); if (d != 0 && d != 2) throw new InvalidOperationException($"código {d}");
    });

    // Escribe normal; si Windows lo niega, con el permiso de restaurar.
    static void SetAny(string path, string name, int v)
    {
        try { Set(path, name, v); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            WithRestoreKey(path, hk =>
            {
                var r = RegSetValueEx(hk, name, 0, 4 /*DWORD*/, ref v, 4); if (r != 0) throw new InvalidOperationException($"código {r}");
            });
        }
    }

    // LUID va a 4 bytes, como en Windows (sin Pack = 4 quedaba en 8 y Windows no daba el permiso)
    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TOKEN_PRIVILEGES { public uint Count; public long Luid; public uint Attr; }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr tok);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string? sys, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr tok, bool all, ref TOKEN_PRIVILEGES tp, uint len, IntPtr prev, IntPtr ret);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegCreateKeyEx(IntPtr hKey, string sub, int res, string? cls, int opt, int sam, IntPtr sa, out IntPtr result, out int disp);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegDeleteValue(IntPtr hKey, string name);
    [DllImport("advapi32.dll")] static extern int RegCloseKey(IntPtr hKey);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegSetValueEx(IntPtr hKey, string name, int res, int type, ref int data, int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static (RegistryKey, string) Split(string path)
    {
        var i = path.IndexOf('\\');
        var root = path[..i] == "HKCU" ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
                                         : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return (root, path[(i + 1)..]);
    }

    static int? Start(string svc) => Get(Svc + svc, "Start") is int v ? v : null;

    /// <summary>Lo que impide que Defender proteja, en palabras.</summary>
    static List<Block> Blocks()
    {
        var b = new List<Block>();
        var pol = Policies.Where(p => Get(Pol + p.Sub, p.Name) is int v && (p.Name == "ServiceKeepAlive" ? v == 0 : v != 0)).ToList();
        if (pol.Count > 0) b.Add(new("policies", $"Hay {pol.Count} reglas del registro que le ordenan a Defender no arrancar."));
        if (Own.Any(n => Get(DefKey, n) is int d && d != 0))
            b.Add(new("local", "Defender tiene marcado «no arrancar» en su propia configuración."));
        var off = Services.Where(s => Start(s.Name) == 4).Select(s => s.Name).ToList();
        if (off.Count > 0) b.Add(new("services", $"{off.Count} servicios de seguridad están deshabilitados ({string.Join(", ", off.Take(4))}{(off.Count > 4 ? "…" : "")})."));
        if (Get(Ifeo + "smartscreen.exe", "Debugger") is string) b.Add(new("smartscreen", "SmartScreen está bloqueado: Windows lo cierra en cuanto arranca."));
        if (Get(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen") is 0)
            b.Add(new("smartscreen-pol", "Una regla apaga SmartScreen, el filtro que avisa antes de abrir descargas peligrosas."));
        if (!File.Exists(Path.Combine(DefenderDir, "MsMpEng.exe")) || MpCmdRun() is null)
            b.Add(new("files", "Faltan archivos de Defender. Windows los puede reponer con su propia reparación (sfc)."));
        return b;
    }

    public static SecView Status()
    {
        bool running = false, rt = false; int? age = null; string? ver = null;
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(@"root\Microsoft\Windows\Defender",
                "SELECT AMServiceEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge, AntivirusSignatureVersion FROM MSFT_MpComputerStatus");
            foreach (var o in s.Get())
            {
                running = o["AMServiceEnabled"] is true; rt = o["RealTimeProtectionEnabled"] is true;
                if (running && o["AntivirusSignatureAge"] is { } a) { var n = Convert.ToInt32(a); age = n > 60000 ? null : n; }
                ver = o["AntivirusSignatureVersion"] as string;
            }
        }
        catch { }
        var others = new List<string>();
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct");
            foreach (var o in s.Get()) if (o["displayName"] is string n && !n.Contains("Defender", StringComparison.OrdinalIgnoreCase)) others.Add(n);
        }
        catch { }
        var blocks = Blocks();
        // ya se quitaron los bloqueos pero el servicio sigue parado: falta reiniciar
        var reboot = !running && blocks.Count == 0;
        return new(running, rt, age, string.IsNullOrEmpty(ver) || ver == "0.0.0.0" ? null : ver, running && MpCmdRun() is not null, reboot, blocks, others);
    }

    /// <summary>Quita los bloqueos con copia previa. Si faltan archivos, los repone con sfc (tarda). Pide reiniciar.</summary>
    public static Done Enable(Action<string>? progress = null)
    {
        using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
            if (!new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                return new(false, "Hace falta abrir Vigía como administrador.", false);
        var items = new List<JsonObject>();
        foreach (var p in Policies) items.Add(Backups.Item.Reg(Pol + p.Sub, p.Name, $"Regla de Defender {p.Name}"));
        foreach (var n in Own) items.Add(Backups.Item.Reg(DefKey, n, "Defender: no arrancar"));
        foreach (var s in Services) items.Add(Backups.Item.Reg(Svc + s.Name, "Start", $"Arranque del servicio {s.Name}"));
        items.Add(Backups.Item.Reg(Svc + "wscsvc", "DelayedAutostart", "Arranque diferido del Centro de seguridad"));
        items.Add(Backups.Item.Reg(Ifeo + "smartscreen.exe", "Debugger", "Bloqueo de SmartScreen"));
        items.Add(Backups.Item.Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", "Regla de SmartScreen"));
        items.Add(Backups.Item.Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "SecurityHealth", "Icono de Seguridad de Windows"));
        progress?.Invoke("Guardando copia…");
        if (Backups.Save("defender", "Activar Microsoft Defender", items) is null)
            return new(false, "No se pudo guardar la copia, así que no se cambió nada.", false);

        var failed = new List<string>();
        void Try(string what, Action a) { try { a(); } catch (Exception e) { failed.Add($"{what}: {e.Message}"); } }

        progress?.Invoke("Quitando las reglas que lo apagan…");
        foreach (var p in Policies) Try(p.Name, () => Delete(Pol + p.Sub, p.Name));
        // la configuración propia de Defender solo la puede escribir el sistema: se usa el permiso de restaurar copias
        foreach (var n in Own) Try(n, () => { if (Get(DefKey, n) is int v && v != 0) DeleteProtected(DefKey, n); });
        Try("SmartScreen", () => Delete(Ifeo + "smartscreen.exe", "Debugger"));
        Try("SmartScreen", () => { if (Get(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen") is 0) Delete(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen"); });

        progress?.Invoke("Dejando los servicios como vienen de fábrica…");
        foreach (var s in Services)
            if (Start(s.Name) == 4) Try(s.Name, () =>
            {
                SetAny(Svc + s.Name, "Start", s.Start);
                // de fábrica el Centro de seguridad arranca un poco después que el resto
                if (s.Name == "wscsvc") SetAny(Svc + "wscsvc", "DelayedAutostart", 1);
            });
        Try("icono", () =>
        {
            var tray = Path.Combine(Environment.SystemDirectory, "SecurityHealthSystray.exe");
            if (File.Exists(tray) && Get(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "SecurityHealth") is null)
            {
                var (root, sub) = Split(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                using (root) using (var k = root.CreateSubKey(sub, true))
                    k.SetValue("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe", RegistryValueKind.ExpandString);
            }
        });

        var repaired = "";
        if (!File.Exists(Path.Combine(DefenderDir, "MsMpEng.exe")) || MpCmdRun() is null)
        {
            progress?.Invoke("Reponiendo archivos de Windows con sfc (10–20 min)…");
            var code = Run(Path.Combine(Environment.SystemDirectory, "sfc.exe"), "/scannow", TimeSpan.FromMinutes(45));
            repaired = MpCmdRun() is not null ? " Windows repuso los archivos que faltaban."
                     : code == 0 ? " sfc terminó pero aún faltan archivos de Defender; tras reiniciar, Windows Update los suele traer."
                     : $" sfc no pudo terminar (código {code}).";
        }

        if (failed.Count > 0)
            return new(false, $"Se quitó casi todo, pero {failed.Count} cosas no se pudieron cambiar: {string.Join("; ", failed.Take(3))}.{repaired}", true);
        return new(true, $"Listo. Reinicia el PC para que Defender arranque.{repaired} Si algo va mal, en Copias está «Activar Microsoft Defender» para volver atrás.", true);
    }

    /// <summary>Firmas nuevas y análisis rápido con las herramientas de Defender. Ventana oculta; termina sola.</summary>
    public static Done UpdateAndScan(Action<string>? progress = null)
    {
        if (MpCmdRun() is not { } mp) return new(false, "Defender no está funcionando todavía.", false);
        progress?.Invoke("Bajando firmas nuevas…");
        Run(mp, "-SignatureUpdate", TimeSpan.FromMinutes(10));
        // el código de salida no basta: se mira la fecha real de las firmas
        var fresh = Status().SigAgeDays is <= 1;
        progress?.Invoke("Analizando lo más común (unos minutos)…");
        var scan = Run(mp, "-Scan -ScanType 1", TimeSpan.FromMinutes(40));
        var sig = fresh ? "Firmas al día." : "No se pudieron bajar firmas nuevas (¿sin internet?).";
        // MpCmdRun -Scan devuelve 0 limpio y 2 cuando encuentra algo; lo demás es que no pudo analizar
        return scan switch
        {
            0 => new(fresh, $"{sig} Análisis rápido limpio.", false),
            2 => new(false, $"{sig} Encontró amenazas: ábrelas en Seguridad de Windows → Historial de protección.", false),
            _ => new(false, $"{sig} El análisis no terminó (código {scan}).", false),
        };
    }

    static int Run(string exe, string args, TimeSpan max)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
              RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.OutputDataReceived += (_, _) => { }; p.ErrorDataReceived += (_, _) => { };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            if (!p.WaitForExit(max)) { try { p.Kill(true); } catch { } return -1; }
            return p.ExitCode;
        }
        catch { return -2; }
    }

    static void Delete(string path, string name)
    {
        var (root, sub) = Split(path);
        using (root) using (var k = root.OpenSubKey(sub, true)) k?.DeleteValue(name, false);
    }

    static void Set(string path, string name, int v)
    {
        var (root, sub) = Split(path);
        using (root) using (var k = root.OpenSubKey(sub, true) ?? throw new InvalidOperationException("no existe"))
            k.SetValue(name, v, RegistryValueKind.DWord);
    }
}
