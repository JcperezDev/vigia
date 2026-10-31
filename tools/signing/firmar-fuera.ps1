<#
  Firma una versión en el EQUIPO DE FIRMA (separado, sin red, sin agentes). Lo ejecuta el dueño de la clave.
  Procedimiento completo: docs/FIRMA_OFFLINE.md.

    powershell -NoProfile -ExecutionPolicy Bypass -File firmar-fuera.ps1 -Version E:\0.21.0 `
      -Clave C:\VigiaFirma\claves\vigia-A.dpapi -Publica C:\VigiaFirma\claves\vigia-A.publica.txt `
      -Huellas C:\VigiaFirma\huellas-0.21.0.txt

  -Huellas  hoja con el SHA-256 de las cuatro piezas (formato de candidato.txt), llevada APARTE de la carpeta de la
            versión (otra memoria, o copiada a mano del informe). Si la carpeta llegó cambiada, no cuadra y no se firma.
  -Publica  la clave pública con la que TIENEN que poder verificar las instalaciones. En la versión de transición es la
            A (la única que conoce 0.20.17). Si lo firmado no verifica con esa clave, se quitan las firmas.
  -Puente / -HuellaPuente  opcionales: el Puente que firma. Por defecto, el Puente.exe de la propia versión, ya
            comprobado con la hoja de huellas.
  -Firma    VigiaFirma.exe (por defecto, junto a este script).
  -ClaveRelevo  solo en la versión de transición: la clave nueva (vigia-B.dpapi). Antes de firmar se firma con ella un
            archivo de prueba fuera de la carpeta de la versión y el Puente de la versión tiene que aceptarlo; si no,
            esa versión no confía en B y no se firma (una transición sin B dejaría tiradas las instalaciones).
  -RelevoPublica  lo mismo cuando B NO es un archivo sino la clave Ed25519 del agente SSH de Bitwarden: en vez de
            firmar una prueba con B (eso pediría una aprobación en Bitwarden), se exige que `Puente --trusted-keys` de la
            versión liste la huella de vigia-B.publica.txt. La versión de transición se sigue firmando con A (-Clave).

  Qué hace, en orden, y para en el primer problema:
    1. la hoja de huellas está fuera de la carpeta de la versión y las cuatro piezas son exactamente las anotadas
    2. el Puente.exe que va a firmar es el anotado (SHA-256)
    3. para si el equipo tiene red (se puede forzar con -PermitirRed, no recomendado)
    4. la clave no puede estar dentro de la carpeta de la versión (esa carpeta vuelve al PC de desarrollo)
    5. Puente --key-check: la clave solo la pueden abrir esta cuenta, SYSTEM y Administradores
       (con -ClaveRelevo, además: el Puente de la versión acepta una firma de prueba hecha con la clave nueva;
        con -RelevoPublica: el Puente de la versión lista la huella de la clave nueva entre las de confianza)
    6. Puente --sign-release: firma las cuatro piezas y comprueba que cuadran con la lista de confianza de ese Puente
    7. VigiaFirma verificar: comprobación aparte (otro programa) con SOLO la clave -Publica y la hoja de huellas;
       si falla, quita las cuatro firmas
    8. deja firmas-<versión>.txt con las huellas
  La clave nunca se copia, no se imprime y no sale de este equipo. A la carpeta de la versión solo van los cuatro .sig.
  Códigos: 0 firmado · 1 problema (nada firmado) · 3 clave con permisos de más · 4 piezas o Puente no son los anotados
           · 5 hay red · 6 lo firmado no verifica con -Publica (firmas quitadas)
           · 7 con -ClaveRelevo / -RelevoPublica: el Puente de la versión no confía en la clave nueva.
#>
param(
  [Parameter(Mandatory)][string]$Version,
  [Parameter(Mandatory)][string]$Clave,
  [Parameter(Mandatory)][string]$Publica,
  [Parameter(Mandatory)][string]$Huellas,
  [string]$Puente,
  [string]$HuellaPuente,
  [string]$Firma = (Join-Path $PSScriptRoot 'VigiaFirma.exe'),
  [string]$ClaveRelevo,
  [string]$RelevoPublica,
  [switch]$PermitirRed
)
$ErrorActionPreference = 'Stop'
$Version = [IO.Path]::GetFullPath($Version); $Clave = [IO.Path]::GetFullPath($Clave)
$Huellas = [IO.Path]::GetFullPath($Huellas); $Publica = [IO.Path]::GetFullPath($Publica); $Firma = [IO.Path]::GetFullPath($Firma)
function Dentro($p) { $p.StartsWith($Version.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) }
foreach ($f in @($Huellas, $Publica, $Firma)) { if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { Write-Host "No existe $f"; exit 1 } }

# 1
if (Dentro $Huellas) { Write-Host "La hoja de huellas está dentro de la carpeta de la versión: tiene que llegar por otro camino. No se firma."; exit 4 }
$want = @{}
foreach ($l in Get-Content -LiteralPath $Huellas) { if ($l -match '^(?:sha256 )?([0-9a-fA-F]{64}) [ *]?(\S+)\s*$') { $want[$Matches[2]] = $Matches[1].ToUpperInvariant() } }
$zip = @(Get-ChildItem -LiteralPath $Version -Filter 'Vigia-*-win-x64.zip')
if ($zip.Count -ne 1) { Write-Host "Tiene que haber un solo Vigia-*-win-x64.zip en $Version."; exit 1 }
$piezas = @($zip[0].Name, 'Puente.exe', 'Vigia-Setup.exe', 'vigia-manifest.json')
foreach ($n in $piezas) {
  $p = Join-Path $Version $n
  $h = if (Test-Path -LiteralPath $p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash } else { '-' }
  if (-not $want.ContainsKey($n) -or $want[$n] -ne $h) { Write-Host "$n no es el anotado en la hoja (aquí $h). No se firma."; exit 4 }
}
Write-Host "Las cuatro piezas coinciden con la hoja de huellas."
# 2
if (-not $Puente) { $Puente = Join-Path $Version 'Puente.exe'; $HuellaPuente = $want['Puente.exe'] }
$Puente = [IO.Path]::GetFullPath($Puente)
if (-not $HuellaPuente) { Write-Host "Con -Puente hace falta -HuellaPuente."; exit 1 }
$hp = (Get-FileHash -LiteralPath $Puente -Algorithm SHA256).Hash
if ($hp -ne $HuellaPuente.Trim().ToUpperInvariant()) { Write-Host "Puente.exe no es el anotado (huella $hp). No se firma."; exit 4 }
# 3
$red = @(Get-NetAdapter -EA SilentlyContinue | Where-Object Status -eq 'Up')
if ($red.Count -gt 0 -and -not $PermitirRed) { Write-Host "El equipo tiene red activa ($($red.Name -join ', ')). Desconéctala y vuelve a intentarlo."; exit 5 }
if ($red.Count -gt 0) { Write-Host "Aviso: firmando con red activa (-PermitirRed)." }
# 4
if (Dentro $Clave) { Write-Host "La clave está dentro de la carpeta de la versión: no se firma."; exit 1 }
if (-not (Test-Path -LiteralPath $Clave -PathType Leaf)) { Write-Host "No existe la clave $Clave"; exit 1 }
# 5
& $Puente --key-check $Clave | Out-Host
if ($LASTEXITCODE -ne 0) { exit 3 }
if ($ClaveRelevo) {
  $ClaveRelevo = [IO.Path]::GetFullPath($ClaveRelevo)
  if (Dentro $ClaveRelevo) { Write-Host "La clave nueva está dentro de la carpeta de la versión: no se firma."; exit 1 }
  & $Puente --key-check $ClaveRelevo | Out-Host
  if ($LASTEXITCODE -ne 0) { exit 3 }
  $probe = Join-Path ([IO.Path]::GetTempPath()) ("vigia-relevo-" + [guid]::NewGuid().ToString('N') + ".txt")
  try {
    Set-Content -LiteralPath $probe -Value "prueba de relevo $(Get-Date -Format o)" -Encoding ASCII
    & $Puente --sign $probe $ClaveRelevo | Out-Host
    $okSign = $LASTEXITCODE -eq 0
    & $Puente --verify $probe | Out-Host
    $okVer = $okSign -and $LASTEXITCODE -eq 0
  } finally { Remove-Item -LiteralPath $probe, "$probe.sig" -Force -EA SilentlyContinue }
  if (-not $okVer) { Write-Host "El Puente de esta versión NO acepta firmas de la clave nueva: no es una versión de transición. No se firma."; exit 7 }
  Write-Host "El Puente de esta versión acepta la clave nueva (relevo listo)."
}
if ($RelevoPublica) {
  $RelevoPublica = [IO.Path]::GetFullPath($RelevoPublica)
  $rt = Get-Content -LiteralPath $RelevoPublica -Raw
  if ($rt -notmatch '(?m)^huella ([0-9a-f]{64})\s*$') { Write-Host "$(Split-Path -Leaf $RelevoPublica) no es una clave pública de Vigía."; exit 1 }
  $hb = $Matches[1]
  $tk = @(& $Puente --trusted-keys 2>&1 | ForEach-Object { "$_" })
  $tk | ForEach-Object { Write-Host "  confía en: $_" }
  if (-not ($tk | Where-Object { $_ -match " $hb$" })) { Write-Host "El Puente de esta versión NO confía en la clave nueva ($hb): no es una versión de transición. No se firma."; exit 7 }
  Write-Host "El Puente de esta versión confía en la clave nueva $hb (relevo listo)."
}
# 6
$ErrorActionPreference = 'Continue'   # en PowerShell 5.1, 2>&1 de un programa con Stop corta el script
$out = & $Puente --sign-release $Version $Clave 2>&1 | ForEach-Object { "$_" }
$rc = $LASTEXITCODE
$out | Out-Host
if ($rc -ne 0) { $ErrorActionPreference = 'Stop'; Write-Host "No se firmó (rc $rc)."; exit 1 }
# 7
$ver = & $Firma verificar $Version $Publica $Huellas 2>&1 | ForEach-Object { "$_" }
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$ver | Out-Host
if ($rc -ne 0) {
  foreach ($n in $piezas) { Remove-Item -LiteralPath (Join-Path $Version "$n.sig") -Force -EA SilentlyContinue }
  Write-Host "Lo firmado no verifica con $(Split-Path -Leaf $Publica): se quitaron las cuatro firmas."; exit 6
}
# 8
$v = Split-Path -Leaf $Version
$txt = Join-Path $Version "firmas-$v.txt"
@("Firmado el $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') en $env:COMPUTERNAME con Puente $hp; comprobado con $(Split-Path -Leaf $Publica)") +
  ($out | Where-Object { $_ -match '^[0-9a-f]{64}  ' }) | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host "Huellas en $txt. Devuelve los cuatro .sig y ese archivo al PC de desarrollo."
exit 0
