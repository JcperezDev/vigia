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

    // ---------- estado de seguridad de Windows (solo lectura) ----------
    /// <summary>Una comprobación: level ok/warn/info; Managed = lo fija una directiva; Open = página de Windows para cambiarlo.</summary>
    public record Pos(string Key, string Level, string Label, string Detail, bool Managed, string? Open, string[]? Items = null);

    // Páginas de Windows que se pueden abrir desde aquí. Lista cerrada: el frontend solo manda la clave.
    static readonly Dictionary<string, string> Pages = new()
    {
        ["threat"] = "windowsdefender://threatsettings", ["apps"] = "windowsdefender://appbrowser", ["network"] = "windowsdefender://network",
        ["device"] = "windowsdefender://devicesecurity", ["update"] = "ms-settings:windowsupdate", ["bitlocker"] = "ms-settings:deviceencryption",
    };

    /// <summary>Abre la página de Windows correspondiente. Vigía no cambia estos ajustes por su cuenta.</summary>
    public static bool OpenPage(string key)
    {
        try
        {
            if (key == "uac") { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "UserAccountControlSettings.exe")) { UseShellExecute = true })?.Dispose(); return true; }
            if (!Pages.TryGetValue(key, out var uri)) return false;
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); return true;
        }
        catch { return false; }
    }

    static List<System.Management.ManagementBaseObject> Wmi(string ns, string q)
    {
        var r = new List<System.Management.ManagementBaseObject>();
        try { using var s = new System.Management.ManagementObjectSearcher(ns, q); foreach (var o in s.Get()) r.Add(o); } catch { }
        return r;
    }
    static bool KeyExists(string path) { var (root, sub) = Split(path); using (root) using (var k = root.OpenSubKey(sub)) return k is not null; }
    static int? Dw(string path, string name) => Get(path, name) is int v ? v : null;

    /// <summary>Lee cómo está protegido Windows. No cambia nada.</summary>
    public static List<Pos> Posture()
    {
        var r = new List<Pos>();
        var st = Wmi(@"root\Microsoft\Windows\Defender", "SELECT IsTamperProtected, AMServiceEnabled FROM MSFT_MpComputerStatus").FirstOrDefault();
        bool defOn = st?["AMServiceEnabled"] is true;
        if (st?["IsTamperProtected"] is bool tp)
            r.Add(tp ? new("tamper", "ok", "Protección contra alteraciones", "Activada: ningún programa puede cambiar Defender a escondidas.", false, "threat")
                     : new("tamper", defOn ? "warn" : "info", "Protección contra alteraciones", "Apagada: un programa podría cambiar la configuración de Defender sin que te enteres.", false, "threat"));

        // SmartScreen para apps y archivos descargados
        var ssPol = Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen");
        var ss = Get(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled") as string;
        bool ssOff = ssPol is 0 || (ssPol is null && string.Equals(ss, "Off", StringComparison.OrdinalIgnoreCase));
        r.Add(ssOff ? new("smartscreen", "warn", "SmartScreen", "Apagado: Windows no avisa antes de abrir programas descargados que no conoce.", ssPol is not null, "apps")
                    : new("smartscreen", "ok", "SmartScreen", "Activado: avisa antes de abrir programas descargados que no conoce.", ssPol is not null, "apps"));

        // Control de cuentas
        const string sys = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        var lua = Dw(sys, "EnableLUA"); var cpa = Dw(sys, "ConsentPromptBehaviorAdmin");
        r.Add(lua is 0 ? new("uac", "warn", "Control de cuentas (UAC)", "Apagado: cualquier programa corre como administrador sin preguntar. Al encenderlo hay que reiniciar.", false, "uac")
            : cpa is 0 ? new("uac", "warn", "Control de cuentas (UAC)", "Encendido, pero no pregunta: los programas toman permisos de administrador sin avisar.", false, "uac")
            : new("uac", "ok", "Control de cuentas (UAC)", "Activado: Windows pregunta antes de que un programa cambie el equipo.", false, "uac"));

        // Firewall por tipo de red. Si hay otro firewall registrado, él manda.
        var fwOther = Wmi(@"root\SecurityCenter2", "SELECT displayName FROM FirewallProduct").Any();
        bool fwSvcOff = Dw(@"HKLM\SYSTEM\CurrentControlSet\Services\mpssvc", "Start") is 4;
        foreach (var (key, pol, local, label) in new[] { ("fw-domain", "DomainProfile", "DomainProfile", "Firewall: red de empresa (dominio)"),
                     ("fw-private", "PrivateProfile", "StandardProfile", "Firewall: red privada"), ("fw-public", "PublicProfile", "PublicProfile", "Firewall: red pública") })
        {
            var pv = Dw($@"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\{pol}", "EnableFirewall");
            var on = !fwSvcOff && (pv ?? Dw($@"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{local}", "EnableFirewall")) is not 0;
            r.Add(on ? new(key, "ok", label, "Activado.", pv is not null, "network")
                : fwOther ? new(key, "info", label, "El de Windows está apagado; lo cubre otro firewall instalado.", pv is not null, "network")
                : new(key, key == "fw-domain" ? "info" : "warn", label, key == "fw-domain" ? "Apagado. Solo cuenta en redes de empresa." : "Apagado: nada filtra las conexiones que entran por esta red.", pv is not null, "network"));
        }

        // Integridad de memoria (HVCI): 2 = integridad de código protegida por el hipervisor
        var dg = Wmi(@"root\Microsoft\Windows\DeviceGuard", "SELECT SecurityServicesRunning, SecurityServicesConfigured FROM Win32_DeviceGuard").FirstOrDefault();
        bool Has(object? o) => o is Array a && a.Cast<object>().Any(x => Convert.ToInt32(x) == 2);
        bool hvciPol = KeyExists(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard");
        if (dg is not null)
            r.Add(Has(dg["SecurityServicesRunning"]) ? new("hvci", "ok", "Integridad de memoria", "Activada: protege el núcleo de Windows de drivers manipulados.", hvciPol, "device")
                : Has(dg["SecurityServicesConfigured"]) ? new("hvci", "info", "Integridad de memoria", "Configurada pero no está corriendo: falta reiniciar o algún driver lo impide.", hvciPol, "device")
                : new("hvci", "info", "Integridad de memoria", "Apagada. Microsoft la recomienda; algunos drivers viejos no la soportan. Se cambia en Seguridad del dispositivo.", hvciPol, "device"));

        // Windows Update: solo cuenta lo que lo bloquea de verdad (Vigía también escribe aquí reglas de reinicio, que no bloquean nada)
        const string wuPol = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        bool wuSvcOff = Dw(@"HKLM\SYSTEM\CurrentControlSet\Services\wuauserv", "Start") is 4;
        bool wuNoAuto = Dw(wuPol + @"\AU", "NoAutoUpdate") is 1, wuNoAccess = Dw(wuPol, "DisableWindowsUpdateAccess") is 1;
        var pd = Get(@"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseUpdatesExpiryTime") is string pe
                 && DateTime.TryParse(pe, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;
        bool paused = pd > DateTime.UtcNow;
        r.Add(wuSvcOff ? new("wu", "warn", "Windows Update", "Su servicio está deshabilitado: el equipo no recibe parches de seguridad.", false, "update")
            : wuNoAuto || wuNoAccess ? new("wu", "warn", "Windows Update", "Una directiva no deja que Windows se actualice solo.", true, "update")
            // Windows deja pausar como mucho 5 semanas; una pausa más larga la puso otra herramienta y deja el equipo sin parches
            : paused && pd > DateTime.UtcNow.AddDays(36) ? new("wu", "warn", "Windows Update", string.Format(WuLongPause, pd.Year), false, "update")
            : paused ? new("wu", "info", "Windows Update", "Las actualizaciones están en pausa.", false, "update")
            : new("wu", "ok", "Windows Update", "Funciona.", false, "update"));

        // Arranque seguro: sin la clave State el equipo arranca en modo antiguo (Legacy/CSM)
        var sb = Dw(@"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        r.Add(sb is 1 ? new("secureboot", "ok", "Arranque seguro", "Activado.", false, null)
            : new("secureboot", "info", "Arranque seguro", sb is 0 ? "Apagado. Algunos antitrampas y Windows 11 lo piden. Se cambia en la BIOS; Vigía no toca la BIOS."
                                                              : "No disponible: Windows arranca en modo antiguo (Legacy/CSM).", false, null));

        // TPM
        var tpm = Wmi(@"root\cimv2\Security\MicrosoftTpm", "SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm").FirstOrDefault();
        bool tpmOn = tpm?["IsEnabled_InitialValue"] is true && tpm?["IsActivated_InitialValue"] is true;
        var spec = (tpm?["SpecVersion"] as string ?? "").Split(',')[0].Trim();
        r.Add(tpmOn && spec.StartsWith("2.") ? new("tpm", "ok", "TPM", "TPM 2.0 activo.", false, "device")
            : tpmOn ? new("tpm", "info", "TPM", "Hay TPM, pero no es 2.0: Windows 11 pide 2.0.", false, "device")
            : new("tpm", "info", "TPM", "No se encontró o está apagado en la BIOS (fTPM en AMD, PTT en Intel). Windows 11 y algunos antitrampas lo piden.", false, "device"));

        // BitLocker en la unidad de Windows
        var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
        var bl = Wmi(@"root\cimv2\Security\MicrosoftVolumeEncryption", $"SELECT ProtectionStatus, ConversionStatus FROM Win32_EncryptableVolume WHERE DriveLetter='{drive}'").FirstOrDefault();
        bool blPol = KeyExists(@"HKLM\SOFTWARE\Policies\Microsoft\FVE");
        int? prot = bl?["ProtectionStatus"] is { } pv2 ? Convert.ToInt32(pv2) : null, conv = bl?["ConversionStatus"] is { } cv ? Convert.ToInt32(cv) : null;
        r.Add(bl is null ? new("bitlocker", "info", "BitLocker", "No disponible en esta edición de Windows o no se pudo leer.", blPol, "bitlocker")
            : prot is 1 ? new("bitlocker", "ok", "BitLocker", "Activado en la unidad de Windows: si te roban el disco no lo pueden leer. Ten guardada la clave de recuperación.", blPol, "bitlocker")
            : conv is > 0 ? new("bitlocker", "info", "BitLocker", "La unidad está cifrada, pero la protección está en pausa.", blPol, "bitlocker")
            : new("bitlocker", "info", "BitLocker", "Apagado en la unidad de Windows.", blPol, "bitlocker"));

        // Exclusiones de Defender: se enseñan, no se quitan
        var pref = Wmi(@"root\Microsoft\Windows\Defender", "SELECT ExclusionPath, ExclusionExtension, ExclusionProcess FROM MSFT_MpPreference").FirstOrDefault();
        if (pref is not null)
        {
            static IEnumerable<string> Vals(object? o) => o is string[] a ? a.Where(x => !string.IsNullOrWhiteSpace(x)) : [];
            var raw = Vals(pref["ExclusionPath"]).Select(x => ("Carpeta o archivo", x)).Concat(Vals(pref["ExclusionExtension"]).Select(x => ("Extensión", x)))
                .Concat(Vals(pref["ExclusionProcess"]).Select(x => ("Proceso", x))).ToList();
            bool hidden = raw.Any(x => x.Item2.StartsWith("N/A", StringComparison.OrdinalIgnoreCase));
            var items = raw.Where(x => !x.Item2.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)).Select(x => $"{x.Item1}: {x.Item2}").ToArray();
            bool exPol = KeyExists(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions");
            r.Add(hidden ? new("exclusions", "info", "Exclusiones de Defender", "Windows no deja verlas sin permiso de administrador.", exPol, "threat")
                : items.Length == 0 ? new("exclusions", "ok", "Exclusiones de Defender", "Ninguna: Defender revisa todo.", exPol, "threat")
                : new("exclusions", "info", "Exclusiones de Defender", items.Length == 1 ? ExclOne : string.Format(ExclMany, items.Length), exPol, "threat", items));
        }
        return r;
    }
    internal const string WuLongPause = "En pausa hasta {0}: Windows solo deja pausar 5 semanas, así que la alargó otro programa. Mientras siga así no llegan parches de seguridad.";
    internal const string ExclOne = "Defender no revisa 1 cosa. Una exclusión no es mala por sí sola (juegos y programas de desarrollo las piden), pero un virus también puede ponerse una. Mira que la reconozcas; Vigía no la quita.";
    internal const string ExclMany = "Defender no revisa {0} cosas. Una exclusión no es mala por sí sola (juegos y programas de desarrollo las piden), pero un virus también puede ponerse una. Mira que las reconozcas todas; Vigía no las quita.";

    public static string SelfTest()
    {
        var o = new List<string>();
        void Check(bool ok, string what) => o.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        var p = Posture();
        foreach (var k in new[] { "smartscreen", "uac", "fw-domain", "fw-private", "fw-public", "wu", "secureboot", "tpm", "bitlocker" })
            Check(p.Any(x => x.Key == k), $"seguridad: se lee {k}");
        Check(p.All(x => x.Level is "ok" or "warn" or "info"), "seguridad: cada lectura tiene un nivel válido");
        Check(p.All(x => x.Open is null || x.Open == "uac" || Pages.ContainsKey(x.Open)), "seguridad: los botones solo abren páginas de Windows de la lista");
        Check(!OpenPage("file:///C:/Windows/System32/cmd.exe") && !OpenPage("ms-settings:../../x"), "seguridad: una clave desconocida no abre nada");
        o.Add("     (en este PC) " + string.Join(" · ", p.Select(x => $"{x.Key}={x.Level}{(x.Managed ? "*" : "")}")));
        return string.Join(Environment.NewLine, o);
    }
}
