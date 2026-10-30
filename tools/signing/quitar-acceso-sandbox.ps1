<#
  Deja la carpeta de la clave de firma (y todo lo de dentro) solo para tu cuenta, SYSTEM y Administradores: quita el
  grupo CodexSandboxUsers y cualquier otro permiso de más, también los heredados del perfil (corta la herencia). No abre, no copia, no mueve ni cambia la clave: solo toca permisos, y antes guarda los permisos actuales para
  poder volver atrás.

  Lo ejecuta el dueño de la clave en SU consola (no un agente):
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\signing\quitar-acceso-sandbox.ps1 -Puente <Puente.exe>
  Volver atrás:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\signing\quitar-acceso-sandbox.ps1 -Deshacer <copia.acl>

  Códigos: 0 hecho y la clave sale «protegida» · 1 error · 3 hecho, pero la clave sigue abierta a otra cuenta o grupo
  (por ejemplo heredado del perfil): no firmes con ella hasta resolverlo.
#>
param(
  [string]$Carpeta = (Join-Path $env:USERPROFILE '.vigia'),
  [string]$Grupo = 'CodexSandboxUsers',
  [string]$Clave = 'release-key.pem',
  [string]$Puente,
  [string]$Copia,
  [string]$Deshacer
)
$ErrorActionPreference = 'Stop'
$icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'

if ($Deshacer) {
  if (-not (Test-Path -LiteralPath $Deshacer)) { Write-Host "No existe la copia $Deshacer"; exit 1 }
  $padre = Split-Path -Parent ([IO.Path]::GetFullPath($Carpeta))
  & $icacls $padre /restore $Deshacer /c | Out-Null
  if ($LASTEXITCODE -ne 0) { Write-Host "icacls /restore falló ($LASTEXITCODE)"; exit 1 }
  Write-Host "Permisos devueltos a como estaban (copia $Deshacer)."
  exit 0
}

$Carpeta = [IO.Path]::GetFullPath($Carpeta)
if (-not (Test-Path -LiteralPath $Carpeta -PathType Container)) { Write-Host "No existe la carpeta $Carpeta"; exit 1 }
if ((Get-Item -LiteralPath $Carpeta -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { Write-Host "$Carpeta es un enlace: no se toca"; exit 1 }

try { $sid = (New-Object Security.Principal.NTAccount($Grupo)).Translate([Security.Principal.SecurityIdentifier]).Value }
catch { Write-Host "El grupo $Grupo no existe en este equipo: nada que quitar."; $sid = $null }

if (-not $Copia) { $Copia = Join-Path (Split-Path -Parent $Carpeta) ("vigia-acl-antes-{0:yyyyMMdd-HHmmss}.acl" -f (Get-Date)) }
if (Test-Path -LiteralPath $Copia) { Write-Host "Ya existe $Copia; no se pisa."; exit 1 }

# 1. copia de los permisos actuales (carpeta y todo lo de dentro), fuera de la carpeta
& $icacls $Carpeta /save $Copia /t /c | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Copia)) { Write-Host "No se pudo guardar la copia de permisos: no se cambia nada."; exit 1 }
Write-Host "Copia de permisos: $Copia"

# 2. la carpeta deja de heredar permisos de arriba (el perfil o lo que haya encima puede dar acceso a grupos de más)
#    y se queda solo con: tu cuenta, SYSTEM y Administradores. Lo de dentro vuelve a heredar solo de ella.
$antes = @((Get-Acl -LiteralPath $Carpeta).Access | ForEach-Object { "$($_.IdentityReference)" } | Sort-Object -Unique)
$yo = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& $icacls $Carpeta /inheritance:r /grant:r "*${yo}:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /c | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "icacls falló ($LASTEXITCODE). Para volver atrás: -Deshacer $Copia"; exit 1 }
$acl = Get-Acl -LiteralPath $Carpeta
foreach ($r in @($acl.Access)) {
  $rs = try { $r.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { "$($r.IdentityReference)" }
  if ($rs -notin @($yo, 'S-1-5-18', 'S-1-5-32-544')) { [void]$acl.RemoveAccessRuleSpecific($r) }
}
Set-Acl -LiteralPath $Carpeta -AclObject $acl
& $icacls (Join-Path $Carpeta '*') /reset /t /c | Out-Null
$despues = @((Get-Acl -LiteralPath $Carpeta).Access | ForEach-Object { "$($_.IdentityReference)" } | Sort-Object -Unique)
$quitados = @($antes | Where-Object { $_ -notin $despues })
if ($quitados) { Write-Host "Sin acceso ahora: $($quitados -join ', ')" }

# 3. ¿queda el grupo, o cualquier cuenta que no sea tuya, SYSTEM o Administradores, en algún sitio?
$quedan = @()
foreach ($i in @(Get-Item -LiteralPath $Carpeta -Force) + @(Get-ChildItem -LiteralPath $Carpeta -Recurse -Force -EA SilentlyContinue)) {
  foreach ($r in (Get-Acl -LiteralPath $i.FullName).Access) {
    $rs = try { $r.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { "$($r.IdentityReference)" }
    if (($sid -and $rs -eq $sid) -or $rs -notin @($yo, 'S-1-5-18', 'S-1-5-32-544')) {
      $quedan += "$($i.FullName): $($r.IdentityReference) ($(if ($r.IsInherited) {'heredado'} else {'explícito'}))"
    }
  }
}
if ($quedan) { Write-Host "Siguen con acceso:"; $quedan | ForEach-Object { Write-Host "  $_" }; Write-Host "Volver atrás: -Deshacer $Copia"; exit 1 }
Write-Host "Solo tu cuenta, SYSTEM y Administradores pueden abrir $Carpeta y lo de dentro ($Grupo incluido: sin acceso)."

# 4. comprobación con Puente (mira permisos, no abre la clave)
$k = Join-Path $Carpeta $Clave
if ($Puente -and (Test-Path -LiteralPath $k)) {
  & $Puente --key-check $k
  $rc = $LASTEXITCODE
  if ($rc -ne 0) { Write-Host "La clave sigue abierta a otra cuenta o grupo (ver arriba). No firmes con ella."; exit 3 }
}
Write-Host "Hecho. Para volver atrás: -Deshacer $Copia"
exit 0
