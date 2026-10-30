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

    /// <summary>Tiempo sin pedidos tras el que el ayudante se cierra solo. Vigía puede quedarse días en la bandeja;
    /// el proceso con permisos no tiene por qué quedarse con ella. Lo siguiente que haga falta vuelve a pedir UAC.</summary>
    internal static TimeSpan IdleLimit = TimeSpan.FromMinutes(10);

    /// <summary>Pedidos que la interfaz manda sola cada pocos segundos mientras el ayudante vive (sensores con permisos).
    /// Se atienden, pero no cuentan como uso: si contaran, el ayudante no se cerraría nunca (visto en VigiaLab).</summary>
    internal static readonly HashSet<string> Background = ["telemetry", "memAuto", "undoRetired", "ping"];

    /// <summary>Quien atiende cada pedido. Las pruebas lo cambian para simular una tarea larga sin tocar el sistema.</summary>
    internal delegate Task Runner(string cmd, JsonObject msg, Action<string, object?> emit);

    internal static Task Loop(Stream pipe, SemaphoreSlim gate, CancellationToken ct) => Loop(pipe, gate, ct, null);

    // Comportamiento previsto: el ayudante se cierra tras IdleLimit sin pedidos de la persona. El plazo cuenta desde que
    // terminó lo último que pidió (no desde que empezó), nunca corta una tarea en marcha y las lecturas automáticas
    // (Background) no lo renuevan. La lectura de la tubería no se cancela al vencer el plazo: así no se pierde medio mensaje.
    internal static async Task Loop(Stream pipe, SemaphoreSlim gate, CancellationToken ct, Runner? run)
    {
        run ??= (c, m, e) => AdminOps.Run(c, m, e);
        var running = new List<Task>();   // solo lo que pidió la persona
        var all = new List<Task>();
        long lastUse = DateTime.UtcNow.Ticks;   // lo actualizan también las tareas al terminar
        DateTime LastUse() => new(Interlocked.Read(ref lastUse), DateTimeKind.Utc);
        void Touch() => Interlocked.Exchange(ref lastUse, DateTime.UtcNow.Ticks);
        Task<string?> next = ReadLineAsync(pipe, ct);
        while (!ct.IsCancellationRequested)
        {
            running.RemoveAll(t => t.IsCompleted);
            if (!next.IsCompleted)
            {
                if (running.Count > 0)
                {
                    // hay algo en marcha (un USB, una ISO): se espera a que llegue un pedido o a que termine
                    await Task.WhenAny(next, Task.WhenAny(running));
                    continue;
                }
                var left = IdleLimit - (DateTime.UtcNow - LastUse());
                if (left > TimeSpan.Zero)
                {
                    using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var delay = Task.Delay(left, timer.Token);
                    if (await Task.WhenAny(next, delay) != next) { continue; }
                    timer.Cancel();
                }
                else
                {
                    UiMessage.Note("ayudante: sin pedidos en un rato, me cierro (lo próximo vuelve a pedir permiso)");
                    break;
                }
            }
            string? line;
            try { line = await next; }
            catch (OperationCanceledException) { break; }
            if (line is null) break;   // la interfaz se cerró
            next = ReadLineAsync(pipe, ct);
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
            all.RemoveAll(t => t.IsCompleted);
            var mine = !Background.Contains(cmd);
            if (mine) Touch();
            var job = Task.Run(async () =>
            {
                string? err = null;
                try
                {
                    await run(cmd, msg, (t, p) =>
                        WriteLineAsync(pipe, gate, "{\"id\":" + id + ",\"ev\":" + JsonSerializer.Serialize(t) + ",\"payload\":" + JsonSerializer.Serialize(p, Program.Json) + "}")
                            .GetAwaiter().GetResult());
                }
                catch (Exception e) { err = e is ArgumentException ? "Datos no válidos." : e.Message; }
                finally { if (mine) Touch(); }   // el plazo empieza cuando termina, no cuando empezó
                try { await WriteLineAsync(pipe, gate, JsonSerializer.Serialize(new { id, end = true, error = err })); } catch { }
            }, CancellationToken.None);
            all.Add(job);
            if (mine) running.Add(job);
        }
        try { await Task.WhenAll(all).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
    }

    // ---------------------------------------------------------------- la interfaz: abre y usa el ayudante

    public sealed class Denied(string message) : Exception(message);

    public sealed class Broker : IDisposable
    {
        static Broker? _current;
        static readonly SemaphoreSlim StartGate = new(1, 1);
        public static bool Alive => _current is { _dead: false };
        /// <summary>El ayudante abierto, o null. Nunca abre uno nuevo: para lo que la interfaz manda sola, que no debe sacar UAC.</summary>
        public static Broker? Existing => _current is { _dead: false } c ? c : null;

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
        // el ayudante se cierra solo si nadie le pide nada (con una tubería de prueba, sin permisos)
        var oldIdle = Elevation.IdleLimit;
        try
        {
            Elevation.IdleLimit = TimeSpan.FromMilliseconds(300);
            var pn = "Vigia." + Guid.NewGuid().ToString("N");
            using var srv = new global::System.IO.Pipes.NamedPipeServerStream(pn, global::System.IO.Pipes.PipeDirection.InOut, 1, global::System.IO.Pipes.PipeTransmissionMode.Byte, global::System.IO.Pipes.PipeOptions.Asynchronous);
            using var cli = new global::System.IO.Pipes.NamedPipeClientStream(".", pn, global::System.IO.Pipes.PipeDirection.InOut, global::System.IO.Pipes.PipeOptions.Asynchronous);
            var acc = srv.WaitForConnectionAsync();
            cli.Connect(5000); acc.GetAwaiter().GetResult();
            var sw = Stopwatch.StartNew();
            var loop = Elevation.Loop(cli, new SemaphoreSlim(1, 1), CancellationToken.None);
            Check(loop.Wait(TimeSpan.FromSeconds(10)) && sw.Elapsed >= TimeSpan.FromMilliseconds(250),
                "ayudante sin pedidos: se cierra solo al pasar el tiempo límite (no se queda elevado en la bandeja)");
            // un pedido no permitido reinicia la espera pero no hace nada
            Elevation.IdleLimit = TimeSpan.FromSeconds(2);
            using var srv2 = new global::System.IO.Pipes.NamedPipeServerStream(pn + "b", global::System.IO.Pipes.PipeDirection.InOut, 1, global::System.IO.Pipes.PipeTransmissionMode.Byte, global::System.IO.Pipes.PipeOptions.Asynchronous);
            using var cli2 = new global::System.IO.Pipes.NamedPipeClientStream(".", pn + "b", global::System.IO.Pipes.PipeDirection.InOut, global::System.IO.Pipes.PipeOptions.Asynchronous);
            var acc2 = srv2.WaitForConnectionAsync();
            cli2.Connect(5000); acc2.GetAwaiter().GetResult();
            var loop2 = Elevation.Loop(cli2, new SemaphoreSlim(1, 1), CancellationToken.None);
            srv2.Write(global::System.Text.Encoding.UTF8.GetBytes("{\"id\":1,\"cmd\":\"cmd.exe\",\"msg\":{}}\n")); srv2.Flush();
            using var rd = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var answer = Elevation.ReadLineAsync(srv2, rd.Token).GetAwaiter().GetResult() ?? "";
            Check(answer.Contains("\"end\":true") && answer.Contains("no est"), "ayudante: un comando fuera de la lista recibe «no permitido»");
            srv2.Dispose();   // la interfaz se va: el ayudante termina sin esperar al límite
            Check(loop2.Wait(TimeSpan.FromSeconds(5)), "ayudante: si la interfaz se cierra, termina enseguida");
            // lo que la interfaz manda sola (sensores cada pocos segundos) no lo mantiene vivo: visto en VigiaLab,
            // seguía elevado 12 min después del último clic porque cada lectura de sensores reiniciaba la espera
            Elevation.IdleLimit = TimeSpan.FromMilliseconds(800);
            using var srv3 = new global::System.IO.Pipes.NamedPipeServerStream(pn + "c", global::System.IO.Pipes.PipeDirection.InOut, 1, global::System.IO.Pipes.PipeTransmissionMode.Byte, global::System.IO.Pipes.PipeOptions.Asynchronous);
            using var cli3 = new global::System.IO.Pipes.NamedPipeClientStream(".", pn + "c", global::System.IO.Pipes.PipeDirection.InOut, global::System.IO.Pipes.PipeOptions.Asynchronous);
            var acc3 = srv3.WaitForConnectionAsync();
            cli3.Connect(5000); acc3.GetAwaiter().GetResult();
            var loop3 = Elevation.Loop(cli3, new SemaphoreSlim(1, 1), CancellationToken.None);
            var sw3 = Stopwatch.StartNew();
            var drain = Task.Run(async () => { try { while (await Elevation.ReadLineAsync(srv3, CancellationToken.None) is not null) { } } catch { } });
            int n3 = 0;
            while (!loop3.IsCompleted && sw3.Elapsed < TimeSpan.FromSeconds(6))
            {
                try { srv3.Write(global::System.Text.Encoding.UTF8.GetBytes("{\"id\":" + (++n3) + ",\"cmd\":\"ping\",\"msg\":{}}\n")); srv3.Flush(); } catch { break; }
                Thread.Sleep(150);
            }
            Check(loop3.Wait(TimeSpan.FromSeconds(2)) && sw3.Elapsed < TimeSpan.FromSeconds(4) && n3 >= 4,
                $"ayudante: los pedidos de fondo (sensores) no lo mantienen elevado ({n3} pedidos, {sw3.Elapsed.TotalSeconds:0.0} s)");
            srv3.Dispose(); drain.Wait(TimeSpan.FromSeconds(2));
            // una tarea pedida por la persona que dura más que el plazo no se corta, y el plazo empieza al terminarla
            Elevation.IdleLimit = TimeSpan.FromMilliseconds(600);
            using var srv4 = new global::System.IO.Pipes.NamedPipeServerStream(pn + "d", global::System.IO.Pipes.PipeDirection.InOut, 1, global::System.IO.Pipes.PipeTransmissionMode.Byte, global::System.IO.Pipes.PipeOptions.Asynchronous);
            using var cli4 = new global::System.IO.Pipes.NamedPipeClientStream(".", pn + "d", global::System.IO.Pipes.PipeDirection.InOut, global::System.IO.Pipes.PipeOptions.Asynchronous);
            var acc4 = srv4.WaitForConnectionAsync();
            cli4.Connect(5000); acc4.GetAwaiter().GetResult();
            var jobEnd = DateTime.MinValue; var jobDone = false;
            Elevation.Runner slow = async (c, m, e) => { await Task.Delay(1500); jobEnd = DateTime.UtcNow; jobDone = true; };
            var loop4 = Elevation.Loop(cli4, new SemaphoreSlim(1, 1), CancellationToken.None, slow);
            var drain4 = Task.Run(async () => { try { while (await Elevation.ReadLineAsync(srv4, CancellationToken.None) is not null) { } } catch { } });
            srv4.Write(global::System.Text.Encoding.UTF8.GetBytes("{\"id\":1,\"cmd\":\"usbList\",\"msg\":{}}\n")); srv4.Flush();
            var ended4 = loop4.Wait(TimeSpan.FromSeconds(8));
            var after = DateTime.UtcNow - jobEnd;
            Check(ended4 && jobDone && after >= TimeSpan.FromMilliseconds(450),
                $"ayudante: una tarea más larga que el plazo termina entera y el plazo cuenta desde que acaba ({after.TotalMilliseconds:0} ms después)");
            srv4.Dispose(); drain4.Wait(TimeSpan.FromSeconds(2));
            // con una tarea en marcha, un pedido que llega en dos trozos a ambos lados del plazo se atiende entero
            // (antes la lectura se cancelaba al vencer el plazo y se perdía la primera mitad)
            Elevation.IdleLimit = TimeSpan.FromMilliseconds(400);
            using var srv5 = new global::System.IO.Pipes.NamedPipeServerStream(pn + "e", global::System.IO.Pipes.PipeDirection.InOut, 1, global::System.IO.Pipes.PipeTransmissionMode.Byte, global::System.IO.Pipes.PipeOptions.Asynchronous);
            using var cli5 = new global::System.IO.Pipes.NamedPipeClientStream(".", pn + "e", global::System.IO.Pipes.PipeDirection.InOut, global::System.IO.Pipes.PipeOptions.Asynchronous);
            var acc5 = srv5.WaitForConnectionAsync();
            cli5.Connect(5000); acc5.GetAwaiter().GetResult();
            Elevation.Runner mixed = (c, m, e) => m["slow"] is null ? Task.CompletedTask : Task.Delay(1500);
            var loop5 = Elevation.Loop(cli5, new SemaphoreSlim(1, 1), CancellationToken.None, mixed);
            var answers5 = new global::System.Collections.Concurrent.ConcurrentQueue<string>();
            var drain5 = Task.Run(async () => { try { while (await Elevation.ReadLineAsync(srv5, CancellationToken.None) is { } l) answers5.Enqueue(l); } catch { } });
            srv5.Write(global::System.Text.Encoding.UTF8.GetBytes("{\"id\":1,\"cmd\":\"usbList\",\"msg\":{\"slow\":1}}\n")); srv5.Flush();
            var msg5 = global::System.Text.Encoding.UTF8.GetBytes("{\"id\":7,\"cmd\":\"usbList\",\"msg\":{}}\n");
            Thread.Sleep(250); srv5.Write(msg5, 0, 10); srv5.Flush();
            Thread.Sleep(350); srv5.Write(msg5, 10, msg5.Length - 10); srv5.Flush();
            var ended5 = loop5.Wait(TimeSpan.FromSeconds(8));
            drain5.Wait(TimeSpan.FromSeconds(1));
            Check(ended5 && answers5.Any(l => l.Contains("\"id\":7") && l.Contains("\"end\":true")) && answers5.Any(l => l.Contains("\"id\":1") && l.Contains("\"end\":true")),
                "ayudante: con una tarea en marcha, un pedido partido en dos trozos se atiende entero y luego se cierra solo");
            srv5.Dispose();
            // todo lo que Program.cs manda sin que la persona pulse nada (sensores, liberación automática, deshacer retirados)
            Check(new[] { "telemetry", "memAuto", "undoRetired" }.All(Elevation.Background.Contains), "ayudante: los pedidos automáticos de la interfaz no cuentan como uso");
        }
        catch (Exception e) { Check(false, "ayudante sin pedidos: " + e.GetType().Name + " " + e.Message); }
        finally { Elevation.IdleLimit = oldIdle; }
        return string.Join(Environment.NewLine, log);
    }
}
