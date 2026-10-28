using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>
/// ISO limpia: parte de la ISO oficial de Windows que pone el usuario y crea otra al lado, sin las apps de relleno,
/// sin anuncios ni apps sugeridas y, si se pide, con cuenta local. La original no se toca. Todo con herramientas de
/// Windows (DISM, IMAPI2), sin ventanas y con prioridad baja para no molestar si estás jugando.
/// La ISO no borra ni formatea discos por su cuenta: eso lo sigue eligiendo quien instala.
/// </summary>
public static partial class IsoBuild
{
    public record Edition(int Index, string Name, long Bytes);
    public record Source(string Path, long Bytes, List<Edition> Editions, string? Error);
    public record Options(string Source, string Output, int Index, string[] Apps, bool NoAds, bool LocalAccount, string? User, bool SkipChecks,
                         bool NoTelemetry = false, bool NoBitLocker = false);
    public record Progress(string Step, int Num, int Of, double? Pct);
    public record Done(bool Ok, string Message, string? Output, long Bytes, double Minutes);

    /// <summary>Solo para la prueba en la máquina virtual: instalación sin preguntas (borra el disco de la VM).</summary>
    internal static string? LabUnattend;

    static Process? _proc;
    static volatile bool _cancel;
    public static bool Busy { get; private set; }
    public static void Cancel() { _cancel = true; try { _proc?.Kill(true); } catch { } }

    // ---------- leer la ISO ----------
    public static Source Inspect(string iso)
    {
        if (!File.Exists(iso)) return new(iso, 0, [], "No encuentro ese archivo.");
        var bytes = new FileInfo(iso).Length;
        string? root = null;
        try
        {
            root = Mount(iso);
            var wim = Wim(root);
            if (wim is null || !File.Exists(Path.Combine(root, "boot", "etfsboot.com")) && !File.Exists(Path.Combine(root, "efi", "microsoft", "boot", "efisys.bin")))
                return new(iso, bytes, [], "No parece una ISO de instalación de Windows (no tiene sources\\install.wim).");
            var eds = Editions(wim);
            if (eds.Count == 0) return new(iso, bytes, [], "No pude leer las ediciones de Windows que trae.");
            return new(iso, bytes, eds, null);
        }
        catch (Exception e) { return new(iso, bytes, [], e.Message); }
        finally { if (root is not null) Unmount(iso); }
    }

    static string? Wim(string root) =>
        new[] { "install.wim", "install.esd" }.Select(n => Path.Combine(root, "sources", n)).FirstOrDefault(File.Exists);

    static List<Edition> Editions(string wim)
    {
        var (_, o) = Dism($"/English /Get-WimInfo /WimFile:\"{wim}\"", null, 120);
        var list = new List<Edition>();
        foreach (Match m in EdRx().Matches(o))
            list.Add(new(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), long.Parse(m.Groups[3].Value.Replace(",", "").Replace(".", ""))));
        return list;
    }
    [GeneratedRegex(@"Index : (\d+)\s+Name : ([^\r\n]+)\s+Description : [^\r\n]*\s+Size : ([\d,\.]+) bytes")] private static partial Regex EdRx();

    // ---------- crear ----------
    public static Done Build(Options o, Action<Progress> report)
    {
        if (Busy) return new(false, "Ya se está creando una ISO.", null, 0, 0);
        Busy = true; _cancel = false;
        var sw = Stopwatch.StartNew();
        var outDir = Path.GetDirectoryName(Path.GetFullPath(o.Output))!;
        var work = Path.Combine(outDir, $".vigia-iso-{Guid.NewGuid():N}"[..20]);
        var files = Path.Combine(work, "files"); var mount = Path.Combine(work, "mount"); var tmpWim = Path.Combine(work, "edition.wim");
        bool mounted = false, isoMounted = false;
        const int N = 6;
        try
        {
            if (!o.Output.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return Fail("El archivo de salida tiene que terminar en .iso.");
            if (Path.GetFullPath(o.Output).Equals(Path.GetFullPath(o.Source), StringComparison.OrdinalIgnoreCase)) return Fail("La ISO nueva no puede pisar la original.");
            if (o.LocalAccount && !ValidUser(o.User)) return Fail("El nombre de usuario solo puede llevar letras, números, espacios, puntos, guiones y guion bajo (hasta 20).");
            var need = new FileInfo(o.Source).Length * 3L;
            var free = new DriveInfo(Path.GetPathRoot(outDir)!).AvailableFreeSpace;
            if (free < need) return Fail($"Hace falta {need / 1_073_741_824} GB libres en {Path.GetPathRoot(outDir)} y hay {free / 1_073_741_824} GB.");
            Directory.CreateDirectory(files); Directory.CreateDirectory(mount);

            // 1. copiar la ISO tal cual (la original queda intacta)
            report(new("Copiando la ISO original", 1, N, 0));
            var root = Mount(o.Source); isoMounted = true;
            var all = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
            long total = all.Sum(f => new FileInfo(f).Length), done = 0;
            foreach (var f in all)
            {
                Check();
                var dst = Path.Combine(files, Path.GetRelativePath(root, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                CopyFile(f, dst, n => { done += n; report(new("Copiando la ISO original", 1, N, 100.0 * done / total)); });
                File.SetAttributes(dst, FileAttributes.Normal);
            }
            Unmount(o.Source); isoMounted = false;
            var srcWim = Wim(files) ?? throw new Exception("La ISO no trae sources\\install.wim.");

            // 2. quedarse solo con la edición elegida
            report(new("Separando la edición", 2, N, null));
            Must(Dism($"/Export-Image /SourceImageFile:\"{srcWim}\" /SourceIndex:{o.Index} /DestinationImageFile:\"{tmpWim}\" /Compress:max /CheckIntegrity", p => report(new("Separando la edición", 2, N, p))), "separar la edición");
            File.Delete(srcWim);

            // 3. abrir la imagen
            report(new("Abriendo la imagen de Windows", 3, N, null));
            Must(Dism($"/Mount-Image /ImageFile:\"{tmpWim}\" /Index:1 /MountDir:\"{mount}\"", p => report(new("Abriendo la imagen de Windows", 3, N, p))), "abrir la imagen");
            mounted = true;

            // 4. quitar apps y anuncios
            report(new("Quitando apps", 4, N, null));
            var want = new HashSet<string>(o.Apps.Where(a => Debloat.All().Any(d => d.Id == a)), StringComparer.OrdinalIgnoreCase);
            var (_, prov) = Dism($"/English /Image:\"{mount}\" /Get-ProvisionedAppxPackages", null, 300);
            var removed = new List<string>();
            var pkgs = PkgRx().Matches(prov).Select(m => m.Groups[1].Value.Trim()).ToList();
            for (var i = 0; i < pkgs.Count; i++)
            {
                Check();
                var name = pkgs[i].Split('_')[0];
                if (!want.Contains(name)) continue;
                report(new($"Quitando {Debloat.All().First(d => d.Id.Equals(name, StringComparison.OrdinalIgnoreCase)).Name}", 4, N, 100.0 * i / pkgs.Count));
                if (Dism($"/Image:\"{mount}\" /Remove-ProvisionedAppxPackage /PackageName:{pkgs[i]}", null, 600).exit == 0) removed.Add(name);
            }
            if (o.NoAds) NoAds(mount);
            if (o.NoTelemetry) NoTelemetry(mount);
            if (o.NoBitLocker) NoBitLocker(mount);

            // 5. guardar y comprimir
            report(new("Guardando los cambios", 5, N, null));
            Must(Dism($"/Unmount-Image /MountDir:\"{mount}\" /Commit", p => report(new("Guardando los cambios", 5, N, p))), "guardar la imagen");
            mounted = false;
            report(new("Comprimiendo", 5, N, null));
            Must(Dism($"/Export-Image /SourceImageFile:\"{tmpWim}\" /SourceIndex:1 /DestinationImageFile:\"{Path.Combine(files, "sources", "install.wim")}\" /Compress:max",
                p => report(new("Comprimiendo", 5, N, p))), "comprimir la imagen");
            File.Delete(tmpWim);
            var xml = Unattend(o);
            if (xml is not null) File.WriteAllText(Path.Combine(files, "autounattend.xml"), xml, new UTF8Encoding(false));

            // 6. ISO arrancable (BIOS y UEFI)
            report(new("Creando la ISO", 6, N, 0));
            var part = o.Output + ".parcial";
            WriteIso(files, part, p => report(new("Creando la ISO", 6, N, p)));
            File.Move(part, o.Output, true);
            var size = new FileInfo(o.Output).Length;
            var what = removed.Count == 0 ? "sin quitar apps" : $"sin {removed.Count} {(removed.Count == 1 ? "app" : "apps")}";
            return new(true, $"ISO lista ({what}{(o.NoAds ? ", sin anuncios" : "")}{(o.LocalAccount ? $", cuenta local «{o.User}»" : "")}{(o.NoTelemetry ? ", sin recopilación de datos" : "")}{(o.NoBitLocker ? ", sin cifrado automático" : "")}). Pásala a un USB con «Pasar a un USB».", o.Output, size, sw.Elapsed.TotalMinutes);
        }
        catch (OperationCanceledException) { return Fail("Cancelado. La ISO original sigue igual."); }
        catch (Exception e) { return Fail(e.Message); }
        finally
        {
            if (mounted) { Dism($"/Unmount-Image /MountDir:\"{mount}\" /Discard", null, 900); Dism("/Cleanup-Wim", null, 300); }
            if (isoMounted) Unmount(o.Source);
            try { File.Delete(o.Output + ".parcial"); } catch { }
            try { Directory.Delete(work, true); } catch { }
            Busy = false; _proc = null;
        }
        Done Fail(string m) => new(false, m, null, 0, sw.Elapsed.TotalMinutes);
    }
    [GeneratedRegex(@"PackageName : ([^\r\n]+)")] private static partial Regex PkgRx();

    static void Check() { if (_cancel) throw new OperationCanceledException(); }
    static void Must((int exit, string output) r, string what)
    {
        Check();
        if (r.exit != 0)
        {
            var line = r.output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? $"código {r.exit}";
            throw new Exception($"No se pudo {what}: {line}");
        }
    }

    internal static bool ValidUser(string? u) => u is { Length: > 0 and <= 20 } && UserRx().IsMatch(u)
        && !new[] { "administrator", "administrador", "guest", "invitado", "defaultaccount", "system" }.Contains(u.Trim().ToLowerInvariant());
    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} ._-]*$")] private static partial Regex UserRx();

    // ---------- registro de la imagen ----------
    /// <summary>Los mismos valores que «Sin anuncios ni apps sugeridas», pero escritos en la imagen antes de instalar.</summary>
    // Telemetría en lo mínimo que acepta Windows Pro/Home («Obligatorios»: 0 solo existe en Enterprise/Education),
    // sin pantalla de privacidad al instalar, sin ID de publicidad, historial de actividad ni experiencias «personalizadas».
    static void NoTelemetry(string mount)
    {
        Hive(Path.Combine(mount, "Windows", "System32", "config", "SOFTWARE"), "VIGIA_SW", k =>
        {
            var pol = $@"{k}\Policies\Microsoft\Windows";
            Reg($@"{pol}\DataCollection", "AllowTelemetry", 1);
            Reg($@"{pol}\DataCollection", "DoNotShowFeedbackNotifications", 1);
            Reg($@"{pol}\OOBE", "DisablePrivacyExperience", 1);
            Reg($@"{pol}\AdvertisingInfo", "DisabledByGroupPolicy", 1);
            Reg($@"{pol}\System", "EnableActivityFeed", 0);
            Reg($@"{pol}\System", "PublishUserActivities", 0);
            Reg($@"{pol}\System", "UploadUserActivities", 0);
        });
        Hive(Path.Combine(mount, "Users", "Default", "NTUSER.DAT"), "VIGIA_DU", k =>
        {
            Reg($@"{k}\Software\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1);
            Reg($@"{k}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
            Reg($@"{k}\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0);
            Reg($@"{k}\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0);
        });
    }

    // Windows 11 cifra solo el disco al instalarse si entras con cuenta de Microsoft; esto lo evita.
    // BitLocker sigue disponible para activarlo a mano.
    static void NoBitLocker(string mount) =>
        Hive(Path.Combine(mount, "Windows", "System32", "config", "SYSTEM"), "VIGIA_SY", k =>
            Reg($@"{k}\ControlSet001\Control\BitLocker", "PreventDeviceEncryption", 1));

    static void NoAds(string mount)
    {
        Hive(Path.Combine(mount, "Windows", "System32", "config", "SOFTWARE"), "VIGIA_SW", k =>
        {
            Reg($@"{k}\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1);
            Reg($@"{k}\Policies\Microsoft\Windows\CloudContent", "DisableConsumerAccountStateContent", 1);
        });
        Hive(Path.Combine(mount, "Users", "Default", "NTUSER.DAT"), "VIGIA_DU", k =>
        {
            var cdm = $@"{k}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            foreach (var v in new[] { "ContentDeliveryAllowed", "OemPreInstalledAppsEnabled", "PreInstalledAppsEnabled", "SilentInstalledAppsEnabled",
                                      "SubscribedContent-310093Enabled", "SubscribedContent-314559Enabled", "SubscribedContent-338387Enabled",
                                      "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SubscribedContent-353698Enabled", "SystemPaneSuggestionsEnabled" })
                Reg(cdm, v, 0);
            Reg($@"{k}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_IrisRecommendations", 0);
        });
    }

    static void Hive(string file, string name, Action<string> body)
    {
        var (e, o) = Tweaks.Run("reg.exe", $"load HKLM\\{name} \"{file}\"");
        if (e != 0) throw new Exception($"No pude abrir el registro de la imagen: {o.Trim()}");
        try { body($@"HKLM\{name}"); }
        finally
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            for (var i = 0; i < 10 && Tweaks.Run("reg.exe", $"unload HKLM\\{name}").exit != 0; i++) Thread.Sleep(500);
        }
    }

    static void Reg(string key, string value, int data)
    {
        var (e, o) = Tweaks.Run("reg.exe", $"add \"{key}\" /v \"{value}\" /t REG_DWORD /d {data} /f");
        if (e != 0) throw new Exception($"No pude escribir {value}: {o.Trim()}");
    }

    // ---------- respuesta automática ----------
    /// <summary>Solo lo que se pidió. Nunca particiona ni borra discos: esa pantalla la sigue viendo quien instala.</summary>
    internal static string? Unattend(Options o)
    {
        if (LabUnattend is not null) return LabUnattend;
        if (!o.LocalAccount && !o.SkipChecks && !o.NoTelemetry) return null;
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        sb.AppendLine("""<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">""");
        const string comp = """processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" """;
        if (o.SkipChecks)
        {
            sb.AppendLine($"""  <settings pass="windowsPE"><component name="Microsoft-Windows-Setup" {comp}><RunSynchronous>""");
            var i = 1;
            foreach (var v in new[] { "BypassTPMCheck", "BypassSecureBootCheck", "BypassRAMCheck" })
                sb.AppendLine($"""    <RunSynchronousCommand wcm:action="add"><Order>{i++}</Order><Path>reg add HKLM\SYSTEM\Setup\LabConfig /v {v} /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>""");
            sb.AppendLine("  </RunSynchronous></component></settings>");
        }
        if (o.LocalAccount)
        {
            var user = SecurityElement.Escape(o.User!.Trim());
            sb.AppendLine($"""  <settings pass="oobeSystem"><component name="Microsoft-Windows-Shell-Setup" {comp}>""");
            sb.AppendLine("""    <OOBE><HideOnlineAccountScreens>true</HideOnlineAccountScreens><HideLocalAccountScreen>true</HideLocalAccountScreen><ProtectYourPC>3</ProtectYourPC></OOBE>""");
            sb.AppendLine($"""    <UserAccounts><LocalAccounts><LocalAccount wcm:action="add"><Name>{user}</Name><DisplayName>{user}</DisplayName><Group>Administrators</Group><Password><Value></Value><PlainText>true</PlainText></Password></LocalAccount></LocalAccounts></UserAccounts>""");
            sb.AppendLine("  </component></settings>");
        }
        else if (o.NoTelemetry)
        {
            // Sin cuenta local: solo se salta la pantalla de privacidad (con todo apagado); la cuenta la elige quien instala.
            sb.AppendLine($"""  <settings pass="oobeSystem"><component name="Microsoft-Windows-Shell-Setup" {comp}>""");
            sb.AppendLine("""    <OOBE><ProtectYourPC>3</ProtectYourPC></OOBE>""");
            sb.AppendLine("  </component></settings>");
        }
        sb.AppendLine("</unattend>");
        return sb.ToString();
    }

    // ---------- DISM ----------
    static (int exit, string output) Dism(string args, Action<double>? pct, int timeoutS = 3600)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "dism.exe"), args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi)!;
        _proc = p;
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        var sb = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        var buf = new char[512]; var line = new StringBuilder();
        int n;
        while ((n = p.StandardOutput.Read(buf, 0, buf.Length)) > 0)
        {
            lock (sb) sb.Append(buf, 0, n);
            line.Append(buf, 0, n);
            var s = line.ToString();
            var ms = PctRx().Matches(s);
            if (ms.Count > 0 && pct is not null && double.TryParse(ms[^1].Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) pct(v);
            if (line.Length > 4096) line.Remove(0, line.Length - 256);
        }
        if (!p.WaitForExit(timeoutS * 1000)) { try { p.Kill(true); } catch { } return (-1, sb + "\nError: tardó demasiado."); }
        _proc = null;
        return (p.ExitCode, sb.ToString());
    }
    [GeneratedRegex(@"(\d{1,3}\.\d)%")] private static partial Regex PctRx();

    // ---------- montar la ISO sin letra (no aparece en el Explorador ni salta la reproducción automática) ----------
    static string Mount(string iso)
    {
        var q = iso.Replace("'", "''");
        var (e, o) = Tweaks.Run("powershell.exe", $"-NoProfile -NonInteractive -Command \"$i = Mount-DiskImage -ImagePath '{q}' -Access ReadOnly -NoDriveLetter -PassThru -ErrorAction Stop; (Get-Volume -DiskImage $i).UniqueId\"");
        var root = o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(@"\\?\Volume", StringComparison.Ordinal));
        if (e != 0 || root is null) throw new Exception($"No pude abrir la ISO: {o.Trim().Split('\n').FirstOrDefault()}");
        return root;
    }

    static void Unmount(string iso) =>
        Tweaks.Run("powershell.exe", $"-NoProfile -NonInteractive -Command \"Dismount-DiskImage -ImagePath '{iso.Replace("'", "''")}' | Out-Null\"");

    static void CopyFile(string src, string dst, Action<long> step)
    {
        using var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var w = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        var b = new byte[4 << 20]; int n; long acc = 0;
        while ((n = i.Read(b, 0, b.Length)) > 0)
        {
            w.Write(b, 0, n); acc += n;
            if (acc >= 64 << 20) { step(acc); acc = 0; Check(); }
        }
        step(acc);
    }

    // ---------- IMAPI2: escribir la ISO ----------
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateStreamOnFileEx(string file, uint mode, uint attr, bool create, IStream? template, out IStream stream);

    /// <summary>UDF 1.02 como las ISO de Microsoft: arranca en BIOS (etfsboot.com) y en UEFI (efisys.bin).</summary>
    static void WriteIso(string dir, string output, Action<double> pct)
    {
        Exception? err = null;
        var t = new Thread(() =>
        {
            try
            {
                dynamic fsi = Activator.CreateInstance(Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage", true)!)!;
                fsi.FileSystemsToCreate = 4;   // UDF
                fsi.UDFRevision = 0x102;
                fsi.FreeMediaBlocks = 0;       // sin límite de tamaño
                fsi.VolumeName = "VIGIA_WIN";
                var boots = new List<object>();
                foreach (var (file, platform) in new[] { (Path.Combine(dir, "boot", "etfsboot.com"), 0), (Path.Combine(dir, "efi", "microsoft", "boot", "efisys.bin"), 0xEF) })
                {
                    if (!File.Exists(file)) continue;
                    SHCreateStreamOnFileEx(file, 0x20 /* STGM_SHARE_DENY_WRITE */, 0, false, null, out var st);
                    dynamic bo = Activator.CreateInstance(Type.GetTypeFromProgID("IMAPI2FS.BootOptions", true)!)!;
                    bo.AssignBootImage(st);
                    bo.PlatformId = platform;
                    bo.Emulation = 0;
                    boots.Add(bo);
                }
                if (boots.Count == 0) throw new Exception("La ISO no trae los archivos de arranque.");
                fsi.BootImageOptionsArray = boots.ToArray();
                fsi.Root.AddTree(dir, false);
                dynamic res = fsi.CreateResultImage();
                var img = (IStream)res.ImageStream;
                long size = (long)res.TotalBlocks * (int)res.BlockSize, done = 0;
                using var w = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                var b = new byte[4 << 20]; var read = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    while (true)
                    {
                        Check();
                        img.Read(b, b.Length, read);
                        var n = Marshal.ReadInt32(read);
                        if (n <= 0) break;
                        w.Write(b, 0, n); done += n;
                        pct(100.0 * done / size);
                    }
                }
                finally { Marshal.FreeHGlobal(read); }
                if (done != size) throw new Exception($"La ISO quedó incompleta ({done} de {size} bytes).");
            }
            catch (Exception e) { err = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start(); t.Join();
        if (err is OperationCanceledException) throw err;
        if (err is not null) throw new Exception($"No pude crear la ISO: {err.Message}");
    }
}
