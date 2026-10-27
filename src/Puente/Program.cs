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

    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { Console.Title = "Puente · Vigía"; } catch { }
        _quiet = args.Contains("--quiet");
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
            Say("");
            Say($"No se pudo terminar: {e.Message}");
            Log(e.ToString());
            Pause();
            return 1;
        }
    }

    // ---------------------------------------------------------------- instalar / actualizar

    static async Task<int> Install(int? waitPid)
    {
        Say("Puente · Vigía");
        Say("");
        CleanLeftovers();
        SelfInstall();

        using var http = Http();
        Say("Buscando la última versión…");
        var rel = await Latest(http);
        var installed = InstalledVersion();
        Say(installed is null ? $"Última versión: {rel.Version}. Vigía no está instalada." : $"Instalada: {installed}. Última: {rel.Version}.");

        await UpdateSelf(http, rel);

        if (installed is not null && installed >= rel.Version)
        {
            Say("Vigía está al día.");
            if (waitPid is not null || !Directory.Exists(AppDir)) return 0;
            if (Ask("¿Abrir Vigía ahora? (S/n) ")) Launch();
            return 0;
        }

        var zipAsset = rel.Asset($"Vigia-{rel.Version}-win-x64.zip");
        var zip = Path.Combine(Work, zipAsset.Name);
        Directory.CreateDirectory(Work);
        await Download(http, zipAsset, zip);
        Say("Comprobando la firma…");
        if (!Verify(zip, await http.GetStringAsync(rel.Asset(zipAsset.Name + ".sig").Url)))
        {
            File.Delete(zip);
            throw new InvalidOperationException("la firma no coincide. El archivo no viene de la publicación de Vigía; no se instaló nada.");
        }

        Say("Descomprimiendo…");
        if (Directory.Exists(NewDir)) Directory.Delete(NewDir, true);
        ZipFile.ExtractToDirectory(zip, NewDir);
        var got = FileVersion(Path.Combine(NewDir, "Vigia.exe"));
        if (got != rel.Version)
            throw new InvalidOperationException($"el paquete dice ser {rel.Version} pero trae {got?.ToString() ?? "nada"}.");

        CloseVigia(waitPid);
        Swap();
        Shortcuts();
        File.Delete(zip);
        Log($"instalada {rel.Version} (antes {installed?.ToString() ?? "ninguna"})");
        Say($"Listo: Vigía {rel.Version} instalada.");
        if (installed is not null) Say("La versión anterior quedó guardada. Para volver a ella: Puente.exe --rollback");
        Launch();
        if (waitPid is null) Pause(4);
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
        if (a is null || !SamePath(Path.GetDirectoryName(me)!, HomeDir)) return;
        if (a.Digest is { } d && d.Equals("sha256:" + Sha256(me), StringComparison.OrdinalIgnoreCase)) return;
        var tmp = Path.Combine(HomeDir, "Puente.new.exe");
        await Download(http, a, tmp, quiet: true);
        if (Sha256(tmp) == Sha256(me) || !Verify(tmp, await http.GetStringAsync(rel.Asset("Puente.exe.sig").Url)))
        {
            File.Delete(tmp);
            return;
        }
        // Windows deja renombrar un .exe en uso: el viejo se borra la próxima vez
        var old = Path.Combine(HomeDir, "Puente.old.exe");
        if (File.Exists(old)) File.Delete(old);
        File.Move(me, old);
        File.Move(tmp, me);
        Log("Puente actualizado");
        Say("Puente actualizado.");
    }

    // ---------------------------------------------------------------- volver atrás / quitar

    static int Rollback()
    {
        if (!Directory.Exists(PrevDir)) { Say("No hay una versión anterior guardada."); Pause(); return 1; }
        var cur = InstalledVersion();
        var prev = FileVersion(Path.Combine(PrevDir, "Vigia.exe"));
        if (!Ask($"Volver de {cur} a {prev}? (S/n) ")) return 0;
        CloseVigia(null);
        var tmp = Path.Combine(Programs, "Vigia.cambio");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        MoveDir(AppDir, tmp);
        MoveDir(PrevDir, AppDir);
        MoveDir(tmp, PrevDir);
        Shortcuts();
        Log($"vuelta atrás {cur} -> {prev}");
        Say($"Listo: Vigía {prev}. La {cur} quedó como anterior.");
        Launch();
        Pause(4);
        return 0;
    }

    static int Uninstall()
    {
        if (!Ask("¿Quitar Vigía de este PC? Tus ajustes y las copias de lo que cambió Vigía se quedan en " + DataDir + " (S/n) ")) return 0;
        CloseVigia(null);
        foreach (var d in new[] { AppDir, PrevDir, NewDir })
            if (Directory.Exists(d)) Directory.Delete(d, true);
        foreach (var l in LinkPaths()) if (File.Exists(l)) File.Delete(l);
        Log("desinstalada");
        Say("Vigía quitada. Puente se borra al cerrar esta ventana.");
        // un .exe no se puede borrar a sí mismo mientras corre: lo borra cmd unos segundos después
        if (SamePath(Path.GetDirectoryName(Environment.ProcessPath!)!, HomeDir))
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 >nul & rmdir /s /q \"{HomeDir}\"") { CreateNoWindow = true, UseShellExecute = false });
        Pause(3);
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
            try { using var p = Process.GetProcessById(pid); Say("Esperando a que se cierre Vigía…"); p.WaitForExit(30_000); }
            catch (ArgumentException) { }
        }
        foreach (var p in Process.GetProcessesByName("Vigia"))
        {
            using (p)
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                if (path is not null && !path.StartsWith(AppDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                Say("Cerrando Vigía…");
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
            if (!quiet && !_quiet && pct != last) { Console.Write($"\rDescargando {a.Name}: {pct} % de {total / 1048576.0:0.0} MB   "); last = pct; }
        }
        if (!quiet && !_quiet) Console.WriteLine();
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

    static void Say(string s) { if (!_quiet) Console.WriteLine(s); }

    static bool Ask(string q)
    {
        if (_quiet || Console.IsInputRedirected) return true;
        Console.Write(q);
        var k = Console.ReadLine()?.Trim().ToLowerInvariant();
        return k is "" or "s" or "si" or "sí" or "y";
    }

    static void Pause(int seconds = 0)
    {
        if (_quiet || Console.IsInputRedirected) return;
        if (seconds > 0) { Thread.Sleep(seconds * 1000); return; }
        Console.Write("Pulsa Enter para cerrar.");
        Console.ReadLine();
    }

    static void Log(string s)
    {
        try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}{Environment.NewLine}"); } catch { }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string?>))]
partial class CheckJson : System.Text.Json.Serialization.JsonSerializerContext { }
