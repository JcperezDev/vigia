# OBSOLETO: el icono actual (Prisma) lo genera tools/make-icon.py. Se deja como referencia del formato .ico.
param([string]$Out)
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $r = [Math]::Max(2, [int]($s * 0.22))
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, 2*$r, 2*$r, 180, 90); $path.AddArc($s-2*$r-1, 0, 2*$r, 2*$r, 270, 90)
  $path.AddArc($s-2*$r-1, $s-2*$r-1, 2*$r, 2*$r, 0, 90); $path.AddArc(0, $s-2*$r-1, 2*$r, 2*$r, 90, 90); $path.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 17, 40))), $path)
  $violet = [System.Drawing.Color]::FromArgb(255, 176, 132, 255)
  $pen = New-Object System.Drawing.Pen $violet, ([Math]::Max(1.2, $s * 0.075))
  $m = $s * 0.16; $w = $s - 2*$m; $cy = $s / 2
  $eye = New-Object System.Drawing.Drawing2D.GraphicsPath
  $eye.AddBezier([single]$m, [single]$cy, [single]($m + $w*0.25), [single]($cy - $w*0.42), [single]($m + $w*0.75), [single]($cy - $w*0.42), [single]($m + $w), [single]$cy)
  $eye.AddBezier([single]($m + $w), [single]$cy, [single]($m + $w*0.75), [single]($cy + $w*0.42), [single]($m + $w*0.25), [single]($cy + $w*0.42), [single]$m, [single]$cy)
  $g.DrawPath($pen, $eye)
  $pr = $s * 0.13
  $g.FillEllipse((New-Object System.Drawing.SolidBrush $violet), [single]($s/2 - $pr), [single]($cy - $pr), [single](2*$pr), [single](2*$pr))
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  ,$ms.ToArray()
}
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $d = $pngs[$i]
  $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$d.Length); $bw.Write([uint32]$offset)
  $offset += $d.Length
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Close()
"ok $Out $((Get-Item $Out).Length) bytes"
