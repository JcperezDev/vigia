# Publica Vigia autocontenido en %LOCALAPPDATA%\Programs\Vigia y crea accesos directos (Escritorio + Inicio).
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Vigia'
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$env:DOTNET_ROOT = Split-Path $dotnet; $env:DOTNET_NOLOGO = '1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Get-Process Vigia -EA SilentlyContinue | Stop-Process -Force
& $dotnet publish (Join-Path $repo 'src\Vigia\Vigia.csproj') -c Release -r win-x64 --self-contained true -o $dest -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "publish falló ($LASTEXITCODE)" }

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
