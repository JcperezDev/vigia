// Verificación Ed25519 (RFC 8032, sección 5.1.7) para las versiones firmadas con la clave de relevo B, que vive en el
// agente SSH de Bitwarden del dueño. SOLO verifica: aquí no hay claves privadas ni firma. La clave A (ECDSA P-256) sigue
// su camino de siempre; esto solo se usa cuando una clave de confianza es Ed25519.
//
// .NET 10 no trae Ed25519, por eso está escrito aquí, sobre BigInteger, siguiendo el texto del RFC. Todo lo que entra
// (clave pública, firma, datos) es público, así que no hace falta tiempo constante. Es estricto:
//   - clave pública y R con codificación canónica (y < p); S < L (sin maleabilidad);
//   - rechaza claves públicas de orden pequeño;
//   - ecuación sin cofactor [S]B = R + [k]A, comparando la codificación de R (como ref10/libsodium).
// Probado con los vectores del RFC 8032 y con firmas de OpenSSL (tools/signing/ed25519-test.js). Sin auditoría externa.
//
// Formato de una firma de B (texto, cabe de sobra en los 4096 bytes que Puente acepta para un .sig):
//   vigia-firma-ed25519 1
//   declaracion <base64 de la declaración>
//   firma <base64 de los 64 bytes de la firma Ed25519 de la declaración>
// La declaración (lo que firma Bitwarden; una sola aprobación por versión) es texto UTF-8 con saltos LF:
//   vigia-declaracion-ed25519 1          <- prefijo de dominio: no puede confundirse con un login SSH ni con SSHSIG
//   version 0.21.3
//   sha256 <64 hex> <nombre>             <- una línea por pieza (zip, Puente.exe, Vigia-Setup.exe, manifiesto)
// Un archivo vale si su SHA-256 está en una declaración firmada por una clave Ed25519 de confianza. El manifiesto
// firmado sigue mandando sobre versión, mínima, tamaños y huellas, como con A.
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

internal static class Ed25519Firma
{
    internal const string SigHeader = "vigia-firma-ed25519 1";
    internal const string DeclHeader = "vigia-declaracion-ed25519 1";
    static readonly byte[] SpkiPrefix = [0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00];

    // ---------------------------------------------------------------- formato de Vigía

    /// <summary>Clave pública en SubjectPublicKeyInfo (base64) -> los 32 bytes de Ed25519, o null si no es Ed25519.</summary>
    internal static byte[]? RawFromSpki(string b64)
    {
        byte[] d;
        try { d = Convert.FromBase64String(b64.Trim()); } catch (FormatException) { return null; }
        if (d.Length != 44 || !d.AsSpan(0, 12).SequenceEqual(SpkiPrefix)) return null;
        var raw = d[12..];
        return Decode(raw) is null ? null : raw;   // tiene que ser un punto válido de la curva
    }

    internal static bool IsEd25519Spki(string b64) => RawFromSpki(b64) is not null;

    internal static string SpkiFromRaw(byte[] raw32) => Convert.ToBase64String([.. SpkiPrefix, .. raw32]);

    internal static bool LooksLikeSig(string sigText) => sigText.TrimStart().StartsWith(SigHeader + "\n", StringComparison.Ordinal)
        || sigText.TrimStart().StartsWith(SigHeader + "\r\n", StringComparison.Ordinal);

    static readonly Regex DeclLine = new(@"^(version \d{1,5}\.\d{1,5}\.\d{1,5}|sha256 [0-9a-f]{64} [A-Za-z0-9._-]{1,128})$", RegexOptions.CultureInvariant);

    /// <summary>Declaración de una versión: cabecera, versión y una línea por pieza (en el orden dado).</summary>
    internal static byte[] Declaracion(string version, IEnumerable<(string Sha256Hex, string Name)> piezas)
    {
        var sb = new StringBuilder(DeclHeader + "\nversion " + version + "\n");
        foreach (var (h, n) in piezas) sb.Append("sha256 ").Append(h.ToLowerInvariant()).Append(' ').Append(n).Append('\n');
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        if (ParseDeclaracion(bytes) is null) throw new ArgumentException("declaración no válida");
        return bytes;
    }

    /// <summary>Lee una declaración estricta: devuelve (versión, huellas) o null si no sigue el formato al pie de la letra.</summary>
    internal static (string Version, List<(string Sha, string Name)> Piezas)? ParseDeclaracion(byte[] decl)
    {
        if (decl.Length is 0 or > 2048) return null;
        string text;
        try { text = new UTF8Encoding(false, true).GetString(decl); } catch (DecoderFallbackException) { return null; }
        if (!text.EndsWith('\n')) return null;
        var lines = text[..^1].Split('\n');
        if (lines.Length < 3 || lines[0] != DeclHeader || !lines[1].StartsWith("version ", StringComparison.Ordinal)) return null;
        var piezas = new List<(string, string)>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (!DeclLine.IsMatch(lines[i])) return null;
            if (i > 1) { if (!lines[i].StartsWith("sha256 ", StringComparison.Ordinal)) return null; piezas.Add((lines[i][7..71], lines[i][72..])); }
        }
        return (lines[1][8..], piezas);
    }

    internal static string SigText(byte[] decl, byte[] sig64) =>
        SigHeader + "\ndeclaracion " + Convert.ToBase64String(decl) + "\nfirma " + Convert.ToBase64String(sig64) + "\n";

    /// <summary>Lee un .sig de B: (declaración, firma) o null.</summary>
    internal static (byte[] Decl, byte[] Sig)? ParseSig(string sigText)
    {
        if (sigText.Length > 4096) return null;
        var lines = sigText.Replace("\r\n", "\n").Trim().Split('\n');
        if (lines.Length != 3 || lines[0] != SigHeader || !lines[1].StartsWith("declaracion ", StringComparison.Ordinal)
            || !lines[2].StartsWith("firma ", StringComparison.Ordinal)) return null;
        try
        {
            var decl = Convert.FromBase64String(lines[1][12..]);
            var sig = Convert.FromBase64String(lines[2][6..]);
            return sig.Length == 64 && ParseDeclaracion(decl) is not null ? (decl, sig) : null;
        }
        catch (FormatException) { return null; }
    }

    /// <summary>
    /// ¿Vale esta firma de B para unos datos cuyo SHA-256 es <paramref name="sha256Hex"/>? La declaración tiene que estar
    /// firmada por la clave Ed25519 (32 bytes) y contener esa huella.
    /// </summary>
    internal static bool VerifyHash(string sha256Hex, string sigText, byte[] pubRaw)
    {
        if (ParseSig(sigText) is not { } s || ParseDeclaracion(s.Decl) is not { } d) return false;
        if (!d.Piezas.Any(p => string.Equals(p.Sha, sha256Hex, StringComparison.OrdinalIgnoreCase))) return false;
        return Verify(pubRaw, s.Decl, s.Sig);
    }

    internal static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    internal static string Sha256Hex(Stream s) => Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();

    // ---------------------------------------------------------------- Ed25519 (RFC 8032)

    static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
    static readonly BigInteger D = Mod(-121665 * Inv(121666));
    static readonly BigInteger SqrtM1 = BigInteger.ModPow(2, (P - 1) / 4, P);
    static readonly Pt B = Decode(Le(Mod(4 * Inv(5)), 32))!.Value;   // punto base: y = 4/5, x par
    static readonly Pt Zero = new(0, 1, 1, 0);

    readonly record struct Pt(BigInteger X, BigInteger Y, BigInteger Z, BigInteger T);

    static BigInteger Mod(BigInteger a) { var r = a % P; return r.Sign < 0 ? r + P : r; }
    static BigInteger Inv(BigInteger a) => BigInteger.ModPow(Mod(a), P - 2, P);
    static BigInteger FromLe(ReadOnlySpan<byte> b) => new(b, isUnsigned: true, isBigEndian: false);
    static byte[] Le(BigInteger v, int n)
    {
        var o = new byte[n];
        if (!v.TryWriteBytes(o, out _, isUnsigned: true, isBigEndian: false)) throw new ArgumentOutOfRangeException(nameof(v));
        return o;
    }

    static Pt Add(Pt p, Pt q)
    {
        var a = Mod((p.Y - p.X) * (q.Y - q.X));
        var b = Mod((p.Y + p.X) * (q.Y + q.X));
        var c = Mod(p.T * 2 * D * q.T);
        var d = Mod(p.Z * 2 * q.Z);
        BigInteger e = b - a, f = d - c, g = d + c, h = b + a;
        return new(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    static Pt Mul(BigInteger s, Pt p)
    {
        var q = Zero;
        for (int i = (int)s.GetBitLength() - 1; i >= 0; i--)
        {
            q = Add(q, q);
            if (!(s >> i).IsEven) q = Add(q, p);
        }
        return q;
    }

    static Pt Neg(Pt p) => new(Mod(-p.X), p.Y, p.Z, Mod(-p.T));

    static byte[] Encode(Pt p)
    {
        var zi = Inv(p.Z);
        BigInteger x = Mod(p.X * zi), y = Mod(p.Y * zi);
        var o = Le(y, 32);
        if (!x.IsEven) o[31] |= 0x80;
        return o;
    }

    static Pt? Decode(ReadOnlySpan<byte> s)
    {
        if (s.Length != 32) return null;
        var b = s.ToArray();
        int sign = b[31] >> 7;
        b[31] &= 0x7f;
        var y = FromLe(b);
        if (y >= P) return null;                                  // codificación no canónica
        var u = Mod(y * y - 1);
        var v = Mod(D * y * y + 1);
        var x = Mod(u * BigInteger.ModPow(v, 3, P) * BigInteger.ModPow(Mod(u * BigInteger.ModPow(v, 7, P)), (P - 5) / 8, P));
        var vx2 = Mod(v * x * x);
        if (vx2 == u) { }
        else if (vx2 == Mod(-u)) x = Mod(x * SqrtM1);
        else return null;                                         // no es un punto de la curva
        if (x.IsZero && sign == 1) return null;
        if ((int)(x % 2) != sign) x = P - x;
        return new Pt(x, y, 1, Mod(x * y));
    }

    static bool IsZero(Pt p) => Mod(p.X).IsZero && Mod(p.Y - p.Z).IsZero;

    /// <summary>Verificación Ed25519 pura (RFC 8032): clave pública de 32 bytes, firma de 64 bytes.</summary>
    internal static bool Verify(ReadOnlySpan<byte> pub, ReadOnlySpan<byte> msg, ReadOnlySpan<byte> sig)
    {
        if (pub.Length != 32 || sig.Length != 64) return false;
        if (Decode(pub) is not { } a) return false;
        if (IsZero(Mul(8, a))) return false;                      // clave de orden pequeño
        var s = FromLe(sig[32..]);
        if (s >= L) return false;                                 // S fuera de rango (maleable)
        var k = new BigInteger(SHA512.HashData([.. sig[..32], .. pub, .. msg]), isUnsigned: true, isBigEndian: false) % L;
        var r = Add(Mul(s, B), Mul(k, Neg(a)));                   // [S]B - [k]A tiene que ser R
        return Encode(r).AsSpan().SequenceEqual(sig[..32]);
    }
}
