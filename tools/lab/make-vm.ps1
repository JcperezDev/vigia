$ErrorActionPreference = 'Stop'
$name = 'VigiaLab'; $dir = 'E:\VMs\VigiaLab'
if (Get-VM -Name $name -ErrorAction SilentlyContinue) { throw "La VM $name ya existe" }
New-VM -Name $name -Generation 2 -MemoryStartupBytes 8GB -Path 'E:\VMs' -NewVHDPath "$dir\$name.vhdx" -NewVHDSizeBytes 100GB -SwitchName 'Default Switch' | Out-Null
Set-VM -Name $name -ProcessorCount 4 -StaticMemory -AutomaticCheckpointsEnabled $false -CheckpointType Standard -AutomaticStopAction ShutDown
Set-VMFirmware -VMName $name -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows
Set-VMKeyProtector -VMName $name -NewLocalKeyProtector
Enable-VMTPM -VMName $name
Enable-VMIntegrationService -VMName $name -Name 'Guest Service Interface'
$dvd = Add-VMDvdDrive -VMName $name -Path 'E:\VMs\iso\Win11_26H2_Ent_Eval_es-es.iso' -Passthru
Add-VMDvdDrive -VMName $name -Path "$dir\unattend.iso"
$hdd = Get-VMHardDiskDrive -VMName $name
Set-VMFirmware -VMName $name -BootOrder $dvd, $hdd
Start-VM -Name $name

# «Presiona una tecla para arrancar desde el CD»: mandar Enter por el teclado virtual durante ~25 s
$cs = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='$name'"
$kb = Get-CimAssociatedInstance -InputObject $cs -ResultClassName Msvm_Keyboard
for ($i = 0; $i -lt 25; $i++) { Invoke-CimMethod -InputObject $kb -MethodName TypeKey -Arguments @{ keyCode = 13 } | Out-Null; Start-Sleep -Milliseconds 1000 }
Get-VM -Name $name | Select-Object Name, State, CPUUsage, MemoryAssigned, Uptime | Format-List
Get-VMFirmware -VMName $name | Select-Object SecureBoot, SecureBootTemplate | Format-List
Get-VMSecurity -VMName $name | Select-Object TpmEnabled | Format-List
