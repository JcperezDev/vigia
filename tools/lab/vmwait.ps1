$c = Get-Content "$env:USERPROFILE\.vigia\vm-cred.txt"
$u = ($c | ? {$_ -like 'user=*'}).Substring(5); $p = ($c | ? {$_ -like 'pass=*'}).Substring(5)
$cred = New-Object PSCredential ".\$u", (ConvertTo-SecureString $p -AsPlainText -Force)
$t0 = Get-Date
while (((Get-Date)-$t0).TotalMinutes -lt 60) {
  try { $r = Invoke-Command -VMName VigiaLab -Credential $cred -ScriptBlock { Test-Path C:\vigia-lab-ready.txt; (Get-CimInstance Win32_OperatingSystem).Caption; [Environment]::OSVersion.Version.ToString() } -EA Stop
        if ($r[0]) { "LISTA tras $([int]((Get-Date)-$t0).TotalMinutes) min: $($r[1]) $($r[2])"; exit 0 } } catch {}
  Start-Sleep 30
}
"timeout"; exit 1
