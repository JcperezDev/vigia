using System.Runtime.InteropServices;

namespace Vigia;

/// <param name="Care">«safe»: temporales que nadie usa; «care»: se puede borrar pero tiene un costo (shaders que se
/// recompilan, informes que sirven para diagnosticar, la Papelera). Lo «care» nunca va marcado ni lo borra el modo simple.</param>
public sealed record CleanTarget(string Id, string Name, string Path, long Bytes, int Files, string Care = "safe");
public sealed record CleanResult(long FreedBytes, int Deleted, int Skipped);

/// <summary>
/// Limpieza conservadora: carpetas temporales (de tu usuario y de Windows), descargas viejas de Windows Update, cachés de shaders,
/// informes de errores y la Papelera. En carpetas solo borra archivos con más de 24 h y nunca sigue enlaces;
/// lo que esté en uso se salta.
/// </summary>
public static class Cleanup
{
    static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    /// <summary>Lo que se puede borrar pero tiene un costo: los shaders se recompilan (tirones la primera partida),
    /// los volcados e informes sirven para saber por qué se cerró algo. Ni se marca solo ni lo borra el modo simple.</summary>
    // Cachés de shaders de los drivers: se regeneran solas, así que borrarlas no da FPS; solo sirve si una
    // está corrupta o para recuperar espacio. Solo salen las que existen y nunca se borran con un juego abierto.
    static readonly string[] ShaderIds = ["d3d_cache", "nv_dx", "nv_gl", "amd_dx", "amd_dxc", "amd_vk"];
    // va después de ShaderIds: los campos estáticos se inicializan en orden
    static readonly HashSet<string> CareIds = ["d3d_cache", "crash_dumps", "wer", "wu_download", RecycleId, .. ShaderIds];
    public static string CareOf(string id) => CareIds.Contains(id) ? "care" : "safe";

    static IEnumerable<(string id, string name, string path)> Roots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return ("user_temp", "Temporales de usuario", Path.GetTempPath());
        yield return ("d3d_cache", "Caché de shaders DirectX", Path.Combine(local, "D3DSCache"));
        yield return ("nv_dx", "Caché de shaders NVIDIA (DirectX)", Path.Combine(local, "NVIDIA", "DXCache"));
        yield return ("nv_gl", "Caché de shaders NVIDIA (OpenGL y Vulkan)", Path.Combine(local, "NVIDIA", "GLCache"));
        yield return ("amd_dx", "Caché de shaders AMD (DirectX)", Path.Combine(local, "AMD", "DxCache"));
        yield return ("amd_dxc", "Caché de shaders AMD (DirectX 12)", Path.Combine(local, "AMD", "DxcCache"));
        yield return ("amd_vk", "Caché de shaders AMD (Vulkan)", Path.Combine(local, "AMD", "VkCache"));
        yield return ("crash_dumps", "Volcados de errores", Path.Combine(local, "CrashDumps"));
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        // de Windows: hace falta Vigía como administrador; si no, se ven en 0 y se saltan
        yield return ("win_temp", "Temporales de Windows", Path.Combine(win, "Temp"));
        yield return ("wu_download", "Descargas de Windows Update ya instaladas", Path.Combine(win, "SoftwareDistribution", "Download"));
        yield return ("wer", "Informes de errores de Windows", Path.Combine(data, "Microsoft", "Windows", "WER"));
    }

    // ---------- Papelera: la de todas las unidades, con la API de Windows (sin ventana ni sonido) ----------
    const string RecycleId = "recycle";
    // en x64 mide 24 bytes; con Pack=4 la API lo rechaza y la Papelera se veía vacía
    [StructLayout(LayoutKind.Sequential)] struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);
    static (long bytes, long items) Recycle()
    {
        var q = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref q) == 0 ? (q.i64Size, q.i64NumItems) : (0, 0);
    }

    static IEnumerable<FileInfo> Files(string root)
    {
        var stack = new Stack<DirectoryInfo>();
        if (Directory.Exists(root)) stack.Push(new DirectoryInfo(root));
        var cutoff = DateTime.UtcNow - MinAge;
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            FileSystemInfo[] items;
            try { items = d.GetFileSystemInfos(); } catch { continue; }
            foreach (var i in items)
            {
                if (i.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (i is DirectoryInfo sub) stack.Push(sub);
                else if (i is FileInfo f && f.LastWriteTimeUtc < cutoff) yield return f;
            }
        }
    }

    public static List<CleanTarget> Scan()
    {
        var list = Roots().Where(r => !ShaderIds.Contains(r.id) || r.id == "d3d_cache" || Directory.Exists(r.path)).Select(r =>
        {
            long b = 0; int n = 0;
            foreach (var f in Files(r.path)) { try { b += f.Length; n++; } catch { } }
            return new CleanTarget(r.id, r.name, r.path, b, n, CareOf(r.id));
        }).ToList();
        var (rb, ri) = Recycle();
        list.Insert(0, new CleanTarget(RecycleId, "Papelera de reciclaje", "Todas las unidades", rb, (int)Math.Min(ri, int.MaxValue), "care"));
        var left = 0;
        try { left = StartupLeft().Count; } catch { }
        list.Add(new CleanTarget(StartupId, "Restos de programas desinstalados", "Inicio de Windows", 0, left));
        return list;
    }

    // ---------- Inicio: entradas de programas que ya no están en el disco ----------
    const string StartupId = "startup_left";
    static List<StartItem> StartupLeft() => Startup.List().Where(x => x.Missing && x.Group == "Programas").ToList();

    /// <summary>Solo los temporales seguros: lo que borra el modo simple («¿Borrar archivos temporales?»).
    /// Ni la Papelera, ni la caché de shaders, ni volcados o informes de errores (sirven para diagnosticar).</summary>
    public static IEnumerable<string> TempIds => Roots().Select(r => r.id).Where(id => !CareIds.Contains(id));

    public static CleanResult Run(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        // con un juego abierto no se tocan sus shaders; con Windows Update trabajando no se tocan sus descargas
        if (AutoStart.Busy()) set.ExceptWith(ShaderIds);
        if (UpdateBusy()) set.Remove("wu_download");
        var r = RunPaths(Roots().Where(x => set.Contains(x.id)).Select(x => x.path));
        if (set.Contains(StartupId))
        {
            int gone = 0, bad = 0;
            foreach (var it in StartupLeft()) { if (Startup.Remove(it.Id).Ok) gone++; else bad++; }   // cada una con copia en Copias
            r = new CleanResult(r.FreedBytes, r.Deleted + gone, r.Skipped + bad);
        }
        if (!set.Contains(RecycleId)) return r;
        var (rb, ri) = Recycle();
        if (ri == 0) return r;
        var ok = SHEmptyRecycleBinW(IntPtr.Zero, null, 0x7) == 0;   // sin confirmar, sin barra, sin sonido
        var (lb, li) = Recycle();
        var freed = ok ? rb : Math.Max(0, rb - lb);
        return new CleanResult(r.FreedBytes + freed, r.Deleted + (int)Math.Min(ri - li, int.MaxValue), r.Skipped + (int)Math.Min(li, int.MaxValue));
    }

    /// <summary>Windows está instalando o descargando algo: el instalador de módulos o el orquestador de actualizaciones corren.</summary>
    static bool UpdateBusy()
    {
        foreach (var n in new[] { "TiWorker", "TrustedInstaller", "MoUsoCoreWorker", "wuauclt", "SetupHost" })
        {
            var ps = System.Diagnostics.Process.GetProcessesByName(n);
            var any = ps.Length > 0; foreach (var x in ps) x.Dispose();
            if (any) return true;
        }
        return false;
    }

    /// <summary>Borra solo archivos (nunca carpetas ni enlaces) con más de 24 h bajo las rutas dadas.</summary>
    internal static CleanResult RunPaths(IEnumerable<string> roots)
    {
        long freed = 0; int del = 0, skip = 0;
        foreach (var root in roots)
            foreach (var f in Files(root).ToList())
            {
                try { var len = f.Length; f.Delete(); freed += len; del++; }
                catch { skip++; }
            }
        return new CleanResult(freed, del, skip);
    }

    /// <summary>Prueba en una carpeta aislada: archivo viejo, archivo nuevo, archivo bloqueado y junction hacia fuera.</summary>
    internal static string SelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "vigia-selftest-" + Guid.NewGuid().ToString("N"));
        var outside = root + "-outside";
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        Directory.CreateDirectory(outside);
        var old = DateTime.UtcNow.AddDays(-3);
        void Mk(string p, bool aged) { File.WriteAllText(p, new string('x', 1000)); if (aged) File.SetLastWriteTimeUtc(p, old); }
        Mk(Path.Combine(root, "viejo.tmp"), true);
        Mk(Path.Combine(root, "sub", "viejo2.tmp"), true);
        Mk(Path.Combine(root, "nuevo.tmp"), false);
        Mk(Path.Combine(root, "bloqueado.tmp"), true);
        Mk(Path.Combine(outside, "protegido.txt"), true);
        var link = Path.Combine(root, "enlace");
        var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false });
        mk!.WaitForExit();
        CleanResult r;
        using (File.Open(Path.Combine(root, "bloqueado.tmp"), FileMode.Open, FileAccess.Read, FileShare.None))
            r = RunPaths(new[] { root });
        var checks = new (string what, bool ok)[]
        {
            ("viejo borrado", !File.Exists(Path.Combine(root, "viejo.tmp"))),
            ("viejo en subcarpeta borrado", !File.Exists(Path.Combine(root, "sub", "viejo2.tmp"))),
            ("nuevo (<24h) intacto", File.Exists(Path.Combine(root, "nuevo.tmp"))),
            ("bloqueado saltado", File.Exists(Path.Combine(root, "bloqueado.tmp")) && r.Skipped == 1),
            ("junction creada", Directory.Exists(link)),
            ("destino del junction intacto", File.Exists(Path.Combine(outside, "protegido.txt"))),
            ("conteo = 2 borrados / 2000 bytes", r.Deleted == 2 && r.FreedBytes == 2000),
            // el modo simple usa TempIds: ni shaders, ni volcados, ni informes de errores, ni la Papelera
            ("modo simple sin cachés de shaders, volcados, informes ni Papelera",
                !TempIds.Any(id => id is "d3d_cache" or "crash_dumps" or "wer" or RecycleId) && CareOf("d3d_cache") == "care" && CareOf("user_temp") == "safe"),
            ("modo simple no toca Windows Update ni cachés de NVIDIA/AMD",
                !TempIds.Any(id => id == "wu_download" || ShaderIds.Contains(id)) && ShaderIds.All(id => CareOf(id) == "care")),
        };
        try { Directory.Delete(link); } catch { }
        try { Directory.Delete(root, true); Directory.Delete(outside, true); } catch { }
        var lines = checks.Select(c => $"{(c.ok ? "OK  " : "FAIL")} {c.what}");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine + (checks.All(c => c.ok) ? "SELFTEST PASS" : "SELFTEST FAIL");
    }
}
