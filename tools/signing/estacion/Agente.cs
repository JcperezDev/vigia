// Proveedor de firma "bitwarden-ssh-agent": habla el protocolo del agente SSH (draft-miller-ssh-agent) por la tubería
// de Windows que abre Bitwarden Desktop (\\.\pipe\openssh-ssh-agent). Solo usa dos mensajes:
//   11 REQUEST_IDENTITIES -> lista de claves PÚBLICAS
//   13 SIGN_REQUEST       -> firma de unos bytes con UNA clave pública concreta (Bitwarden pide aprobación al dueño)
// El protocolo no tiene ningún mensaje para leer o exportar una clave privada, y este código no envía ningún otro.
// Ed25519 por SSH firma los bytes tal cual (sin hash previo y sin el envoltorio SSHSIG de Git): la respuesta trae
// string "ssh-ed25519" + string <64 bytes>, que es la firma Ed25519 pura de los datos enviados.
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

internal static class Agente
{
    internal const string PipeReal = "openssh-ssh-agent";
    /// <summary>Solo para las pruebas con un agente de laboratorio (tools/signing/agente-lab.js); nunca para firmar de verdad.</summary>
    internal static string? PipeLab => Environment.GetEnvironmentVariable("VIGIA_AGENTE_PIPE") is { Length: > 0 } p ? p : null;
    static string PipeName => PipeLab ?? PipeReal;

    internal sealed class AgentError(string m) : Exception(m);

    /// <summary>Clave SSH Ed25519 (blob del protocolo) -> los 32 bytes. null si no es ssh-ed25519.</summary>
    internal static byte[]? Ed25519Raw(byte[] blob)
    {
        try
        {
            int o = 0;
            var t = Str(blob, ref o); var k = Str(blob, ref o);
            return o == blob.Length && Encoding.ASCII.GetString(t) == "ssh-ed25519" && k.Length == 32 ? k : null;
        }
        catch (AgentError) { return null; }
    }

    internal static byte[] BlobFromRaw(byte[] raw32) => [.. S("ssh-ed25519"u8.ToArray()), .. S(raw32)];

    /// <summary>Línea "ssh-ed25519 AAAA... comentario" -> los 32 bytes de la clave pública.</summary>
    internal static byte[] FromOpenSshLine(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "ssh-ed25519") throw new InvalidDataException("No es una clave pública ssh-ed25519.");
        byte[] blob;
        try { blob = Convert.FromBase64String(parts[1]); } catch (FormatException) { throw new InvalidDataException("La clave pública no es base64 válido."); }
        return Ed25519Raw(blob) ?? throw new InvalidDataException("La clave pública ssh-ed25519 está mal formada.");
    }

    internal static string SshFingerprint(byte[] raw32) =>
        "SHA256:" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(BlobFromRaw(raw32))).TrimEnd('=');

    /// <summary>Claves públicas que ofrece el agente: (tipo, blob, comentario).</summary>
    internal static List<(string Type, byte[] Blob, string Comment)> Identities()
    {
        var r = Call([11], 15_000);
        int o = 0;
        if (r[o++] != 12) throw new AgentError("el agente no devolvió la lista de claves (respuesta " + r[0] + ")");
        uint n = U32(r, ref o);
        if (n > 1000) throw new AgentError("respuesta del agente no válida");
        var list = new List<(string, byte[], string)>();
        for (int i = 0; i < n; i++)
        {
            var blob = Str(r, ref o); var com = Str(r, ref o);
            int bo = 0;
            list.Add((Encoding.ASCII.GetString(Str(blob, ref bo)), blob, Encoding.UTF8.GetString(com)));
        }
        return list;
    }

    /// <summary>
    /// Pide al agente la firma Ed25519 de <paramref name="data"/> con la clave <paramref name="raw32"/>. Devuelve los
    /// 64 bytes de la firma. Si el dueño la rechaza en Bitwarden, o la bóveda está bloqueada y no la abre, falla.
    /// </summary>
    internal static byte[] SignEd25519(byte[] raw32, byte[] data, int timeoutMs)
    {
        var req = new List<byte> { 13 };
        req.AddRange(S(BlobFromRaw(raw32))); req.AddRange(S(data)); req.AddRange(BE(0));
        var r = Call([.. req], timeoutMs);
        if (r.Length == 1 && r[0] == 5) throw new AgentError("el agente no firmó (rechazada en Bitwarden, bóveda bloqueada o la clave no está en el agente)");
        int o = 0;
        if (r[o++] != 14) throw new AgentError("respuesta inesperada del agente (" + r[0] + ")");
        var sig = Str(r, ref o);
        if (o != r.Length) throw new AgentError("respuesta del agente con datos de más");
        int so = 0;
        var alg = Encoding.ASCII.GetString(Str(sig, ref so)); var raw = Str(sig, ref so);
        if (alg != "ssh-ed25519" || raw.Length != 64 || so != sig.Length) throw new AgentError("el agente devolvió una firma que no es Ed25519 de 64 bytes (" + alg + ")");
        return raw;
    }

    static byte[] Call(byte[] msg, int timeoutMs)
    {
        using var p = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { p.Connect(3000); }
        catch (TimeoutException) { throw new AgentError("no hay agente SSH escuchando: ¿Bitwarden Desktop abierto y con el agente SSH activado?"); }
        catch (IOException e) { throw new AgentError("no se pudo hablar con el agente SSH (" + e.Message + ")"); }
        var frame = new byte[4 + msg.Length];
        BE((uint)msg.Length).CopyTo(frame, 0); msg.CopyTo(frame, 4);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            p.WriteAsync(frame, cts.Token).AsTask().GetAwaiter().GetResult();
            var len = new byte[4]; ReadAll(p, len, cts.Token);
            uint n = (uint)(len[0] << 24 | len[1] << 16 | len[2] << 8 | len[3]);
            if (n == 0 || n > 256 * 1024) throw new AgentError("respuesta del agente de tamaño no válido");
            var body = new byte[n]; ReadAll(p, body, cts.Token);
            return body;
        }
        catch (OperationCanceledException) { throw new AgentError("el agente no contestó a tiempo (no se aprobó en Bitwarden)"); }
    }

    static void ReadAll(Stream s, byte[] b, CancellationToken ct)
    {
        int got = 0;
        while (got < b.Length)
        {
            int n = s.ReadAsync(b.AsMemory(got), ct).AsTask().GetAwaiter().GetResult();
            if (n == 0) throw new AgentError("el agente cerró la conexión");
            got += n;
        }
    }

    static byte[] BE(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    static byte[] S(byte[] d) => [.. BE((uint)d.Length), .. d];
    static uint U32(byte[] b, ref int o)
    {
        if (o + 4 > b.Length) throw new AgentError("respuesta del agente cortada");
        var v = (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); o += 4; return v;
    }
    static byte[] Str(byte[] b, ref int o)
    {
        var n = U32(b, ref o);
        if (n > b.Length - o) throw new AgentError("respuesta del agente cortada");
        var r = b[o..(o + (int)n)]; o += (int)n; return r;
    }

    // ---------------------------------------------------------------- quién nos llamó (comprobación de buena fe)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PE32 { public int dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint f, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr h, ref PE32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr h, ref PE32 e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    /// <summary>
    /// Nombres de los procesos padre (hasta 12 niveles). Sirve para negarse a firmar si nos lanzó un agente de IA o un
    /// automatismo. NO es una barrera de seguridad: un programa con los permisos de esta cuenta puede saltársela.
    /// </summary>
    internal static List<string> Ancestors()
    {
        var map = new Dictionary<int, (int Parent, string Name)>();
        var h = CreateToolhelp32Snapshot(2, 0);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return [];
        try
        {
            var e = new PE32 { dwSize = Marshal.SizeOf<PE32>() };
            for (bool ok = Process32FirstW(h, ref e); ok; ok = Process32NextW(h, ref e)) map[e.th32ProcessID] = (e.th32ParentProcessID, e.szExeFile);
        }
        finally { CloseHandle(h); }
        var res = new List<string>();
        int pid = Environment.ProcessId;
        for (int i = 0; i < 12 && map.TryGetValue(pid, out var me) && map.TryGetValue(me.Parent, out var par) && me.Parent != pid; i++)
        { res.Add(par.Name); pid = me.Parent; }
        return res;
    }
}
