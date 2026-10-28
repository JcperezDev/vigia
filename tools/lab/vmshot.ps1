param([string]$out)
Add-Type -AssemblyName System.Drawing
$svc = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_VirtualSystemManagementService
$vm = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='VigiaLab'"
$sd = Get-CimAssociatedInstance -InputObject $vm -ResultClassName Msvm_VirtualSystemSettingData | ? { $_.VirtualSystemType -eq 'Microsoft:Hyper-V:System:Realized' }
$w=640;$h=480
$r = Invoke-CimMethod -InputObject $svc -MethodName GetVirtualSystemThumbnailImage -Arguments @{TargetSystem=$sd; WidthPixels=[uint16]$w; HeightPixels=[uint16]$h}
[byte[]]$bytes = $r.ImageData; if(-not $bytes){ "sin imagen rv=$($r.ReturnValue)"; exit 1 }; $w=640;$h=480; if($bytes.Length -lt $w*$h*2){ "len=$($bytes.Length)"; exit 1 }
$bmp = New-Object System.Drawing.Bitmap $w,$h,([System.Drawing.Imaging.PixelFormat]::Format16bppRgb565)
$rect = New-Object System.Drawing.Rectangle 0,0,$w,$h
$d = $bmp.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::WriteOnly,$bmp.PixelFormat)
for($y=0;$y -lt $h;$y++){ [System.Runtime.InteropServices.Marshal]::Copy($bytes,$y*$w*2,[IntPtr]($d.Scan0.ToInt64()+$y*$d.Stride),$w*2) }
$bmp.UnlockBits($d); $bmp.Save($out,[System.Drawing.Imaging.ImageFormat]::Png); "ok $($bytes.Length)"
