namespace Vigia;

/// <summary>
/// «Abrir Vigía al iniciar Windows» (apagado de entrada).
/// Es una entrada en Run de TU usuario (HKCU): Vigía arranca como tú, sin permisos de administrador y sin aviso de UAC.
/// Antes era una tarea programada con «privilegios más altos» que abría Vigía desde una carpeta en la que tu usuario
/// puede escribir: cualquier programa tuyo podía cambiar ese .exe y Windows lo abría como administrador. Esa tarea
/// ya no se crea; <see cref="Repair"/> (con permiso) la quita y deja la entrada nueva si estaba activa.
/// Vigía arranca escondida en la bandeja (en reposo, ~0,1 % de CPU). Una sola instancia (mutex en Program).
/// A los 2 minutos hace un único repaso barato: programas nuevos que arrancan con Windows y ajustes de Vigía que alguien deshizo.
/// Si hay un juego o algo a pantalla completa, espera; nunca abre la ventana, solo deja un aviso en la bandeja.
/// </summary>
public static class AutoStart
{
    const string Folder = @"\Vigia", Name = "Iniciar Vigia";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "Vigia";
    public const string Arg = "--tray";
    static string KnownFile => Path.Combine(Settings.Dir, "startup-known.json");

    /// <summary>Lo que se escribe en Run: siempre el Vigia.exe que está corriendo, entre comillas, y --tray.</summary>
    internal static string Command(string exe) => $"\"{exe}\" {Arg}";

    static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Vigia.exe");

    public static bool Enabled() => RunEntry() is not null || LegacyTask() is { Enabled: true };

    /// <summary>El valor de Run de Vigía, si existe y arranca un Vigia.exe.</summary>
    internal static string? RunEntry()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunValue) is string v && IsOursExe(v) ? v : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Activa o quita el inicio. No necesita administrador. Si queda la tarea antigua elevada, se avisa en
    /// <c>Legacy</c> para que la interfaz ofrezca quitarla (eso sí pide permiso).
    /// </summary>
    public static (bool Ok, string? Message) Set(bool on)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (on)
            {
                k.SetValue(RunValue, Command(Exe), Microsoft.Win32.RegistryValueKind.String);
                if (!File.Exists(KnownFile)) Remember();   // lo que arranca hoy cuenta como conocido
            }
            else if (k.GetValue(RunValue) is string v && IsOursExe(v)) k.DeleteValue(RunValue, false);
            // la tarea antigua: si se puede, fuera ahora mismo (con permisos); si no, queda avisada
            if (LegacyTask() is not null)
            {
                if (Elevation.IsAdmin) Repair();
                else if (!on) return (true, "legacy");
            }
            return (true, null);
        }
        catch (Exception e) { return (false, e.Message); }
    }

    public record LegacyInfo(bool Enabled, string Path, int RunLevel);

    /// <summary>La tarea antigua de Vigía (\Vigia\Iniciar Vigia que abre un Vigia.exe). Ninguna otra.</summary>
    public static LegacyInfo? LegacyTask()
    {
        try
        {
            dynamic svc = Connect();
            dynamic t = svc.GetFolder(Folder).GetTask(Name);
            dynamic def = t.Definition;
            string path = "";
            foreach (dynamic a in def.Actions) { try { path = (string)a.Path; } catch { } break; }
            if (!IsOursExe(path)) return null;   // con ese nombre pero no es nuestra: no se toca
            return new LegacyInfo((bool)t.Enabled, path, (int)def.Principal.RunLevel);
        }
        catch { return null; }
    }

    public record RepairDone(bool Ok, bool Removed, bool Migrated, string? Message);

    /// <summary>
    /// Quita la tarea antigua (solo la de Vigía, comprobada por nombre, carpeta y .exe) y, si estaba activa, deja el
    /// inicio en Run para el usuario. Se hace desde el ayudante con permisos, una vez, y desde Puente al migrar.
    /// La carpeta \Vigia del Programador se borra solo si queda vacía.
    /// </summary>
    public static RepairDone Repair()
    {
        var info = LegacyTask();
        if (info is null) return new(true, false, false, null);
        try
        {
            dynamic svc = Connect();
            dynamic folder = svc.GetFolder(Folder);
            folder.DeleteTask(Name, 0);
            try { if (folder.GetTasks(1).Count == 0 && folder.GetFolders(0).Count == 0) svc.GetFolder(@"\").DeleteFolder(Folder.TrimStart('\\'), 0); } catch { }
            var migrated = false;
            if (info.Enabled)
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                k.SetValue(RunValue, Command(Exe), Microsoft.Win32.RegistryValueKind.String);
                migrated = true;
            }
            UiMessage.Note($"inicio: tarea antigua quitada (nivel {info.RunLevel}){(migrated ? ", inicio pasado a Run" : "")}");
            return new(LegacyTask() is null, true, migrated, null);
        }
        catch (UnauthorizedAccessException) { return new(false, false, false, "needs-admin"); }
        catch (Exception e) { return new(false, false, false, e.Message); }
    }

    static dynamic Connect()
    {
        dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        svc.Connect();
        return svc;
    }

    /// <summary>
    /// ¿Esta entrada de inicio es de Vigía? Se mira el nombre exacto o el .exe que arranca, no el texto completo:
    /// una ruta como C:\Users\vigia\…\OneDrive.exe no es nuestra aunque el usuario se llame así.
    /// </summary>
    public static bool IsOurs(StartItem s) => IsOursName(s.Name) || IsOursExe(s.Command);

    static bool IsOursName(string? n)
    {
        var t = n?.Trim() ?? "";
        return t.Equals("Vigia", StringComparison.OrdinalIgnoreCase) || t.Equals("Vigía", StringComparison.OrdinalIgnoreCase)
            || t.Equals(Name, StringComparison.OrdinalIgnoreCase) || t.Equals(Folder.TrimStart('\\') + "\\" + Name, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsOursExe(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return false;
        var c = cmd.Trim();
        string exe;
        if (c.StartsWith('"')) { var end = c.IndexOf('"', 1); exe = end > 0 ? c[1..end] : c[1..]; }
        else { var i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); exe = i > 0 ? c[..(i + 4)] : c.Split(' ')[0]; }
        var file = System.IO.Path.GetFileName(exe);
        return file.Equals("Vigia.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("Vigía.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Guarda lo que arranca ahora como «conocido» para avisar solo de lo nuevo.</summary>
    public static void Remember(IEnumerable<StartItem>? items = null)
    {
        try
        {
            var ids = (items ?? Startup.List()).Where(s => s.Enabled).Select(s => s.Id).Distinct().OrderBy(x => x).ToList();
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(KnownFile, System.Text.Json.JsonSerializer.Serialize(ids));
        }
        catch { }
    }

    public record Check(List<StartItem> NewItems, List<TweakView> Undone);

    /// <summary>El repaso único tras iniciar sesión. Lee el registro y las tareas; no toca discos ni la BIOS.</summary>
    public static Check Review()
    {
        var now = Startup.List();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { known.UnionWith(System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(KnownFile)) ?? []); } catch { }
        var fresh = known.Count == 0 ? [] : now.Where(s => s.Enabled && !known.Contains(s.Id) && !IsOurs(s)).ToList();
        List<TweakView> undone = [];
        try { undone = Tweaks.Undone(); } catch { }
        Remember(now);   // cada cosa nueva se avisa una sola vez
        return new(fresh, undone);
    }

    // Algo a pantalla completa (juego, vídeo, presentación): no molestar.
    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    public static bool Busy()
    {
        if (SHQueryUserNotificationState(out var s) == 0 && s is 2 /* busy: pantalla completa */ or 3 /* D3D a pantalla completa */ or 4 /* presentación */ or 7 /* app UWP a pantalla completa */)
            return true;
        return Borderless();
    }

    // «Ventana a pantalla completa» (Fortnite, Valorant, CS2…): Windows no la cuenta como pantalla completa,
    // así que se mira si la ventana de delante tapa su monitor entero y no es el escritorio ni Vigía.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFO mi);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder sb, int n);
    static bool Borderless()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || !GetWindowRect(h, out var r)) return false;
        GetWindowThreadProcessId(h, out var pid);
        if (pid == Environment.ProcessId) return false;
        var cls = new System.Text.StringBuilder(64); GetClassNameW(h, cls, 64);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var mi = new MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(MonitorFromWindow(h, 2 /* NEAREST */), ref mi)) return false;
        return r.L <= mi.Monitor.L && r.T <= mi.Monitor.T && r.R >= mi.Monitor.R && r.B >= mi.Monitor.B;
    }

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        var exe = @"C:\Program Files\Vigia\Vigia.exe";
        Check(Command(exe) == "\"C:\\Program Files\\Vigia\\Vigia.exe\" --tray", "inicio: ruta entre comillas y --tray");
        Check(IsOursExe(Command(exe)), "inicio: reconoce su propia entrada");
        Check(!IsOursExe("\"C:\\Tools\\otro.exe\" --tray") && !IsOursExe(""), "inicio: no toma como suya una entrada de otro programa");
        Check(Command(exe).IndexOf("--broker", StringComparison.Ordinal) < 0, "inicio: nunca arranca el ayudante con permisos");
        return string.Join(Environment.NewLine, log);
    }
}
