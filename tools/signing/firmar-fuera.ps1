<#
  Firma una versión en el EQUIPO DE FIRMA (separado, sin red, sin agentes). Lo ejecuta el dueño de la clave.

    powershell -NoProfile -ExecutionPolicy Bypass -File firmar-fuera.ps1 -Version E:\0.21.0 -Clave C:\VigiaFirma\release-key.dpapi `
      -Puente C:\VigiaFirma\Puente.exe -HuellaPuente <SHA-256 anotado cuando copiaste ese Puente.exe>

  Qué hace, en orden, y para en el primer problema:
    1. comprueba que el Puente.exe que va a firmar es el que anotaste (SHA-256) y no otro que alguien cambió
    2. avisa y para si el equipo tiene red (se puede forzar con -PermitirRed, no recomendado)
    3. la clave no puede estar dentro de la carpeta de la versión (esa carpeta vuelve al PC de desarrollo)
    4. Puente --key-check: la clave solo la pueden abrir esta cuenta, SYSTEM y Administradores
    5. Puente --sign-release: firma las cuatro piezas y comprueba que cuadran con la clave pública de ese Puente;
       si algo no cuadra, borra lo firmado
    6. deja firmas-<versión>.txt con las huellas para compararlas con las de release.sh
  La clave nunca se copia, no se imprime y no sale de este equipo. A la carpeta de la versión solo van los cuatro .sig.
  Códigos: 0 firmado · 1 problema (nada firmado) · 3 clave con permisos de más · 4 Puente.exe no es el anotado · 5 hay red.
#>
param(
  [Parameter(Mandatory)][string]$Version,
  [Parameter(Mandatory)][string]$Clave,
  [Parameter(Mandatory)][string]$Puente,
  [Parameter(Mandatory)][string]$HuellaPuente,
  [switch]$PermitirRed
)
$ErrorActionPreference = 'Stop'
$Version = [IO.Path]::GetFullPath($Version); $Clave = [IO.Path]::GetFullPath($Clave); $Puente = [IO.Path]::GetFullPath($Puente)

# 1
$h = (Get-FileHash -LiteralPath $Puente -Algorithm SHA256).Hash
if ($h -ne $HuellaPuente.Trim().ToUpperInvariant()) { Write-Host "Puente.exe no es el anotado (huella $h). No se firma."; exit 4 }
# 2
$red = @(Get-NetAdapter -EA SilentlyContinue | Where-Object Status -eq 'Up')
if ($red.Count -gt 0 -and -not $PermitirRed) { Write-Host "El equipo tiene red activa ($($red.Name -join ', ')). Desconéctala y vuelve a intentarlo."; exit 5 }
if ($red.Count -gt 0) { Write-Host "Aviso: firmando con red activa (-PermitirRed)." }
# 3
if ($Clave.StartsWith($Version.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { Write-Host "La clave está dentro de la carpeta de la versión: no se firma."; exit 1 }
if (-not (Test-Path -LiteralPath $Clave -PathType Leaf)) { Write-Host "No existe la clave $Clave"; exit 1 }
# 4
& $Puente --key-check $Clave | Out-Host
if ($LASTEXITCODE -ne 0) { exit 3 }
# 5
$ErrorActionPreference = 'Continue'   # en PowerShell 5.1, 2>&1 de un programa con Stop corta el script
$out = & $Puente --sign-release $Version $Clave 2>&1 | ForEach-Object { "$_" }
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$out | Out-Host
if ($rc -ne 0) { Write-Host "No se firmó (rc $rc)."; exit 1 }
# 6
$ver = Split-Path -Leaf $Version
$txt = Join-Path $Version "firmas-$ver.txt"
@("Firmado el $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') en $env:COMPUTERNAME con Puente $h") + ($out | Where-Object { $_ -match '^[0-9a-f]{64}  ' }) |
  Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host "Huellas en $txt. Devuelve los cuatro .sig a out\$ver y compara estas huellas con las de release.sh."
exit 0
