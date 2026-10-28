# Prueba de laboratorio en VigiaLab: copia Vigía, checkpoint, aplica todo, reinicia, revierte, restaura original, compara.
param([string]$Phase = 'all')
$ErrorActionPreference = 'Stop'
$here = if ($env:VIGIA_LAB_DIR) { $env:VIGIA_LAB_DIR } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$out = Join-Path $here 'lab'; New-Item -ItemType Directory -Force $out | Out-Null
$c = Get-Content "$env:USERPROFILE\.vigia\vm-cred.txt"
$u = ($c | ? { $_ -like 'user=*' }).Substring(5); $p = ($c | ? { $_ -like 'pass=*' }).Substring(5)
$cred = New-Object PSCredential ".\$u", (ConvertTo-SecureString $p -AsPlainText -Force)
function S { New-PSSession -VMName VigiaLab -Credential $cred }
function WaitUp { $t0 = Get-Date; while (((Get-Date) - $t0).TotalMinutes -lt 10) { try { $s = S; return $s } catch { Start-Sleep 10 } }; throw 'la VM no volvió' }
function Run($s, [string]$a, [string]$name) {
  Invoke-Command -Session $s -ScriptBlock { param($a)
    $env:VIGIA_LAB = '1'; $env:VIGIA_DATA = 'C:\vlab\data'
    $p = Start-Process C:\vlab\app\Vigia.exe -ArgumentList $a -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput C:\vlab\stdout.txt; "$($p.ExitCode) $(Get-Content C:\vlab\stdout.txt -Raw)" } -ArgumentList $a
  if ($name -ne 'none') { Copy-Item -FromSession $s "C:\vlab\$name" (Join-Path $out $name) -Force }
}
$log = Join-Path $out 'log.txt'
function L($m) { $l = "$(Get-Date -f HH:mm:ss) $m"; $l; Add-Content $log $l }

if ($Phase -in 'all', 'prep') {
  Remove-Item $log -EA SilentlyContinue
  if (-not (Get-VMSnapshot VigiaLab -Name 'limpia' -EA SilentlyContinue)) { Checkpoint-VM VigiaLab -SnapshotName 'limpia'; L 'checkpoint limpia' }
  else { Restore-VMSnapshot -VMName VigiaLab -Name 'limpia' -Confirm:$false; Start-VM VigiaLab; L 'vuelta a checkpoint limpia'; Start-Sleep 20 }
  Remove-Item (Join-Path $out '*.json') -EA SilentlyContinue
  $s = WaitUp
  Invoke-Command -Session $s { New-Item -ItemType Directory -Force C:\vlab\app, C:\vlab\data | Out-Null }
  Copy-Item -ToSession $s (Join-Path $here 'vigia-lab\*') C:\vlab\app -Recurse -Force
  L 'Vigía copiada'
  $e = Run $s '--original C:\vlab\original.json --rp' 'original.json'; L "original exit=$e"
  $e = Run $s '--lab list all C:\vlab\before.json' 'before.json'; L "list exit=$e"
  $e = Run $s '--lab apply all C:\vlab\apply.json' 'apply.json'; L "apply exit=$e"
  $e = Run $s '--lab debloat all C:\vlab\debloat.json' 'debloat.json'; L "debloat exit=$e"
  Invoke-Command -Session $s { Restart-Computer -Force }; Remove-PSSession $s -EA SilentlyContinue
  Start-Sleep 25; L 'reinicio 1 (limpio, desde Windows)'
}
if ($Phase -in 'all', 'check') {
  $s = WaitUp
  $e = Run $s '--lab list all C:\vlab\after-reboot.json' 'after-reboot.json'; L "tras reinicio exit=$e"
  Invoke-Command -Session $s { Get-WinEvent -FilterHashtable @{LogName = 'System'; Level = 1, 2; StartTime = (Get-Date).AddMinutes(-15) } -EA SilentlyContinue | select -First 15 TimeCreated, ProviderName, Id, Message | ConvertTo-Json } | Set-Content (Join-Path $out 'errors-after.json')
  Invoke-Command -Session $s { @{ net = (Test-NetConnection 1.1.1.1 -InformationLevel Quiet); explorer = [bool](Get-Process explorer -EA SilentlyContinue); start = [bool](Get-Process StartMenuExperienceHost -EA SilentlyContinue) } | ConvertTo-Json } | Set-Content (Join-Path $out 'health-after.json')
  $e = Run $s '--lab revert all C:\vlab\revert.json' 'revert.json'; L "revert exit=$e"
  $e = Run $s '--backup-restore original' 'none'; L "restore original exit=$e"
  $e = Run $s '--lab list all C:\vlab\final.json' 'final.json'; L "final exit=$e"
  Remove-PSSession $s
}
L 'fin'
