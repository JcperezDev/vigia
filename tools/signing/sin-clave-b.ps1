# Comprueba, en el PC de desarrollo, que no hay ninguna clave privada de relevo (B, C, ...) de VigiaFirma.
# Solo busca por nombre y por la cabecera de clave cifrada; no abre .pem ajenos ni la clave A (~/.vigia/release-key.pem).
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File tools\signing\sin-clave-b.ps1 [-Raiz <carpeta>]
# rc 0 = no hay; rc 1 = hay algo (lista las rutas, nunca el contenido).
param([string[]]$Raiz = @($env:USERPROFILE, 'C:\ProgramData'))
$nombres = 'vigia-*.dpapi', 'vigia-*.respaldo.pem'
$hallado = @()
foreach ($r in $Raiz) {
  if (-not (Test-Path -LiteralPath $r)) { continue }
  # -Include no filtra junto a -LiteralPath en PowerShell 5.1 (lo devolvería todo): se filtra por nombre aparte
  $hallado += Get-ChildItem -LiteralPath $r -Recurse -Force -File -ErrorAction SilentlyContinue |
    Where-Object { $n = $_.Name; @($nombres | Where-Object { $n -like $_ }).Count -gt 0 } |
    Select-Object -ExpandProperty FullName
}
if ($hallado.Count) { 'Claves de VigiaFirma en este equipo (no deberían estar en el de desarrollo):'; $hallado | ForEach-Object { "  $_" }; exit 1 }
"Ninguna clave de VigiaFirma (vigia-*.dpapi, vigia-*.respaldo.pem) en: $($Raiz -join ', ')"
exit 0
