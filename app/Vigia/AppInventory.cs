using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace Vigia;

/// <summary>Un paquete AppX/MSIX tal como lo ve Windows. Me: instalado en tu usuario. Others: cuántos otros usuarios lo tienen
/// (solo el número: los nombres no salen de aquí). Provisioned: Windows lo instala a cada usuario nuevo.</summary>
public sealed record AppPkg(
    string Name, string FullName, string Family, string Version, string Arch, string Publisher, string Signature,
    bool Framework, bool NonRemovable, bool Resource, bool Bundle, string Status, string? Location, string[] Deps,
    bool Me, int Others, bool Provisioned, string? ProvName, string? Display = null, string? PublisherName = null);

/// <summary>Un programa clásico de los registrados en «Desinstalar programas». Key: clave del registro (sin ruta personal).</summary>
public sealed record Win32App(
    string Key, string Name, string? Publisher, string? Version, string Scope, string Arch, string? MsiCode, bool HasUninstaller,
    string? Location, long SizeKb);

/// <summary>Lo que se leyó. Ok = false es un fallo de lectura, que no es lo mismo que «no hay apps».</summary>
public sealed record AppInventoryData(
    bool Ok, string? Error, bool AllUsers, bool ProvisionedOk, List<AppPkg> Packages, List<Win32App> Win32,
    List<string> Warnings, DateTime At, bool StoreBlocked, bool Managed);

/// <summary>Todo lo que toca Windows pasa por aquí, para poder probar el resto con un falso (sin quitar nada real).</summary>
public interface IAppHost
{
    AppInventoryData Scan(CancellationToken ct);
    /// <summary>Quita el paquete de tu usuario. state: removed, gone (ya no estaba), protected, failed, timeout, cancelled.</summary>
    (string State, string? Error) RemoveForUser(string fullName, CancellationToken ct);
    (string State, string? Error) Deprovision(string provName, CancellationToken ct);
    (string State, string? Error) Register(string manifest, CancellationToken ct);
    /// <summary>Procesos abiertos cuyo ejecutable está dentro de esa carpeta (solo nombres). No cierra nada.</summary>
    List<string> RunningUnder(string? folder);
    bool FileExists(string path);
    bool Win32Present(string key);
}

/// <summary>Ejecuta PowerShell con un guion fijo (-EncodedCommand) y los parámetros por variables de entorno, nunca pegados
/// al texto de la orden. La respuesta va en una línea marcada; avisos y errores van aparte y no rompen el JSON.</summary>
static class Ps
{
    public const string Mark = "<<VIGIA-JSON>>";

    public sealed record Result(int Exit, string Out, string Err, bool TimedOut, bool Cancelled);

    const string Prelude = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $WarningPreference='SilentlyContinue'; " +
        "$InformationPreference='SilentlyContinue'; [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)\n";

    public static Result Run(string script, IDictionary<string, string>? env, TimeSpan timeout, CancellationToken ct)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Prelude + script));
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            "-NoProfile -NonInteractive -NoLogo -EncodedCommand " + encoded)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
        };
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var se = p.StandardError.ReadToEndAsync(CancellationToken.None);
        var sw = Stopwatch.StartNew();
        bool timedOut = false, cancelled = false;
        while (!p.WaitForExit(200))
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            if (sw.Elapsed > timeout) { timedOut = true; break; }
        }
        if (timedOut || cancelled) { try { p.Kill(true); } catch { } }
        Task.WaitAll([so, se], 5000);
        return new(timedOut || cancelled ? -1 : p.ExitCode, so.IsCompletedSuccessfully ? so.Result : "", se.IsCompletedSuccessfully ? se.Result : "", timedOut, cancelled);
    }

    /// <summary>El objeto JSON de la línea marcada. Lo de antes (avisos, basura) se ignora; si no hay o está roto, null.</summary>
    public static JsonObject? Payload(string? stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return null;
        var i = stdout.LastIndexOf(Mark, StringComparison.Ordinal);
        if (i < 0) return null;
        var s = stdout[(i + Mark.Length)..].Trim();
        try { return JsonNode.Parse(s, documentOptions: new JsonDocumentOptions { MaxDepth = 32 }) as JsonObject; }
        catch (JsonException) { return null; }
    }

    /// <summary>Primera línea útil de un error de PowerShell, sin la ruta del guion ni el ruido de «At line».</summary>
    public static string Short(string? err)
    {
        var line = (err ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("At ") && !l.StartsWith("+") && !l.StartsWith("En línea")) ?? "";
        return line.Length > 300 ? line[..300] + "…" : line;
    }
}

/// <summary>Lectura real de Windows. Solo lee, salvo las tres operaciones de quitar/aprovisionar/registrar, que reciben
/// un nombre ya validado y lo pasan por variable de entorno.</summary>
public sealed partial class WindowsAppHost : IAppHost
{
    // Lista de paquetes de todos los usuarios (si se puede) + aprovisionados. Las enumeraciones van como texto (en PowerShell 5.1
    // ConvertTo-Json las deja como números). Cada lista se arma con @() para que 0 o 1 elemento sigan siendo una lista.
    const string ScanScript = """
        $me = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $out = [ordered]@{ allUsers = $false; provOk = $false; errors = @(); packages = @(); provisioned = @() }
        try { $pk = @(Get-AppxPackage -AllUsers); $out.allUsers = $true }
        catch { $out.errors += ('allusers: ' + $_.Exception.Message); $pk = @(Get-AppxPackage) }
        $list = foreach ($p in $pk) {
          $mine = $false; $others = 0
          if ($out.allUsers) {
            foreach ($u in @($p.PackageUserInformation)) {
              $sid = ''; try { $sid = [string]$u.UserSecurityId.Sid } catch { }
              $st = [string]$u.InstallState
              if ($st -ne 'Installed') { continue }
              if ($sid -eq $me) { $mine = $true } else { $others++ }
            }
          } else { $mine = $true }
          [ordered]@{
            name = [string]$p.Name; full = [string]$p.PackageFullName; family = [string]$p.PackageFamilyName; ver = [string]$p.Version
            arch = [string]$p.Architecture; pub = [string]$p.Publisher; sig = [string]$p.SignatureKind
            fw = [bool]$p.IsFramework; nr = [bool]$p.NonRemovable; res = [bool]$p.IsResourcePackage; bundle = [bool]$p.IsBundle
            status = [string]$p.Status; loc = [string]$p.InstallLocation; deps = @($p.Dependencies | ForEach-Object { [string]$_.Name })
            me = $mine; others = $others
          }
        }
        $out.packages = @($list)
        try {
          $out.provisioned = @(Get-AppxProvisionedPackage -Online | ForEach-Object { [ordered]@{ name = [string]$_.DisplayName; pkg = [string]$_.PackageName; ver = [string]$_.Version } })
          $out.provOk = $true
        } catch { $out.errors += ('provisioned: ' + $_.Exception.Message) }
        Write-Output ('<<VIGIA-JSON>>' + ($out | ConvertTo-Json -Depth 6 -Compress))
        """;

    // Quita solo de tu usuario el paquete exacto. Antes vuelve a comprobar que no es framework, ni no removible, ni del sistema.
    const string RemoveScript = """
        $full = $env:VIGIA_PKG
        $p = @(Get-AppxPackage | Where-Object { $_.PackageFullName -eq $full }) | Select-Object -First 1
        if (-not $p) { Write-Output '<<VIGIA-JSON>>{"state":"gone"}'; exit 0 }
        if ($p.IsFramework -or $p.NonRemovable -or ([string]$p.SignatureKind -eq 'System')) { Write-Output '<<VIGIA-JSON>>{"state":"protected"}'; exit 0 }
        try { Remove-AppxPackage -Package $full -ErrorAction Stop }
        catch { Write-Output ('<<VIGIA-JSON>>' + (@{ state = 'failed'; error = $_.Exception.Message; hr = ('0x{0:X8}' -f $_.Exception.HResult) } | ConvertTo-Json -Compress)); exit 0 }
        $still = @(Get-AppxPackage | Where-Object { $_.PackageFullName -eq $full }).Count -gt 0
        if ($still) { Write-Output '<<VIGIA-JSON>>{"state":"failed","error":"Windows dice que la quitó, pero sigue instalada."}' } else { Write-Output '<<VIGIA-JSON>>{"state":"removed"}' }
        """;

    const string DeprovisionScript = """
        $n = $env:VIGIA_PKG
        $p = @(Get-AppxProvisionedPackage -Online | Where-Object { $_.PackageName -eq $n }) | Select-Object -First 1
        if (-not $p) { Write-Output '<<VIGIA-JSON>>{"state":"gone"}'; exit 0 }
        try { Remove-AppxProvisionedPackage -Online -PackageName $n -ErrorAction Stop | Out-Null }
        catch { Write-Output ('<<VIGIA-JSON>>' + (@{ state = 'failed'; error = $_.Exception.Message; hr = ('0x{0:X8}' -f $_.Exception.HResult) } | ConvertTo-Json -Compress)); exit 0 }
        $still = @(Get-AppxProvisionedPackage -Online | Where-Object { $_.PackageName -eq $n }).Count -gt 0
        if ($still) { Write-Output '<<VIGIA-JSON>>{"state":"failed","error":"Sigue aprovisionada."}' } else { Write-Output '<<VIGIA-JSON>>{"state":"removed"}' }
        """;

    const string RegisterScript = """
        $m = $env:VIGIA_MANIFEST
        try { Add-AppxPackage -DisableDevelopmentMode -Register $m -ErrorAction Stop }
        catch { Write-Output ('<<VIGIA-JSON>>' + (@{ state = 'failed'; error = $_.Exception.Message } | ConvertTo-Json -Compress)); exit 0 }
        Write-Output '<<VIGIA-JSON>>{"state":"registered"}'
        """;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._~\-]{2,250}$")] internal static partial Regex PkgRx();

    public AppInventoryData Scan(CancellationToken ct)
    {
        var warnings = new List<string>();
        var r = Ps.Run(ScanScript, null, TimeSpan.FromSeconds(90), ct);
        List<AppPkg> pkgs = [];
        bool ok = false, all = false, prov = false; string? error = null;
        if (r.Cancelled) error = "Cancelado.";
        else if (r.TimedOut) error = "Windows tardó más de 90 s en dar la lista de apps.";
        else if (Ps.Payload(r.Out) is not { } o) error = "No pude leer la lista de apps de Windows." + (Ps.Short(r.Err) is { Length: > 0 } e ? " " + e : "");
        else
        {
            (pkgs, all, prov) = AppParse.Packages(o, warnings);
            ok = true;
            foreach (var p in pkgs.ToList()) if (p.Location is { Length: > 0 })
            {
                var (d, pn) = AppManifest.Names(p.Location, p.Name, p.FullName);
                if (d is not null || pn is not null) pkgs[pkgs.IndexOf(p)] = p with { Display = d, PublisherName = pn };
            }
        }
        List<Win32App> w32 = [];
        try { w32 = Win32Reader.Read(); } catch (Exception ex) { warnings.Add("Programas clásicos: " + ex.Message); }
        return new(ok, error, all, prov, pkgs, w32, warnings, DateTime.Now, Policies.StoreBlocked(), Policies.Managed());
    }

    static (string, string?) Act(string script, string envKey, string value, CancellationToken ct, int seconds)
    {
        var r = Ps.Run(script, new Dictionary<string, string> { [envKey] = value }, TimeSpan.FromSeconds(seconds), ct);
        if (r.Cancelled) return ("cancelled", null);
        if (r.TimedOut) return ("timeout", $"tardó más de {seconds} s");
        if (Ps.Payload(r.Out) is not { } o) return ("failed", Ps.Short(r.Err) is { Length: > 0 } e ? e : $"código {r.Exit}");
        var st = AppParse.Str(o, "state") ?? "failed";
        var err = AppParse.Str(o, "error");
        if (AppParse.Str(o, "hr") is { } hr) err = $"{err} ({hr})";
        return (st, err);
    }

    public (string State, string? Error) RemoveForUser(string fullName, CancellationToken ct) =>
        PkgRx().IsMatch(fullName) ? Act(RemoveScript, "VIGIA_PKG", fullName, ct, 180) : ("failed", "Nombre de paquete no válido.");

    public (string State, string? Error) Deprovision(string provName, CancellationToken ct) =>
        PkgRx().IsMatch(provName) ? Act(DeprovisionScript, "VIGIA_PKG", provName, ct, 300) : ("failed", "Nombre de paquete no válido.");

    public (string State, string? Error) Register(string manifest, CancellationToken ct) =>
        AppRecovery.SafeManifest(manifest) ? Act(RegisterScript, "VIGIA_MANIFEST", manifest, ct, 300) : ("failed", "Esa ruta no es la de un paquete de Windows.");

    public List<string> RunningUnder(string? folder)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(folder)) return res;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = ImagePath(p);
                if (path is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) res.Add(p.ProcessName);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return res.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr h, uint flags, [Out] char[] buf, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    /// <summary>Ruta del ejecutable con el permiso mínimo (consultar, no leer memoria): no abre ni toca el proceso.</summary>
    static string? ImagePath(Process p)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, p.Id);
        if (h == IntPtr.Zero) return null;
        try { var buf = new char[1024]; var n = buf.Length; return QueryFullProcessImageName(h, 0, buf, ref n) ? new string(buf, 0, n) : null; }
        finally { CloseHandle(h); }
    }

    public bool FileExists(string path) => File.Exists(path);
    public bool Win32Present(string key) => Win32Reader.Read().Any(w => w.Key == key);
}

/// <summary>Convierte la respuesta de PowerShell en registros. Nunca lanza: lo que falta queda vacío, lo raro se salta.</summary>
public static class AppParse
{
    public static string? Str(JsonObject? o, string k)
    {
        try { return o?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null; } catch { return null; }
    }
    static bool Bool(JsonObject o, string k) { try { return o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b; } catch { return false; } }
    static int Int(JsonObject o, string k) { try { return o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0; } catch { return 0; } }
    static IEnumerable<JsonObject> Objs(JsonNode? n) => n is JsonArray a ? a.OfType<JsonObject>() : n is JsonObject o ? [o] : [];
    static string[] Strs(JsonNode? n) => n is JsonArray a ? a.Select(x => { try { return x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null; } catch { return null; } }).OfType<string>().Where(s => s.Length > 0).ToArray()
        : n is JsonValue v1 && v1.TryGetValue<string>(out var one) && one.Length > 0 ? [one] : [];

    public static (List<AppPkg> Pkgs, bool AllUsers, bool ProvOk) Packages(JsonObject o, List<string> warnings)
    {
        var all = Bool(o, "allUsers"); var provOk = Bool(o, "provOk");
        foreach (var e in Strs(o["errors"])) warnings.Add(e);
        var prov = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Objs(o["provisioned"]))
            if (Str(p, "name") is { Length: > 0 } n && Str(p, "pkg") is { Length: > 0 } pk && WindowsAppHost.PkgRx().IsMatch(n)) prov[n] = pk;
        var byFull = new Dictionary<string, AppPkg>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Objs(o["packages"]))
        {
            var name = Str(p, "name"); var full = Str(p, "full");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(full) || !WindowsAppHost.PkgRx().IsMatch(full) || !WindowsAppHost.PkgRx().IsMatch(name)) continue;   // sin identidad válida: no se lista
            var me = Bool(p, "me"); var others = Int(p, "others");
            if (byFull.TryGetValue(full, out var dup)) { byFull[full] = dup with { Me = dup.Me || me, Others = Math.Max(dup.Others, others) }; continue; }
            byFull[full] = new AppPkg(name, full, Str(p, "family") ?? "", Str(p, "ver") ?? "", Str(p, "arch") ?? "", Str(p, "pub") ?? "",
                Str(p, "sig") ?? "", Bool(p, "fw"), Bool(p, "nr"), Bool(p, "res"), Bool(p, "bundle"), Str(p, "status") ?? "",
                Str(p, "loc") is { Length: > 0 } l ? l : null, Strs(p["deps"]), me, others, prov.ContainsKey(name), prov.GetValueOrDefault(name));
        }
        var list = byFull.Values.ToList();
        // aprovisionadas que nadie tiene instaladas todavía: también salen (solo se pueden desaprovisionar)
        foreach (var (n, pk) in prov)
            if (!list.Any(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) && WindowsAppHost.PkgRx().IsMatch(pk))
                list.Add(new AppPkg(n, pk, "", pk.Split('_').ElementAtOrDefault(1) ?? "", "", "", "Store", false, false, false, false, "Ok", null, [], false, 0, true, pk));
        return (list, all, provOk);
    }
}

/// <summary>Nombre visible y editor desde el AppxManifest.xml del paquete (los «ms-resource:» se resuelven con Windows).</summary>
static class AppManifest
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int SHLoadIndirectString(string src, StringBuilder buf, int cap, IntPtr reserved);

    public static (string? Display, string? Publisher) Names(string folder, string name, string full)
    {
        try
        {
            var f = Path.Combine(folder, "AppxManifest.xml");
            if (!File.Exists(f)) return (null, null);
            var x = new XmlDocument { XmlResolver = null };
            using (var r = XmlReader.Create(f, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 })) x.Load(r);
            var ns = new XmlNamespaceManager(x.NameTable); ns.AddNamespace("m", x.DocumentElement!.NamespaceURI);
            string? Read(string xp) => x.SelectSingleNode(xp, ns)?.InnerText?.Trim();
            return (Resolve(Read("/m:Package/m:Properties/m:DisplayName"), name, full), Resolve(Read("/m:Package/m:Properties/m:PublisherDisplayName"), name, full));
        }
        catch { return (null, null); }
    }

    static string? Resolve(string? s, string name, string full)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!s.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return s.Length > 120 ? s[..120] : s;
        var rest = s["ms-resource:".Length..];
        var uri = rest.StartsWith("//") ? "ms-resource:" + rest : rest.StartsWith('/') ? $"ms-resource://{name}{rest}" : $"ms-resource://{name}/Resources/{rest}";
        var sb = new StringBuilder(512);
        if (SHLoadIndirectString($"@{{{full}?{uri}}}", sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0) return sb.ToString();
        sb.Clear();
        return SHLoadIndirectString($"@{{{full}?ms-resource://{name}/{rest}}}", sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0 ? sb.ToString() : null;
    }
}

/// <summary>Programas clásicos: claves Uninstall de HKLM (64 y 32 bits) y HKCU. Solo lee; nunca ejecuta UninstallString.</summary>
public static partial class Win32Reader
{
    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")] internal static partial Regex GuidRx();
    const string Sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<Win32App> Read()
    {
        var res = new List<Win32App>();
        void From(RegistryHive hive, RegistryView view, string scope, string arch)
        {
            using var b = RegistryKey.OpenBaseKey(hive, view);
            using var u = b.OpenSubKey(Sub);
            if (u is null) return;
            foreach (var k in u.GetSubKeyNames())
            {
                try
                {
                    using var e = u.OpenSubKey(k);
                    if (e is null) continue;
                    var row = Entry(k, scope, arch, n => e.GetValue(n));
                    if (row is not null) res.Add(row);
                }
                catch { }
            }
        }
        From(RegistryHive.LocalMachine, RegistryView.Registry64, "machine", "x64");
        if (Environment.Is64BitOperatingSystem) From(RegistryHive.LocalMachine, RegistryView.Registry32, "machine", "x86");
        From(RegistryHive.CurrentUser, RegistryView.Default, "user", "");
        // el mismo programa puede salir en dos vistas: uno por nombre + versión + ámbito
        return res.GroupBy(w => (w.Name.ToLowerInvariant(), w.Version, w.Scope)).Select(g => g.First()).OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Una entrada del registro como programa visible, o null si es una actualización, un componente oculto o no tiene nombre.</summary>
    internal static Win32App? Entry(string keyName, string scope, string arch, Func<string, object?> get)
    {
        string? S(string n) => get(n) is string s && s.Trim().Length > 0 ? s.Trim() : null;
        var name = S("DisplayName");
        if (name is null) return null;
        if (get("SystemComponent") is int sc && sc == 1) return null;
        if (S("ParentKeyName") is not null) return null;
        if (S("ReleaseType") is { } rt && Regex.IsMatch(rt, "Update|Hotfix|Security", RegexOptions.IgnoreCase)) return null;
        var msi = get("WindowsInstaller") is int wi && wi == 1 && GuidRx().IsMatch(keyName) ? keyName.ToUpperInvariant() : null;
        long size = get("EstimatedSize") is int es ? es : 0;
        var key = $"{scope}:{arch}:{keyName}";
        return new Win32App(key, name.Length > 160 ? name[..160] : name, S("Publisher"), S("DisplayVersion"), scope, arch, msi,
            S("UninstallString") is not null || msi is not null, S("InstallLocation"), size);
    }
}

/// <summary>Políticas de empresa que cambian lo que se puede hacer. Solo se leen; Vigía no las salta.</summary>
static class Policies
{
    static object? Val(string path, string name)
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(path); return k?.GetValue(name); } catch { return null; }
    }
    public static bool StoreBlocked() =>
        Val(@"SOFTWARE\Policies\Microsoft\WindowsStore", "RemoveWindowsStore") is int a && a == 1
        || Val(@"SOFTWARE\Policies\Microsoft\WindowsStore", "DisableStoreApps") is int b && b == 1;
    /// <summary>¿Lo administra una organización? (unido a un dominio o con la política de quitar apps de Microsoft activa)</summary>
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetGetJoinInformation(string? server, out IntPtr name, out int status);
    [DllImport("netapi32.dll")] static extern int NetApiBufferFree(IntPtr buf);

    public static bool Managed()
    {
        try
        {
            // 3 = NetSetupDomainName: unido a un dominio. No consulta la red.
            if (NetGetJoinInformation(null, out var buf, out var st) == 0) { NetApiBufferFree(buf); if (st == 3) return true; }
        }
        catch { }
        return Val(@"SOFTWARE\Policies\Microsoft\Windows\Appx\RemoveDefaultMicrosoftStorePackages", "Enabled") is int e && e == 1;
    }
}
