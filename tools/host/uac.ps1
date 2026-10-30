<#
  Reactivar el Control de cuentas de usuario (UAC) en el PC del propietario. Lo ejecuta el propietario, no Vigía.

    uac.ps1                       solo mira: enseña los valores actuales y lo que cambiaría (no toca nada)
    uac.ps1 -Activar              guarda una copia de los valores actuales y pone los de fábrica de Windows:
                                  EnableLUA=1, ConsentPromptBehaviorAdmin=5, PromptOnSecureDesktop=1. Requiere reiniciar.
    uac.ps1 -Deshacer <copia>     vuelve exactamente a los valores guardados en <copia>. Requiere reiniciar.

  No reinicia el equipo. No toca ninguna otra clave. Códigos: 0 bien; 1 error; 3 falta abrirlo como administrador.
#>
param([switch]$Activar, [string]$Deshacer)
$ErrorActionPreference = 'Stop'
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
$names = 'EnableLUA', 'ConsentPromptBehaviorAdmin', 'PromptOnSecureDesktop'
$want = @{ EnableLUA = 1; ConsentPromptBehaviorAdmin = 5; PromptOnSecureDesktop = 1 }

function Now { $p = Get-ItemProperty $k; $o = [ordered]@{}; foreach ($n in $names) { $o[$n] = $p.$n }; $o }
function IsAdmin { ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
function Show($t, $o) { "${t}: " + (($o.GetEnumerator() | ForEach-Object { "$($_.Key)=$(if ($null -eq $_.Value) { '(no existe)' } else { $_.Value })" }) -join '  ') }

$cur = Now
Show 'Ahora' $cur
$pol = Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Policies\System' -EA SilentlyContinue
if ($pol -and $null -ne $pol.EnableLUA) { "Atención: hay una directiva de grupo que fija EnableLUA=$($pol.EnableLUA); manda sobre este cambio." }

if (-not $Activar -and -not $Deshacer) {
    Show 'Con -Activar quedaría' $want
    if ($cur.EnableLUA -eq 1 -and $cur.ConsentPromptBehaviorAdmin -eq 5 -and $cur.PromptOnSecureDesktop -eq 1) { 'UAC ya está como viene de fábrica.' }
    'No se cambió nada.'
    exit 0
}
if (-not (IsAdmin)) { 'Ábrelo como administrador (clic derecho en PowerShell > Ejecutar como administrador).'; exit 3 }

try {
    if ($Activar) {
        $copia = Join-Path $env:USERPROFILE ("uac-antes-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
        ($cur | ConvertTo-Json) | Set-Content -Encoding UTF8 $copia
        if (-not (Test-Path $copia)) { throw 'no pude guardar la copia; no se cambió nada' }
        "Copia de los valores actuales: $copia"
        foreach ($n in $names) { Set-ItemProperty $k -Name $n -Value $want[$n] -Type DWord }
        $after = Now
        Show 'Quedó' $after
        foreach ($n in $names) { if ($after[$n] -ne $want[$n]) { throw "$n no quedó en $($want[$n])" } }
        "Hecho. Reinicia cuando te venga bien: hasta entonces todo sigue como antes. Para volver atrás: uac.ps1 -Deshacer `"$copia`""
    } else {
        $old = Get-Content $Deshacer -Raw | ConvertFrom-Json
        foreach ($n in $names) {
            $v = $old.$n
            if ($null -eq $v) { Remove-ItemProperty $k -Name $n -EA SilentlyContinue } else { Set-ItemProperty $k -Name $n -Value ([int]$v) -Type DWord }
        }
        Show 'Quedó' (Now)
        'Hecho. Reinicia para que cuente.'
    }
    exit 0
} catch { "Error: $($_.Exception.Message)"; exit 1 }
