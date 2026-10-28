using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Puente;

/// <summary>
/// Puente instala y actualiza Vigía desde las versiones publicadas en GitHub (JcperezDev/vigia-releases).
/// Solo instala archivos firmados con la clave de publicación: si alguien cambia un archivo de la versión,
/// la firma no coincide y Puente no lo instala. La versión anterior se guarda para poder volver a ella.
///
///   Puente.exe                 instala Vigía o la actualiza si hay una versión nueva
///   Puente.exe --update --wait PID   lo usa Vigía: espera a que se cierre, actualiza y la vuelve a abrir
///   Puente.exe --check         dice qué versión hay instalada y cuál es la última
///   Puente.exe --rollback      vuelve a la versión anterior
///   Puente.exe --uninstall     quita Vigía (tus ajustes y copias de seguridad se quedan)
///   Puente.exe --genkey / --sign  solo para publicar versiones
/// </summary>
static class Program
{
    const string Repo = "JcperezDev/vigia-releases";

    // Clave pública de publicación. La privada está solo en el PC donde se publican las versiones.
    const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2sn60BucOhX4a3HZt59BBg3gWbZ77dyDNEdmP0/+cFAaUg7jSEjaywk5q54fPIZKzpuUtQlsS01dcTNdbv0muA==";

    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Programs = Path.Combine(Local, "Programs");
    static readonly string AppDir = Path.Combine(Programs, "Vigia");
    static readonly string NewDir = Path.Combine(Programs, "Vigia.nueva");
    static readonly string PrevDir = Path.Combine(Programs, "Vigia.anterior");
    static readonly string HomeDir = Path.Combine(Programs, "Vigia-Puente");
    static readonly string DataDir = Path.Combine(Local, "Vigia");
    static readonly string Work = Path.Combine(DataDir, "updates");
    static readonly string LogFile = Path.Combine(DataDir, "puente.log");

    static bool _quiet;
    static string[] _args = [];

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    static async Task<int> Main(string[] args)
    {
        _args = args;
        _quiet = args.Contains("--quiet");
        if (args.FirstOrDefault() == "--render")   // revisar el diseño sin abrir la ventana
        {
            var d = args[1];
            Ui.Render(Path.Combine(d, "1-buscando.bmp"), "Buscando actualizaciones…", "Mirando la última versión publicada.", double.NaN, []);
            Ui.Render(Path.Combine(d, "2-descargando.bmp"), "Descargando Vigía 0.7.0", "18,4 de 41,2 MB · 44 %", 0.44, []);
            Ui.Render(Path.Combine(d, "3-listo.bmp"), "Listo: Vigía 0.7.0", "La 0.6.0 quedó guardada por si quieres volver a ella.", 1.0, []);
            Ui.Render(Path.Combine(d, "4-aldia.bmp"), "Vigía está al día", "Tienes la versión 0.7.0, que es la última.", null, ["Abrir Vigía", "Cerrar"], 0);
            Ui.Render(Path.Combine(d, "5-error.bmp"), "No se pudo terminar", "No hay conexión con GitHub. Vigía sigue como estaba.", null, ["Cerrar"]);
            return 0;
        }
        var cli = args.FirstOrDefault() is "--genkey" or "--sign" or "--verify" or "--check";
        if (cli)
        {
            // Puente es una app de ventana; para los comandos de publicación escribe en la terminal que lo llamó
            if (!Console.IsOutputRedirected) AttachConsole(-1);
            Console.OutputEncoding = Encoding.UTF8;
        }
        else if (!_quiet) Ui.Start();
        try
        {
            switch (args.FirstOrDefault())
            {
                case "--genkey": return GenKey(args[1]);
                case "--sign": return SignFile(args[1], args[2]);
                case "--verify": return Verify(args[1], File.ReadAllText(args[1] + ".sig")) ? 0 : 1;
                case "--check": return await Check();
                case "--rollback": return Rollback();
                case "--uninstall": return Uninstall();
                default: return await Install(WaitPid(args));
            }
        }
        catch (Exception e)
        {
            Log(e.ToString());
            Ui.Lock(false);
            if (cli || !Ui.Active) { Console.Error.WriteLine($"No se pudo terminar: {e.Message}"); return 1; }
            Ui.Choose("No se pudo terminar", Sentence(e.Message) + " Vigía sigue como estaba.", "Cerrar");
            return 1;
        }
        finally { Ui.Close(); }
    }

    static string Sentence(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..] + (s.EndsWith('.') ? "" : ".");
    static string Mb(double bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    // ---------------------------------------------------------------- instalar / actualizar

    static async Task<int> Install(int? waitPid)
    {
        Ui.Show("Buscando actualizaciones…", "Mirando la última versión publicada.", double.NaN);
        CleanLeftovers();
        SelfInstall();

        using var http = Http();
        var rel = await Latest(http);
        var installed = InstalledVersion();

        await UpdateSelf(http, rel);

        if (installed is not null && installed >= rel.Version)
        {
            if (waitPid is not null)
            {
                // Vigía se cerró para actualizarse; si ya estaba al día, se vuelve a abrir
                CloseVigia(waitPid);
                Launch();
                return 0;
            }
            if (Ui.Choose("Vigía está al día", $"Tienes la versión {installed}, que es la última.", "Abrir Vigía", "Cerrar") == 0) Launch();
            return 0;
        }
        Ui.Show(installed is null ? $"Instalando Vigía {rel.Version}" : $"Actualizando a Vigía {rel.Version}",
            installed is null ? "Preparando la descarga." : $"Tienes la {installed}.", double.NaN);

        var zipAsset = rel.Asset($"Vigia-{rel.Version}-win-x64.zip");
        var zip = Path.Combine(Work, zipAsset.Name);
        Directory.CreateDirectory(Work);
        await Download(http, zipAsset, zip);
        Ui.Show($"Comprobando Vigía {rel.Version}", "Revisando la firma: solo se instala lo que publicó Vigía.", double.NaN);
        if (!Verify(zip, await http.GetStringAsync(rel.Asset(zipAsset.Name + ".sig").Url)))
        {
            File.Delete(zip);
            throw new InvalidOperationException("la firma no coincide. El archivo no viene de la publicación de Vigía; no se instaló nada.");
        }

        Ui.Show($"Preparando Vigía {rel.Version}", "Descomprimiendo.", double.NaN);
        if (Directory.Exists(NewDir)) Directory.Delete(NewDir, true);
        ZipFile.ExtractToDirectory(zip, NewDir);
        var got = FileVersion(Path.Combine(NewDir, "Vigia.exe"));
        if (got != rel.Version)
            throw new InvalidOperationException($"el paquete dice ser {rel.Version} pero trae {got?.ToString() ?? "nada"}.");

        CloseVigia(waitPid);
        Ui.Lock(true);
        Ui.Show($"Instalando Vigía {rel.Version}", "No apagues el PC; tarda unos segundos.", double.NaN);
        Swap();
        Shortcuts();
        Ui.Lock(false);
        File.Delete(zip);
        Log($"instalada {rel.Version} (antes {installed?.ToString() ?? "ninguna"})");
        Ui.Show($"Listo: Vigía {rel.Version}", installed is not null
            ? $"La {installed} quedó guardada por si quieres volver a ella."
            : "Tienes accesos en el Escritorio y en Inicio.", 1.0);
        Launch();
        Thread.Sleep(2500);
        return 0;
    }

    static async Task<int> Check()
    {
        using var http = Http();
        var rel = await Latest(http);
        var installed = InstalledVersion();
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["installed"] = installed?.ToString(), ["latest"] = rel.Version.ToString(),
            ["update"] = (installed is null || installed < rel.Version) ? "yes" : "no",
        }, CheckJson.Default.DictionaryStringString));
        return 0;
    }

    /// <summary>Puente también se actualiza solo: baja Puente.exe de la versión, comprueba la firma y se reemplaza.</summary>
    static async Task UpdateSelf(HttpClient http, Release rel)
    {
        var a = rel.Assets.FirstOrDefault(x => x.Name == "Puente.exe");
        var me = Environment.ProcessPath!;
        if (a is null || _args.Contains("--relaunched") || !SamePath(Path.GetDirectoryName(me)!, HomeDir)) return;
        if (a.Digest is { } d && d.Equals("sha256:" + Sha256(me), StringComparison.OrdinalIgnoreCase)) return;
        var tmp = Path.Combine(HomeDir, "Puente.new.exe");
        await Download(http, a, tmp, quiet: true);
        if (Sha256(tmp) == Sha256(me) || !Verify(tmp, await http.GetStringAsync(rel.Asset("Puente.exe.sig").Url)))
        {
            File.Delete(tmp);
            return;
        }
        // Windows deja renombrar un .exe en uso: el viejo se borra la próxima vez.
        // Después hay que reiniciar desde el nuevo: este proceso carga sus piezas del .exe por su ruta, y en esa ruta
        // ya está otro archivo (ZipFile fallaba con FileNotFoundException).
        var psi = new ProcessStartInfo(me) { UseShellExecute = false, WorkingDirectory = HomeDir };
        foreach (var a2 in _args.Where(x => x != "--relaunched")) psi.ArgumentList.Add(a2);
        psi.ArgumentList.Add("--relaunched");
        using (Process.GetCurrentProcess()) { }   // deja cargado System.Diagnostics.Process antes de mover el .exe
        var old = Path.Combine(HomeDir, "Puente.old.exe");
        if (File.Exists(old)) File.Delete(old);
        File.Move(me, old);
        File.Move(tmp, me);
        Log("Puente actualizado");
        Process.Start(psi)?.Dispose();
        Environment.Exit(0);
    }

    // ---------------------------------------------------------------- volver atrás / quitar

    static int Rollback()
    {
        if (!Directory.Exists(PrevDir)) { Ui.Choose("No hay versión anterior", "Puente guarda la versión anterior cuando actualiza Vigía. Todavía no hay ninguna.", "Cerrar"); return 1; }
        var cur = InstalledVersion();
        var prev = FileVersion(Path.Combine(PrevDir, "Vigia.exe"));
        if (!Ask($"¿Volver a Vigía {prev}?", $"Ahora tienes la {cur}. Quedará guardada y podrás volver a ella.", "Volver")) return 0;
        Ui.Show($"Volviendo a Vigía {prev}", "Cerrando Vigía.", double.NaN);
        CloseVigia(null);
        Ui.Lock(true);
        var tmp = Path.Combine(Programs, "Vigia.cambio");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        MoveDir(AppDir, tmp);
        MoveDir(PrevDir, AppDir);
        MoveDir(tmp, PrevDir);
        Shortcuts();
        Ui.Lock(false);
        Log($"vuelta atrás {cur} -> {prev}");
        Ui.Show($"Listo: Vigía {prev}", $"La {cur} quedó guardada como anterior.", 1.0);
        Launch();
        Thread.Sleep(2500);
        return 0;
    }

    static int Uninstall()
    {
        if (!Ask("¿Quitar Vigía de este PC?", "Tus ajustes y las copias de lo que cambió Vigía se quedan en " + DataDir + ".", "Quitar")) return 0;
        Ui.Show("Quitando Vigía", "", double.NaN);
        CloseVigia(null);
        foreach (var d in new[] { AppDir, PrevDir, NewDir })
            if (Directory.Exists(d)) Directory.Delete(d, true);
        foreach (var l in LinkPaths()) if (File.Exists(l)) File.Delete(l);
        Log("desinstalada");
        Ui.Show("Listo: Vigía quitada", "Puente se borra solo al cerrar esta ventana.", 1.0);
        // un .exe no se puede borrar a sí mismo mientras corre: lo borra cmd unos segundos después
        if (SamePath(Path.GetDirectoryName(Environment.ProcessPath!)!, HomeDir))
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 >nul & rmdir /s /q \"{HomeDir}\"") { CreateNoWindow = true, UseShellExecute = false });
        Thread.Sleep(2500);
        return 0;
    }

    // ---------------------------------------------------------------- piezas

    static void Swap()
    {
        if (Directory.Exists(PrevDir)) Directory.Delete(PrevDir, true);
        if (Directory.Exists(AppDir)) MoveDir(AppDir, PrevDir);
        try { MoveDir(NewDir, AppDir); }
        catch
        {
            if (!Directory.Exists(AppDir) && Directory.Exists(PrevDir)) MoveDir(PrevDir, AppDir);
            throw;
        }
    }

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
                if (path is not null && !path.StartsWith(AppDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                Ui.Show("Cerrando Vigía", "", double.NaN);
                try { p.CloseMainWindow(); if (!p.WaitForExit(10_000)) { p.Kill(); p.WaitForExit(5_000); } } catch { }
            }
        }
    }

    static void SelfInstall()
    {
        var me = Environment.ProcessPath!;
        if (SamePath(Path.GetDirectoryName(me)!, HomeDir)) return;
        Directory.CreateDirectory(HomeDir);
        var dest = Path.Combine(HomeDir, "Puente.exe");
        if (File.Exists(dest) && Sha256(dest) == Sha256(me)) return;
        try { File.Copy(me, dest, true); }
        catch (IOException) { return; } // el Puente instalado está abierto: se actualiza solo la próxima vez
        Log("Puente copiado a " + dest);
    }

    static void CleanLeftovers()
    {
        try { File.Delete(Path.Combine(HomeDir, "Puente.old.exe")); } catch { }
        try { if (Directory.Exists(NewDir)) Directory.Delete(NewDir, true); } catch { }
    }

    static IEnumerable<string> LinkPaths() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Vigia.lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Vigia.lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Puente (actualizar Vigia).lnk"),
    ];

    /// <summary>Accesos en Escritorio e Inicio. Se crean con WScript.Shell desde PowerShell (Puente va recortado y sin COM).</summary>
    static void Shortcuts()
    {
        var exe = Path.Combine(AppDir, "Vigia.exe");
        var puente = Path.Combine(HomeDir, "Puente.exe");
        var l = LinkPaths().ToArray();
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var ps = "$s=New-Object -ComObject WScript.Shell;" +
                 $"foreach($l in @({Q(l[0])},{Q(l[1])})){{$k=$s.CreateShortcut($l);$k.TargetPath={Q(exe)};$k.WorkingDirectory={Q(AppDir)};$k.IconLocation={Q(exe + ",0")};$k.Description='Vigía';$k.Save()}};" +
                 (File.Exists(puente) ? $"$k=$s.CreateShortcut({Q(l[2])});$k.TargetPath={Q(puente)};$k.WorkingDirectory={Q(HomeDir)};$k.Description='Instalar o actualizar Vigía';$k.Save()" : "");
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(ps)))
        { CreateNoWindow = true, UseShellExecute = false });
        p?.WaitForExit(30_000);
    }

    static void Launch()
    {
        var exe = Path.Combine(AppDir, "Vigia.exe");
        if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppDir });
    }

    // ---------------------------------------------------------------- GitHub

    record Asset(string Name, string Url, long Size, string? Digest);

    record Release(Version Version, string Notes, List<Asset> Assets)
    {
        public Asset Asset(string name) => Assets.FirstOrDefault(a => a.Name == name)
            ?? throw new InvalidOperationException($"la versión {Version} no trae {name}.");
    }

    static HttpClient Http()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Puente/" + typeof(Program).Assembly.GetName().Version);
        return h;
    }

    static async Task<Release> Latest(HttpClient http)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await http.SendAsync(req);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("todavía no hay ninguna versión publicada.");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var r = doc.RootElement;
        var tag = r.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');
        var assets = r.GetProperty("assets").EnumerateArray().Select(a => new Asset(
            a.GetProperty("name").GetString()!, a.GetProperty("browser_download_url").GetString()!, a.GetProperty("size").GetInt64(),
            a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null)).ToList();
        return new Release(Norm(Version.Parse(tag)), r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", assets);
    }

    static async Task Download(HttpClient http, Asset a, string dest, bool quiet = false)
    {
        using var res = await http.GetAsync(a.Url, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? a.Size;
        await using var src = await res.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        var buf = new byte[1 << 16];
        long done = 0; int n; var last = -1;
        while ((n = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n));
            done += n;
            var pct = total > 0 ? (int)(done * 100 / total) : 0;
            if (!quiet && pct != last)
            {
                Ui.Show($"Descargando Vigía {Ver(a.Name)}", $"{Mb(done)} de {Mb(total)} MB · {pct} %", total > 0 ? (double)done / total : double.NaN);
                last = pct;
            }
        }
    }

    // ---------------------------------------------------------------- firma

    static bool Verify(string file, string sigB64)
    {
        if (PublicKey.StartsWith("__")) throw new InvalidOperationException("este Puente se compiló sin clave pública.");
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        byte[] sig;
        try { sig = Convert.FromBase64String(sigB64.Trim()); } catch (FormatException) { return false; }
        using var fs = File.OpenRead(file);
        return ec.VerifyData(fs, sig, HashAlgorithmName.SHA256);
    }

    static int GenKey(string privPath)
    {
        if (File.Exists(privPath)) { Console.Error.WriteLine("Ya existe " + privPath + "; no se sobrescribe."); return 1; }
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(privPath))!);
        File.WriteAllText(privPath, ec.ExportPkcs8PrivateKeyPem());
        Console.WriteLine(Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    static int SignFile(string file, string privPath)
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(File.ReadAllText(privPath));
        using var fs = File.OpenRead(file);
        File.WriteAllText(file + ".sig", Convert.ToBase64String(ec.SignData(fs, HashAlgorithmName.SHA256)));
        return 0;
    }

    // ---------------------------------------------------------------- utilidades

    static Version? InstalledVersion() => FileVersion(Path.Combine(AppDir, "Vigia.exe"));

    static Version? FileVersion(string exe)
    {
        if (!File.Exists(exe)) return null;
        var v = FileVersionInfo.GetVersionInfo(exe);
        return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart);
    }

    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    static int? WaitPid(string[] a)
    {
        var i = Array.IndexOf(a, "--wait");
        return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out var p) ? p : null;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static string Sha256(string file) { using var fs = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(fs)); }

    static string Ver(string asset) => System.Text.RegularExpressions.Regex.Match(asset, @"\d+\.\d+\.\d+").Value;

    /// <summary>Pregunta con dos botones; sin ventana (--quiet) se da por aceptado.</summary>
    static bool Ask(string q, string detail, string yes) => !Ui.Active || Ui.Choose(q, detail, yes, "Cancelar") == 0;

    static void Log(string s)
    {
        try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}{Environment.NewLine}"); } catch { }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string?>))]
partial class CheckJson : System.Text.Json.Serialization.JsonSerializerContext { }
