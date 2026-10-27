namespace Vigia;

public sealed record CleanTarget(string Id, string Name, string Path, long Bytes, int Files);
public sealed record CleanResult(long FreedBytes, int Deleted, int Skipped);

/// <summary>Limpieza conservadora: solo carpetas temporales del usuario, archivos con más de 24 h, nunca sigue enlaces.</summary>
public static class Cleanup
{
    static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    static IEnumerable<(string id, string name, string path)> Roots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return ("user_temp", "Temporales de usuario", Path.GetTempPath());
        yield return ("d3d_cache", "Caché de shaders DirectX", Path.Combine(local, "D3DSCache"));
        yield return ("crash_dumps", "Volcados de errores", Path.Combine(local, "CrashDumps"));
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

    public static List<CleanTarget> Scan() =>
        Roots().Select(r =>
        {
            long b = 0; int n = 0;
            foreach (var f in Files(r.path)) { try { b += f.Length; n++; } catch { } }
            return new CleanTarget(r.id, r.name, r.path, b, n);
        }).ToList();

    public static CleanResult Run(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        return RunPaths(Roots().Where(r => set.Contains(r.id)).Select(r => r.path));
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
        };
        try { Directory.Delete(link); } catch { }
        try { Directory.Delete(root, true); Directory.Delete(outside, true); } catch { }
        var lines = checks.Select(c => $"{(c.ok ? "OK  " : "FAIL")} {c.what}");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine + (checks.All(c => c.ok) ? "SELFTEST PASS" : "SELFTEST FAIL");
    }
}
