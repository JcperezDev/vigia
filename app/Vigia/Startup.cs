using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Vigia;

public record StartItem(string Id, string Group, string Name, string Publisher, string Command, string Where, bool Enabled, bool Missing, string? Warning);
public record StartDone(string Id, bool Ok, string? Message);

/// <summary>
/// Lo que arranca con Windows y cómo apagarlo sin borrar nada.
/// - Programas (claves Run y carpetas Inicio): el mismo interruptor que usa el Administrador de tareas
///   (Explorer\StartupApproved). La entrada sigue ahí; Windows solo la salta. Encenderla la deja como estaba.
/// - Tareas programadas al iniciar sesión o al arrancar (fuera de \Microsoft): se deshabilitan, no se borran.
/// - Servicios de terceros en automático: pasan a manual (arrancan cuando un programa los pide).
///   Vigía guarda el tipo de inicio que tenían para devolverlo.
/// </summary>
public static class Startup
{
    const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunKey32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    static string SvcBackup => Path.Combine(Settings.Dir, "startup-services.json");

    // Cosas que conviene dejar encendidas aunque se puedan apagar.
    static readonly (string Match, string Why)[] Careful =
    [
        ("securityhealth", "Es el icono de Seguridad de Windows: sin él no ves los avisos del antivirus."),
        ("fancontrol", "Controla tus ventiladores: si no arranca, quedan a la velocidad de la BIOS."),
        ("pawnio", "Lo usan FanControl y los sensores para leer temperaturas."),
        ("nvdisplay", "Es parte del driver de NVIDIA (panel de control y perfiles por juego)."),
        ("nvidia", "Es de NVIDIA: revisa qué hace antes de apagarlo."),
        ("realtek", "Es del driver de audio o de red."),
        ("rtkaud", "Es del driver de audio Realtek."),
        ("amd ", "Es de AMD: puede ser parte del driver del chipset."),
        ("amdryzen", "Es del driver del chipset AMD."),
        ("easyanticheat", "Antitrampas: algunos juegos no abren sin él."),
        ("battleye", "Antitrampas: algunos juegos no abren sin él."),
        ("vgc", "Vanguard (Valorant): sin él Valorant no abre."),
        ("bluetooth", "Bluetooth: tus auriculares o mandos pueden dejar de conectar."),
        ("ctfmon", "Teclado e idioma de Windows."),
        ("onedrive", "Si lo apagas, OneDrive deja de sincronizar hasta que lo abras."),
        ("hermes", "Es Hermes, tu asistente: si lo apagas, no arranca solo con Windows."),
        ("remoting_host", "Escritorio remoto de Chrome: sin él no puedes entrar a este PC desde fuera."),
        ("memurai", "Base de datos Redis: algún programa tuyo puede depender de ella."),
        ("openrgb", "Controla las luces RGB: sin él vuelven al color por defecto."),
        ("timerresolution", "Fija el temporizador a 0,5 ms al iniciar sesión."),
    ];

    static string? Warn(params string?[] texts)
    {
        var all = string.Join(" ", texts.Where(t => t is not null)).ToLowerInvariant();
        foreach (var (m, why) in Careful) if (all.Contains(m)) return why;
        return null;
    }

    // ------------------------------------------------------------------ lista
    public static List<StartItem> List()
    {
        var items = new List<StartItem>();
        items.AddRange(RunEntries(Registry.CurrentUser, RunKey, "Run", "HKCU", "Tu usuario"));
        items.AddRange(RunEntries(Registry.LocalMachine, RunKey, "Run", "HKLM", "Todos los usuarios"));
        items.AddRange(RunEntries(Registry.LocalMachine, RunKey32, "Run32", "HKLM32", "Todos los usuarios (32 bits)"));
        items.AddRange(FolderEntries(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, "user", "Carpeta Inicio de tu usuario"));
        items.AddRange(FolderEntries(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, "common", "Carpeta Inicio de todos"));
        try { items.AddRange(Tasks()); } catch (Exception e) { Debug.WriteLine(e); }
        try { items.AddRange(Services()); } catch (Exception e) { Debug.WriteLine(e); }
        return items;
    }

    static IEnumerable<StartItem> RunEntries(RegistryKey hive, string path, string approvedSub, string tag, string where)
    {
        using var k = hive.OpenSubKey(path);
        if (k is null) yield break;
        using var ap = hive.OpenSubKey(Approved + approvedSub);
        foreach (var name in k.GetValueNames())
        {
            if (name.Length == 0) continue;
            var cmd = k.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
            var exe = ExePath(cmd);
            var (pub, desc) = Describe(exe);
            yield return new StartItem($"run:{tag}:{name}", "Programas", Friendly(desc, name), pub, cmd, where,
                IsOn(ap?.GetValue(name)), exe is not null && !File.Exists(exe), Warn(name, cmd, pub));
        }
    }

    static IEnumerable<StartItem> FolderEntries(string dir, RegistryKey hive, string tag, string where)
    {
        if (!Directory.Exists(dir)) yield break;
        using var ap = hive.OpenSubKey(Approved + "StartupFolder");
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var file = Path.GetFileName(f);
            if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var target = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? LinkTarget(f) : f;
            var (pub, desc) = Describe(target);
            yield return new StartItem($"folder:{tag}:{file}", "Programas", desc ?? Path.GetFileNameWithoutExtension(file), pub,
                target ?? f, where, IsOn(ap?.GetValue(file)), target is not null && !File.Exists(target), Warn(file, target, pub));
        }
    }

    static string Friendly(string? desc, string name)
    {
        if (name.StartsWith("MicrosoftEdgeAutoLaunch", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (name.StartsWith("electron.app.", StringComparison.OrdinalIgnoreCase)) name = name[13..];
        return desc ?? name;
    }

    /// <summary>StartupApproved: sin valor = encendido; el primer byte impar (03, 07…) = apagado.</summary>
    static bool IsOn(object? v) => v is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;

    // tareas programadas con disparador al arrancar (8) o al iniciar sesión (9), fuera de \Microsoft
    static IEnumerable<StartItem> Tasks()
    {
        var list = new List<StartItem>();
        dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        svc.Connect();
        void Walk(dynamic folder)
        {
            string fpath = folder.Path;
            if (fpath.StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase)) return;
            foreach (dynamic t in folder.GetTasks(1))
            {
                try
                {
                    dynamic def = t.Definition;
                    bool atStart = false;
                    foreach (dynamic tr in def.Triggers) { int ty = tr.Type; if (ty is 8 or 9) atStart = true; }
                    if (!atStart) continue;
                    string cmd = "";
                    foreach (dynamic a in def.Actions) { if ((int)a.Type == 0) { cmd = $"{a.Path} {a.Arguments}".Trim(); break; } }
                    var exe = ExePath(Environment.ExpandEnvironmentVariables(cmd));
                    var (pub, desc) = Describe(exe);
                    string name = t.Name, path = t.Path;
                    list.Add(new StartItem($"task:{path}", "Tareas programadas", desc ?? name, pub, cmd.Length > 0 ? cmd : "(sin programa: acción COM)",
                        $"Programador de tareas · {path}", (bool)t.Enabled, exe is not null && !File.Exists(exe), Warn(name, cmd, pub)));
                }
                catch { /* tarea sin permisos de lectura */ }
            }
            foreach (dynamic sub in folder.GetFolders(0)) Walk(sub);
        }
        Walk(svc.GetFolder(@"\"));
        return list;
    }

    // servicios de terceros en automático o que Vigía pasó a manual
    static IEnumerable<StartItem> Services()
    {
        var saved = LoadSvcBackup();
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
        var list = new List<StartItem>();
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (root is null) return list;
        foreach (var name in root.GetSubKeyNames())
        {
            try
            {
                using var k = root.OpenSubKey(name);
                if (k is null) continue;
                // 0x10 / 0x20 = servicio de Windows (los drivers son 1, 2, 4, 8 y quedan fuera)
                var type = k.GetValue("Type") is int ty ? ty : 0;
                if ((type & 0x30) == 0) continue;
                var auto = k.GetValue("Start") is int st && st == 2;
                if (!auto && !saved.ContainsKey(name)) continue;
                var img = k.GetValue("ImagePath", "")?.ToString() ?? "";
                var exe = ExePath(Environment.ExpandEnvironmentVariables(img));
                if (exe is null || exe.StartsWith(win, StringComparison.OrdinalIgnoreCase)) continue;
                var (pub, desc) = Describe(exe);
                if (pub.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                var display = k.GetValue("DisplayName")?.ToString() ?? name;
                if (display.StartsWith('@')) display = desc ?? name;
                list.Add(new StartItem($"svc:{name}", "Servicios", display, pub, img,
                    auto ? "Servicio · automático" : "Servicio · manual (lo cambió Vigía)", auto, !File.Exists(exe),
                    Warn(name, display, img, pub)));
            }
            catch { }
        }
        return list.OrderBy(x => x.Name);
    }

    // ------------------------------------------------------------------ cambiar
    public static StartDone Set(string id, bool on)
    {
        try
        {
            var parts = id.Split(':', 3);
            // copia de seguridad del estado de antes; sin copia no se cambia nada
            var items = BackupItems(parts, id).ToList();
            var verb = on ? "Encender al inicio" : "Apagar al inicio";
            if (items.Count > 0 && Backups.Save("inicio", $"{verb}: {items[0]["label"]}", items) is null)
                return new(id, false, "No pude guardar la copia de seguridad; no cambié nada.");
            switch (parts[0])
            {
                case "run":
                {
                    var (hive, sub) = parts[1] switch
                    {
                        "HKCU" => (Registry.CurrentUser, "Run"),
                        "HKLM" => (Registry.LocalMachine, "Run"),
                        "HKLM32" => (Registry.LocalMachine, "Run32"),
                        _ => throw new InvalidOperationException("Origen desconocido."),
                    };
                    WriteApproved(hive, sub, parts[2], on);
                    break;
                }
                case "folder":
                    WriteApproved(parts[1] == "user" ? Registry.CurrentUser : Registry.LocalMachine, "StartupFolder", parts[2], on);
                    break;
                case "task":
                {
                    dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
                    svc.Connect();
                    var path = id[5..];
                    var slash = path.LastIndexOf('\\');
                    dynamic folder = svc.GetFolder(slash <= 0 ? @"\" : path[..slash]);
                    dynamic t = folder.GetTask(path[(slash + 1)..]);
                    t.Enabled = on;
                    break;
                }
                case "svc":
                {
                    var name = id[4..];
                    var saved = LoadSvcBackup();
                    if (!on)
                    {
                        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name)
                            ?? throw new InvalidOperationException("Ese servicio ya no existe.");
                        var delayed = k.GetValue("DelayedAutostart") is int d && d == 1;
                        saved[name] = delayed ? "delayed-auto" : "auto";
                        SaveSvcBackup(saved);
                        Sc(name, "demand");
                    }
                    else
                    {
                        Sc(name, saved.TryGetValue(name, out var mode) ? mode : "auto");
                        saved.Remove(name);
                        SaveSvcBackup(saved);
                    }
                    break;
                }
                default: return new(id, false, "No sé qué es esto.");
            }
            return new(id, true, null);
        }
        catch (UnauthorizedAccessException) { return new(id, false, "Windows no deja cambiar esta entrada (protegida)."); }
        catch (Exception e) { return new(id, false, e.Message); }
    }

    /// <summary>Estado actual de todo lo que arranca con Windows (para la copia «original»).</summary>
    internal static List<System.Text.Json.Nodes.JsonObject> SnapshotAll()
    {
        var res = new List<System.Text.Json.Nodes.JsonObject>();
        foreach (var it in List().Where(x => !x.Missing))
        {
            try { res.AddRange(BackupItems(it.Id.Split(':', 3), it.Id)); } catch { }
        }
        return res;
    }

    static IEnumerable<System.Text.Json.Nodes.JsonObject> BackupItems(string[] parts, string id)
    {
        switch (parts[0])
        {
            case "run" when parts.Length == 3:
                var (root, sub) = parts[1] switch { "HKCU" => ("HKCU", "Run"), "HKLM" => ("HKLM", "Run"), _ => ("HKLM", "Run32") };
                yield return Backups.Item.Reg($@"{root}\{Approved}{sub}", parts[2], parts[2]);
                break;
            case "folder" when parts.Length == 3:
                yield return Backups.Item.Reg($@"{(parts[1] == "user" ? "HKCU" : "HKLM")}\{Approved}StartupFolder", parts[2], parts[2]);
                break;
            case "task":
                if (TaskEnabled(id[5..]) is bool en) yield return Backups.Item.Task(id[5..], en, id[5..]);
                break;
            case "svc":
                var k = $@"HKLM\SYSTEM\CurrentControlSet\Services\{id[4..]}";
                yield return Backups.Item.Reg(k, "Start", $"Servicio {id[4..]}");
                yield return Backups.Item.Reg(k, "DelayedAutostart", $"Servicio {id[4..]} (retrasado)");
                break;
        }
    }

    static dynamic? FindTask(string path)
    {
        try
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            svc.Connect();
            var slash = path.LastIndexOf('\\');
            return svc.GetFolder(slash <= 0 ? @"\" : path[..slash]).GetTask(path[(slash + 1)..]);
        }
        catch { return null; }
    }

    internal static bool? TaskEnabled(string path) => FindTask(path) is { } t ? (bool)t.Enabled : null;

    internal static void SetTask(string path, bool on)
    {
        var t = FindTask(path) ?? throw new InvalidOperationException("Esa tarea ya no existe.");
        t.Enabled = on;
    }

    /// <summary>El mismo formato que escribe el Administrador de tareas: 12 bytes, 02 = encendido, 03 + fecha = apagado.</summary>
    static void WriteApproved(RegistryKey hive, string sub, string name, bool on)
    {
        using var k = hive.CreateSubKey(Approved + sub, true);
        var b = new byte[12];
        b[0] = on ? (byte)2 : (byte)3;
        if (!on) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(b, 4);
        k.SetValue(name, b, RegistryValueKind.Binary);
    }

    static void Sc(string name, string mode)
    {
        var p = Process.Start(new ProcessStartInfo("sc.exe", $"config \"{name}\" start= {mode}")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        if (p.ExitCode != 0) throw new InvalidOperationException($"sc config falló ({p.ExitCode}): {output.Trim()}");
    }

    static Dictionary<string, string> LoadSvcBackup()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SvcBackup)) ?? []; }
        catch { return []; }
    }

    static void SaveSvcBackup(Dictionary<string, string> d)
    {
        Directory.CreateDirectory(Settings.Dir);
        File.WriteAllText(SvcBackup, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ------------------------------------------------------------------ utilidades
    /// <summary>La ruta del programa dentro de una orden: "C:\x y\a.exe" -b, C:\a.exe -b, rundll32 x.dll,…</summary>
    public static string? ExePath(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.Length == 0) return null;
        string path;
        if (cmd[0] == '"')
        {
            var end = cmd.IndexOf('"', 1);
            path = end > 0 ? cmd[1..end] : cmd[1..];
        }
        else
        {
            var i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = i > 0 ? cmd[..(i + 4)] : cmd.Split(' ')[0];
        }
        if (path.StartsWith(@"\??\")) path = path[4..];
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            path = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + path[11..];
        if (!Path.IsPathRooted(path))
        {
            var sys = Path.Combine(Environment.SystemDirectory, path);
            if (File.Exists(sys)) return sys;
            if (File.Exists(sys + ".exe")) return sys + ".exe";
            return null;   // en el PATH: no se puede saber con certeza si falta
        }
        return path;
    }

    // programas que solo ejecutan otra cosa: su descripción («Windows Based Script Host») no dice nada
    static readonly string[] Hosts = ["wscript.exe", "cscript.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "rundll32.exe", "conhost.exe", "javaw.exe", "python.exe", "pythonw.exe", "node.exe"];

    static (string Publisher, string? Description) Describe(string? exe)
    {
        if (exe is null || !File.Exists(exe)) return ("", null);
        if (Hosts.Contains(Path.GetFileName(exe).ToLowerInvariant())) return ("", null);
        try
        {
            var v = FileVersionInfo.GetVersionInfo(exe);
            var desc = string.IsNullOrWhiteSpace(v.FileDescription) ? null : v.FileDescription.Trim();
            return ((v.CompanyName ?? "").Trim(), desc);
        }
        catch { return ("", null); }
    }

    static string? LinkTarget(string lnk)
    {
        try
        {
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic sc = sh.CreateShortcut(lnk);
            string t = sc.TargetPath;
            Marshal.FinalReleaseComObject(sc); Marshal.FinalReleaseComObject(sh);
            return string.IsNullOrEmpty(t) ? null : t;
        }
        catch { return null; }
    }
}
