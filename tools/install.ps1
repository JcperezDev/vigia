# SOLO DESARROLLO. Publica Vigia en %LOCALAPPDATA%\Programs\Vigia (carpeta de tu usuario, sin proteger) y crea accesos.
# Una Vigía ahí no pide permisos de administrador (Elevation.cs exige carpeta protegida; no hay forma de saltarlo).
# La instalación de verdad la hace Puente en %ProgramFiles%\Vigia (ver docs/INSTALL.md).
param([switch]$Dev)
$ErrorActionPreference = 'Stop'
if (-not $Dev) { throw 'install.ps1 es solo para desarrollo: pasa -Dev. Para instalar de verdad usa Puente (docs/INSTALL.md).' }
$repo = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Vigia'
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$env:DOTNET_ROOT = Split-Path $dotnet; $env:DOTNET_NOLOGO = '1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Get-Process Vigia -EA SilentlyContinue | Stop-Process -Force
& $dotnet publish (Join-Path $repo 'app\Vigia\Vigia.csproj') -c Release -r win-x64 --self-contained true -o $dest -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "publish falló ($LASTEXITCODE)" }

# Puente (instalador y actualizaciones) en su propia carpeta: así puede reemplazar la de Vigía
$puente = Join-Path $env:LOCALAPPDATA 'Programs\Vigia-Puente'
& $dotnet publish (Join-Path $repo 'app\Puente\Puente.csproj') -c Release -o $puente -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "publish de Puente falló ($LASTEXITCODE)" }

$exe = Join-Path $dest 'Vigia.exe'
$shell = New-Object -ComObject WScript.Shell
$links = @(
  (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Vigia.lnk'),
  (Join-Path ([Environment]::GetFolderPath('Programs')) 'Vigia.lnk')
)
foreach ($l in $links) {
  $s = $shell.CreateShortcut($l)
  $s.TargetPath = $exe; $s.WorkingDirectory = $dest
  $s.IconLocation = "$exe,0"; $s.Description = 'Vigía - monitor de hardware'
  $s.Save()
  "acceso directo: $l"
}
"instalado: $exe ($([math]::Round((Get-ChildItem $dest -Recurse | Measure-Object Length -Sum).Sum / 1MB)) MB)"
