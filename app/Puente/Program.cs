using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Puente;

/// <summary>
/// Puente instala y actualiza Vigía desde las versiones publicadas en GitHub (JcperezDev/vigia).
/// Solo instala archivos firmados con la clave de publicación: si alguien cambia un archivo de la versión,
/// la firma no coincide y Puente no lo instala. La versión anterior se guarda para poder volver a ella.
///
/// Vigía se instala en %ProgramFiles%\Vigia (ver Layout.cs y Setup.cs). Puente abre sin permisos y pide el de
/// administrador (UAC) solo para instalar, reparar, volver atrás o quitar.
///
///   Puente.exe                 instala Vigía, la actualiza o migra la instalación vieja de %LOCALAPPDATA%\Programs
///   Puente.exe --update --wait PID   lo usa Vigía: espera a que se cierre, actualiza y la vuelve a abrir
///   Puente.exe --offline DIR   instala desde una carpeta con los archivos publicados (sin internet)
///   Puente.exe --repair        vuelve a poner la versión actual si sus archivos cambiaron
///   Puente.exe --check         dice qué versión hay instalada y cuál es la última
///   Puente.exe --rollback      vuelve a la versión anterior (si sigue intacta y no es insegura)
///   Puente.exe --uninstall     quita Vigía (tus ajustes y copias de seguridad se quedan)
///   Puente.exe --genkey / --sign / --manifest / --protect-key   solo para publicar versiones
/// </summary>
static partial class Program
{
    const string Repo = "JcperezDev/vigia";

    // Clave pública de publicación. La privada está solo en el PC donde se publican las versiones.
    const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2sn60BucOhX4a3HZt59BBg3gWbZ77dyDNEdmP0/+cFAaUg7jSEjaywk5q54fPIZKzpuUtQlsS01dcTNdbv0muA==";

    static string LogFile => Native.IsAdmin ? Path.Combine(Layout.LogDir, "puente.log")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia", "puente.log");

    static bool _quiet;
    static string[] _args = [];

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    static readonly HashSet<string> Commands = ["--render", "--genkey", "--sign", "--verify", "--check", "--zip-test", "--manifest",
        "--protect-key", "--verify-manifest", "--acl-test", "--state", "--fetch-test", "--rollback", "--uninstall",
#if PUENTE_TEST
        "--as-user",
#endif
    ];
    static readonly HashSet<string> Flags = ["--quiet", "--repair", "--offline", "--wait", "--elevated", "--yes", "--relaunched",
        "--update"];   // --update: lo que pasan Vigía nueva y las ya publicadas (= instalar/actualizar)
    static readonly HashSet<string> TakesValue = ["--offline", "--wait"];

    /// <summary>Primer argumento que no se reconoce, o null. Los comandos llevan sus propios parámetros detrás.</summary>
    static string? BadArgs(string[] args)
    {
        if (args.Length == 0) return null;
        if (Commands.Contains(args[0])) return null;
        for (int i = 0; i < args.Length; i++)
        {
            if (!Flags.Contains(args[i])) return args[i];
            if (TakesValue.Contains(args[i])) i++;
        }
        return null;
    }

    static async Task<int> Main(string[] args)
    {
        // Puente puede abrirse desde Descargas y subir con UAC: las DLL que cargue después solo salen de System32 o
        // de las rutas que se den explícitamente, nunca de la carpeta donde está el .exe (donde otro pudo dejar una).
        SetDefaultDllDirectories(0x00000800 /*LOAD_LIBRARY_SEARCH_SYSTEM32*/);
        Native.UseAdminsAsOwner();   // lo que cree Puente nace con dueño Administradores (ver Native.UseAdminsAsOwner)
        _args = args;
        _quiet = args.Contains("--quiet");
        // Sin argumentos (doble clic) se instala o actualiza. Cualquier argumento desconocido se rechaza: un error al
        // escribir un comando nunca debe acabar en una instalación con permisos de administrador.
        if (BadArgs(args) is string bad)
        {
            if (!Console.IsOutputRedirected) AttachConsole(-1);
            Console.Error.WriteLine("Puente: argumento no reconocido: " + bad);
            return 2;
        }
        if (args.FirstOrDefault() == "--render")   // revisar el diseño sin abrir la ventana
        {
            var d = args[1];
            Ui.Render(Path.Combine(d, "1-buscando.bmp"), "Buscando actualizaciones…", "Mirando la última versión publicada.", double.NaN, []);
            Ui.Render(Path.Combine(d, "2-descargando.bmp"), "Descargando Vigía 0.7.0", "18,4 de 41,2 MB · 44 %", 0.44, []);
            Ui.Render(Path.Combine(d, "3-listo.bmp"), "Listo: Vigía 0.7.0", "La 0.6.0 quedó guardada por si quieres volver a ella.", 1.0, []);
            Ui.Render(Path.Combine(d, "4-aldia.bmp"), "Vigía está al día", "Tienes la versión 0.7.0, que es la última.", null, ["Abrir Vigía", "Cerrar"], 0);
            Ui.Render(Path.Combine(d, "5-error.bmp"), "No se pudo terminar", "No hay conexión con GitHub. Vigía sigue como estaba.", null, ["Cerrar"]);
            Ui.Render(Path.Combine(d, "6-reintentando.bmp"), "Se cortó la conexión", "Reintentando en unos segundos.", 0.44, []);
            Ui.Render(Path.Combine(d, "7-corte.bmp"), "No se pudo terminar", CutText, null, ["Reintentar", "Cerrar"]);
            return 0;
        }
        var cli = args.FirstOrDefault() is "--genkey" or "--sign" or "--verify" or "--check" or "--zip-test" or "--manifest"
            or "--protect-key" or "--verify-manifest" or "--acl-test" or "--state"
#if PUENTE_TEST
            or "--as-user"
#endif
            ;
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
                case "--manifest": return MakeManifest(args[1], args[2], args.Length > 3 ? args[3] : null);
                case "--verify-manifest": return VerifyManifest(args[1]);
                case "--protect-key": return ProtectKey(args[1], args[2]);
                case "--check": return await Check();
                case "--state": return PrintState();
                case "--acl-test": return AclTest();
                case "--zip-test": return ZipTest(args.Length > 1 ? args[1] : null);
                case "--fetch-test": return await FetchTest(args[1]);
#if PUENTE_TEST
                case "--as-user": return TestHooks.AsUser(args[1]);
#endif
                case "--rollback": return Elevate() ?? RollbackFlow();
                case "--uninstall": return Elevate() ?? UninstallFlow();
                default:
                    if (Elevate() is int code) return code;
                    while (true)
                    {
                        try { return await InstallFlow(WaitPid(args), ArgAfter("--offline"), args.Contains("--repair")); }
                        catch (Exception e) when (IsNet(e) && Ui.Active)
                        {
                            // la red se cayó y los reintentos no alcanzaron: se ofrece seguir, sin el texto técnico de Windows
                            Log(e.ToString());
                            Ui.Lock(false);
                            if (Ui.Choose("No se pudo terminar", CutText, "Reintentar", "Cerrar") != 0) return 1;
                        }
                    }
            }
        }
        catch (Exception e)
        {
            Log(e.ToString());
            Ui.Lock(false);
            var why = IsNet(e) ? CutText
                : e is InvalidOperationException ? Sentence(e.Message) + " Vigía sigue como estaba."
                : $"Algo falló en este PC. Vigía sigue como estaba. Los detalles quedaron en {LogFile}.";
            if (cli || !Ui.Active) { Console.Error.WriteLine($"No se pudo terminar: {e.Message}"); return 1; }
            Ui.Choose("No se pudo terminar", why, "Cerrar");
            return 1;
        }
        finally { Ui.Close(); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetDefaultDllDirectories(uint flags);

    const string CutText = "Se cortó la conexión con GitHub. Revisa tu internet y dale a Reintentar: la descarga sigue donde se quedó. Vigía sigue como estaba.";

    /// <summary>¿Es un fallo de red (conexión cortada, sin internet, tiempo agotado)? Esos se reintentan.</summary>
    static bool IsNet(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
            if (e is HttpRequestException or System.Net.Sockets.SocketException or TimeoutException or TaskCanceledException or HttpIOException) return true;
        return false;
    }

    /// <summary>Repite una consulta corta si la red falla: 4 intentos, esperando 2, 5 y 10 s.</summary>
    static async Task<T> Net<T>(Func<Task<T>> f)
    {
        for (int i = 1; ; i++)
        {
            try { return await f(); }
            catch (Exception e) when (IsNet(e) && i < 4)
            {
                Log($"red, intento {i}: {e.Message}");
                if (!_quiet) Ui.Show("Se cortó la conexión", "Reintentando en unos segundos.", double.NaN);
                await Task.Delay(TimeSpan.FromSeconds(i switch { 1 => 2, 2 => 5, _ => 10 }));
            }
        }
    }

    static string Sentence(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..] + (s.EndsWith('.') ? "" : ".");
    static string Mb(double bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    static async Task<int> Check()
    {
        using var http = Http();
        var rel = await Net(() => Latest(http));
        var installed = State.Load().Current ?? LegacyVersion();
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["installed"] = installed?.ToString(), ["latest"] = rel.Version.ToString(),
            ["update"] = (installed is null || installed < rel.Version) ? "yes" : "no",
        }, CheckJson.Default.DictionaryStringString));
        return 0;
    }

    /// <summary>El driver de los grados del procesador se va con Vigía solo si lo puso Vigía (pawnio.json).</summary>
    static void RemovePawnIfOurs()
    {
        try
        {
            var mark = Path.Combine(Layout.UserData, "pawnio.json");
            if (!File.Exists(mark) || !File.ReadAllText(mark).Contains("\"Ok\":true")) return;
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            var exe = Path.Combine(k?.GetValue("InstallLocation") as string ?? "", "uninstall.exe");
            if (!File.Exists(exe)) return;
            using var p = Process.Start(new ProcessStartInfo(exe, "-uninstall -silent") { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(120_000);
            File.Delete(mark);
            Log("PawnIO quitado");
        }
        catch { }
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
        // conexión en 20 s; cada pedido 2 min como mucho hasta que llegan los encabezados; la descarga se vigila por lectura (ver Download)
        var h = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20), PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            { Timeout = TimeSpan.FromMinutes(2) };   // con ResponseHeadersRead solo cuenta hasta los encabezados
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

    /// <summary>
    /// Baja un archivo. Si la conexión se corta, espera y sigue desde donde iba (pide el resto con Range);
    /// si un Reintentar empieza de nuevo, también aprovecha lo que ya estaba bajado. La firma se revisa después,
    /// así que un trozo malo nunca se instala.
    /// </summary>
    static async Task Download(HttpClient http, Asset a, string dest, bool quiet = false)
    {
        long done = File.Exists(dest) ? new FileInfo(dest).Length : 0;
        if (done >= a.Size) { File.Delete(dest); done = 0; }
        var last = -1;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, a.Url);
                if (done > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(done, null);
                using var head = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, head.Token);
                res.EnsureSuccessStatusCode();
                if (done > 0 && res.StatusCode != System.Net.HttpStatusCode.PartialContent) done = 0;   // el servidor manda todo de nuevo
                var total = done + (res.Content.Headers.ContentLength ?? a.Size - done);
                await using var src = await res.Content.ReadAsStreamAsync();
                await using var dst = new FileStream(dest, done > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write);
                var buf = new byte[1 << 16];
                while (true)
                {
                    using var stall = new CancellationTokenSource(TimeSpan.FromSeconds(30));   // 30 s sin recibir nada = conexión muerta
                    int n;
                    try { n = await src.ReadAsync(buf, stall.Token); }
                    catch (OperationCanceledException) { throw new TimeoutException("la descarga se quedó parada."); }
                    if (n == 0) break;
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (TestCut is { } cut && done >= cut) { TestCut = null; throw new HttpIOException(HttpRequestError.ResponseEnded, "corte de prueba"); }
                    var pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (!quiet && pct != last)
                    {
                        Ui.Show($"Descargando Vigía {Ver(a.Name)}", $"{Mb(done)} de {Mb(total)} MB · {pct} %", total > 0 ? (double)done / total : double.NaN);
                        last = pct;
                    }
                }
                if (done < a.Size) throw new HttpIOException(HttpRequestError.ResponseEnded, "la descarga llegó incompleta.");
                return;
            }
            catch (Exception e) when (IsNet(e) && attempt < 6)
            {
                Log($"descarga cortada en {done} bytes, intento {attempt}: {e.Message}");
                if (!quiet) Ui.Show("Se cortó la conexión", "Reintentando en unos segundos.", a.Size > 0 ? (double)done / a.Size : double.NaN);
                await Task.Delay(TimeSpan.FromSeconds(attempt switch { 1 => 2, 2 => 4, 3 => 8, _ => 15 }));
                last = -1;
            }
        }
    }

    // para probar los cortes sin desenchufar nada: PUENTE_TEST_CUT=bytes corta la descarga una vez al llegar ahí
#if PUENTE_TEST
    static long? TestCut = long.TryParse(Environment.GetEnvironmentVariable("PUENTE_TEST_CUT"), out var c) ? c : null;
#else
    static long? TestCut = null;   // la Puente publicada no lee variables de prueba
#endif

    /// <summary>Solo para pruebas: baja el zip de la última versión a una carpeta y revisa la firma. No instala nada.</summary>
    static async Task<int> FetchTest(string dir)
    {
        using var http = Http();
        var rel = await Net(() => Latest(http));
        var a = rel.Asset($"Vigia-{rel.Version}-win-x64.zip");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, a.Name);
        await Download(http, a, dest, quiet: true);
        var ok = Verify(dest, await Net(() => http.GetStringAsync(rel.Asset(a.Name + ".sig").Url)));
        Console.WriteLine($"{a.Name} {new FileInfo(dest).Length}/{a.Size} firma {(ok ? "ok" : "MAL")}");
        return ok ? 0 : 1;
    }

    // ---------------------------------------------------------------- firma

    const long MaxUnpacked = 2L << 30;   // 2 GB: Vigía ocupa ~200 MB; más que eso es un paquete raro

    /// <summary>
    /// Descomprime solo si cada entrada cae dentro de <paramref name="dir"/>: sin rutas absolutas, sin «..», sin «:»
    /// (flujos alternativos de NTFS) y sin pasar de <see cref="MaxUnpacked"/>. La firma ya se comprobó antes, esto es
    /// una segunda barrera por si algún día se firma un paquete mal hecho.
    /// </summary>
    static void SafeExtract(string zip, string dir)
    {
        var root = Path.GetFullPath(dir).TrimEnd('\\') + '\\';
        using var z = ZipFile.OpenRead(zip);
        long total = 0;
        var plan = new List<(ZipArchiveEntry e, string to)>();
        foreach (var e in z.Entries)
        {
            var n = e.FullName.Replace('/', '\\');
            if (n.Length == 0 || Path.IsPathRooted(n) || n.Contains(':') || n.Split('\\').Any(p => p == ".."))
                throw new InvalidOperationException($"el paquete trae una ruta que no se acepta: {e.FullName}");
            var to = Path.GetFullPath(Path.Combine(root, n));
            if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"el paquete intenta escribir fuera de la carpeta: {e.FullName}");
            total += e.Length;
            if (e.Length < 0 || total > MaxUnpacked) throw new InvalidOperationException("el paquete es más grande de lo normal; no se instaló.");
            plan.Add((e, to));
        }
        Directory.CreateDirectory(root);
        foreach (var (e, to) in plan)
        {
            if (IsDir(e)) { Directory.CreateDirectory(to); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            e.ExtractToFile(to, overwrite: false);
        }
        static bool IsDir(ZipArchiveEntry e) => e.FullName.EndsWith('/') || e.FullName.EndsWith('\\');
    }

    /// <summary>Prueba SafeExtract con paquetes buenos y malos hechos en una carpeta temporal. No toca la instalación.</summary>
    static int ZipTest(string? real)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "puente-ziptest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        int fails = 0;
        if (real is not null)
        {
            // un paquete publicado de verdad tiene que seguir entrando entero
            var dest = Path.Combine(tmp, "real");
            try
            {
                SafeExtract(real, dest);
                using var z = ZipFile.OpenRead(real);
                var files = z.Entries.Count(e => !e.FullName.EndsWith('/'));
                var got = Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Length;
                var ok = got == files && File.Exists(Path.Combine(dest, "Vigia.exe"));
                if (!ok) fails++;
                Console.WriteLine($"{(ok ? "OK  " : "FAIL")} real: {got}/{files} archivos, Vigia.exe {(File.Exists(Path.Combine(dest, "Vigia.exe")) ? "sí" : "no")}");
            }
            catch (Exception ex) { fails++; Console.WriteLine($"FAIL real: {ex.Message}"); }
        }
        void Case(string name, string[] entries, bool shouldPass)
        {
            var zp = Path.Combine(tmp, name + ".zip");
            using (var z = ZipFile.Open(zp, ZipArchiveMode.Create))
                foreach (var en in entries) { using var w = new StreamWriter(z.CreateEntry(en).Open()); w.Write("x"); }
            var dest = Path.Combine(tmp, name);
            bool ok; string why = "";
            try { SafeExtract(zp, dest); ok = true; } catch (InvalidOperationException ex) { ok = false; why = ex.Message; }
            var outside = File.Exists(Path.Combine(tmp, "fuera.txt")) || File.Exists(Path.Combine(Path.GetPathRoot(tmp)!, "fuera.txt"));
            var good = ok == shouldPass && !outside;
            if (!good) fails++;
            Console.WriteLine($"{(good ? "OK  " : "FAIL")} {name}: {(ok ? "extraído" : "rechazado")} {why}");
        }
        Case("normal", ["Vigia.exe", "wwwroot/app.js", "wwwroot/i18n/en.json"], true);
        Case("puntos", ["Vigia.exe", "../fuera.txt"], false);
        Case("puntos-hondo", ["a/b/../../../fuera.txt"], false);
        Case("absoluta", ["C:/fuera.txt"], false);
        Case("barra-inicial", ["/fuera.txt"], false);
        Case("flujo-ntfs", ["Vigia.exe:oculto"], false);
        Case("barra-invertida", ["..\\fuera.txt"], false);
        try { Directory.Delete(tmp, true); } catch { }
        Console.WriteLine(fails == 0 ? "zip-test: todo bien" : $"zip-test: {fails} fallos");
        return fails == 0 ? 0 : 1;
    }

#if PUENTE_TEST
    // solo en la compilación de pruebas: una clave de prueba hecha en la carpeta temporal (nunca la de publicar)
    static string Key => Environment.GetEnvironmentVariable("PUENTE_TEST_PUBKEY") is { Length: > 40 } k ? k : PublicKey;
#else
    static string Key => PublicKey;
#endif

    static bool VerifyBytes(byte[] data, string sigB64)
    {
        if (PublicKey.StartsWith("__")) throw new InvalidOperationException("este Puente se compiló sin clave pública.");
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Key), out _);
        byte[] sig;
        try { sig = Convert.FromBase64String(sigB64.Trim()); } catch (FormatException) { return false; }
        return ec.VerifyData(data, sig, HashAlgorithmName.SHA256);
    }

    static bool Verify(string file, string sigB64)
    {
        if (PublicKey.StartsWith("__")) throw new InvalidOperationException("este Puente se compiló sin clave pública.");
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Key), out _);
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
        LoadKey(ec, privPath);   // .pem o .dpapi (cifrada con tu cuenta); nunca se escribe en otro sitio
        using var fs = File.OpenRead(file);
        File.WriteAllText(file + ".sig", Convert.ToBase64String(ec.SignData(fs, HashAlgorithmName.SHA256)));
        return 0;
    }

    // ---------------------------------------------------------------- utilidades

    static Version? FileVersion(string exe)
    {
        if (!File.Exists(exe)) return null;
        var v = FileVersionInfo.GetVersionInfo(exe);
        return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart);
    }

    internal static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>¿Cambiar de <paramref name="cur"/> a <paramref name="next"/> sería volver atrás? Sin versión conocida, no.</summary>
    internal static bool IsDowngrade(Version? cur, Version? next) => cur is not null && next is not null && next < cur;

    static int? WaitPid(string[] a)
    {
        var i = Array.IndexOf(a, "--wait");
        return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out var p) ? p : null;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static string Sha256(string file) { using var fs = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(fs)); }

    static string Ver(string asset) => System.Text.RegularExpressions.Regex.Match(asset, @"\d+\.\d+\.\d+").Value;

    /// <summary>Pregunta con dos botones. Sin ventana (--quiet) solo se acepta si además se pasó --yes.</summary>
    static bool Ask(string q, string detail, string yes) => Ui.Active ? Ui.Choose(q, detail, yes, "Cancelar") == 0 : Yes;

    static void Log(string s)
    {
        try { var f = LogFile; Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}{Environment.NewLine}"); } catch { }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string?>))]
partial class CheckJson : System.Text.Json.Serialization.JsonSerializerContext { }
