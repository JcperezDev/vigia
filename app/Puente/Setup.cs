using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Puente;

/// <summary>
/// Instalar, actualizar, migrar, reparar, volver atrás y quitar Vigía en %ProgramFiles%\Vigia.
///
/// Cómo se evita que algo a medias rompa Vigía:
///  - cada versión va en su carpeta (versiones\x.y.z); nunca se escribe encima de archivos que puedan estar abiertos;
///  - lo bajado se guarda en la carpeta protegida (.descargas) y se comprueba ahí: manifiesto firmado (versión, tamaño,
///    SHA-256 de cada pieza) + la firma de cada archivo. Ningún usuario sin permisos puede cambiarlo después;
///  - la versión nueva se prepara aparte (x.y.z.preparando), se le ponen los permisos, se comprueba que arranca
///    (como tu usuario, sin permisos) y solo entonces se cambia estado.json, que es un único archivo que se escribe
///    de una vez: o queda la versión vieja o la nueva, nunca una mezcla;
///  - diario.json dice en qué paso se iba: si se va la luz, la próxima vez se termina o se deshace;
///  - la versión anterior se queda para volver a ella; lo más viejo se borra cuando nadie lo tiene abierto.
/// </summary>
static partial class Program
{
    /// <summary>Lo más viejo que este Puente instala: la primera Vigía que corre sin permisos y vive en Program Files.</summary>
    internal static readonly Version MinVigia = new(0, 21, 0);
    const string ManifestName = "vigia-manifest.json";
    const long MaxManifest = 64 * 1024, MaxPuente = 256L << 20, MaxPackage = 1L << 30;

    static bool Yes => _args.Contains("--yes");

    internal static void AtomicWrite(string path, string text)
    {
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(Encoding.UTF8.GetBytes(text));
            fs.Flush(flushToDisk: true);   // que esté en el disco antes de ponerlo en su sitio
        }
        File.Move(tmp, path, overwrite: true);
    }

    // ---------------------------------------------------------------- manifiesto firmado

    internal sealed record PkgFile(string Name, long Size, string Sha256);
    internal sealed record Manifest(Version Version, Version? MinVersion, PkgFile Package, PkgFile? Puente, Version? PuenteVersion);

    static readonly Regex HexRx = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Comprueba la firma del manifiesto (sobre sus bytes exactos) y después cada campo. Un manifiesto firmado pero de
    /// otro producto, con nombres raros o sin huellas no vale.
    /// </summary>
    internal static Manifest ParseManifest(byte[] bytes, string sigB64)
    {
        if (bytes.Length is 0 or > (int)MaxManifest) throw new InvalidOperationException("el manifiesto de la versión no es válido.");
        if (!VerifyBytes(bytes, sigB64)) throw new InvalidOperationException("la firma del manifiesto no coincide. No viene de la publicación de Vigía; no se instaló nada.");
        if (JsonNode.Parse(bytes) is not JsonObject o || (int?)o["schema"] != 1 || (string?)o["product"] != "Vigia")
            throw new InvalidOperationException("el manifiesto de la versión no es de Vigía.");
        static Version Ver(JsonNode? n, string what) => n?.GetValueKind() == System.Text.Json.JsonValueKind.String
            && Version.TryParse((string?)n, out var v) && v.Revision <= 0 ? Norm(v)
            : throw new InvalidOperationException($"el manifiesto no trae {what}.");
        var ver = Ver(o["version"], "la versión");
        Version? min = o["minVersion"] is null ? null : Ver(o["minVersion"], "la versión mínima");
        PkgFile Piece(JsonNode? n, string expected)
        {
            long size = 0; string? h = null;
            var ok = n is JsonObject f && (string?)f["name"] == expected
                && f["size"]?.GetValueKind() == System.Text.Json.JsonValueKind.Number && (size = (long)f["size"]!) is > 0 and <= MaxPackage
                && (h = f["sha256"]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? (string?)f["sha256"] : null) is not null && HexRx.IsMatch(h);
            if (!ok) throw new InvalidOperationException($"el manifiesto no describe bien {expected}.");
            return new PkgFile(expected, size, h!.ToUpperInvariant());
        }
        var pkg = Piece(o["package"], $"Vigia-{ver.ToString(3)}-win-x64.zip");
        PkgFile? pu = o["puente"] is JsonObject ? Piece(o["puente"], "Puente.exe") : null;
        Version? puv = pu is null ? null : Ver(o["puente"]!["version"], "la versión de Puente");
        if (min is not null && min > ver) throw new InvalidOperationException("el manifiesto pide una versión mínima mayor que la suya.");
        return new Manifest(ver, min, pkg, pu, puv);
    }

    /// <summary>Para publicar: escribe el manifiesto de una carpeta con el zip y Puente.exe (lo firma luego --sign).</summary>
    static int MakeManifest(string dir, string version, string? minVersion)
    {
        var v = Norm(Version.Parse(version));
        JsonObject F(string name, Version? fileVer = null)
        {
            var p = Path.Combine(dir, name);
            var o = new JsonObject { ["name"] = name, ["size"] = new FileInfo(p).Length, ["sha256"] = Sha256(p) };
            if (fileVer is not null) o["version"] = fileVer.ToString(3);
            return o;
        }
        var m = new JsonObject
        {
            ["schema"] = 1, ["product"] = "Vigia", ["version"] = v.ToString(3),
            ["package"] = F($"Vigia-{v.ToString(3)}-win-x64.zip"),
        };
        if (minVersion is { Length: > 0 })
        {
            var min = Norm(Version.Parse(minVersion));
            if (min > v) { Console.Error.WriteLine($"La versión mínima {min.ToString(3)} no puede ser más nueva que {v.ToString(3)}."); return 1; }
            m["minVersion"] = min.ToString(3);
        }
        var pu = Path.Combine(dir, "Puente.exe");
        if (File.Exists(pu)) m["puente"] = F("Puente.exe", FileVersion(pu));
        File.WriteAllText(Path.Combine(dir, ManifestName), m.ToJsonString(new() { WriteIndented = true }));
        Console.WriteLine(Path.Combine(dir, ManifestName));
        return 0;
    }

    /// <summary>Para publicar: comprueba un manifiesto firmado y que cada pieza de su carpeta coincide.</summary>
    static int VerifyManifest(string file)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        try
        {
            var m = ParseManifest(File.ReadAllBytes(file), File.ReadAllText(file + ".sig"));
            int bad = 0;
            foreach (var f in new[] { m.Package, m.Puente }.OfType<PkgFile>())
            {
                var p = Path.Combine(dir, f.Name);
                var ok = File.Exists(p) && new FileInfo(p).Length == f.Size && Sha256(p) == f.Sha256 && File.Exists(p + ".sig") && Verify(p, File.ReadAllText(p + ".sig"));
                if (!ok) bad++;
                Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {f.Name} {f.Size}");
            }
            Console.WriteLine($"manifiesto {m.Version} (mínima {m.MinVersion?.ToString(3) ?? "-"}, Puente {m.PuenteVersion?.ToString(3) ?? "-"}): {(bad == 0 ? "bien" : bad + " fallos")}");
            return bad == 0 ? 0 : 1;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e.Message); return 1; }
    }

    static int PrintState()
    {
        var st = State.Load();
        var cur = st.Current is { } c ? Layout.VersionDir(c) : null;
        Console.WriteLine(new JsonObject
        {
            ["root"] = Layout.Root, ["current"] = st.Current?.ToString(3), ["previous"] = st.Previous?.ToString(3),
            ["highest"] = st.Highest?.ToString(3), ["floor"] = st.Floor?.ToString(3), ["legacy"] = st.Legacy,
            ["journal"] = Journal.Read()?.Phase,
            ["filesOk"] = cur is not null && FilesProblem(cur) is null,
            ["aclProblem"] = Directory.Exists(Layout.Root) ? Acl.Problem(Layout.Root) : "no existe",
            ["legacyFound"] = LegacyVersion()?.ToString(3),
        }.ToJsonString());
        return 0;
    }

    /// <summary>Prueba los permisos en una carpeta temporal: protegida, con un permiso suelto, con un enlace. No toca la instalación.</summary>
    static int AclTest()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "vigia-acltest-" + Guid.NewGuid().ToString("N")[..8]);
        int fails = 0;
        void Case(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {what}"); }
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "versiones", "0.21.0"));
            File.WriteAllText(Path.Combine(tmp, "versiones", "0.21.0", "Vigia.exe"), "x");
            Case(Acl.Problem(tmp) is not null, "carpeta temporal sin proteger: se detecta");
            Acl.Protect(tmp); Acl.ResetChildren(tmp);
            Case(Acl.Problem(tmp) is null, "protegida: solo SYSTEM y Administradores escriben");
            var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
            var f = new FileInfo(Path.Combine(tmp, "versiones", "0.21.0", "Vigia.exe"));
            var fs = f.GetAccessControl();
            fs.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(users, System.Security.AccessControl.FileSystemRights.Modify, System.Security.AccessControl.AccessControlType.Allow));
            f.SetAccessControl(fs);
            Case(Acl.Problem(tmp) is { } p1 && p1.Contains("Vigia.exe"), "un archivo con «Usuarios: modificar» se detecta");
            Acl.ResetChildren(tmp);
            Case(Acl.Problem(tmp) is null, "rehacer permisos lo quita");
            var link = Path.Combine(tmp, "versiones", "enlace");
            Directory.CreateSymbolicLink(link, Path.GetTempPath());
            Case(Acl.Problem(tmp) is { } p2 && p2.Contains("enlace"), "un enlace dentro se detecta");
            var threw = false;
            try { Acl.ResetChildren(tmp); } catch (InvalidOperationException) { threw = true; }
            Case(threw, "rehacer permisos no sigue enlaces (se niega)");
            Directory.Delete(link);
            var j = Path.Combine(Path.GetTempPath(), "vigia-acltest-j-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateSymbolicLink(j, tmp);
            threw = false;
            try { Acl.Protect(j); } catch (InvalidOperationException) { threw = true; }
            Case(threw, "no se instala en una carpeta que es un enlace");
            Directory.Delete(j);
        }
        catch (Exception e) { fails++; Console.WriteLine("FAIL " + e.Message); }
        finally { try { DeleteTree(tmp, quiet: true); } catch { } }
        Console.WriteLine(fails == 0 ? "acl-test: todo bien" : $"acl-test: {fails} fallos");
        return fails == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- de dónde salen los archivos

    /// <summary>GitHub o una carpeta (instalación sin conexión). Todo se copia primero a la carpeta protegida.</summary>
    sealed class Source(HttpClient? http, Release? rel, string? offline)
    {
        public Version? Tag => rel?.Version;

        public async Task<string> Fetch(string name, long max, bool quiet = true, long? size = null)
        {
            Directory.CreateDirectory(Layout.Downloads);
            var dest = Path.Combine(Layout.Downloads, name);
            if (offline is not null)
            {
                var src = new FileInfo(Path.Combine(offline, name));
                if (!src.Exists) throw new InvalidOperationException($"falta {name} en {offline}.");
                if (src.Length > max) throw new InvalidOperationException($"{name} es más grande de lo normal.");
                File.Copy(src.FullName, dest, overwrite: true);   // se comprueba la copia, no el original
                return dest;
            }
            var a = rel!.Asset(name);
            if (a.Size > max) throw new InvalidOperationException($"{name} es más grande de lo normal.");
            if (size is { } s && a.Size != s) throw new InvalidOperationException($"{name} no tiene el tamaño que dice el manifiesto.");
            await Download(http!, a, dest, quiet);
            return dest;
        }

        public bool Has(string name) => offline is not null ? File.Exists(Path.Combine(offline, name)) : rel!.Assets.Any(x => x.Name == name);
    }

    static async Task<(Manifest M, Source Src)> Open(HttpClient http, string? offline)
    {
        Source src;
        if (offline is not null) src = new Source(null, null, Path.GetFullPath(offline));
        else src = new Source(http, await Net(() => Latest(http)), null);
        if (!src.Has(ManifestName))
            throw new InvalidOperationException("esta versión no trae el manifiesto firmado que pide este Puente. No se instaló nada.");
        var mf = await src.Fetch(ManifestName, MaxManifest);
        var sf = await src.Fetch(ManifestName + ".sig", 4096);
        var m = ParseManifest(File.ReadAllBytes(mf), File.ReadAllText(sf));
        if (src.Tag is { } tag && tag != m.Version)
            throw new InvalidOperationException($"la versión publicada ({tag}) no coincide con su manifiesto ({m.Version}).");
        return (m, src);
    }

    /// <summary>Baja (o copia) una pieza y comprueba tamaño, SHA-256 del manifiesto y su firma propia.</summary>
    static async Task<string> FetchVerified(Source src, PkgFile f, bool quiet)
    {
        var path = await src.Fetch(f.Name, f.Size, quiet, f.Size);
        var sig = await src.Fetch(f.Name + ".sig", 4096);
        if (new FileInfo(path).Length != f.Size || !string.Equals(Sha256(path), f.Sha256, StringComparison.OrdinalIgnoreCase) || !Verify(path, File.ReadAllText(sig)))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException($"{f.Name} no coincide con la publicación firmada; no se instaló nada.");
        }
        return path;
    }

    // ---------------------------------------------------------------- instalar / actualizar / migrar

    static async Task<int> InstallFlow(int? waitPid, string? offline, bool repair)
    {
        Ui.Show("Buscando actualizaciones…", "Mirando la última versión publicada.", double.NaN);
        PrepareRoot();
        using var busy = Exclusive();
        var st = State.Load();
        Recover(st);
        CleanPending(st);

        using var http = Http();
        var (m, src) = await Open(http, offline);
        if (m.Version < st.MinAllowed)
            throw new InvalidOperationException($"la versión {m.Version} es más vieja que la mínima segura ({st.MinAllowed}); no se instala.");
        if (st.Highest is { } hi && m.Version < hi)
            throw new InvalidOperationException($"se ofreció la {m.Version}, más vieja que la {hi} que ya se instaló aquí. Un manifiesto viejo no puede volver atrás.");
        if (m.MinVersion is { } mv && (st.Floor is null || mv > st.Floor))
        {
            // lo que dice un manifiesto bien firmado sobre versiones inseguras se guarda ya, aunque la instalación
            // falle después: si no, un manifiesto viejo podría volver a meter una versión que ya se sabe insegura
            st.Floor = mv; st.Save();
        }

        await PlacePuente(m, src, st);

        var legacy = LegacyVersion();
        var cur = st.Current;
        var curDir = cur is null ? null : Layout.VersionDir(cur);
        var curOk = curDir is not null && Directory.Exists(curDir) && FilesProblem(curDir) is null;

        if (cur == m.Version && curOk && !repair)
        {
            // al día; aun así se termina una migración que quedó a medias y se dejan bien accesos e inicio
            Activate(st, m.Version, migrated: legacy);
            st.Save();
            Log($"al día ({m.Version})");
            if (waitPid is not null) { CloseVigia(waitPid); LaunchVigia(); return 0; }
            if (!Ui.Active || Ui.Choose("Vigía está al día", $"Tienes la versión {m.Version}, que es la última.", "Abrir Vigía", "Cerrar") == 0) { if (Ui.Active) LaunchVigia(); }
            return 0;
        }

        var from = cur ?? legacy;
        Ui.Show(from is null ? $"Instalando Vigía {m.Version}" : $"Actualizando a Vigía {m.Version}",
            from is null ? "Preparando la descarga." : $"Tienes la {from}.", double.NaN);
        SpaceCheck(m.Package.Size);

        Journal.Write("install", m.Version, cur, "descarga");
        var zip = await FetchVerified(src, m.Package, quiet: false);

        Journal.Write("install", m.Version, cur, "preparando");
        Ui.Show($"Preparando Vigía {m.Version}", "Descomprimiendo y revisando cada archivo.", double.NaN);
        var stage = Layout.StagingDir(m.Version);
        DeleteTree(stage);
        SafeExtract(zip, stage);
        var got = FileVersion(Path.Combine(stage, "Vigia.exe"));
        if (got != m.Version) throw new InvalidOperationException($"el paquete dice ser {m.Version} pero trae {got?.ToString() ?? "nada"}.");
        WriteFileList(stage);
        Acl.ResetChildren(stage);
        if (Acl.Problem(stage) is { } why) throw new InvalidOperationException("la carpeta nueva no quedó protegida (" + why + ").");

        var target = Layout.VersionDir(m.Version);
        var aside = Aside(target);
        if (Directory.Exists(aside)) DeleteTree(aside, quiet: true);   // de un corte viejo que Recover ya no necesitaba
        if (Directory.Exists(target))
        {
            // la misma versión (reparar): se aparta y se guarda hasta que la nueva pase la prueba;
            // si está abierta, primero se cierra Vigía
            if (cur == m.Version) CloseVigia(waitPid);
            Journal.Write("install", m.Version, cur, "apartando");
            MoveDir(target, aside);
        }
        MoveDir(stage, target);

        Journal.Write("install", m.Version, cur, "verificando");
        Ui.Show($"Comprobando Vigía {m.Version}", "Viendo que abre bien antes de cambiar nada.", double.NaN);
        if (HealthCheck(target) is { } bad)
        {
            Log("la versión nueva no pasó la prueba: " + bad);
            if (Directory.Exists(aside)) { DeleteTree(target, quiet: true); MoveDir(aside, target); }   // vuelve tal cual estaba
            else if (cur != m.Version) DeleteTree(target, quiet: true);
            Journal.Clear();
            throw new InvalidOperationException("la versión nueva no arrancó bien en este PC, así que se dejó la que tenías.");
        }

        CloseVigia(waitPid);
        Ui.Lock(true);
        Journal.Write("install", m.Version, cur, "activando");
        if (cur is not null && cur != m.Version) st.Previous = cur;
        st.Current = m.Version;
        if (st.Highest is null || m.Version > st.Highest) st.Highest = m.Version;
        st.Save();                                   // el cambio de verdad: un archivo, de una vez
        Activate(st, m.Version, migrated: legacy);
        st.Save();
        Journal.Clear();
        Ui.Lock(false);
        if (Directory.Exists(aside)) DeleteTree(aside, quiet: true);
        CleanOldVersions(st);
        try { File.Delete(zip); } catch { }

        Log($"instalada {m.Version} (antes {from?.ToString() ?? "ninguna"}{(legacy is not null ? ", migrada desde la carpeta vieja" : "")})");
        Ui.Show($"Listo: Vigía {m.Version}", legacy is not null
            ? "Ahora vive en Archivos de programa. Tus ajustes y copias siguen donde estaban."
            : st.Previous is { } p ? $"La {p} quedó guardada por si quieres volver a ella." : "Tienes accesos en el Escritorio y en Inicio.", 1.0);
        if (Ui.Active || waitPid is not null) LaunchVigia();
        if (Ui.Active) Thread.Sleep(2500);
        return 0;
    }

    /// <summary>Crea %ProgramFiles%\Vigia con sus permisos, o los arregla si alguien los cambió.</summary>
    static void PrepareRoot()
    {
        var root = Layout.Root;
        for (var d = new DirectoryInfo(Path.GetDirectoryName(root)!); d is not null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0 && !Layout.Test)
                throw new InvalidOperationException($"{d.FullName} es un enlace; no se instala ahí.");
        var fresh = !Directory.Exists(root);
        if (!fresh && (new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"{root} es un enlace; no se instala ahí.");
        if (!fresh && !File.Exists(Layout.StateFile) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException($"ya hay una carpeta {root} que no es de Puente. No se toca: muévela o bórrala tú y vuelve a intentarlo.");
        Directory.CreateDirectory(root);
        if (fresh || Acl.Problem(root) is not null)
        {
            Acl.Protect(root);
            Acl.ResetChildren(root);
            if (Acl.Problem(root) is { } why) throw new InvalidOperationException("no pude dejar protegida la carpeta de Vigía (" + why + ").");
            if (!fresh) Log("permisos de la carpeta de Vigía rehechos");
        }
        if (!Layout.Test)
        {
            // registros: solo administradores escriben (lo que diga el registro no da permisos a nada, pero que no lo cambie cualquiera)
            var pd = Path.GetDirectoryName(Layout.LogDir)!;
            Directory.CreateDirectory(Layout.LogDir);
            if (Acl.Problem(pd, 200) is not null) { try { Acl.Protect(pd); } catch (Exception e) { Log("permisos de ProgramData: " + e.Message); } }
        }
    }

    static void SpaceCheck(long pkg)
    {
        try
        {
            var free = new DriveInfo(Path.GetPathRoot(Layout.Root)!).AvailableFreeSpace;
            if (free < pkg * 5) throw new InvalidOperationException($"falta espacio en el disco: se necesitan unos {Mb(pkg * 5)} MB libres.");
        }
        catch (ArgumentException) { }
    }

    /// <summary>Uno a la vez: dos Puentes cargarían cada uno su estado y se pisarían diario, descargas y versiones.</summary>
    static FileStream Exclusive() => Acl.Lock(Layout.Root)
        ?? throw new InvalidOperationException("ya hay otro Puente instalando, reparando o quitando Vigía. Espera a que termine y vuelve a intentarlo.");

    /// <summary>Dónde se guarda la versión que se está reparando mientras se prueba la nueva.</summary>
    internal static string Aside(string versionDir) => versionDir + ".apartada";

    /// <summary>Si la vez anterior se cortó, se termina o se deshace según en qué paso iba.</summary>
    static void Recover(State st)
    {
        if (Journal.Read() is not { } j) return;
        Log($"diario pendiente: {j.Op} {j.Target} en «{j.Phase}»");
        var dir = Layout.VersionDir(j.Target);
        var aside = Aside(dir);
        if (Directory.Exists(aside) && (j.Phase != "activando" || FilesProblem(dir) is not null))
        {
            // reparación cortada antes de terminar: vuelve la carpeta que había (la nueva no llegó a pasar la prueba)
            if (Directory.Exists(dir)) DeleteTree(dir, quiet: true);
            if (!Directory.Exists(dir)) { MoveDir(aside, dir); Log($"la {j.Target} que se estaba reparando volvió a su sitio"); }
        }
        else if (Directory.Exists(aside)) DeleteTree(aside, quiet: true);
        if (j.Phase == "activando" && st.Current == j.Target)
        {
            if (FilesProblem(dir) is null) { Activate(st, j.Target, migrated: LegacyVersion()); st.Save(); }
            else if (st.Previous is { } p && p >= st.MinAllowed && FilesProblem(Layout.VersionDir(p)) is null)   // nunca por debajo del mínimo seguro
            {
                st.Current = p; st.Previous = null; st.Save(); Activate(st, p, migrated: null); st.Save();
                Log($"la {j.Target} quedó mal tras el corte; se volvió a la {p}");
            }
        }
        else if (st.Current != j.Target && st.Previous != j.Target)
        {
            DeleteTree(dir, quiet: true);   // nunca llegó a estar activa
        }
        DeleteTree(Layout.StagingDir(j.Target), quiet: true);
        Journal.Clear();
    }

    /// <summary>Puente se pone en Program Files desde una copia comprobada; nunca baja a una versión más vieja.</summary>
    static async Task PlacePuente(Manifest m, Source src, State st)
    {
        var me = Environment.ProcessPath!;
        var target = Layout.PuenteExe;
        var mine = Layout.Same(me, target);
        var have = File.Exists(target) ? FileVersion(target) : null;

        if (m.Puente is null || m.PuenteVersion is null)
        {
            if (!File.Exists(target)) { File.Copy(me, target); Log("Puente copiado a " + target); }
            return;
        }
        if (have is not null && have >= m.PuenteVersion)
        {
            if (have > m.PuenteVersion) Log($"Puente instalado ({have}) es más nuevo que el publicado ({m.PuenteVersion}); se queda");
            return;   // igual o más nuevo: no se cambia (nunca hacia atrás)
        }
        if (_args.Contains("--relaunched") && mine) return;

        var tmp = await FetchVerified(src, m.Puente, quiet: true);
        if (FileVersion(tmp) != m.PuenteVersion) { File.Delete(tmp); throw new InvalidOperationException("Puente.exe no trae la versión que dice su manifiesto."); }
        var old = Path.Combine(Layout.Root, "Puente.old.exe");
        try { if (File.Exists(old)) File.Delete(old); } catch { }
        if (File.Exists(target)) File.Move(target, old);   // un .exe abierto se puede renombrar, no sobrescribir
        File.Move(tmp, target);
        Log($"Puente {have?.ToString() ?? "nuevo"} -> {m.PuenteVersion}");
        if (mine && !Layout.Test)
        {
            // este proceso es el Puente viejo: sigue el nuevo (carga sus piezas por la ruta del .exe)
            var psi = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Layout.Root };
            foreach (var a in _args.Where(x => x != "--relaunched")) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--relaunched");
            Ui.Close();
            Process.Start(psi)?.Dispose();
            Environment.Exit(0);
        }
    }

    /// <summary>Después de cambiar de versión: accesos, Apps, inicio con Windows y retirada de lo viejo. Se puede repetir.</summary>
    static void Activate(State st, Version v, Version? migrated)
    {
        var exe = Path.Combine(Layout.VersionDir(v), "Vigia.exe");
        try { Shortcuts(exe); } catch (Exception e) { Log("accesos: " + e.Message); }
        try { RegisterApp(exe, v); } catch (Exception e) { Log("registro en Apps: " + e.Message); }
        try { AutoStartMigrate(exe); } catch (Exception e) { Log("inicio con Windows: " + e.Message); }
        if (migrated is not null) { st.Legacy ??= migrated.ToString(3); RetireLegacy(st); }
    }

    // ---------------------------------------------------------------- integridad de una versión instalada

    const string ListName = ".vigia-archivos.json";

    static void WriteFileList(string dir)
    {
        var root = Path.GetFullPath(dir).TrimEnd('\\') + '\\';
        var arr = new JsonArray();
        foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }).Order(StringComparer.OrdinalIgnoreCase))
        {
            var rel = f[root.Length..];
            if (rel.Equals(ListName, StringComparison.OrdinalIgnoreCase)) continue;
            arr.Add((JsonNode)new JsonObject { ["p"] = rel, ["n"] = new FileInfo(f).Length, ["h"] = Sha256(f) });
        }
        File.WriteAllText(Path.Combine(dir, ListName), new JsonObject { ["schema"] = 1, ["files"] = arr }.ToJsonString());
    }

    /// <summary>null si cada archivo está como se instaló y no apareció ningún .exe/.dll de más; si no, el motivo.</summary>
    internal static string? FilesProblem(string dir)
    {
        try
        {
            var list = Path.Combine(dir, ListName);
            if (!File.Exists(list)) return "sin lista de archivos";
            if (JsonNode.Parse(File.ReadAllText(list))?["files"] is not JsonArray arr || arr.Count == 0) return "lista vacía";
            var root = Path.GetFullPath(dir).TrimEnd('\\') + '\\';
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in arr)
            {
                var rel = (string?)n?["p"] ?? "";
                var full = Path.GetFullPath(Path.Combine(root, rel));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "ruta rara en la lista";
                var fi = new FileInfo(full);
                if (!fi.Exists) return "falta " + rel;
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) return rel + " es un enlace";
                if (fi.Length != (long?)n!["n"] || !string.Equals(Sha256(full), (string?)n["h"], StringComparison.OrdinalIgnoreCase)) return "cambió " + rel;
                known.Add(rel);
            }
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var rel = f[root.Length..];
                if (known.Contains(rel) || rel.Equals(ListName, StringComparison.OrdinalIgnoreCase)) continue;
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".exe" or ".dll" or ".sys" or ".js" or ".html" or ".json" or ".ps1" or ".cmd" or ".bat") return "sobra " + rel;
            }
            return null;
        }
        catch (Exception e) { return "no se pudo revisar: " + e.Message; }
    }

    /// <summary>Abre la versión nueva con --install-check (como tu usuario, sin permisos) y espera que diga que está bien.</summary>
    static string? HealthCheck(string dir)
    {
        if (FilesProblem(dir) is { } p) return p;
        var exe = Path.Combine(dir, "Vigia.exe");
        if (Environment.GetEnvironmentVariable("PUENTE_TEST_SKIP_HEALTH") == "1" && Layout.Test) return null;
        int? code = Layout.Test ? null : Native.RunAsShellUser(exe, "--install-check", dir, 60_000);
        if (code is null)
        {
            // sin escritorio (o en pruebas): se abre directo; --install-check solo lee, no cambia nada
            using var proc = Process.Start(new ProcessStartInfo(exe, "--install-check") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir });
            if (proc is null) return "no abrió";
            if (!proc.WaitForExit(60_000)) { try { proc.Kill(); } catch { } return "no terminó la prueba"; }
            code = proc.ExitCode;
        }
        return code == 0 ? null : $"--install-check devolvió {code}";
    }

    // ---------------------------------------------------------------- volver atrás, reparar, quitar

    static int RollbackFlow()
    {
        PrepareRoot();
        using var busy = Exclusive();
        var st = State.Load();
        Recover(st);
        if (st.Previous is not { } prev || !Directory.Exists(Layout.VersionDir(prev)))
        {
            if (Ui.Active) Ui.Choose("No hay versión anterior", "Puente guarda la versión anterior cuando actualiza Vigía. Todavía no hay ninguna.", "Cerrar");
            return 1;
        }
        if (prev < st.MinAllowed)
        {
            if (Ui.Active) Ui.Choose("No se puede volver a esa versión", $"La {prev} tiene fallos de seguridad que ya se arreglaron. La mínima segura es la {st.MinAllowed}.", "Cerrar");
            Log($"vuelta atrás a {prev} rechazada: piso {st.MinAllowed}");
            return 2;
        }
        if (FilesProblem(Layout.VersionDir(prev)) is { } why)
        {
            if (Ui.Active) Ui.Choose("La versión anterior está dañada", "Sus archivos no son los que se instalaron, así que no se usa. Vigía sigue como estaba.", "Cerrar");
            Log($"vuelta atrás a {prev} rechazada: {why}");
            return 3;
        }
        var cur = st.Current;
        if (!Ask($"¿Volver a Vigía {prev}?", $"Ahora tienes la {cur}. Quedará guardada y podrás volver a ella.", "Volver")) return 0;
        Ui.Show($"Volviendo a Vigía {prev}", "Cerrando Vigía.", double.NaN);
        CloseVigia(null);
        Journal.Write("rollback", prev, cur, "activando");
        st.Previous = cur; st.Current = prev;
        st.Save();
        Activate(st, prev, migrated: null);
        st.Save();
        Journal.Clear();
        Log($"vuelta atrás {cur} -> {prev}");
        Ui.Show($"Listo: Vigía {prev}", $"La {cur} quedó guardada como anterior.", 1.0);
        if (Ui.Active) { LaunchVigia(); Thread.Sleep(2500); }
        return 0;
    }

    static int UninstallFlow()
    {
        if (!Ask("¿Quitar Vigía de este PC?", "Tus ajustes y las copias de lo que cambió Vigía se quedan en " + Layout.UserData + ".", "Quitar")) return 0;
        using var busy = Directory.Exists(Layout.Root) ? Exclusive() : null;
        Ui.Show("Quitando Vigía", "", double.NaN);
        CloseVigia(null);
        if (!Layout.Test) RemovePawnIfOurs();
        foreach (var l in LinkPaths()) try { if (File.Exists(l)) File.Delete(l); } catch { }
        foreach (var l in LegacyLinkPaths()) try { if (File.Exists(l)) File.Delete(l); } catch { }
        try { UninstallHive().DeleteSubKeyTree(UninstallKeyPath, false); } catch { }
        try { using var run = RunKey(writable: true); if (run?.GetValue("Vigia") is string c && IsVigiaCommand(c)) run.DeleteValue("Vigia", false); } catch { }
        RemoveLegacyTask();
        var st = State.Load();
        RetireLegacy(st);
        var me = Environment.ProcessPath!;
        // todo lo de Program Files menos este Puente (se borra solo al cerrar)
        foreach (var e in Directory.Exists(Layout.Root) ? Directory.EnumerateFileSystemEntries(Layout.Root).ToList() : [])
        {
            if (Layout.Same(e, me) || Path.GetFileName(e) == ".ocupado") continue;   // el candado se borra solo al cerrar
            if (Directory.Exists(e)) DeleteTree(e, quiet: true); else try { File.Delete(e); } catch { ScheduleDelete(e); }
        }
        Log("desinstalada");
        Ui.Show("Listo: Vigía quitada", "Puente se borra solo al cerrar esta ventana.", 1.0);
        if (Layout.Under(me, Layout.Root))
            Process.Start(new ProcessStartInfo(Native.Sys("cmd.exe"), $"/d /c timeout /t 3 /nobreak >nul & rmdir /s /q \"{Layout.Root}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })?.Dispose();
        if (Ui.Active) Thread.Sleep(2500);
        return 0;
    }

    // ---------------------------------------------------------------- instalación vieja (%LOCALAPPDATA%\Programs)

    static Version? LegacyVersion() => FileVersion(Path.Combine(Layout.LegacyApp, "Vigia.exe")) is { } v ? v
        : Directory.Exists(Layout.LegacyHome) ? new Version(0, 0, 0) : null;

    /// <summary>¿Alguna carpeta desde %LOCALAPPDATA% hasta esta es un enlace? Entonces no se borra nada ahí.</summary>
    static bool LinkOnTheWay(string dir)
    {
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (Layout.Same(d.FullName, Layout.UserLocal)) return false;
        }
        return true;   // no cuelga de %LOCALAPPDATA%: no es la carpeta vieja de verdad
    }

    /// <summary>
    /// Quita la instalación vieja: tarea de inicio elevada, accesos, entrada en Apps y carpetas. Las carpetas solo si
    /// nadie las tiene abiertas; si no, quedan anotadas y se borran la próxima vez. Nunca toca %LOCALAPPDATA%\Vigia.
    /// </summary>
    static void RetireLegacy(State st)
    {
        RemoveLegacyTask();
        foreach (var l in LegacyLinkPaths()) try { if (File.Exists(l)) File.Delete(l); } catch { }
        try { LegacyUninstallHive()?.DeleteSubKeyTree(UninstallKeyPath, false); } catch { }
        var me = Environment.ProcessPath!;
        foreach (var d in Layout.LegacyDirs)
        {
            if (!Directory.Exists(d)) { st.PendingDelete.RemoveAll(p => Layout.Same(p, d)); continue; }
            if (LinkOnTheWay(d)) { Log($"no se borra {d}: hay un enlace en el camino"); continue; }
            var busy = Layout.Under(me, d) || Native.Lockers(d).Count > 0;
            if (!busy && DeleteTree(d, quiet: true)) st.PendingDelete.RemoveAll(p => Layout.Same(p, d));
            else if (!st.PendingDelete.Any(p => Layout.Same(p, d))) st.PendingDelete.Add(d);
        }
    }

    static void CleanPending(State st)
    {
        if (st.PendingDelete.Count == 0 && !File.Exists(Path.Combine(Layout.Root, "Puente.old.exe"))) return;
        try { File.Delete(Path.Combine(Layout.Root, "Puente.old.exe")); } catch { }
        if (st.PendingDelete.Count > 0) { RetireLegacy(st); st.Save(); }
    }

    /// <summary>Deja la versión activa y la anterior; las demás se borran si nadie las usa.</summary>
    static void CleanOldVersions(State st)
    {
        if (!Directory.Exists(Layout.Versions)) return;
        foreach (var d in Directory.EnumerateDirectories(Layout.Versions))
        {
            var name = Path.GetFileName(d);
            if (Version.TryParse(name, out var v) && (Norm(v) == st.Current || Norm(v) == st.Previous)) continue;
            if (Native.Lockers(d).Count > 0) continue;
            DeleteTree(d, quiet: true);
        }
    }

    // ---------------------------------------------------------------- inicio con Windows

    const string LegacyTask = @"\Vigia\Iniciar Vigia";
    static string TestTaskFile => Path.Combine(Layout.UserLocal, "_tarea-vieja.xml");

    static RegistryKey? RunKey(bool writable)
    {
        if (Layout.Test) return Registry.CurrentUser.CreateSubKey($@"Software\Vigia-test\{Path.GetFileName(Layout.Root)}\Run", writable);
        var sid = Native.ShellUser()?.Value;
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        return sid is null ? Registry.CurrentUser.OpenSubKey(run, writable) : Registry.Users.OpenSubKey($@"{sid}\{run}", writable);
    }

    /// <summary>¿Es el comando de un Vigia.exe de Vigía (nuevo o viejo)? Así nunca se toca la entrada de otro programa.</summary>
    static bool IsVigiaCommand(string cmd)
    {
        var m = Regex.Match(cmd, "^\"([^\"]+)\"");
        var exe = m.Success ? m.Groups[1].Value : cmd.Split(' ')[0];
        if (!Path.GetFileName(exe).Equals("Vigia.exe", StringComparison.OrdinalIgnoreCase)) return false;
        try { return Layout.Under(exe, Layout.Root) || Layout.Under(exe, Layout.LegacyApp); } catch { return false; }
    }

    /// <summary>
    /// El inicio vuelve a ser una entrada de Run de tu usuario, sin permisos: si tenías la tarea vieja activa
    /// (la que abría Vigía como administrador) o una entrada de Run de Vigía, queda una entrada apuntando a la versión
    /// nueva. La tarea vieja se borra solo si es la de Vigía (nombre y programa).
    /// </summary>
    static void AutoStartMigrate(string exe)
    {
        var wanted = LegacyTaskEnabled();
        RemoveLegacyTask();
        using var run = RunKey(writable: true);
        if (run is null) return;
        if (run.GetValue("Vigia") is string c && IsVigiaCommand(c)) wanted = true;
        if (wanted) run.SetValue("Vigia", $"\"{exe}\" --tray");
    }

    static string? LegacyTaskXml()
    {
        // las pruebas no tocan el Programador de tareas del PC: la «tarea» es un XML exportado en su carpeta temporal
        if (Layout.Test) return File.Exists(TestTaskFile) ? File.ReadAllText(TestTaskFile) : null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Native.Sys("schtasks.exe"), $"/query /tn \"{LegacyTask}\" /xml")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.Unicode });
            if (p is null) return null;
            var xml = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15_000);
            return p.ExitCode == 0 ? xml : null;
        }
        catch { return null; }
    }

    static string? TaskExe(string xml) => Regex.Match(xml, @"<Command>\s*""?([^""<]+?)""?\s*</Command>") is { Success: true } m ? Environment.ExpandEnvironmentVariables(m.Groups[1].Value) : null;

    static bool LegacyTaskEnabled() => LegacyTaskXml() is { } x && TaskExe(x) is { } e && IsVigiaCommand($"\"{e}\"")
        && !Regex.IsMatch(x, @"<Enabled>\s*false\s*</Enabled>", RegexOptions.IgnoreCase);

    static void RemoveLegacyTask()
    {
        if (LegacyTaskXml() is not { } x || TaskExe(x) is not { } e || !IsVigiaCommand($"\"{e}\"")) return;   // solo la de Vigía
        if (Layout.Test) { File.Delete(TestTaskFile); Log("tarea de inicio vieja quitada (" + e + ")"); return; }
        using var p = Process.Start(new ProcessStartInfo(Native.Sys("schtasks.exe"), $"/delete /tn \"{LegacyTask}\" /f") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        p?.WaitForExit(15_000);
        Log("tarea de inicio vieja quitada (" + e + ")");
    }

    // ---------------------------------------------------------------- accesos y Apps

    static string CommonPrograms => Layout.Test ? Path.Combine(Layout.Root, "_accesos", "Inicio") : Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
    static string CommonDesktop => Layout.Test ? Path.Combine(Layout.Root, "_accesos", "Escritorio") : Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    static IEnumerable<string> LinkPaths() =>
    [
        Path.Combine(CommonDesktop, "Vigia.lnk"),
        Path.Combine(CommonPrograms, "Vigia.lnk"),
        Path.Combine(CommonPrograms, "Puente (actualizar Vigia).lnk"),
    ];

    /// <summary>Los accesos que creaba la instalación vieja en el Escritorio e Inicio de tu usuario.</summary>
    static IEnumerable<string> LegacyLinkPaths()
    {
        if (Layout.Test) return [Path.Combine(Layout.UserLocal, "_accesos-viejos", "Vigia.lnk"), Path.Combine(Layout.UserLocal, "_accesos-viejos", "Puente (actualizar Vigia).lnk")];
        var desk = Native.ShellFolder(Native.FolderDesktop) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var progs = Native.ShellFolder(Native.FolderPrograms) ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        return [Path.Combine(desk, "Vigia.lnk"), Path.Combine(progs, "Vigia.lnk"), Path.Combine(progs, "Puente (actualizar Vigia).lnk")];
    }

    /// <summary>Accesos para todos en Escritorio e Inicio. Con WScript.Shell desde PowerShell, oculto (Puente va recortado y sin COM).</summary>
    static void Shortcuts(string exe)
    {
        var dir = Path.GetDirectoryName(exe)!;
        var puente = Layout.PuenteExe;
        var l = LinkPaths().ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(l[0])!);
        Directory.CreateDirectory(Path.GetDirectoryName(l[1])!);
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var ps = "$s=New-Object -ComObject WScript.Shell;" +
                 $"foreach($l in @({Q(l[0])},{Q(l[1])})){{$k=$s.CreateShortcut($l);$k.TargetPath={Q(exe)};$k.WorkingDirectory={Q(dir)};$k.IconLocation={Q(exe + ",0")};$k.Description='Vigía';$k.Save()}};" +
                 (File.Exists(puente) ? $"$k=$s.CreateShortcut({Q(l[2])});$k.TargetPath={Q(puente)};$k.WorkingDirectory={Q(Layout.Root)};$k.Description='Instalar o actualizar Vigía';$k.Save()" : "");
        using var p = Process.Start(new ProcessStartInfo(Native.Sys("powershell.exe"), "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(ps)))
        { CreateNoWindow = true, UseShellExecute = false });
        p?.WaitForExit(30_000);
    }

    const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Vigia";

    static RegistryKey UninstallHive() => Layout.Test ? Registry.CurrentUser.CreateSubKey($@"Software\Vigia-test\{Path.GetFileName(Layout.Root)}") : Registry.LocalMachine;

    static RegistryKey? LegacyUninstallHive()
    {
        if (Layout.Test) return Registry.CurrentUser.CreateSubKey($@"Software\Vigia-test\{Path.GetFileName(Layout.Root)}\viejo");
        var sid = Native.ShellUser()?.Value;
        return sid is null ? Registry.CurrentUser : Registry.Users.OpenSubKey(sid, writable: true);
    }

    /// <summary>Configuración › Apps (para todo el PC) y se quita desde ahí con Puente.</summary>
    static void RegisterApp(string exe, Version v)
    {
        using var k = UninstallHive().CreateSubKey(UninstallKeyPath);
        var size = new DirectoryInfo(Path.GetDirectoryName(exe)!).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
        k.SetValue("DisplayName", "Vigía");
        k.SetValue("DisplayVersion", v.ToString(3));
        k.SetValue("Publisher", "Vigía");
        k.SetValue("DisplayIcon", exe + ",0");
        k.SetValue("InstallLocation", Layout.Root);
        k.SetValue("UninstallString", $"\"{Layout.PuenteExe}\" --uninstall");
        k.SetValue("QuietUninstallString", $"\"{Layout.PuenteExe}\" --uninstall --quiet --yes");
        k.SetValue("URLInfoAbout", "https://github.com/JcperezDev/vigia");
        k.SetValue("EstimatedSize", (int)Math.Min(size, int.MaxValue), RegistryValueKind.DWord);
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    // ---------------------------------------------------------------- abrir y cerrar Vigía

    static string? CurrentExe() => State.Load().Current is { } v && Path.Combine(Layout.VersionDir(v), "Vigia.exe") is var e && File.Exists(e) ? e : null;

    /// <summary>Vigía se abre como tu usuario y sin permisos, aunque Puente los tenga.</summary>
    static void LaunchVigia()
    {
        if (CurrentExe() is not { } exe || Layout.Test) return;
        var dir = Path.GetDirectoryName(exe)!;
        if (Native.RunAsShellUser(exe, "", dir) is not null) return;
        if (!Native.IsAdmin) { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir })?.Dispose(); return; }
        // el Explorador abre las cosas con la cuenta del escritorio, sin permisos de administrador
        Process.Start(new ProcessStartInfo(Native.Sys("explorer.exe")) { ArgumentList = { exe }, UseShellExecute = false })?.Dispose();
    }

    static void CloseVigia(int? waitPid)
    {
        if (waitPid is int pid)
        {
            try { using var p = Process.GetProcessById(pid); Ui.Show("Esperando a que se cierre Vigía", "", double.NaN); p.WaitForExit(30_000); }
            catch (ArgumentException) { }
        }
        foreach (var p in Process.GetProcessesByName("Vigia"))
        {
            using (p)
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                if (path is null || !(Layout.Under(path, Layout.Root) || Layout.Under(path, Layout.LegacyApp))) continue;
                Ui.Show("Cerrando Vigía", "", double.NaN);
                try { p.CloseMainWindow(); if (!p.WaitForExit(10_000)) { p.Kill(); p.WaitForExit(5_000); } } catch { }
            }
        }
    }

    // ---------------------------------------------------------------- piezas

    /// <summary>Borra una carpeta sin seguir enlaces (un enlace se quita, no lo que apunta). false si quedó algo.</summary>
    static bool DeleteTree(string dir, bool quiet = false)
    {
        try
        {
            var di = new DirectoryInfo(dir);
            if (!di.Exists) return true;
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) { di.Delete(false); return true; }
            foreach (var sub in di.EnumerateDirectories())
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) sub.Delete(false);
                else DeleteTree(sub.FullName, quiet: true);
            }
            foreach (var f in di.EnumerateFiles())
            {
                try { f.Attributes = FileAttributes.Normal; f.Delete(); }
                catch (Exception) when (quiet) { ScheduleDelete(f.FullName); }
            }
            di.Delete(false);
            return true;
        }
        catch (Exception e) when (quiet) { Log($"no se pudo borrar {dir}: {e.Message}"); return false; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool MoveFileEx(string from, string? to, int flags);
    /// <summary>Un archivo en uso se borra al reiniciar (lo hace Windows, no se reinicia nada ahora).</summary>
    static void ScheduleDelete(string path) { if (!Layout.Test) MoveFileEx(path, null, 4 /*MOVEFILE_DELAY_UNTIL_REBOOT*/); }

    /// <summary>Mover una carpeta falla si un archivo sigue abierto un instante después de cerrar Vigía: se reintenta.</summary>
    static void MoveDir(string from, string to)
    {
        for (var i = 0; ; i++)
        {
            try { Directory.Move(from, to); return; }
            catch (IOException) when (i < 20) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) when (i < 20) { Thread.Sleep(500); }
        }
    }

    // ---------------------------------------------------------------- permisos de administrador bajo demanda

    /// <summary>
    /// Puente abre sin permisos. Si lo que se pidió los necesita, se vuelve a abrir con UAC (una vez). Mientras tanto
    /// tiene su propio .exe abierto sin dejar que nadie lo cambie o lo renombre, para que el que suba sea este mismo.
    /// null = ya tiene permisos y sigue; un número = código de salida del Puente con permisos.
    /// </summary>
    static int? Elevate()
    {
        if (Native.IsAdmin || Layout.Test) return null;   // las pruebas nunca piden UAC
        if (_args.Contains("--elevated")) throw new InvalidOperationException("Windows no dio permisos de administrador.");
        var me = Environment.ProcessPath!;
        // Fuera de la carpeta protegida (p. ej. Descargas) se deja el .exe abierto sin permitir cambiarlo ni renombrarlo
        // mientras sube con UAC. Dentro no hace falta (tu usuario no lo puede cambiar) y estorbaría: el Puente con
        // permisos tiene que poder renombrarse para actualizarse.
        using var hold = Layout.Under(me, Layout.Root) ? null : new FileStream(me, FileMode.Open, FileAccess.Read, FileShare.Read);
        var args = string.Join(' ', _args.Append("--elevated").Select(QuoteArg));
        try
        {
            using var p = Process.Start(new ProcessStartInfo(me, args) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(me)! });
            if (p is null) return 1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            if (Ui.Active) Ui.Choose("No se dio permiso", "Para instalar o cambiar Vigía hace falta el permiso de administrador. No cambié nada.", "Cerrar");
            return 1223;
        }
    }

    static string QuoteArg(string a) => a.Length > 0 && !a.Any(c => c is ' ' or '\t' or '"') ? a : "\"" + a.Replace("\\\"", "\\\\\"").Replace("\"", "\\\"") + "\"";

    static string? ArgAfter(string flag)
    {
        var i = Array.IndexOf(_args, flag);
        return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : null;
    }

    // ---------------------------------------------------------------- clave de firma protegida

    /// <summary>
    /// Guarda la clave de firma cifrada con DPAPI de tu cuenta de Windows (solo tu cuenta, en este PC, la puede abrir).
    /// No borra ni cambia el .pem original: eso lo decides tú.
    /// </summary>
    static int ProtectKey(string pem, string outFile)
    {
        if (File.Exists(outFile)) { Console.Error.WriteLine("Ya existe " + outFile + "; no se sobrescribe."); return 1; }
        var plain = File.ReadAllBytes(pem);
        try
        {
            using (var ec = ECDsa.Create()) ec.ImportFromPem(Encoding.ASCII.GetString(plain));   // que sea una clave de verdad
            // DPAPI impide descifrarla desde otra cuenta, y con permisos propios otras cuentas ni siquiera pueden copiarla
            WriteOwnOnly(outFile, Native.Protect(plain));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Console.WriteLine("Clave cifrada en " + outFile);
        return 0;
    }

    /// <summary>
    /// Crea un archivo nuevo con permisos propios (sin heredar de la carpeta): solo tu cuenta. Así una carpeta que da
    /// lectura a otros grupos (por ejemplo a cuentas de un sandbox) no se la pasa a la clave.
    /// </summary>
    internal static void WriteOwnOnly(string path, byte[] data)
    {
        var acl = new System.Security.AccessControl.FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        using var fs = new FileInfo(path).Create(FileMode.CreateNew, System.Security.AccessControl.FileSystemRights.Write,
            FileShare.None, 4096, FileOptions.None, acl);
        fs.Write(data);
    }

    /// <summary>
    /// ¿Quién más puede abrir la clave? Mira dueño y permisos (no la abre). Solo valen tu cuenta, SYSTEM y Administradores:
    /// cualquier otro permiso (lectura, cambiar permisos…) es un problema y no se firma con ella.
    /// </summary>
    internal static string? KeyAclProblem(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) return "no existe";
        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) return "es un enlace, no el archivo de la clave";
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var trusted = new HashSet<System.Security.Principal.SecurityIdentifier> { me,
            new(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
            new(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null) };
        static string Name(System.Security.Principal.SecurityIdentifier? s)
        {
            if (s is null) return "desconocido";
            try { return s.Translate(typeof(System.Security.Principal.NTAccount)).Value; } catch { return s.Value; }
        }
        var sec = fi.GetAccessControl();
        var bad = new List<string>();
        var owner = sec.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)) as System.Security.Principal.SecurityIdentifier;
        if (owner is null || !trusted.Contains(owner)) bad.Add("dueño " + Name(owner));
        foreach (System.Security.AccessControl.FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)))
            if (r.AccessControlType == System.Security.AccessControl.AccessControlType.Allow
                && r.IdentityReference is System.Security.Principal.SecurityIdentifier s && !trusted.Contains(s))
                bad.Add(Name(s) + (r.IsInherited ? " (heredado de la carpeta)" : ""));
        return bad.Count == 0 ? null : "la pueden abrir otras cuentas o grupos: " + string.Join(", ", bad.Distinct());
    }

    static int KeyCheck(string path)
    {
        if (KeyAclProblem(path) is { } p) { Console.WriteLine("Clave NO protegida: " + p); return 3; }
        Console.WriteLine("Clave protegida: solo tu cuenta, SYSTEM y Administradores pueden abrirla.");
        return 0;
    }

    /// <summary>
    /// Firma de una versión fuera del entorno que compila (otra cuenta de Windows o un PC sin conexión): firma las cuatro
    /// piezas de la carpeta, comprueba con la clave pública de este Puente que el manifiesto cuadra con el paquete y
    /// con Puente, y si algo no cuadra borra lo que acaba de firmar. No pisa firmas que ya existan.
    /// </summary>
    static int SignRelease(string dir, string key)
    {
        if (KeyAclProblem(key) is { } kp) { Console.Error.WriteLine("No se firma: la clave " + kp + "."); return 3; }
        var man = Path.Combine(dir, "vigia-manifest.json");
        var zips = Directory.GetFiles(dir, "Vigia-*-win-x64.zip");
        var pu = Path.Combine(dir, "Puente.exe"); var setup = Path.Combine(dir, "Vigia-Setup.exe");
        if (!File.Exists(man) || zips.Length != 1 || !File.Exists(pu) || !File.Exists(setup))
        { Console.Error.WriteLine("Faltan piezas: hacen falta un Vigia-*-win-x64.zip, Puente.exe, Vigia-Setup.exe y vigia-manifest.json."); return 1; }
        if (!File.ReadAllBytes(pu).AsSpan().SequenceEqual(File.ReadAllBytes(setup)))
        { Console.Error.WriteLine("Vigia-Setup.exe no es el mismo archivo que Puente.exe: no se firma."); return 1; }
        var files = new[] { zips[0], pu, setup, man };
        if (files.Any(f => File.Exists(f + ".sig"))) { Console.Error.WriteLine("Ya hay firmas en esa carpeta; no se pisan."); return 1; }
        using (var ec = ECDsa.Create())
        {
            LoadKey(ec, key);
            foreach (var f in files)
            {
                using var fs = File.OpenRead(f);
                File.WriteAllText(f + ".sig", Convert.ToBase64String(ec.SignData(fs, HashAlgorithmName.SHA256)));
            }
        }
        if (VerifyManifest(man) != 0 || !files.All(f => Verify(f, File.ReadAllText(f + ".sig"))))
        {
            foreach (var f in files) File.Delete(f + ".sig");
            Console.Error.WriteLine("Lo firmado no cuadra (manifiesto, paquete o clave que no es la de Vigía): se quitaron las firmas.");
            return 1;
        }
        foreach (var f in files) Console.WriteLine(Sha256(f) + "  " + Path.GetFileName(f));
        Console.WriteLine("Firmado. Compara estas huellas con las que muestre release.sh antes de publicar.");
        return 0;
    }

    /// <summary>Lee la clave: .dpapi (cifrada con tu cuenta) o .pem. Lo descifrado se borra de la memoria al terminar.</summary>
    static void LoadKey(ECDsa ec, string path)
    {
        var raw = File.ReadAllBytes(path);
        byte[]? plain = null;
        try
        {
            plain = path.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase) ? Native.Unprotect(raw) : raw;
            var chars = Encoding.ASCII.GetChars(plain);
            try { ec.ImportFromPem(chars); } finally { Array.Clear(chars); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
            if (plain is not null && !ReferenceEquals(plain, raw)) CryptographicOperations.ZeroMemory(plain);
        }
    }
}
