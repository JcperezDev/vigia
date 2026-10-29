using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Puente;

/// <summary>
/// Dónde vive cada cosa.
///
///   %ProgramFiles%\Vigia\                    solo SYSTEM y Administradores escriben; los usuarios leen y ejecutan
///       Puente.exe                           el instalador (se actualiza solo, nunca a una versión más vieja)
///       estado.json                          versión activa, anterior, la más nueva vista y el piso de seguridad
///       diario.json                          en qué paso va una instalación (para seguir o deshacer tras un corte)
///       versiones\0.21.0\                    cada versión en su carpeta: nunca se pisa una que esté abierta
///       versiones\0.21.0\.vigia-archivos.json  la huella de cada archivo, para reparar y volver atrás
///       .descargas\                          lo que se baja, antes de comprobar la firma
///   %LOCALAPPDATA%\Vigia\                   tus ajustes, historial y copias (no cambia de sitio)
///   %ProgramData%\Vigia\registros\puente.log  lo que hizo Puente (solo administradores escriben)
///
/// La instalación vieja estaba en %LOCALAPPDATA%\Programs\Vigia (+ Vigia-Puente): se migra y luego se quita.
/// </summary>
static class Layout
{
    // ---------------------------------------------------------------- modo de prueba (solo compilando con PUENTE_TEST; nunca sale de %TEMP%\vigia-test-xxxxxxxx)

    static readonly Regex TestLeaf = new(@"^vigia-test-[0-9a-f]{8}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Las pruebas instalan en carpetas de %TEMP% llamadas vigia-test-xxxxxxxx. Cualquier otra ruta se ignora, así que
    /// una variable de entorno no puede llevar a Puente (con permisos) a escribir ni borrar en otro sitio.
    /// </summary>
    static string? TestDir(string var)
    {
#if !PUENTE_TEST
        _ = var;
        return null;   // la Puente que se publica no tiene modo de prueba: ninguna variable cambia dónde instala
#else
        var v = Environment.GetEnvironmentVariable(var);
        if (string.IsNullOrWhiteSpace(v)) return null;
        try
        {
            var full = Path.GetFullPath(v).TrimEnd('\\');
            var tmp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
            if (!string.Equals(Path.GetDirectoryName(full), tmp, StringComparison.OrdinalIgnoreCase)) return null;
            return TestLeaf.IsMatch(Path.GetFileName(full)) ? full : null;
        }
        catch { return null; }
#endif
    }

    public static readonly string? TestRoot = TestDir("PUENTE_TEST_ROOT");
    public static readonly string? TestLocal = TestDir("PUENTE_TEST_LOCAL");
    public static bool Test => TestRoot is not null;

    // ---------------------------------------------------------------- instalación nueva

    /// <summary>%ProgramFiles%\Vigia, sin depender del idioma ni de la letra de la unidad.</summary>
    public static string Root => TestRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Vigia");
    public static string PuenteExe => Path.Combine(Root, "Puente.exe");
    public static string Versions => Path.Combine(Root, "versiones");
    public static string Downloads => Path.Combine(Root, ".descargas");
    public static string StateFile => Path.Combine(Root, "estado.json");
    public static string JournalFile => Path.Combine(Root, "diario.json");
    public static string VersionDir(Version v) => Path.Combine(Versions, v.ToString(3));
    public static string StagingDir(Version v) => Path.Combine(Versions, v.ToString(3) + ".preparando");
    public static string ManifestOf(string versionDir) => Path.Combine(versionDir, ".vigia-archivos.json");

    public static string LogDir => Test ? Path.Combine(Root, "_registros")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vigia", "registros");

    // ---------------------------------------------------------------- instalación vieja (de la persona del escritorio)

    /// <summary>%LOCALAPPDATA% de la persona que usa el PC (no la de la cuenta que dio el permiso).</summary>
    public static string UserLocal => TestLocal ?? Native.ShellLocalAppData()
        ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string LegacyPrograms => Path.Combine(UserLocal, "Programs");
    public static string LegacyApp => Path.Combine(LegacyPrograms, "Vigia");
    public static string LegacyHome => Path.Combine(LegacyPrograms, "Vigia-Puente");
    /// <summary>Las carpetas que dejaba la instalación vieja. Solo estas se pueden borrar al terminar la migración.</summary>
    public static string[] LegacyDirs => [LegacyApp, Path.Combine(LegacyPrograms, "Vigia.anterior"), Path.Combine(LegacyPrograms, "Vigia.nueva"),
        Path.Combine(LegacyPrograms, "Vigia.cambio"), LegacyHome];
    public static string UserData => Path.Combine(UserLocal, "Vigia");

    public static bool IsLegacyDir(string p) => LegacyDirs.Any(d => Same(d, p));

    public static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static bool Under(string path, string dir)
    {
        var d = Path.GetFullPath(dir).TrimEnd('\\') + '\\';
        return Path.GetFullPath(path).StartsWith(d, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>estado.json: lo que Puente sabe de la instalación. Vive en la carpeta protegida; aun así se lee con cuidado.</summary>
sealed class State
{
    public Version? Current, Previous, Highest, Floor;
    public string? Legacy;                 // versión vieja de la que se migró (solo informativo)
    public List<string> PendingDelete = [];  // carpetas viejas que estaban en uso y se borran en otra pasada

    static Version? V(JsonNode? n) => n?.GetValueKind() == System.Text.Json.JsonValueKind.String && Version.TryParse((string?)n, out var v) ? Program.Norm(v) : null;

    public static State Load()
    {
        var s = new State();
        try
        {
            var f = new FileInfo(Layout.StateFile);
            if (!f.Exists || f.Length > 64 * 1024 || (f.Attributes & FileAttributes.ReparsePoint) != 0) return s;
            if (JsonNode.Parse(File.ReadAllText(f.FullName)) is not JsonObject o) return s;
            s.Current = V(o["current"]); s.Previous = V(o["previous"]); s.Highest = V(o["highest"]); s.Floor = V(o["floor"]);
            s.Legacy = (string?)o["legacy"] is { Length: <= 32 } l ? l : null;
            if (o["pendingDelete"] is JsonArray a)
                foreach (var x in a.Take(16))
                    if ((string?)x is { Length: > 3 and < 400 } p && Layout.IsLegacyDir(p)) s.PendingDelete.Add(p);   // solo carpetas viejas conocidas
        }
        catch { }
        return s;
    }

    public void Save()
    {
        var o = new JsonObject
        {
            ["schema"] = 1,
            ["current"] = Current?.ToString(3), ["previous"] = Previous?.ToString(3),
            ["highest"] = Highest?.ToString(3), ["floor"] = Floor?.ToString(3), ["legacy"] = Legacy,
            ["pendingDelete"] = new JsonArray(PendingDelete.Select(p => (JsonNode?)p).ToArray()),
        };
        Program.AtomicWrite(Layout.StateFile, o.ToJsonString(new() { WriteIndented = true }));
    }

    /// <summary>Lo más viejo que se acepta instalar: el piso que compiló Puente, el que trajo un manifiesto firmado y la última instalada.</summary>
    public Version MinAllowed => new[] { Program.MinVigia, Floor ?? Program.MinVigia }.Max()!;
}

/// <summary>diario.json: «voy por este paso». Si se va la luz, la siguiente vez Puente sabe qué terminar o deshacer.</summary>
static class Journal
{
    public static void Write(string op, Version target, Version? from, string phase) =>
        Program.AtomicWrite(Layout.JournalFile, new JsonObject
        {
            ["op"] = op, ["target"] = target.ToString(3), ["from"] = from?.ToString(3), ["phase"] = phase,
            ["at"] = DateTime.UtcNow.ToString("o"),
        }.ToJsonString());

    public static (string Op, Version Target, Version? From, string Phase)? Read()
    {
        try
        {
            if (!File.Exists(Layout.JournalFile)) return null;
            if (JsonNode.Parse(File.ReadAllText(Layout.JournalFile)) is not JsonObject o) return null;
            if (!Version.TryParse((string?)o["target"], out var t)) return null;
            Version? f = Version.TryParse((string?)o["from"], out var ff) ? ff : null;
            return ((string?)o["op"] ?? "", t, f, (string?)o["phase"] ?? "");
        }
        catch { return null; }
    }

    public static void Clear() { try { File.Delete(Layout.JournalFile); } catch { } }
}

/// <summary>Permisos NTFS de la instalación: quién puede escribir, y comprobarlo de verdad (enlaces, herencia, dueño).</summary>
static class Acl
{
    static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
    static readonly SecurityIdentifier AllPackages = new("S-1-15-2-1");
    static readonly SecurityIdentifier CreatorOwner = new(WellKnownSidType.CreatorOwnerSid, null);

    const FileSystemRights WriteMask = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    static bool Trusted(SecurityIdentifier s) => s.Equals(Admins) || s.Equals(System) || s.Equals(TrustedInstaller);

    /// <summary>
    /// Deja la carpeta con permisos propios (sin heredar de arriba): SYSTEM y Administradores control total;
    /// Usuarios y las apps empaquetadas solo leer y ejecutar. Dueño: Administradores. Todo lo de dentro hereda esto.
    /// </summary>
    /// <summary>
    /// Candado de la instalación: un archivo dentro de la carpeta abierto en exclusiva mientras Puente instala, repara,
    /// vuelve atrás o quita. Solo SYSTEM y Administradores pueden abrirlo (ni leerlo), así que un usuario normal no
    /// puede quedárselo para trabar a Puente. null = otro Puente lo tiene.
    /// </summary>
    public static FileStream? Lock(string dir)
    {
        var sec = new FileSecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        sec.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, AccessControlType.Allow));
        if (Layout.Test && WindowsIdentity.GetCurrent().User is { } me)
            sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
        var fi = new FileInfo(Path.Combine(dir, ".ocupado"));
        try
        {
            var fs = fi.Create(FileMode.OpenOrCreate, FileSystemRights.ReadData | FileSystemRights.WriteData | FileSystemRights.Delete
                | FileSystemRights.ReadPermissions | FileSystemRights.ChangePermissions | FileSystemRights.Synchronize,
                FileShare.None, 4096, FileOptions.DeleteOnClose, sec);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) { fs.Dispose(); throw new InvalidOperationException($"{fi.FullName} es un enlace."); }
            try { fs.SetAccessControl(sec); } catch { }   // si quedó de antes con otros permisos, vuelve a los suyos
            return fs;
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) is 32 or 33) { return null; }   // compartido / bloqueado
    }

    public static void Protect(string dir)
    {
        var di = new DirectoryInfo(dir);
        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"{dir} es un enlace; no se instala ahí.");
        var sec = new DirectorySecurity();
        sec.SetOwner(Admins);
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags All = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, All, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(AllPackages, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, All, PropagationFlags.None, AccessControlType.Allow));
        di.SetAccessControl(sec);
    }

    /// <summary>
    /// Todo lo de dentro vuelve a heredar solo de la carpeta (sin permisos sueltos) y con dueño Administradores.
    /// Un archivo con permisos propios, o que dejó otra cuenta, deja de tenerlos.
    /// </summary>
    public static void ResetChildren(string dir)
    {
        // la carpeta misma también: quien es dueño de algo siempre puede reescribir sus permisos, y según la directiva
        // «Propietario predeterminado de objetos creados por administradores» (o con UAC apagado) el dueño sería tu usuario
        var top = new DirectoryInfo(dir);
        if ((top.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"{dir} es un enlace; no se acepta dentro de la instalación.");
        var ts = top.GetAccessControl(); ts.SetOwner(Admins); top.SetAccessControl(ts);
        foreach (var fsi in top.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            if ((fsi.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"{fsi.FullName} es un enlace; no se acepta dentro de la instalación.");
            if (fsi is DirectoryInfo d)
            {
                var s = new DirectorySecurity(); s.SetOwner(Admins); s.SetAccessRuleProtection(false, false);
                d.SetAccessControl(s);
            }
            else
            {
                var f = (FileInfo)fsi;
                var s = new FileSecurity(); s.SetOwner(Admins); s.SetAccessRuleProtection(false, false);
                f.SetAccessControl(s);
            }
        }
    }

    static HashSet<SecurityIdentifier> Untrusted()
    {
        var set = new HashSet<SecurityIdentifier>
        {
            Users, new(WellKnownSidType.AuthenticatedUserSid, null), new(WellKnownSidType.WorldSid, null),
            new(WellKnownSidType.InteractiveSid, null), AllPackages,
        };
        if (Native.ShellUser() is { } u) set.Add(u);   // la persona del escritorio
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            if (id.User is { } me && !Trusted(me)) set.Add(me);
            foreach (var g in id.Groups ?? new IdentityReferenceCollection())
                if (g is SecurityIdentifier s && !Trusted(s)) set.Add(s);
        }
        catch { }
        return set;
    }

    /// <summary>null si nadie fuera de SYSTEM/Administradores puede cambiar nada de la carpeta; si no, el motivo.</summary>
    public static string? Problem(string dir, int maxFiles = 8000)
    {
        try
        {
            var root = new DirectoryInfo(Path.GetFullPath(dir));
            if (!root.Exists) return "no existe";
            for (var d = root; d is not null; d = d.Parent)
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) return $"{d.FullName} es un enlace";
            var all = new List<FileSystemInfo>();
            foreach (var fsi in root.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false }))
            {
                if (all.Count >= maxFiles) return "demasiados archivos";
                if ((fsi.Attributes & FileAttributes.ReparsePoint) != 0) return $"{fsi.FullName} es un enlace";
                all.Add(fsi);
            }
            var bad = Untrusted();
            if (Check(root.GetAccessControl(), bad, root.FullName) is { } p) return p;
            foreach (var fsi in all)
            {
                FileSystemSecurity sec = fsi is DirectoryInfo di ? di.GetAccessControl() : ((FileInfo)fsi).GetAccessControl();
                if (Check(sec, bad, fsi.FullName) is { } q) return q;
            }
            return null;
        }
        catch (Exception e) { return "no se pudo leer: " + e.Message; }
    }

    static string? Check(FileSystemSecurity sec, HashSet<SecurityIdentifier> bad, string what)
    {
        if (sec.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && !Trusted(owner)) return $"{what}: dueño {owner}";
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (r.AccessControlType != AccessControlType.Allow) continue;
            if ((r.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            var s = (SecurityIdentifier)r.IdentityReference;
            if (s.Equals(CreatorOwner)) continue;
            if (bad.Contains(s) && (r.FileSystemRights & WriteMask) != 0) return $"{what}: {s} puede escribir";
        }
        return null;
    }
}
