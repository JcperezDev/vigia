$ErrorActionPreference='Continue'
Stop-VM VigiaLab -TurnOff -Force -EA SilentlyContinue
Start-Sleep 3
$vm = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='VigiaLab'"
Start-VM VigiaLab
$t0=Get-Date; $n=0
while(((Get-Date)-$t0).TotalSeconds -lt 25){
  try { $kb = Get-CimAssociatedInstance -InputObject $vm -ResultClassName Msvm_Keyboard; if($kb){ $r=Invoke-CimMethod -InputObject $kb -MethodName TypeKey -Arguments @{keyCode=13}; $n++ } } catch {}
  Start-Sleep -Milliseconds 400
}
"teclas=$n"
Get-VM VigiaLab | select State,Uptime,CPUUsage | ft -HideTableHeaders
