// VigiaFirma: herramienta del EQUIPO DE FIRMA de Vigía (separado del PC de desarrollo, sin red).
//
//   VigiaFirma nueva <carpeta> <nombre>                   clave nueva (p. ej. B): nunca se escribe en claro
//   VigiaFirma importar <clave.pem> <carpeta> <nombre> [<esperada.publica.txt>]
//                                                         pasa una clave existente (A) al mismo formato; no toca el .pem
//   VigiaFirma publica <archivo>                          clave pública y huella de un .dpapi, .respaldo.pem o .publica.txt
//   VigiaFirma comprobar-respaldo <respaldo.pem> <publica.txt>   el respaldo abre con la frase y es esa clave
//   VigiaFirma restaurar <respaldo.pem> <salida.dpapi>    recupera la clave de uso diario desde el respaldo
//   VigiaFirma verificar <carpeta-versión> <publica.txt> [<huellas.txt>]
//                                                         comprueba firmas y manifiesto con UNA clave pública concreta
//   VigiaFirma huellas <carpeta-versión>                  SHA-256 de las cuatro piezas (formato de candidato.txt)
//
// Por cada clave deja tres archivos:
//   vigia-<nombre>.dpapi         la de uso diario, cifrada con DPAPI de ESTA cuenta en ESTE equipo (otra cuenta u otro
//                                equipo no la pueden abrir) y con permisos solo para esta cuenta. Puente --sign-release la usa.
//   vigia-<nombre>.respaldo.pem  respaldo cifrado con una frase (PKCS#8, AES-256-CBC, PBKDF2-SHA256 600 000 vueltas).
//                                Sirve en cualquier equipo con la frase. Va a dos memorias USB guardadas en sitios distintos.
//   vigia-<nombre>.publica.txt   la clave PÚBLICA y su huella. Es lo único que vuelve al PC de desarrollo.
// Ninguna orden imprime, registra ni deja en disco la clave privada en claro. Códigos: 0 bien, 1 problema, 2 uso.
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

static class Program
{
    const int MinFrase = 16;
    static readonly Regex NombreRx = new("^[A-Za-z0-9-]{1,16}$", RegexOptions.CultureInvariant);
    static readonly Regex HexRx = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
    static readonly PbeParameters Pbe = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);

    static int Main(string[] a)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return (a.FirstOrDefault(), a.Length) switch
            {
                ("nueva", 3) => Nueva(a[1], a[2]),
                ("importar", 4 or 5) => Importar(a[1], a[2], a[3], a.Length == 5 ? a[4] : null),
                ("publica", 2) => Publica(a[1]),
                ("comprobar-respaldo", 3) => ComprobarRespaldo(a[1], a[2]),
                ("restaurar", 3) => Restaurar(a[1], a[2]),
                ("verificar", 3 or 4) => Verificar(a[1], a[2], a.Length == 4 ? a[3] : null),
                ("huellas", 2) => Huellas(a[1]),
                _ => Uso(),
            };
        }
        catch (Exception e) { Console.Error.WriteLine("Error: " + e.Message); return 1; }
    }

    static int Uso()
    {
        Console.Error.WriteLine("Uso: VigiaFirma nueva <carpeta> <nombre> | importar <clave.pem> <carpeta> <nombre> [<esperada.publica.txt>]\n" +
            "     | publica <archivo> | comprobar-respaldo <respaldo.pem> <publica.txt> | restaurar <respaldo.pem> <salida.dpapi>\n" +
            "     | verificar <carpeta-versión> <publica.txt> [<huellas.txt>] | huellas <carpeta-versión>");
        return 2;
    }

    // ---------------------------------------------------------------- claves

    static int Nueva(string dir, string nombre)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);   // se genera aquí, en memoria
        return Crear(ec, dir, nombre);
    }

    static int Importar(string pem, string dir, string nombre, string? esperada)
    {
        using var ec = ECDsa.Create();
        var raw = File.ReadAllBytes(pem);   // solo se lee; el .pem no se cambia, no se mueve y no se borra
        var chars = Encoding.ASCII.GetChars(raw);
        try { ec.ImportFromPem(chars); }
        finally { CryptographicOperations.ZeroMemory(raw); Array.Clear(chars); }
        if (ec.KeySize != 256) { Console.Error.WriteLine("No es una clave P-256."); return 1; }
        if (esperada is not null)
        {
            var (_, pubEsp) = LeerPublica(esperada);
            if (Pub(ec) != pubEsp) { Console.Error.WriteLine("Esa clave no es la esperada (" + Path.GetFileName(esperada) + "): no se importa nada."); return 1; }
        }
        return Crear(ec, dir, nombre);
    }

    static int Crear(ECDsa ec, string dir, string nombre)
    {
        if (!NombreRx.IsMatch(nombre)) { Console.Error.WriteLine("Nombre de clave: letras, números o guiones (máx. 16)."); return 2; }
        Directory.CreateDirectory(dir);
        string fd = Path.Combine(dir, $"vigia-{nombre}.dpapi"), fr = Path.Combine(dir, $"vigia-{nombre}.respaldo.pem"),
               fp = Path.Combine(dir, $"vigia-{nombre}.publica.txt");
        foreach (var f in new[] { fd, fr, fp })
            if (File.Exists(f)) { Console.Error.WriteLine("Ya existe " + f + "; no se sobrescribe nada."); return 1; }
        var f1 = LeerFrase($"Frase del respaldo de la clave {nombre} ({MinFrase} caracteres o más; no se muestra): ");
        var f2 = LeerFrase("Repítela: ");
        var hechos = new List<string>();
        try
        {
            if (f1.Length < MinFrase) { Console.Error.WriteLine($"La frase tiene que tener {MinFrase} caracteres o más. No se creó nada."); return 1; }
            if (!f1.AsSpan().SequenceEqual(f2)) { Console.Error.WriteLine("Las dos frases no coinciden. No se creó nada."); return 1; }
            var pub = Pub(ec);
            // 1. la de uso diario: PEM en memoria -> DPAPI -> disco. El PEM en claro nunca toca el disco.
            var der = ec.ExportPkcs8PrivateKey();
            var pemChars = PemEncoding.Write("PRIVATE KEY", der);
            var plain = Encoding.ASCII.GetBytes(pemChars);
            try { WriteOwnOnly(fd, Dpapi(plain, protect: true)); hechos.Add(fd); }
            finally { CryptographicOperations.ZeroMemory(der); Array.Clear(pemChars); CryptographicOperations.ZeroMemory(plain); }
            // 2. el respaldo: cifrado con la frase (estándar PKCS#8, se abre con cualquier herramienta compatible)
            var enc = ec.ExportEncryptedPkcs8PrivateKeyPem(f1, Pbe);
            WriteOwnOnly(fr, Encoding.ASCII.GetBytes(enc)); hechos.Add(fr);
            // 3. comprobaciones: los dos abren y son la misma clave
            using (var r = ECDsa.Create()) { r.ImportFromEncryptedPem(File.ReadAllText(fr), f1); if (Pub(r) != pub) throw new CryptographicException("el respaldo no coincide"); }
            using (var d = ECDsa.Create()) { LoadDpapi(d, fd); if (Pub(d) != pub) throw new CryptographicException("la clave cifrada no coincide"); }
            File.WriteAllText(fp, TextoPublica(nombre, pub), new UTF8Encoding(false)); hechos.Add(fp);
            Console.WriteLine($"Clave {nombre} lista en {dir}:");
            Console.WriteLine($"  {Path.GetFileName(fd)}         uso diario (solo esta cuenta en este equipo)");
            Console.WriteLine($"  {Path.GetFileName(fr)}  respaldo con frase -> dos USB en sitios distintos");
            Console.WriteLine($"  {Path.GetFileName(fp)}   PÚBLICA -> es lo único que va al PC de desarrollo");
            Imprimir(nombre, pub);
            return 0;
        }
        catch
        {
            foreach (var f in hechos) try { File.Delete(f); } catch { }
            throw;
        }
        finally { Array.Clear(f1); Array.Clear(f2); }
    }

    static int Publica(string file)
    {
        if (file.EndsWith(".publica.txt", StringComparison.OrdinalIgnoreCase))
        { var (n, p) = LeerPublica(file); Imprimir(n, p); return 0; }
        using var ec = ECDsa.Create();
        if (file.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)) LoadDpapi(ec, file);
        else
        {
            var f = LeerFrase("Frase del respaldo: ");
            try { ec.ImportFromEncryptedPem(File.ReadAllText(file), f); } finally { Array.Clear(f); }
        }
        Imprimir(Path.GetFileName(file), Pub(ec));
        return 0;
    }

    static int ComprobarRespaldo(string resp, string publica)
    {
        var (nombre, esperada) = LeerPublica(publica);
        var f = LeerFrase("Frase del respaldo: ");
        using var ec = ECDsa.Create();
        try { ec.ImportFromEncryptedPem(File.ReadAllText(resp), f); }
        catch (CryptographicException) { Console.Error.WriteLine("El respaldo no abre con esa frase (o está dañado)."); return 1; }
        finally { Array.Clear(f); }
        if (Pub(ec) != esperada) { Console.Error.WriteLine($"El respaldo abre, pero NO es la clave {nombre}."); return 1; }
        Console.WriteLine($"Respaldo bien: abre con la frase y es la clave {nombre} (huella {Huella(esperada)}).");
        return 0;
    }

    static int Restaurar(string resp, string salida)
    {
        if (File.Exists(salida)) { Console.Error.WriteLine("Ya existe " + salida + "; no se sobrescribe."); return 1; }
        var f = LeerFrase("Frase del respaldo: ");
        using var ec = ECDsa.Create();
        try { ec.ImportFromEncryptedPem(File.ReadAllText(resp), f); }
        catch (CryptographicException) { Console.Error.WriteLine("El respaldo no abre con esa frase (o está dañado)."); return 1; }
        finally { Array.Clear(f); }
        var der = ec.ExportPkcs8PrivateKey(); var pem = PemEncoding.Write("PRIVATE KEY", der); var plain = Encoding.ASCII.GetBytes(pem);
        try { WriteOwnOnly(salida, Dpapi(plain, protect: true)); }
        finally { CryptographicOperations.ZeroMemory(der); Array.Clear(pem); CryptographicOperations.ZeroMemory(plain); }
        using (var d = ECDsa.Create()) { LoadDpapi(d, salida); if (Pub(d) != Pub(ec)) { File.Delete(salida); Console.Error.WriteLine("La clave restaurada no coincide."); return 1; } }
        Console.WriteLine("Restaurada en " + salida + " (solo esta cuenta en este equipo).");
        Imprimir(Path.GetFileName(resp), Pub(ec));
        return 0;
    }

    // ---------------------------------------------------------------- comprobación de una versión firmada

    /// <summary>
    /// Comprueba una carpeta de versión con UNA clave pública concreta (no con la lista de confianza de Puente):
    /// manifiesto firmado, de Vigía y con la versión de su zip; zip y Puente.exe con el tamaño y el SHA-256 del manifiesto
    /// y su firma; Vigia-Setup.exe igual a Puente.exe y firmado. Con huellas.txt (llevado aparte), además, cada pieza tiene
    /// que ser exactamente la anotada: así no se puede cambiar el manifiesto por otro firmado (otra versión, otro zip).
    /// </summary>
    static int Verificar(string dir, string publica, string? huellas)
    {
        var (nombre, pub) = LeerPublica(publica);
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pub), out _);
        int bad = 0;
        void Res(bool ok, string what) { if (!ok) bad++; Console.WriteLine((ok ? "OK   " : "FAIL ") + what); }
        bool Firma(string f) => File.Exists(f) && File.Exists(f + ".sig") && Sig(File.ReadAllText(f + ".sig")) is { } s
            && ec.VerifyData(File.ReadAllBytes(f), s, HashAlgorithmName.SHA256);

        var man = Path.Combine(dir, "vigia-manifest.json");
        Res(Firma(man), $"el manifiesto está firmado con la clave {nombre}");
        JsonObject? o = null;
        try { o = JsonNode.Parse(File.ReadAllBytes(man)) as JsonObject; } catch { }
        var ver = (string?)o?["version"];
        Res(o is not null && (int?)o["schema"] == 1 && (string?)o["product"] == "Vigia" && ver is not null && Regex.IsMatch(ver, @"^\d+\.\d+\.\d+$"),
            $"el manifiesto es de Vigía ({ver ?? "?"})");
        var zipName = $"Vigia-{ver}-win-x64.zip";
        bool Pieza(string key, string name)
        {
            var p = o?[key] as JsonObject; var f = Path.Combine(dir, name);
            return p is not null && (string?)p["name"] == name && File.Exists(f) && (long?)p["size"] == new FileInfo(f).Length
                && string.Equals((string?)p["sha256"], Sha(f), StringComparison.OrdinalIgnoreCase);
        }
        Res(Pieza("package", zipName), $"{zipName}: nombre, tamaño y SHA-256 del manifiesto");
        Res(Firma(Path.Combine(dir, zipName)), $"{zipName}: firmado con la clave {nombre}");
        Res(Pieza("puente", "Puente.exe"), "Puente.exe: tamaño y SHA-256 del manifiesto");
        Res(Firma(Path.Combine(dir, "Puente.exe")), $"Puente.exe: firmado con la clave {nombre}");
        var setup = Path.Combine(dir, "Vigia-Setup.exe");
        Res(File.Exists(setup) && File.Exists(Path.Combine(dir, "Puente.exe")) && Sha(setup) == Sha(Path.Combine(dir, "Puente.exe")) && Firma(setup),
            $"Vigia-Setup.exe = Puente.exe y firmado con la clave {nombre}");
        if (huellas is not null)
        {
            var want = LeerHuellas(huellas);
            var names = new[] { zipName, "Puente.exe", "Vigia-Setup.exe", "vigia-manifest.json" };
            Res(want.Count == 4 && names.All(n => want.TryGetValue(n, out var h) && File.Exists(Path.Combine(dir, n)) && h == Sha(Path.Combine(dir, n))),
                "las cuatro piezas son exactamente las de " + Path.GetFileName(huellas));
        }
        Console.WriteLine(bad == 0 ? $"Versión {ver}: todo bien con la clave {nombre} (huella {Huella(pub)})." : $"{bad} fallo(s): NO sirve.");
        return bad == 0 ? 0 : 1;
    }

    static int Huellas(string dir)
    {
        var zips = Directory.GetFiles(dir, "Vigia-*-win-x64.zip");
        if (zips.Length != 1) { Console.Error.WriteLine("Tiene que haber un solo Vigia-*-win-x64.zip."); return 1; }
        foreach (var f in new[] { zips[0], Path.Combine(dir, "Puente.exe"), Path.Combine(dir, "Vigia-Setup.exe"), Path.Combine(dir, "vigia-manifest.json") })
            Console.WriteLine($"sha256 {Sha(f)} *{Path.GetFileName(f)}");
        return 0;
    }

    // ---------------------------------------------------------------- utilidades

    internal static Dictionary<string, string> LeerHuellas(string file)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"^(?:sha256 )?([0-9a-fA-F]{64}) [ *]?(\S+)\s*$", RegexOptions.Multiline))
            d[m.Groups[2].Value] = m.Groups[1].Value.ToLowerInvariant();
        return d;
    }

    static string TextoPublica(string nombre, string pub) =>
        $"vigia-clave-publica 1\nnombre {nombre}\npublica {pub}\nhuella {Huella(pub)}\n";

    /// <summary>Lee un .publica.txt y comprueba que la clave es P-256 y que la huella anotada es la suya.</summary>
    internal static (string nombre, string pub) LeerPublica(string file)
    {
        var t = File.ReadAllText(file);
        string? G(string k) => Regex.Match(t, "^" + k + @" (\S+)\s*$", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;
        var nombre = G("nombre"); var pub = G("publica"); var h = G("huella");
        if (!t.StartsWith("vigia-clave-publica 1") || nombre is null || pub is null || h is null)
            throw new InvalidDataException(Path.GetFileName(file) + " no es un archivo de clave pública de Vigía.");
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pub), out _);
        if (ec.KeySize != 256 || Pub(ec) != pub) throw new InvalidDataException(Path.GetFileName(file) + ": la clave no es P-256.");
        if (!string.Equals(h, Huella(pub), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Path.GetFileName(file) + ": la huella no corresponde a la clave.");
        return (nombre, pub);
    }

    static string Pub(ECDsa ec) => Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
    static string Huella(string pub) => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(pub))).ToLowerInvariant();
    static string Sha(string f) { using var s = File.OpenRead(f); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
    static byte[]? Sig(string b64) { try { var s = Convert.FromBase64String(b64.Trim()); return s.Length == 64 ? s : null; } catch { return null; } }

    static void Imprimir(string nombre, string pub)
    {
        var h = Huella(pub);
        Console.WriteLine($"clave {nombre}");
        Console.WriteLine("  pública: " + pub);
        Console.WriteLine("  huella:  " + string.Join(" ", Enumerable.Range(0, 16).Select(i => h.Substring(i * 4, 4))));
    }

    static char[] LeerFrase(string prompt)
    {
        Console.Error.Write(prompt);
        if (Console.IsInputRedirected) return (Console.ReadLine() ?? "").ToCharArray();   // solo para las pruebas automáticas
        var buf = new List<char>();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (buf.Count > 0) buf.RemoveAt(buf.Count - 1); continue; }
            if (!char.IsControl(k.KeyChar)) buf.Add(k.KeyChar);
        }
        Console.Error.WriteLine();
        var r = buf.ToArray();
        for (int i = 0; i < buf.Count; i++) buf[i] = '\0';
        return r;
    }

    static void LoadDpapi(ECDsa ec, string path)
    {
        var raw = File.ReadAllBytes(path);
        var plain = Dpapi(raw, protect: false);
        var chars = Encoding.ASCII.GetChars(plain);
        try { ec.ImportFromPem(chars); }
        finally { CryptographicOperations.ZeroMemory(plain); Array.Clear(chars); }
    }

    /// <summary>Archivo nuevo con permisos propios (sin heredar): solo esta cuenta. No sobrescribe.</summary>
    static void WriteOwnOnly(string path, byte[] data)
    {
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        using var fs = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, acl);
        fs.Write(data);
    }

    // DPAPI de la cuenta actual, con la misma entropía que Puente (Native.cs): Puente --sign-release abre estos .dpapi
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int cb; public IntPtr pb; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob d, string? desc, ref Blob ent, IntPtr r, IntPtr p, int fl, out Blob o);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob d, IntPtr desc, ref Blob ent, IntPtr r, IntPtr p, int fl, out Blob o);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    static readonly byte[] Entropy = "Vigia.release-key.v1"u8.ToArray();

    static byte[] Dpapi(byte[] input, bool protect)
    {
        var hIn = GCHandle.Alloc(input, GCHandleType.Pinned); var hEnt = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var i = new Blob { cb = input.Length, pb = hIn.AddrOfPinnedObject() };
            var e = new Blob { cb = Entropy.Length, pb = hEnt.AddrOfPinnedObject() };
            bool ok = protect ? CryptProtectData(ref i, "Vigia release key", ref e, IntPtr.Zero, IntPtr.Zero, 1, out var o)
                              : CryptUnprotectData(ref i, IntPtr.Zero, ref e, IntPtr.Zero, IntPtr.Zero, 1, out o);
            if (!ok) throw new CryptographicException("Windows no pudo " + (protect ? "proteger" : "abrir") + " la clave (error " + Marshal.GetLastWin32Error() + "). ¿Es de otra cuenta u otro equipo?");
            try { var r = new byte[o.cb]; Marshal.Copy(o.pb, r, 0, o.cb); return r; }
            finally { for (int k = 0; k < o.cb; k++) Marshal.WriteByte(o.pb, k, 0); LocalFree(o.pb); }
        }
        finally { hIn.Free(); hEnt.Free(); }
    }
}
