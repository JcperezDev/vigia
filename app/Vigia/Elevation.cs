using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>
/// Permisos de Vigía.
///
/// Vigia.exe corre como el usuario (sin «requireAdministrator»). Lo que de verdad necesita administrador lo hace
/// un ayudante: el mismo Vigia.exe instalado, abierto con UAC solo cuando hace falta (<c>Vigia.exe --broker</c>).
/// El ayudante no recibe órdenes libres: solo los nombres de <see cref="AdminOps.Names"/>, cada uno con sus
/// parámetros comprobados otra vez dentro del ayudante.
///
/// Canal: tubería con nombre aleatorio que crea la interfaz ANTES de abrir el ayudante (FirstPipeInstance: nadie
/// puede haberla creado antes), con acceso solo para tu usuario y nada por red. Cada lado comprueba al otro:
///   · la interfaz: que quien se conectó es el proceso que ella abrió (PID) y que es el mismo Vigia.exe;
///   · el ayudante: que el dueño de la tubería es el PID que le pasaron, que es el mismo Vigia.exe y del mismo usuario.
/// Si la cuenta que da permiso en UAC no es la tuya (otra cuenta de administrador), el ayudante no arranca:
/// los ajustes de usuario irían a esa otra cuenta.
/// El ayudante se cierra cuando se cierra la interfaz.
/// </summary>
public static class Elevation
{
    /// <summary>Este proceso es el ayudante elevado.</summary>
    public static bool InBroker { get; private set; }

    static bool? _admin;
    /// <summary>¿Este proceso tiene permisos de administrador de verdad (no el token filtrado de UAC)?</summary>
    public static bool IsAdmin => _admin ??= CheckAdmin();

    static bool CheckAdmin()
    {
        try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }


    internal static readonly Regex PipeRx = new(@"^Vigia\.[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------- Win32
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(SafeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder sb, ref int size);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    const uint ProcessQueryLimited = 0x1000, TokenQuery = 0x0008;

    internal static string? ImagePath(int pid)
    {
        var h = OpenProcess(ProcessQueryLimited, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024); var n = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
        }
        finally { CloseHandle(h); }
    }

    internal static SecurityIdentifier? UserOf(int pid)
    {
        var h = OpenProcess(ProcessQueryLimited, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(h, TokenQuery, out var tok)) return null;
            try { using var id = new WindowsIdentity(tok); return id.User; }
            finally { CloseHandle(tok); }
        }
        catch { return null; }
        finally { CloseHandle(h); }
    }

    internal static bool SamePath(string? a, string? b) =>
        a is { Length: > 0 } && b is { Length: > 0 } &&
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    internal static uint ClientPid(NamedPipeServerStream p) => GetNamedPipeClientProcessId(p.SafePipeHandle, out var pid) ? pid : 0;
    internal static uint ServerPid(NamedPipeClientStream p) => GetNamedPipeServerProcessId(p.SafePipeHandle, out var pid) ? pid : 0;

    // ---------------------------------------------------------------- tubería: una línea JSON por mensaje
    internal const int MaxLine = 8 << 20;   // 8 MB: un inventario de apps grande cabe de sobra

    internal static async Task<string?> ReadLineAsync(Stream s, CancellationToken ct)
    {
        var buf = new List<byte>(4096);
        var one = new byte[1];
        while (true)
        {
            var n = await s.ReadAsync(one, ct);
            if (n == 0) return buf.Count == 0 ? null : Encoding.UTF8.GetString(buf.ToArray());
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(buf.ToArray());
            if (buf.Count >= MaxLine) throw new InvalidDataException("mensaje demasiado largo");
            buf.Add(one[0]);
        }
    }

    internal static async Task WriteLineAsync(Stream s, SemaphoreSlim gate, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await gate.WaitAsync();
        try { await s.WriteAsync(bytes); await s.FlushAsync(); }
        finally { gate.Release(); }
    }

    // ---------------------------------------------------------------- el ayudante (proceso elevado)

    /// <summary>Vigia.exe --broker &lt;tubería&gt; &lt;pid de la interfaz&gt;. Devuelve el código de salida.</summary>
    public static int RunBroker(string[] args)
    {
        InBroker = true;
        void Log(string m) => UiMessage.Note("ayudante: " + m);
        if (args.Length < 3 || !PipeRx.IsMatch(args[1]) || !int.TryParse(args[2], out var uiPid) || uiPid <= 0) { Log("argumentos no válidos"); return 2; }
        if (!IsAdmin) { Log("sin permisos de administrador"); return 3; }
        var me = Environment.ProcessPath;
        // la interfaz tiene que ser este mismo Vigia.exe, de tu misma cuenta
        if (!SamePath(ImagePath(uiPid), me)) { Log("la interfaz no es este Vigia.exe"); return 4; }
        using (var self = WindowsIdentity.GetCurrent())
            if (UserOf(uiPid) is not { } u || self.User is null || !u.Equals(self.User)) { Log("la interfaz es de otra cuenta"); return 5; }
        if (InstallTrust.Problem(AppContext.BaseDirectory) is { } why) { Log("carpeta sin proteger: " + why); return 7; }

        Process ui;
        try { ui = Process.GetProcessById(uiPid); } catch { return 4; }
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
        try { pipe.Connect(15_000); } catch (Exception e) { Log("no pude conectar: " + e.Message); return 6; }
        if (ServerPid(pipe) != (uint)uiPid) { Log("la tubería no es de la interfaz"); return 6; }

        using var stop = new CancellationTokenSource();
        _ = ui.WaitForExitAsync(stop.Token).ContinueWith(_ => stop.Cancel(), TaskScheduler.Default);
        var gate = new SemaphoreSlim(1, 1);
        try
        {
            WriteLineAsync(pipe, gate, JsonSerializer.Serialize(new { hello = "vigia-broker", v = 1, pid = Environment.ProcessId })).GetAwaiter().GetResult();
            Loop(pipe, gate, stop.Token).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
        return 0;
    }

    static async Task Loop(NamedPipeClientStream pipe, SemaphoreSlim gate, CancellationToken ct)
    {
        var running = new List<Task>();
        while (!ct.IsCancellationRequested)
        {
            var line = await ReadLineAsync(pipe, ct);
            if (line is null) break;   // la interfaz se cerró
            int id; string cmd; JsonObject msg;
            try
            {
                var o = JsonNode.Parse(line) as JsonObject ?? throw new FormatException();
                id = (int)o["id"]!;
                cmd = (string?)o["cmd"] ?? "";
                msg = o["msg"] as JsonObject ?? new JsonObject();
            }
            catch { UiMessage.Note("ayudante: mensaje mal formado"); continue; }
            // lista cerrada: lo que no está aquí no se hace, ni con permisos ni sin ellos
            if (!AdminOps.Names.Contains(cmd))
            {
                UiMessage.Note($"ayudante: comando no permitido: {(cmd.Length > 40 ? cmd[..40] : cmd)}");
                await WriteLineAsync(pipe, gate, JsonSerializer.Serialize(new { id, end = true, error = "Esa operación no está permitida." }));
                continue;
            }
            msg["cmd"] = cmd;
            running.RemoveAll(t => t.IsCompleted);
            running.Add(Task.Run(async () =>
            {
                string? err = null;
                try
                {
                    await AdminOps.Run(cmd, msg, (t, p) =>
                        WriteLineAsync(pipe, gate, "{\"id\":" + id + ",\"ev\":" + JsonSerializer.Serialize(t) + ",\"payload\":" + JsonSerializer.Serialize(p, Program.Json) + "}")
                            .GetAwaiter().GetResult());
                }
                catch (Exception e) { err = e is ArgumentException ? "Datos no válidos." : e.Message; }
                try { await WriteLineAsync(pipe, gate, JsonSerializer.Serialize(new { id, end = true, error = err })); } catch { }
            }, CancellationToken.None));
        }
        try { await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
    }

    // ---------------------------------------------------------------- la interfaz: abre y usa el ayudante

    public sealed class Denied(string message) : Exception(message);

    public sealed class Broker : IDisposable
    {
        static Broker? _current;
        static readonly SemaphoreSlim StartGate = new(1, 1);
        public static bool Alive => _current is { _dead: false };

        readonly NamedPipeServerStream _pipe;
        readonly Process _proc;
        readonly SemaphoreSlim _wgate = new(1, 1);
        readonly Dictionary<int, (TaskCompletionSource<string?> Done, Action<string, JsonElement> Ev)> _pending = [];
        int _next;
        volatile bool _dead;

        Broker(NamedPipeServerStream pipe, Process proc) { _pipe = pipe; _proc = proc; }

        public const string NoConsent = "Para esto Vigía necesita permiso de administrador y no se dio. No cambié nada.";

        /// <summary>Abre el ayudante si no está abierto (sale el aviso de UAC). Lanza <see cref="Denied"/> con un texto para el usuario.</summary>
        /// <summary>Por qué no se puede abrir el ayudante desde aquí (null = se puede). Sin excepciones ni variables
        /// de entorno: un .exe en una carpeta que tu usuario puede cambiar nunca se abre con permisos, ni para probar
        /// (el ayudante lo volvería a rechazar, pero ya habría corrido elevado).</summary>
        internal static string? StartRefusal(string? dir = null) =>
            InstallTrust.Problem(dir ?? AppContext.BaseDirectory) is null ? null
            : "Vigía no está en una carpeta protegida, así que no pide permisos de administrador desde ahí. Instálala con Puente (Archivos de programa) y vuelve a intentarlo.";

        public static async Task<Broker> EnsureAsync()
        {
            if (_current is { _dead: false } c) return c;
            await StartGate.WaitAsync();
            try
            {
                if (_current is { _dead: false } c2) return c2;
                _current = await StartAsync();
                return _current;
            }
            finally { StartGate.Release(); }
        }

        static async Task<Broker> StartAsync()
        {
            var me = Environment.ProcessPath ?? throw new Denied("No sé desde dónde corre Vigía.");
            if (StartRefusal() is { } no) throw new Denied(no);
            var name = "Vigia." + Guid.NewGuid().ToString("N");
            using var self = WindowsIdentity.GetCurrent();
            var ps = new PipeSecurity();
            ps.SetAccessRuleProtection(true, false);
            ps.AddAccessRule(new PipeAccessRule(self.User!, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            const PipeOptions FirstInstance = (PipeOptions)0x00080000;   // FILE_FLAG_FIRST_PIPE_INSTANCE: falla si alguien ya la creó
            var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | FirstInstance, 1 << 16, 1 << 16, ps);
            Process proc;
            try
            {
                proc = Process.Start(new ProcessStartInfo(me)
                {
                    UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory,
                    Arguments = $"--broker {name} {Environment.ProcessId}", WindowStyle = ProcessWindowStyle.Hidden,
                }) ?? throw new Denied(NoConsent);
            }
            catch (Win32Exception e) when (e.NativeErrorCode == 1223) { pipe.Dispose(); throw new Denied(NoConsent); }   // cancelado en UAC
            catch { pipe.Dispose(); throw; }

            try
            {
                using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(2));   // lo que tarde la persona en contestar a UAC
                var conn = pipe.WaitForConnectionAsync(wait.Token);
                var exit = proc.WaitForExitAsync(wait.Token);
                if (await Task.WhenAny(conn, exit) != conn || !conn.IsCompletedSuccessfully)
                    throw new Denied(proc.HasExited && proc.ExitCode == 5
                        ? "La cuenta que dio el permiso no es la tuya. Vigía solo pide permisos a tu propia cuenta (tiene que ser administradora)."
                        : NoConsent);
                // quien se conectó tiene que ser el ayudante que abrimos, y el mismo Vigia.exe
                if (ClientPid(pipe) != (uint)proc.Id || !SamePath(ImagePath(proc.Id), me))
                    throw new Denied("Se conectó otro programa en lugar del ayudante de Vigía. No le mandé nada.");
                using var hello = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var first = await ReadLineAsync(pipe, hello.Token);
                if (first is null || (string?)JsonNode.Parse(first)?["hello"] != "vigia-broker") throw new Denied("El ayudante de Vigía no respondió.");
            }
            catch
            {
                pipe.Dispose();
                try { if (!proc.HasExited) proc.Kill(); } catch { }
                throw;
            }
            var b = new Broker(pipe, proc);
            _ = b.ReadLoop();
            return b;
        }

        async Task ReadLoop()
        {
            try
            {
                while (true)
                {
                    var line = await ReadLineAsync(_pipe, CancellationToken.None);
                    if (line is null) break;
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    if (!r.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
                    (TaskCompletionSource<string?> Done, Action<string, JsonElement> Ev) p;
                    lock (_pending) if (!_pending.TryGetValue(id, out p)) continue;
                    if (r.TryGetProperty("ev", out var ev) && ev.GetString() is { } type)
                        p.Ev(type, r.TryGetProperty("payload", out var pl) ? pl.Clone() : default);
                    else if (r.TryGetProperty("end", out _))
                    {
                        lock (_pending) _pending.Remove(id);
                        p.Done.TrySetResult(r.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String ? er.GetString() : null);
                    }
                }
            }
            catch { }
            finally
            {
                _dead = true;
                lock (_pending)
                {
                    foreach (var p in _pending.Values) p.Done.TrySetResult("El ayudante de Vigía se cerró.");
                    _pending.Clear();
                }
            }
        }

        /// <summary>Manda una operación de la lista cerrada y espera a que termine. Devuelve el error (o null).</summary>
        public async Task<string?> RunAsync(string cmd, JsonObject msg, Action<string, JsonElement> onEvent)
        {
            if (!AdminOps.Names.Contains(cmd)) return "Esa operación no está permitida.";
            if (_dead) return "El ayudante de Vigía se cerró.";
            var id = Interlocked.Increment(ref _next);
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) _pending[id] = (tcs, onEvent);
            var body = msg.DeepClone().AsObject();
            body.Remove("cmd");
            await WriteLineAsync(_pipe, _wgate, new JsonObject { ["id"] = id, ["cmd"] = cmd, ["msg"] = body }.ToJsonString());
            return await tcs.Task;
        }

        public void Dispose()
        {
            _dead = true;
            try { _pipe.Dispose(); } catch { }
            try { _proc.Dispose(); } catch { }
        }

        public static void Shutdown() { _current?.Dispose(); _current = null; }
    }
}

/// <summary>
/// ¿La carpeta de Vigía está protegida? Solo SYSTEM, Administradores y TrustedInstaller pueden escribir en ella
/// (y en cada archivo de dentro), no es un enlace (junction o simbólico) y su dueño es de confianza.
/// Si no, Vigía no pide permisos de administrador desde ahí: cualquiera podría cambiar el .exe antes.
/// </summary>
public static class InstallTrust
{
    const FileSystemRights WriteMask = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
    static readonly SecurityIdentifier CreatorOwner = new(WellKnownSidType.CreatorOwnerSid, null);

    static bool Trusted(SecurityIdentifier s) => s.Equals(Admins) || s.Equals(System) || s.Equals(TrustedInstaller);

    /// <summary>Quién NO debe poder escribir: tu usuario, sus grupos (menos Administradores) y los grupos amplios de Windows.</summary>
    static HashSet<SecurityIdentifier> Untrusted()
    {
        var set = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.BuiltinUsersSid, null), new(WellKnownSidType.AuthenticatedUserSid, null),
            new(WellKnownSidType.WorldSid, null), new(WellKnownSidType.InteractiveSid, null),
            new("S-1-15-2-1"),   // ALL APPLICATION PACKAGES
        };
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            // la cuenta en curso, salvo que sea de confianza: el servicio corre como SYSTEM y SYSTEM es dueño de la carpeta
            // (antes se contaba a sí mismo como «puede escribir» y el servicio de sensores no arrancaba nunca)
            if (id.User is { } u && !Trusted(u)) set.Add(u);
            foreach (var g in id.Groups ?? new IdentityReferenceCollection())
                if (g is SecurityIdentifier s && !Trusted(s)) set.Add(s);
        }
        catch { }
        return set;
    }

    /// <summary>null si la carpeta está protegida; si no, el motivo (para el registro, no para la pantalla).</summary>
    public static string? Problem(string dir, int maxFiles = 4000)
    {
        try
        {
            var root = new DirectoryInfo(Path.GetFullPath(dir));
            if (!root.Exists) return "no existe";
            // ni la carpeta ni ninguna de las de arriba puede ser un enlace
            for (var d = root; d is not null; d = d.Parent)
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) return $"{d.FullName} es un enlace";
            // primero los enlaces (no se siguen: EnumerateFileSystemInfos no entra en ellos), luego los permisos
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
            if ((r.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;   // no se aplica a este objeto
            var s = (SecurityIdentifier)r.IdentityReference;
            if (s.Equals(CreatorOwner)) continue;
            if (bad.Contains(s) && (r.FileSystemRights & WriteMask) != 0) return $"{what}: {s} puede escribir";
        }
        return null;
    }

    public static string SelfTest()
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        Check(!Untrusted().Any(Trusted), "las cuentas de confianza (SYSTEM, Administradores, TrustedInstaller) nunca cuentan como «no debe escribir»");
        var tmp = Path.Combine(Path.GetTempPath(), "vigia-trust-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, "Vigia.exe"), "x");
            // la carpeta temporal es tuya: tiene que salir «sin proteger»
            Check(Problem(tmp) is not null, "una carpeta del usuario no cuenta como protegida");
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Check(Problem(Path.Combine(sys, "drivers", "etc"), 50) is null, "una carpeta de Windows sí cuenta como protegida");
            Check(Problem(Path.Combine(tmp, "no-existe")) is not null, "una carpeta que no existe no cuenta como protegida");
            // un junction dentro de la carpeta la deja sin proteger
            var link = Path.Combine(tmp, "enlace");
            Directory.CreateSymbolicLink(link, tmp);
            Check(Problem(tmp) is { } p && p.Contains("enlace"), "un enlace dentro de la carpeta se detecta");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.Add("OK   enlace simbólico: sin permiso para crearlo aquí (se prueba en la VM)"); }
        finally { try { Directory.Delete(tmp, true); } catch { } }
        // ninguna variable de entorno abre el ayudante desde una carpeta sin proteger (antes VIGIA_DEV=1 lo intentaba)
        var oldDev = Environment.GetEnvironmentVariable("VIGIA_DEV");
        try
        {
            Environment.SetEnvironmentVariable("VIGIA_DEV", "1");
            Check(Elevation.Broker.StartRefusal(Path.GetTempPath()) is not null, "VIGIA_DEV=1 no abre el ayudante desde una carpeta sin proteger");
        }
        finally { Environment.SetEnvironmentVariable("VIGIA_DEV", oldDev); }
        var sysd = Environment.SystemDirectory;
        Check(Tweaks.Sys("schtasks.exe") == Path.Combine(sysd, "schtasks.exe") && Tweaks.Sys("sc.exe") == Path.Combine(sysd, "sc.exe")
              && Tweaks.Sys("powershell.exe") == Path.Combine(sysd, "WindowsPowerShell", "v1.0", "powershell.exe")
              && Tweaks.Sys("explorer.exe") == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")
              && Tweaks.Sys(@"C:\x\y.exe") == @"C:\x\y.exe" && File.Exists(Tweaks.Sys("powershell.exe")),
              "programas de Windows con ruta fija (no se buscan junto al .exe ni en la carpeta actual)");
        Check(Elevation.PipeRx.IsMatch("Vigia." + new string('a', 32)) && !Elevation.PipeRx.IsMatch("Vigia.x") && !Elevation.PipeRx.IsMatch("..\\Vigia." + new string('a', 32)),
            "nombre de la tubería: solo Vigia.<32 hex>");
        return string.Join(Environment.NewLine, log);
    }
}
