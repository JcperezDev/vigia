# Crea E:\VMs\VigiaLab\unattend.iso con autounattend.xml (instalación sin clics de Windows 11 en la VM de pruebas).
# La contraseña de la cuenta local de la VM se genera aquí y se guarda en %USERPROFILE%\.vigia\vm-cred.txt (fuera del repo).
$ErrorActionPreference = 'Stop'
$dir = 'E:\VMs\VigiaLab'
New-Item -ItemType Directory -Force -Path $dir, "$dir\unattend", "$env:USERPROFILE\.vigia" | Out-Null

$credFile = "$env:USERPROFILE\.vigia\vm-cred.txt"
if (Test-Path $credFile) { $pass = (Get-Content $credFile | Select-String '^pass=').ToString().Substring(5) }
else {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $pass = 'Vl-' + (-join (1..14 | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })) + '!7'
    Set-Content -Path $credFile -Value "user=vigia`npass=$pass" -Encoding ascii
}
$xmlPass = [Security.SecurityElement]::Escape($pass)

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <SetupUILanguage><UILanguage>es-ES</UILanguage></SetupUILanguage>
      <InputLocale>0c0a:0000040a</InputLocale><SystemLocale>es-ES</SystemLocale><UILanguage>es-ES</UILanguage><UserLocale>es-ES</UserLocale>
    </component>
    <component name="Microsoft-Windows-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <DiskConfiguration>
        <Disk wcm:action="add">
          <DiskID>0</DiskID><WillWipeDisk>true</WillWipeDisk>
          <CreatePartitions>
            <CreatePartition wcm:action="add"><Order>1</Order><Type>EFI</Type><Size>300</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>2</Order><Type>MSR</Type><Size>16</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>3</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
          </CreatePartitions>
          <ModifyPartitions>
            <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Format>FAT32</Format><Label>System</Label></ModifyPartition>
            <ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>3</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter></ModifyPartition>
          </ModifyPartitions>
        </Disk>
      </DiskConfiguration>
      <ImageInstall><OSImage>
        <InstallFrom><MetaData wcm:action="add"><Key>/IMAGE/INDEX</Key><Value>1</Value></MetaData></InstallFrom>
        <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>
      </OSImage></ImageInstall>
      <UserData><AcceptEula>true</AcceptEula></UserData>
    </component>
  </settings>
  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <ComputerName>VIGIA-LAB</ComputerName>
    </component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-International-Core" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <InputLocale>0c0a:0000040a</InputLocale><SystemLocale>es-ES</SystemLocale><UILanguage>es-ES</UILanguage><UserLocale>es-ES</UserLocale>
    </component>
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <OOBE>
        <HideEULAPage>true</HideEULAPage><HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
        <HideOnlineAccountScreens>true</HideOnlineAccountScreens><HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <HideLocalAccountScreen>true</HideLocalAccountScreen><ProtectYourPC>3</ProtectYourPC>
      </OOBE>
      <UserAccounts><LocalAccounts>
        <LocalAccount wcm:action="add">
          <Name>vigia</Name><Group>Administradores</Group><DisplayName>vigia</DisplayName>
          <Password><Value>$xmlPass</Value><PlainText>true</PlainText></Password>
        </LocalAccount>
      </LocalAccounts></UserAccounts>
      <AutoLogon>
        <Enabled>true</Enabled><Username>vigia</Username><LogonCount>999</LogonCount>
        <Password><Value>$xmlPass</Value><PlainText>true</PlainText></Password>
      </AutoLogon>
      <FirstLogonCommands>
        <SynchronousCommand wcm:action="add"><Order>1</Order><CommandLine>powercfg /change standby-timeout-ac 0</CommandLine></SynchronousCommand>
        <SynchronousCommand wcm:action="add"><Order>2</Order><CommandLine>powercfg /change monitor-timeout-ac 0</CommandLine></SynchronousCommand>
        <SynchronousCommand wcm:action="add"><Order>3</Order><CommandLine>cmd /c echo listo &gt; C:\vigia-lab-ready.txt</CommandLine></SynchronousCommand>
      </FirstLogonCommands>
    </component>
  </settings>
</unattend>
"@
[IO.File]::WriteAllText("$dir\unattend\autounattend.xml", $xml, [Text.UTF8Encoding]::new($false))
[xml](Get-Content "$dir\unattend\autounattend.xml" -Raw) | Out-Null   # que sea XML válido

# ISO pequeña con IMAPI2FS (viene con Windows)
$fs = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
$fs.FileSystemsToCreate = 3   # ISO9660 + Joliet
$fs.VolumeName = 'UNATTEND'
$fs.Root.AddTree("$dir\unattend", $false)
$res = $fs.CreateResultImage()
$src = $res.ImageStream
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Runtime.InteropServices.ComTypes;
public static class IsoW { public static void Save(object o, string path) {
  var s = (IStream)o; var buf = new byte[65536]; var p = Marshal_.Alloc();
  using (var f = File.Create(path)) { while (true) { s.Read(buf, buf.Length, p); int n = System.Runtime.InteropServices.Marshal.ReadInt32(p); if (n == 0) break; f.Write(buf, 0, n); } }
  System.Runtime.InteropServices.Marshal.FreeHGlobal(p); } }
public static class Marshal_ { public static IntPtr Alloc() { return System.Runtime.InteropServices.Marshal.AllocHGlobal(4); } }
'@
[IsoW]::Save($src, "$dir\unattend.iso")
Get-Item "$dir\unattend.iso" | Select-Object Name, Length
