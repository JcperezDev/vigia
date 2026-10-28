using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Vigia;

/// <summary>Un cierre (o grupo de cierres iguales) con su causa probable en palabras normales.</summary>
public sealed record Crash(string Kind, string App, DateTime Last, int Count, string Level, string Why, string Fix,
    bool Hardware, IReadOnlyList<string> Tech);

/// <summary>
/// «¿Por qué se cerró?»: lee el registro de eventos de Windows y los informes de juegos Unreal (Fortnite, VALORANT…)
/// y explica la causa probable de cada cierre. Solo lee; se ejecuta al abrir Diagnóstico, nunca en segundo plano.
/// </summary>
public static partial class Diagnostics
{
    record Cause(string Level, string Why, string Fix);

    const string FixSelf = "Actualízalo o repáralo (en juegos: Verificar archivos en el launcher). Si se actualizó hace poco, puede ser un fallo suyo que arreglen en el siguiente parche.";
    const string FixRamOc = "Actualízalo o reinstálalo. Si le pasa a muchos programas distintos, revisa la RAM (EXPO/XMP) o el overclock.";
    const string FixGpu = "Instala el driver más reciente de la web del fabricante (instalación limpia). Si la gráfica tiene overclock o undervolt, quítalo y prueba. Mira también su temperatura en Monitor.";
    const string FixDrivers = "Actualiza los drivers de la gráfica, el chipset y la red. Si sigue, prueba con EXPO/XMP apagado.";
    const string FixHw = "Casi siempre es overclock, undervolt (Curve Optimizer) o EXPO inestable, temperatura o la fuente. Deja la BIOS en valores de fábrica unos días y prueba.";
    const string FixDisk = "Revisa el disco en Salud de discos y, si es SATA, el cable.";

    // Módulos que no son del programa: dónde «cayó» el cierre dice quién lo provocó
    static readonly (string Prefix, string Name, string Group)[] Modules =
    [
        ("nvwgf2um", "NVIDIA", "gpu"), ("nvd3dum", "NVIDIA", "gpu"), ("nvoglv", "NVIDIA", "gpu"), ("nvcuda", "NVIDIA", "gpu"),
        ("nvldumd", "NVIDIA", "gpu"), ("nvlddmkm", "NVIDIA", "gpu"), ("nvgpucomp", "NVIDIA", "gpu"), ("nvppe", "NVIDIA", "gpu"),
        ("atiumd", "AMD", "gpu"), ("atidxx", "AMD", "gpu"), ("amdxx", "AMD", "gpu"), ("aticfx", "AMD", "gpu"), ("amdvlk", "AMD", "gpu"),
        ("amdxc", "AMD", "gpu"), ("atio6axx", "AMD", "gpu"),
        ("igd10iumd", "Intel", "gpu"), ("igd12umd", "Intel", "gpu"), ("igdumdim", "Intel", "gpu"), ("igc64", "Intel", "gpu"), ("igvk", "Intel", "gpu"),
        ("d3d11", "DirectX", "dx"), ("d3d12", "DirectX", "dx"), ("dxgi", "DirectX", "dx"), ("d3d9", "DirectX", "dx"), ("dxcore", "DirectX", "dx"),
        ("gameoverlayrenderer", "Steam", "overlay"), ("discordhook", "Discord", "overlay"), ("rtsshooks", "RivaTuner", "overlay"),
        ("nvspcap", "NVIDIA overlay", "overlay"), ("graphics-hook", "OBS", "overlay"), ("owclient", "Overwolf", "overlay"),
        ("owexplorer", "Overwolf", "overlay"), ("igo64", "EA", "overlay"), ("eosovh", "Epic overlay", "overlay"), ("medal", "Medal", "overlay"),
        ("reshade", "ReShade", "overlay"), ("xboxgamebar", "Xbox Game Bar", "overlay"),
        ("easyanticheat", "Easy Anti-Cheat", "ac"), ("beclient", "BattlEye", "ac"), ("battleye", "BattlEye", "ac"), ("vgc", "Vanguard", "ac"),
        ("eaanticheat", "EA Anti-Cheat", "ac"), ("faceit", "FACEIT", "ac"),
        ("aswhook", "Avast", "av"), ("aswamsi", "Avast", "av"), ("bdhkm", "Bitdefender", "av"), ("atcuf", "Bitdefender", "av"),
        ("sophosed", "Sophos", "av"), ("mfehcthe", "McAfee", "av"), ("hmpalert", "HitmanPro", "av"),
        ("clr.dll", ".NET", "net"), ("coreclr", ".NET", "net"),
    ];

    static Cause ForAppCrash(string app, string module, uint code, out string? who)
    {
        who = null;
        var m = module.ToLowerInvariant();
        var hit = Modules.FirstOrDefault(x => m.StartsWith(x.Prefix));
        if (code is 0x887a0005 or 0x887a0006 or 0x887a0007)
            return new("bad", "La gráfica dejó de responder mientras el programa la usaba (dispositivo perdido).", FixGpu);
        if (hit.Prefix is not null)
        {
            who = hit.Name;
            switch (hit.Group)
            {
                case "gpu": return new("bad", hit.Name switch {
                    "NVIDIA" => "Falló dentro del driver de NVIDIA.", "AMD" => "Falló dentro del driver de AMD.", _ => "Falló dentro del driver de Intel." }, FixGpu);
                case "dx": return new("warn", "Falló en DirectX, la parte de Windows que habla con la gráfica. Casi siempre lo provoca el driver de la gráfica.", FixGpu);
                case "overlay": return new("warn", $"Falló dentro de un overlay que se mete en el programa: {hit.Name}.",
                    "Apaga ese overlay para este programa (Discord, Steam, RivaTuner, GeForce, OBS…) y prueba.");
                case "ac": return new("warn", "Falló dentro del anti-cheat del juego.", "Repara el juego desde su launcher (Verificar archivos). Vigía no toca el anti-cheat.");
                case "av": return new("warn", $"Falló dentro de un antivirus que se mete en los programas: {hit.Name}.", "Añade el programa a las exclusiones de ese antivirus o actualízalo.");
                case "net": return new("info", "Error no controlado dentro del programa (.NET).", FixSelf);
            }
        }
        switch (code)
        {
            case 0xc0000006: return new("warn", "Windows no pudo leer del disco una parte del programa.", FixDisk);
            case 0xc000001d or 0xc0000096: return new("warn", "El procesador recibió una instrucción que no pudo ejecutar.",
                "Si la CPU tiene overclock o Curve Optimizer, quítalo y prueba. Si no, es un fallo del programa.");
            case 0xc0000374: return new("warn", "La memoria del programa se dañó mientras corría.", FixRamOc);
            case 0xc0000142: return new("info", "El programa no pudo arrancar: falló al cargar una de sus piezas.", "Reinstálalo y reinstala los Visual C++ Redistributable de Microsoft.");
            case 0xc00000fd: return new("info", "El programa se quedó sin pila (un bucle sin fin dentro de él).", FixSelf);
            case 0xe0434352: return new("info", "Error no controlado dentro del programa (.NET).", FixSelf);
            case 0x80000003: return new("info", "El programa se detuvo en una comprobación interna (un fallo suyo).", FixSelf);
            case 0xc0000409 or 0xc0000417: return new("info", "El programa se cerró solo al detectar un error interno.", FixSelf);
        }
        var self = Path.GetFileNameWithoutExtension(m) == Path.GetFileNameWithoutExtension(app.ToLowerInvariant());
        if (code == 0xc0000005)
            return self ? new("info", "Falló dentro del propio programa: intentó usar memoria que no era suya.", FixSelf)
                        : new("info", "El programa intentó usar memoria que no era suya.", FixRamOc);
        return self ? new("info", "Falló dentro del propio programa.", FixSelf)
                    : new("info", "Se cerró de golpe por un error interno.", FixSelf);
    }

    static (string Name, Cause Cause) ForBugCheck(uint c) => c switch
    {
        0x1a => ("MEMORY_MANAGEMENT", new("bad", "Windows encontró la memoria RAM en un estado imposible.",
            "Suele ser la RAM o el perfil EXPO/XMP. Prueba con EXPO apagado o pasa el Diagnóstico de memoria de Windows (mdsched).")),
        0x50 => ("PAGE_FAULT_IN_NONPAGED_AREA", new("bad", "Un driver o la RAM pidió memoria que no existía.", FixDrivers)),
        0x0a => ("IRQL_NOT_LESS_OR_EQUAL", new("bad", "Un driver usó memoria en un momento en que no podía.", FixDrivers)),
        0xd1 => ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", new("bad", "Un driver usó memoria en un momento en que no podía.", FixDrivers)),
        0x3b => ("SYSTEM_SERVICE_EXCEPTION", new("bad", "Un driver falló dentro de Windows.", FixDrivers)),
        0x7e or 0x1000007e => ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", new("bad", "Un driver falló dentro de Windows.", FixDrivers)),
        0x1e or 0x1000001e => ("KMODE_EXCEPTION_NOT_HANDLED", new("bad", "Un driver falló dentro de Windows.", FixDrivers)),
        0x19 => ("BAD_POOL_HEADER", new("bad", "Un driver falló dentro de Windows.", FixDrivers)),
        0xc2 => ("BAD_POOL_CALLER", new("bad", "Un driver falló dentro de Windows.", FixDrivers)),
        0xc5 => ("DRIVER_CORRUPTED_EXPOOL", new("bad", "Un driver falló dentro de Windows.", FixDrivers)),
        0x139 => ("KERNEL_SECURITY_CHECK_FAILURE", new("bad", "Un driver falló dentro de Windows.", FixDrivers)),
        0x13a => ("KERNEL_MODE_HEAP_CORRUPTION", new("bad", "Un driver falló dentro de Windows.", FixDrivers)),
        0x116 => ("VIDEO_TDR_FAILURE", new("bad", "La gráfica dejó de responder y Windows no pudo recuperarla.", FixGpu)),
        0x117 => ("VIDEO_TDR_TIMEOUT_DETECTED", new("bad", "La gráfica dejó de responder y Windows no pudo recuperarla.", FixGpu)),
        0x124 => ("WHEA_UNCORRECTABLE_ERROR", new("bad", "El propio hardware reportó un error grave (procesador, RAM o PCIe).", FixHw)),
        0x101 => ("CLOCK_WATCHDOG_TIMEOUT", new("bad", "Un núcleo del procesador dejó de responder.", FixHw)),
        0x133 => ("DPC_WATCHDOG_VIOLATION", new("bad", "Un driver tardó demasiado sin soltar el procesador.",
            "Actualiza el driver del chipset y el firmware del SSD. También pasa con drivers de audio o de red viejos.")),
        0x9f => ("DRIVER_POWER_STATE_FAILURE", new("bad", "Un driver no respondió al dormir o despertar el equipo.",
            "Actualiza los drivers de la gráfica, la red y el chipset. Mientras, puedes apagar la suspensión.")),
        0xef => ("CRITICAL_PROCESS_DIED", new("bad", "Se cerró un proceso que Windows necesita para funcionar.",
            "Revisa el disco en Salud de discos y repara Windows con sfc /scannow como administrador.")),
        0xf4 => ("CRITICAL_OBJECT_TERMINATION", new("bad", "Se cerró un proceso que Windows necesita para funcionar.",
            "Revisa el disco en Salud de discos y repara Windows con sfc /scannow como administrador.")),
        0x7a => ("KERNEL_DATA_INPAGE_ERROR", new("bad", "Windows no pudo leer del disco algo que necesitaba.", FixDisk)),
        0x77 => ("KERNEL_STACK_INPAGE_ERROR", new("bad", "Windows no pudo leer del disco algo que necesitaba.", FixDisk)),
        0x154 => ("UNEXPECTED_STORE_EXCEPTION", new("bad", "Windows no pudo leer del disco algo que necesitaba.", FixDisk)),
        0x109 => ("CRITICAL_STRUCTURE_CORRUPTION", new("bad", "Algo modificó una parte protegida de Windows.",
            "Suele ser un driver viejo o la RAM. Actualiza los drivers y prueba con EXPO/XMP apagado.")),
        _ => ($"0x{c:x}", new("bad", "Windows se detuvo por un error grave.",
            "Busca el código en internet o abre el volcado de C:\\Windows\\Minidump con WinDbg para ver qué driver fue.")),
    };

    static uint Hex(object? v)
    {
        if (v is uint u) return u;
        if (v is int i) return unchecked((uint)i);
        if (v is ulong ul) return unchecked((uint)ul);
        var s = (v?.ToString() ?? "").Trim();
        var mm = Regex.Match(s, @"0x([0-9a-fA-F]+)");
        var hex = mm.Success ? mm.Groups[1].Value : s;
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var r) ? r : 0;
    }

    static string Pretty(string exe)
    {
        var n = Path.GetFileNameWithoutExtension(exe.Trim());
        n = Regex.Replace(n, @"-Win64-(Shipping|Test|Development)$", "", RegexOptions.IgnoreCase);
        return n.ToLowerInvariant() switch
        {
            "fortniteclient" or "fortnitegame" => "Fortnite", "cs2" => "Counter-Strike 2", "r5apex" or "r5apex_dx12" => "Apex Legends",
            "valorant" => "VALORANT", "shootergame" => "VALORANT", "cod" => "Call of Duty", "rocketleague" => "Rocket League",
            "gta5" or "gta5_enhanced" => "GTA V", "eldenring" => "Elden Ring", "overwatch" => "Overwatch 2",
            "minecraft.windows" or "javaw" => n, "msedge" => "Microsoft Edge", "chrome" => "Google Chrome", "explorer" => "Explorador de Windows",
            _ => n,
        };
    }

    static List<(DateTime Time, Dictionary<string, string> Data)> QueryNamed(string log, string xpath, int max = 100)
    {
        var res = new List<(DateTime, Dictionary<string, string>)>();
        try
        {
            using var r = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true });
            for (EventRecord? e; res.Count < max && (e = r.ReadEvent()) is not null;)
                using (e)
                {
                    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var x = XElement.Parse(e.ToXml());
                        foreach (var el in x.Descendants().Where(el => el.Name.LocalName == "Data" && el.Attribute("Name") is not null))
                            d[el.Attribute("Name")!.Value] = el.Value;
                    }
                    catch { }
                    res.Add((e.TimeCreated ?? DateTime.MinValue, d));
                }
        }
        catch { }
        return res;
    }

    public static List<Crash> Crashes(int days = 7)
    {
        var raw = new List<(string Kind, string App, DateTime Time, Cause Cause, string Key, string Tech)>();

        // Pantallazos azules
        foreach (var e in Query("System", $"*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001 and {Since(days)}]]", 50))
        {
            var code = Hex(e.Props.Count > 0 ? e.Props[0].Value : null);
            var (name, cause) = ForBugCheck(code);
            raw.Add(("Pantallazo azul", name, e.Time, cause, $"bsod|{code:x}", $"{When(e.Time)} · 0x{code:x8} {name}"));
        }

        // Apagados de golpe sin pantallazo (Kernel-Power 41)
        foreach (var (t, d) in QueryNamed("System", $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and {Since(days)}]]", 30))
        {
            if (Hex(d.GetValueOrDefault("BugcheckCode")) != 0) continue;   // ya sale como pantallazo azul
            var button = d.TryGetValue("PowerButtonTimestamp", out var pb) && pb.Trim() is var p && p != "" && p != "0";
            var cause = button
                ? new Cause("warn", "Se apagó porque mantuviste el botón de encendido; seguramente el PC se había colgado.",
                    "Si se cuelga a menudo, mira si hay errores de hardware (WHEA) más abajo y prueba sin overclock ni EXPO.")
                : new Cause("bad", "Se apagó de golpe, sin pantallazo azul. Suele ser un corte de luz, la fuente que no da para tanto, o un cuelgue total.",
                    "Si pasa jugando, apunta a la fuente o a las temperaturas. Si tienes overclock, undervolt o EXPO, quítalo y prueba.");
            raw.Add(("Apagado de golpe", "Windows", t, cause, button ? "kp|btn" : "kp|cut", $"{When(t)} · Kernel-Power 41{(button ? " · botón" : "")}"));
        }

        // La gráfica se reinició (TDR): pantalla negra unos segundos y a veces el juego se cierra
        var tdr = Query("System", $"*[System[(Provider[@Name='Display'] and EventID=4101) or (Provider[@Name='nvlddmkm'] and (EventID=13 or EventID=14 or EventID=153)) or (Provider[@Name='amdkmdag'] and Level<=2)] and System[{Since(days)}]]", 50);
        foreach (var e in tdr)
            raw.Add(("La gráfica se reinició", e.Provider == "Display" ? "Driver de la gráfica" : e.Provider == "nvlddmkm" ? "NVIDIA" : "AMD", e.Time,
                new("bad", "El driver de la gráfica dejó de responder y Windows lo reinició. Se ve como pantalla negra unos segundos, o el juego se cierra.", FixGpu),
                "tdr", $"{When(e.Time)} · {e.Provider} {e.Id}"));
        foreach (var e in Query("Application", $"*[System[Provider[@Name='Windows Error Reporting'] and EventID=1001 and {Since(days)}]]", 80))
        {
            if (e.Props.Count < 6 || !string.Equals(e.Props[2].Value?.ToString(), "LiveKernelEvent", StringComparison.OrdinalIgnoreCase)) continue;
            var p1 = e.Props[5].Value?.ToString() ?? "";
            if (p1 is "141" or "117" or "1a8" or "1b8")
                raw.Add(("La gráfica se reinició", "Driver de la gráfica", e.Time,
                    new("bad", "El driver de la gráfica dejó de responder y Windows lo reinició. Se ve como pantalla negra unos segundos, o el juego se cierra.", FixGpu),
                    "tdr", $"{When(e.Time)} · LiveKernelEvent {p1}"));
        }

        // Programas: cierres y cuelgues
        foreach (var e in Query("Application", $"*[System[Provider[@Name='Application Error'] and EventID=1000 and {Since(days)}]]", 200))
        {
            string P(int i) => e.Props.Count > i ? e.Props[i].Value?.ToString() ?? "" : "";
            var app = P(0); var module = P(3); var code = Hex(e.Props.Count > 6 ? e.Props[6].Value : null);
            if (app.Length == 0) continue;
            var cause = ForAppCrash(app, module, code, out _);
            raw.Add(("Se cerró", Pretty(app), e.Time, cause, $"app|{app.ToLowerInvariant()}|{cause.Why}",
                $"{When(e.Time)} · {app} · {(module.Length > 0 ? module : "?")} · 0x{code:x8}"));
        }
        foreach (var e in Query("Application", $"*[System[Provider[@Name='Application Hang'] and EventID=1002 and {Since(days)}]]", 100))
        {
            var app = e.Props.Count > 0 ? e.Props[0].Value?.ToString() ?? "" : "";
            if (app.Length == 0) continue;
            raw.Add(("Se colgó", Pretty(app), e.Time, new("info", "Dejó de responder y se cerró (lo cerró Windows o lo cerraste tú).",
                "Si pasa siempre en el mismo punto, actualízalo. Si se cuelga con el disco al 100 %, el disco puede ir lento."),
                $"hang|{app.ToLowerInvariant()}", $"{When(e.Time)} · {app} · Application Hang"));
        }

        // Juegos con motor Unreal guardan su propio informe: %LOCALAPPDATA%\<Juego>\Saved\Crashes\<id>\CrashContext.runtime-xml
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var game in Directory.EnumerateDirectories(local))
            {
                var dir = Path.Combine(game, "Saved", "Crashes");
                if (!Directory.Exists(dir)) continue;
                foreach (var c in new DirectoryInfo(dir).GetDirectories().Where(d => d.LastWriteTime > DateTime.Now.AddDays(-days))
                    .OrderByDescending(d => d.LastWriteTime).Take(10))
                {
                    var f = Path.Combine(c.FullName, "CrashContext.runtime-xml");
                    if (!File.Exists(f) || new FileInfo(f).Length > 4_000_000) continue;
                    string Tag(XElement x, string n) => x.Descendants().FirstOrDefault(el => el.Name.LocalName == n)?.Value.Trim() ?? "";
                    XElement xml; try { xml = XElement.Load(f); } catch { continue; }
                    var type = Tag(xml, "CrashType"); var msg = Tag(xml, "ErrorMessage");
                    if (type.Equals("Ensure", StringComparison.OrdinalIgnoreCase) || c.Name.StartsWith("UECC-") is false && type.Length == 0 && msg.Length == 0) continue;
                    var name = Tag(xml, "GameName"); name = name.StartsWith("UE-") ? name[3..] : name.Length > 0 ? name : Path.GetFileName(game);
                    var mm = msg.ToLowerInvariant();
                    var cause = mm.Contains("d3d device") || mm.Contains("device removed") || mm.Contains("device_hung") || mm.Contains("device_removed") || mm.Contains("gpu crash")
                            ? new Cause("bad", "La gráfica dejó de responder mientras el programa la usaba (dispositivo perdido).", FixGpu)
                        : mm.Contains("video memory") || mm.Contains("out of vram")
                            ? new Cause("warn", "Se quedó sin memoria de la gráfica (VRAM).", "Baja la calidad de texturas o la resolución, y cierra otros programas que usen la gráfica.")
                        : mm.Contains("out of memory") || mm.Contains("ran out of memory")
                            ? new Cause("warn", "Se quedó sin memoria RAM.", "Cierra programas que no uses mientras juegas y deja que Windows administre el archivo de paginación.")
                        : type.Equals("Hang", StringComparison.OrdinalIgnoreCase) || mm.Contains("hang detected")
                            ? new Cause("info", "El juego se quedó congelado y su propio vigilante lo cerró.", FixSelf)
                        : mm.Contains("assertion failed") || mm.Contains("fatal error")
                            ? new Cause("info", "El juego se detuvo en una comprobación interna (un fallo suyo).", FixSelf)
                        : mm.Contains("access_violation") || mm.Contains("access violation")
                            ? new Cause("info", "Falló dentro del propio programa: intentó usar memoria que no era suya.", FixSelf)
                        : new Cause("info", "Falló dentro del propio programa.", FixSelf);
                    var first = (msg.Split('\n').FirstOrDefault() ?? "").Trim();
                    raw.Add(("Se cerró", Pretty(name), c.LastWriteTime, cause, $"ue|{name.ToLowerInvariant()}|{cause.Why}",
                        $"{When(c.LastWriteTime)} · {name} · {(first.Length > 140 ? first[..140] + "…" : first.Length > 0 ? first : type)}"));
                }
            }
        }
        catch { }

        // ¿Hubo un error de hardware justo a esa hora? Entonces la causa probable es el hardware, no el programa
        var whea = Query("System", $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and {Since(days)}]]", 200).Select(e => e.Time).ToList();
        bool Hw(DateTime t) => whea.Any(w => Math.Abs((w - t).TotalMinutes) <= 30);

        var groups = raw.GroupBy(r => r.Kind + "|" + r.Key).Select(g =>
        {
            var l = g.OrderByDescending(r => r.Time).ToList();
            var hw = l.Any(r => Hw(r.Time));
            return new Crash(l[0].Kind, l[0].App, l[0].Time, l.Count, hw ? "bad" : l[0].Cause.Level, l[0].Cause.Why,
                hw ? FixHw : l[0].Cause.Fix, hw, l.Take(6).Select(r => r.Tech).ToList());
        }).ToList();

        // Muchos programas distintos fallando con la memoria = la RAM o el overclock, no los programas
        var memApps = raw.Where(r => r.Key.StartsWith("app|") && (r.Cause.Why.Contains("memoria que no era suya") || r.Cause.Why.Contains("memoria del programa se dañó")))
            .Select(r => r.App).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (memApps.Count >= 4)
            groups.Add(new Crash("Varios programas", string.Join(", ", memApps.Take(5)), raw.Where(r => memApps.Contains(r.App)).Max(r => r.Time), memApps.Count, "warn",
                "Varios programas distintos fallaron con la memoria. Cuando pasa en muchos a la vez, la causa suele ser la RAM o un overclock inestable, no los programas.",
                "Prueba unos días con EXPO/XMP apagado o sin overclock. Si deja de pasar, era eso. También puedes pasar el Diagnóstico de memoria de Windows (mdsched).",
                false, Array.Empty<string>()));

        var order = new Dictionary<string, int> { ["bad"] = 0, ["warn"] = 1, ["info"] = 2 };
        return groups.OrderBy(c => order.GetValueOrDefault(c.Level, 3)).ThenByDescending(c => c.Last).Take(12).ToList();
    }
}
