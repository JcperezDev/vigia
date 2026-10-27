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
        long freed = 0; int del = 0, skip = 0;
        foreach (var r in Roots().Where(r => set.Contains(r.id)))
            foreach (var f in Files(r.path).ToList())
            {
                try { var len = f.Length; f.Delete(); freed += len; del++; }
                catch { skip++; }
            }
        return new CleanResult(freed, del, skip);
    }
}
