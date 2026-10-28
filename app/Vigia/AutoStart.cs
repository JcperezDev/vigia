namespace Vigia;

/// <summary>
/// «Abrir Vigía al iniciar Windows» (apagado de entrada).
/// Es una tarea programada oculta al iniciar sesión con privilegios altos: así no sale el aviso de UAC ni ninguna ventana.
/// Vigía arranca escondida en la bandeja (en reposo, ~0,1 % de CPU) para que la liberación automática de memoria funcione.
/// A los 2 minutos hace un único repaso barato: programas nuevos que arrancan con Windows y ajustes de Vigía que alguien deshizo.
/// Si hay un juego o algo a pantalla completa, espera; nunca abre la ventana, solo deja un aviso en la bandeja.
/// </summary>
public static class AutoStart
{
    const string Folder = @"\Vigia", Name = "Iniciar Vigia";
    public const string Arg = "--tray";
    static string KnownFile => Path.Combine(Settings.Dir, "startup-known.json");

    public static bool Enabled()
    {
        try
        {
            dynamic svc = Connect();
            dynamic t = svc.GetFolder(Folder).GetTask(Name);
            return t.Enabled;
        }
        catch { return false; }
    }

    public static (bool Ok, string? Message) Set(bool on)
    {
        try
        {
            dynamic svc = Connect();
            dynamic root = svc.GetFolder(@"\");
            dynamic folder;
            try { folder = svc.GetFolder(Folder); }
            catch { if (!on) return (true, null); folder = root.CreateFolder(Folder); }
            if (!on)
            {
                try { folder.DeleteTask(Name, 0); } catch { }
                return (true, null);
            }
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            dynamic def = svc.NewTask(0);
            def.RegistrationInfo.Description = "Abre Vigía escondida en la bandeja al iniciar sesión. Se quita desde Configuración de Vigía.";
            def.RegistrationInfo.Author = "Vigía";
            def.Principal.UserId = user;
            def.Principal.LogonType = 3;          // TASK_LOGON_INTERACTIVE_TOKEN: corre en tu escritorio, sin contraseña guardada
            def.Principal.RunLevel = 1;           // TASK_RUNLEVEL_HIGHEST: sin aviso de UAC
            def.Settings.Enabled = true;
            def.Settings.Hidden = true;
            def.Settings.DisallowStartIfOnBatteries = false;
            def.Settings.StopIfGoingOnBatteries = false;
            def.Settings.ExecutionTimeLimit = "PT0S";
            def.Settings.MultipleInstances = 2;   // TASK_INSTANCES_IGNORE_NEW
            def.Settings.Priority = 7;            // por debajo de lo normal: no compite con lo que abras al entrar
            dynamic trig = def.Triggers.Create(9); // TASK_TRIGGER_LOGON
            trig.UserId = user;
            trig.Delay = "PT20S";                 // deja que Windows termine de arrancar primero
            dynamic act = def.Actions.Create(0);
            act.Path = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Vigia.exe");
            act.Arguments = Arg;
            act.WorkingDirectory = AppContext.BaseDirectory;
            folder.RegisterTaskDefinition(Name, def, 6 /* crear o actualizar */, null, null, 3, null);
            if (!File.Exists(KnownFile)) Remember();   // lo que arranca hoy cuenta como conocido
            return (true, null);
        }
        catch (Exception e) { return (false, e.Message); }
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

    static bool IsOursExe(string? cmd)
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
        if (SHQueryUserNotificationState(out var s) != 0) return false;
        return s is 2 /* busy: pantalla completa */ or 3 /* D3D a pantalla completa */ or 4 /* presentación */ or 7 /* app UWP a pantalla completa */;
    }
}
