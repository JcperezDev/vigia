using System.Text.Json;

namespace Vigia;

// Pasa una ISO de Windows a una memoria USB para instalar desde ella (lo que hace Rufus, sin Rufus).
// Solo acepta memorias USB extraíbles: nunca discos internos, el de arranque, el del sistema ni discos externos.
// La memoria se borra: la UI pide confirmar dos veces y aquí se vuelve a comprobar el disco justo antes.
// Formato: GPT + una partición FAT32 (arranca en cualquier PC con UEFI, que es lo que pide Windows 11).
// Si install.wim pasa de 4 GB (límite de FAT32) se parte en install.swm con DISM, como documenta Microsoft.
public static partial class IsoBuild
{
    public record UsbDrive(int Number, string Name, long Bytes, string Serial, string[] Letters);
    public record UsbDone(bool Ok, string Message, string? Letter, double Minutes);

    const long Fat32Max = 32L << 30;          // Format-Volume no crea FAT32 de más de 32 GB
    const long FileMax = (4L << 30) - 1;      // tamaño máximo de un archivo en FAT32
    const long MinUsb = 7L << 30, MaxUsb = 256L << 30;

    public static List<UsbDrive> UsbList()
    {
        // BusType USB + medio extraíble (Win32_DiskDrive): deja fuera discos externos con datos.
        const string ps = "$r = Get-CimInstance Win32_DiskDrive | ? { $_.InterfaceType -eq 'USB' -and $_.MediaType -like 'Removable*' } | % { $_.Index }; " +
            "Get-Disk | ? { $_.BusType -eq 'USB' -and -not $_.IsBoot -and -not $_.IsSystem -and $r -contains $_.Number } | % { " +
            "[pscustomobject]@{ Number = $_.Number; Name = $_.FriendlyName; Bytes = [int64]$_.Size; Serial = ('' + $_.SerialNumber).Trim(); " +
            "Letters = @(Get-Partition -DiskNumber $_.Number -ErrorAction SilentlyContinue | ? DriveLetter | % { [string]$_.DriveLetter }) } } | ConvertTo-Json -Compress -Depth 3";
        var (e, o) = Ps(ps);
        if (e != 0 || string.IsNullOrWhiteSpace(o)) return [];
        var t = o.Trim();
        if (t.StartsWith('{')) t = $"[{t}]";
        try
        {
            return (JsonSerializer.Deserialize<List<UsbDrive>>(t, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [])
                .Where(d => d.Bytes is >= MinUsb and <= MaxUsb).ToList();
        }
        catch { return []; }
    }

    public static UsbDone ToUsb(string iso, int number, string serial, Action<Progress> report)
    {
        if (Busy) return new(false, "Vigía ya está trabajando con una ISO.", null, 0);
        Busy = true; _cancel = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool isoMounted = false;
        const int N = 4;
        try
        {
            if (!File.Exists(iso) || !iso.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return UFail("No encuentro la ISO.");
            // 0. el disco tiene que seguir siendo la misma memoria USB que eligió el usuario
            var d = UsbList().FirstOrDefault(x => x.Number == number && x.Serial == serial);
            if (d is null) return UFail("Esa memoria USB ya no está o cambió. Vuelve a elegirla.");
            var isoRoot = Path.GetPathRoot(Path.GetFullPath(iso))?.TrimEnd('\\', ':');
            if (isoRoot is { Length: 1 } && d.Letters.Contains(isoRoot, StringComparer.OrdinalIgnoreCase))
                return UFail("La ISO está dentro de esa misma memoria USB. Cópiala antes a otro disco.");

            // 1. revisar la ISO antes de borrar nada
            report(new("Revisando la ISO", 1, N, null));
            var root = Mount(iso); isoMounted = true;
            var src = root.EndsWith('\\') ? root : root + "\\";
            if (!File.Exists(Path.Combine(src, "efi", "boot", "bootx64.efi"))) return UFail("Esa ISO no arranca en UEFI (no trae efi\\boot\\bootx64.efi).");
            var wim = Wim(src) ?? throw new Exception("La ISO no trae sources\\install.wim.");
            var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).ToList();
            long total = files.Sum(f => new FileInfo(f).Length);
            var big = files.Where(f => new FileInfo(f).Length > FileMax).ToList();
            if (big.Any(f => !f.Equals(wim, StringComparison.OrdinalIgnoreCase) || f.EndsWith(".esd", StringComparison.OrdinalIgnoreCase)))
                return UFail($"La ISO trae un archivo de más de 4 GB que no se puede partir: {Path.GetFileName(big[0])}.");
            if (total + (256L << 20) > Math.Min(d.Bytes, Fat32Max)) return UFail($"No cabe: la ISO ocupa {total / 1_073_741_824.0:0.0} GB.");
            Check();

            // 2. borrar y preparar la memoria (aquí se pierde lo que tenía)
            report(new($"Preparando {d.Name}", 2, N, null));
            var size = Math.Min(d.Bytes - (16L << 20), Fat32Max - (1L << 20));
            var (pe, po) = Ps(
                $"$ErrorActionPreference = 'Stop'; $d = Get-Disk -Number {number}; " +
                $"if ($d.BusType -ne 'USB' -or $d.IsBoot -or $d.IsSystem -or ('' + $d.SerialNumber).Trim() -ne '{serial.Replace("'", "''")}') {{ throw 'disco cambiado' }}; " +
                $"if ($d.PartitionStyle -ne 'RAW') {{ Clear-Disk -Number {number} -RemoveData -RemoveOEM -Confirm:$false }}; " +
                $"Initialize-Disk -Number {number} -PartitionStyle GPT -ErrorAction SilentlyContinue; " +
                $"$p = New-Partition -DiskNumber {number} -Size {size} -GptType '{{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}}'; " +
                // sin letra hasta tener formato: así Windows no salta con «Hay que formatear el disco»
                "$null = Format-Volume -Partition $p -FileSystem FAT32 -NewFileSystemLabel 'VIGIA' -Confirm:$false -Force; " +
                $"Add-PartitionAccessPath -DiskNumber {number} -PartitionNumber $p.PartitionNumber -AssignDriveLetter; " +
                $"(Get-Partition -DiskNumber {number} -PartitionNumber $p.PartitionNumber).DriveLetter", 300);
            var letter = po.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length == 1 && char.IsLetter(l[0]));
            if (pe != 0 || letter is null) return UFail($"No pude preparar la memoria: {po.Trim().Split('\n').LastOrDefault()?.Trim()}");
            var dst = $"{letter}:\\";

            // 3. copiar todo menos install.wim cuando no cabe en FAT32
            long done = 0;
            var wimBig = new FileInfo(wim).Length > FileMax;
            var copy = files.Where(f => !(wimBig && f.Equals(wim, StringComparison.OrdinalIgnoreCase))).ToList();
            long copyTotal = copy.Sum(f => new FileInfo(f).Length);
            foreach (var f in copy)
            {
                Check();
                var to = Path.Combine(dst, Path.GetRelativePath(src, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                CopyFile(f, to, n => { done += n; report(new("Copiando Windows a la memoria", 3, N, 100.0 * done / Math.Max(1, copyTotal))); });
                File.SetAttributes(to, FileAttributes.Normal);
            }

            // 4. partir la imagen grande en trozos de 3,8 GB
            if (wimBig)
            {
                report(new("Partiendo la imagen de Windows", 4, N, null));
                Directory.CreateDirectory(Path.Combine(dst, "sources"));
                Must(Dism($"/Split-Image /ImageFile:\"{wim}\" /SWMFile:\"{Path.Combine(dst, "sources", "install.swm")}\" /FileSize:3800",
                    p => report(new("Partiendo la imagen de Windows", 4, N, p))), "partir la imagen");
            }
            Ps($"Write-VolumeCache -DriveLetter {letter}", 120);
            Unmount(iso); isoMounted = false;
            return new(true, $"Listo: {d.Name} ({letter}:) ya sirve para instalar Windows. Arranca el PC desde el USB (en MSI, F11 al encender).", letter, sw.Elapsed.TotalMinutes);
        }
        catch (OperationCanceledException) { return UFail("Cancelado. La memoria quedó a medias: vuelve a pasarla antes de usarla."); }
        catch (Exception ex) { return UFail(ex.Message); }
        finally
        {
            if (isoMounted) try { Unmount(iso); } catch { }
            Busy = false; _proc = null;
        }

        UsbDone UFail(string m) => new(false, m, null, sw.Elapsed.TotalMinutes);
    }

    static (int exit, string output) Ps(string command, int timeoutS = 60)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(Tweaks.Sys("powershell.exe"), "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
            Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue'; " + command)))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var o = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(timeoutS * 1000)) { try { p.Kill(true); } catch { } return (-1, o + "\ntardó demasiado"); }
        return (p.ExitCode, o + err.Result);
    }
}
