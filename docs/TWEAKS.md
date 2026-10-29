# Vigía tweak catalog

Generated from `app/Vigia/tweaks.json` by `node tools/tweaks-doc.js`. Do not edit by hand.

44 entries: 9 experimental, 7 deprecated, 26 optional, 2 recommended; 26 with an official source.

**Statuses.** `recommended`: documented and safe for most PCs. `optional`: documented, depends on the user. `experimental`: effect depends on hardware or game; measure before and after; never in Simple mode or "Apply all". `deprecated`: retired; only its undo remains, so earlier users can get their original values back.

**Validation.** "Tested" means the apply/undo cycle was run on a real PC by the maintainer. The engine itself (backup, apply, read back, undo, rollback of a half-applied tweak) is covered by `Vigia.exe --selftest` on a test registry key. No entry claims an FPS or latency gain.

**How undo works.** Before the first apply, Vigía saves the exact previous value of every operation and makes a restore point. "Revert" restores those values. If an operation fails halfway, the operations already done in that attempt are undone right away.

| Id | Tweak | Status | Reboot | Tested | Source |
|---|---|---|---|---|---|
| `power-ultimate` | Vigía power plan | experimental | no | yes | — |
| `mmcss-games` | High priority for games | experimental | yes | no | [link](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service) |
| `responsiveness` | No CPU reserved for background tasks | deprecated | yes | no | [link](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service) |
| `priority-separation` | More CPU time for the active window | experimental | no | no | [link](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities) |
| `svchost-split` | Group Windows services | deprecated | yes | no | [link](https://learn.microsoft.com/en-us/windows/application-management/svchost-service-refactoring) |
| `hibernate-off` | Turn off hibernation | optional | no | no | [link](https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/disable-and-re-enable-hibernation) |
| `game-dvr-off` | Turn off Xbox background recording | optional | no | no | — |
| `background-apps` | Store apps with no background work | optional | no | no | — |
| `transparency-off` | Remove Windows transparency | optional | no | no | — |
| `mouse-raw` | Mouse without acceleration | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/win32/dxtecharts/taking-advantage-of-high-dpi-mouse-movement) |
| `accessibility-keys` | No accessibility shortcuts mid-match | optional | no | no | — |
| `input-queue` | Short mouse and keyboard queue | deprecated | yes | no | — |
| `shutdown-fast` | Close frozen programs without waiting | deprecated | no | yes | — |
| `end-task` | “End task” on the taskbar | optional | no | no | — |
| `suggestions-off` | No ads or suggested apps | optional | no | no | — |
| `telemetry-off` | Windows telemetry at minimum | optional | yes | no | [link](https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization) |
| `telemetry-tasks` | Data-collection scheduled tasks | optional | no | no | — |
| `activity-off` | No activity history | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy) |
| `location-off` | Turn off Windows location | experimental | no | no | [link](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy) |
| `services-unused` | Services almost nobody uses | deprecated | yes | no | — |
| `print-off` | Turn off printing | optional | yes | no | [link](https://msrc.microsoft.com/update-guide/vulnerability/CVE-2021-34527) |
| `services-xbox` | Xbox services | deprecated | yes | yes | — |
| `search-off` | Search indexer | deprecated | yes | yes | — |
| `update-control` | Windows Update doesn't restart while you're signed in | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings) |
| `wu-drivers-off` | Windows Update without drivers | experimental | no | no | [link](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings) |
| `gpu-high` | Games on the powerful graphics card | experimental | no | no | — |
| `windowed-opt` | Optimizations for windowed games | optional | no | no | — |
| `hags` | Hardware-accelerated GPU scheduling | optional | yes | no | [link](https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/) |
| `game-mode` | Windows Game Mode | recommended | no | no | — |
| `animations-off` | Fewer Windows animations | optional | yes | no | — |
| `startup-delay` | Startup programs without delay | experimental | yes | no | — |
| `boot-info` | See what Windows does at startup and shutdown | optional | no | no | [link](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/enable-verbose-startup-shutdown-logon-logoff-status-messages) |
| `smb1-off` | Turn off the SMB1 server (old file sharing) | recommended | yes | no | [link](https://learn.microsoft.com/en-us/windows-server/storage/file-server/troubleshoot/detect-enable-and-disable-smbv1-v2-v3) |
| `browsers-bg` | Chrome and Edge actually close | optional | no | no | [link](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies) |
| `ad-id-off` | No advertising ID | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy) |
| `firefox-telemetry` | Firefox without sending usage data | optional | no | no | [link](https://mozilla.github.io/policy-templates/) |
| `wer-off` | Don't send error reports to Microsoft | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/win32/wer/wer-settings) |
| `p2p-updates-off` | Updates without sharing your internet | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference) |
| `usb-suspend-off` | USB without suspend | experimental | no | no | [link](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-selective-suspend) |
| `pcie-aspm-off` | Graphics card and drive without link power saving | experimental | no | no | [link](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/pci-express-settings-link-state-power-management) |
| `vrr-windowed` | G-Sync/FreeSync in windowed games | optional | no | no | [link](https://devblogs.microsoft.com/directx/os-variable-refresh-rate/) |
| `widgets-off` | No Widgets | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests) |
| `start-web-off` | Start searches only your PC | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search) |
| `recall-off` | No Recall snapshots | optional | no | no | [link](https://learn.microsoft.com/en-us/windows/client-management/manage-recall) |

## Details

### `power-ultimate` — Vigía power plan

- **Status:** experimental
- **Group:** Performance
- **What it does:** Creates the «Vigía» plan: plugged in, the processor never drops below 100 % even when idle. More power draw and heat at rest; the Balanced plan already ramps up instantly on most PCs. Try it by measuring before and after. Your previous plan comes back with «Undo».
- **Changes:**
  - special: `power-ultimate`
- **Undo:** switches back to the previous plan and deletes the Vigía plan if Vigía created it
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `mmcss-games` — High priority for games

- **Status:** experimental
- **Group:** Performance
- **What it does:** Raises the priority of the «Games» task in the Windows multimedia scheduler. It only affects games that register with that service (few do); for the rest nothing changes. Microsoft states that «GPU Priority» is not used yet.
- **Changes:**
  - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\GPU Priority` = `8` (dword)
  - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\Priority` = `6` (dword)
  - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\Scheduling Category` = `High` (string)
  - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\SFIO Priority` = `High` (string)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** yes
- **Evidence:** <https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service>

### `responsiveness` — No CPU reserved for background tasks

- **Status:** deprecated (retired: not offered; undo only)
- **Group:** Performance
- **What it does:** Retired: according to Microsoft, SystemResponsiveness below 10 is treated as 20 (the default), so setting it to 0 changes nothing. If Vigía applied it before, it is undone automatically.
- **Changes:**
  - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\NetworkThrottlingIndex` = `4294967295` (dword)
  - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\SystemResponsiveness` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** yes
- **Evidence:** <https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service>

### `priority-separation` — More CPU time for the active window

- **Status:** experimental
- **Group:** Performance
- **What it does:** Changes how Windows shares processor time between the active window and the rest. There are no public measurements showing a gain in games; compare by measuring FPS before and after.
- **Changes:**
  - `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation` = `22` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities>

### `svchost-split` — Group Windows services

- **Status:** deprecated (retired: not offered; undo only)
- **Group:** Memory
- **What it does:** Retired: it groups services into fewer processes so RAM looks lower, but Microsoft splits them on purpose so one failure does not take down the rest. If Vigía applied it before, it is undone automatically.
- **Changes:**
  - `HKLM\SYSTEM\CurrentControlSet\Control\SvcHostSplitThresholdInKB` = `$ramKB` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** yes
- **Evidence:** <https://learn.microsoft.com/en-us/windows/application-management/svchost-service-refactoring>

### `hibernate-off` — Turn off hibernation

- **Status:** optional
- **Group:** Storage
- **What it does:** Deletes the hibernation file (hiberfil.sys) and frees that space on C:. In exchange you cannot hibernate and Windows Fast Startup is turned off, so a cold boot may take a little longer.
- **Changes:**
  - special: `hibernate-off`
- **Undo:** turns hibernation back on if it was on
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/disable-and-re-enable-hibernation>

### `game-dvr-off` — Turn off Xbox background recording

- **Status:** optional
- **Group:** Games
- **What it does:** Game DVR records the last few seconds of gameplay all the time. With it off, the Game Bar still works.
- **Changes:**
  - `HKCU\System\GameConfigStore\GameDVR_Enabled` = `0` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled` = `0` (dword)
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `background-apps` — Store apps with no background work

- **Status:** optional
- **Group:** Games
- **What it does:** Microsoft Store apps stop running when they aren't open.
- **Changes:**
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\GlobalUserDisabled` = `1` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Search\BackgroundAppGlobalToggle` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `transparency-off` — Remove Windows transparency

- **Status:** optional
- **Group:** Games
- **What it does:** The taskbar and menus stop blurring the background. Less work for the GPU on the desktop.
- **Changes:**
  - `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `mouse-raw` — Mouse without acceleration

- **Status:** optional
- **Group:** Mouse and keyboard
- **What it does:** Turns off «Enhance pointer precision» (Windows mouse acceleration): the same hand movement always moves the same distance. Games that read the mouse with Raw Input already ignore it; this changes the desktop and games that do not use it.
- **Changes:**
  - `HKCU\Control Panel\Mouse\MouseSpeed` = `0` (string)
  - `HKCU\Control Panel\Mouse\MouseThreshold1` = `0` (string)
  - `HKCU\Control Panel\Mouse\MouseThreshold2` = `0` (string)
  - `HKCU\Control Panel\Mouse\SmoothMouseXCurve` = `0000000000000000C0CC0C0000000000809919000000000040662600000000000033330000000000` (binary)
  - `HKCU\Control Panel\Mouse\SmoothMouseYCurve` = `0000000000000000000038000000000000007000000000000000A800000000000000E00000000000` (binary)
  - special: `mouse-refresh`
- **Undo:** the exact previous value (or its absence) is restored; nothing to undo (only tells Windows to reload mouse settings)
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/win32/dxtecharts/taking-advantage-of-high-dpi-mouse-movement>

### `accessibility-keys` — No accessibility shortcuts mid-match

- **Status:** optional
- **Group:** Mouse and keyboard
- **What it does:** Pressing Shift five times no longer opens “Sticky Keys”. Same for Filter Keys, Toggle Keys and Mouse Keys.
- **Changes:**
  - `HKCU\Control Panel\Accessibility\StickyKeys\Flags` = `506` (string)
  - `HKCU\Control Panel\Accessibility\Keyboard Response\Flags` = `122` (string)
  - `HKCU\Control Panel\Accessibility\ToggleKeys\Flags` = `58` (string)
  - `HKCU\Control Panel\Accessibility\MouseKeys\Flags` = `0` (string)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `input-queue` — Short mouse and keyboard queue

- **Status:** deprecated (retired: not offered; undo only)
- **Group:** Mouse and keyboard
- **What it does:** Lowers the input buffer from 100 to 20 events. With 4000-8000 Hz mice it can drop movements: test it first.
- **Changes:**
  - `HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters\MouseDataQueueSize` = `20` (dword)
  - `HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters\KeyboardDataQueueSize` = `20` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** yes
- **Evidence:** no official source yet; treat as unverified

### `shutdown-fast` — Close frozen programs without waiting

- **Status:** deprecated (retired: not offered; undo only)
- **Group:** Windows
- **What it does:** Menus with no delay and faster shutdown. When shutting down, programs that don't respond get closed: save first.
- **Changes:**
  - `HKCU\Control Panel\Desktop\MenuShowDelay` = `0` (string)
  - `HKCU\Control Panel\Desktop\WaitToKillAppTimeout` = `5000` (string)
  - `HKCU\Control Panel\Desktop\HungAppTimeout` = `4000` (string)
  - `HKCU\Control Panel\Desktop\AutoEndTasks` = `1` (string)
  - `HKLM\SYSTEM\CurrentControlSet\Control\WaitToKillServiceTimeout` = `2000` (string)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `end-task` — “End task” on the taskbar

- **Status:** optional
- **Group:** Windows
- **What it does:** Right-click a program on the taskbar to close it even if it's frozen.
- **Changes:**
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings\TaskbarEndTask` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `suggestions-off` — No ads or suggested apps

- **Status:** optional
- **Group:** Windows
- **What it does:** Windows stops installing promoted apps and showing suggestions in Start and Settings.
- **Changes:**
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\ContentDeliveryAllowed` = `0` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\OemPreInstalledAppsEnabled` = `0` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\PreInstalledAppsEnabled` = `0` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SilentInstalledAppsEnabled` = `0` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-310093Enabled` = `0` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-314559Enabled` = `0` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-338387Enabled` = `0` (dword)
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent\DisableWindowsConsumerFeatures` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `telemetry-off` — Windows telemetry at minimum

- **Status:** optional
- **Group:** Privacy
- **What it does:** Privacy, not performance: asks Windows to send the minimum diagnostic data and turns off the service that sends it. On Home and Pro, Windows still sends the required data; only Enterprise and Education reach zero.
- **Changes:**
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry` = `0` (dword)
  - service `DiagTrack` start = 4
  - service `dmwappushservice` start = 4
- **Undo:** the exact previous value (or its absence) is restored; the previous start type is restored
- **Reboot needed:** yes
- **Evidence:** <https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization>

### `telemetry-tasks` — Data-collection scheduled tasks

- **Status:** optional
- **Group:** Privacy
- **What it does:** Turns off six tasks that scan compatibility, USB and disk usage to send it to Microsoft.
- **Changes:**
  - scheduled task `\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser` enabled = false
  - scheduled task `\Microsoft\Windows\Application Experience\ProgramDataUpdater` enabled = false
  - scheduled task `\Microsoft\Windows\Customer Experience Improvement Program\Consolidator` enabled = false
  - scheduled task `\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip` enabled = false
  - scheduled task `\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector` enabled = false
  - scheduled task `\Microsoft\Windows\Windows Error Reporting\QueueReporting` enabled = false
- **Undo:** the task goes back to its previous enabled state
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `activity-off` — No activity history

- **Status:** optional
- **Group:** Privacy
- **What it does:** Windows stops saving and uploading the list of what you open (activity history). It doesn't touch location.
- **Changes:**
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableActivityFeed` = `0` (dword)
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\PublishUserActivities` = `0` (dword)
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\UploadUserActivities` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy>

### `location-off` — Turn off Windows location

- **Status:** experimental
- **Group:** Privacy
- **What it does:** No app, and not Windows, can use your location. “Find my device”, automatic time zone and apps that use it (maps, weather) stop working. Turn it back on in Settings > Privacy > Location or with “Undo”.
- **Changes:**
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location\Value` = `Deny` (string)
  - service `lfsvc` start = 4
- **Undo:** the exact previous value (or its absence) is restored; the previous start type is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy>

### `services-unused` — Services almost nobody uses

- **Status:** deprecated (retired: not offered; undo only)
- **Group:** Services
- **What it does:** Retired: it turned off ten services at once, including the error-reporting and compatibility ones used for diagnosis. Services set to «Manual» cost nothing while unused. If Vigía applied it before, it is undone automatically.
- **Changes:**
  - service `Fax` start = 4
  - service `RemoteRegistry` start = 4
  - service `MapsBroker` start = 4
  - service `GraphicsPerfSvc` start = 4
  - service `WerSvc` start = 4
  - service `PcaSvc` start = 4
  - service `Wecsvc` start = 4
  - service `WMPNetworkSvc` start = 4
  - service `NetTcpPortSharing` start = 4
  - service `TrkWks` start = 4
- **Undo:** the previous start type is restored
- **Reboot needed:** yes
- **Evidence:** no official source yet; treat as unverified

### `print-off` — Turn off printing

- **Status:** optional
- **Group:** Services
- **What it does:** Stops the print queue if you don't have a printer. “Print to PDF” stops working too. Shuts the door on bugs like PrintNightmare.
- **Changes:**
  - service `Spooler` start = 4
- **Undo:** the previous start type is restored
- **Reboot needed:** yes
- **Evidence:** <https://msrc.microsoft.com/update-guide/vulnerability/CVE-2021-34527>

### `services-xbox` — Xbox services

- **Status:** deprecated (retired: not offered; undo only)
- **Group:** Services
- **What it does:** Fortnite and Steam don't use them. Breaks Xbox Game Pass, Xbox achievements and Minecraft from the Store.
- **Changes:**
  - service `XblAuthManager` start = 4
  - service `XblGameSave` start = 4
  - service `XboxNetApiSvc` start = 4
- **Undo:** the previous start type is restored
- **Reboot needed:** yes
- **Evidence:** no official source yet; treat as unverified

### `search-off` — Search indexer

- **Status:** deprecated (retired: not offered; undo only)
- **Group:** Services
- **What it does:** Fewer background disk reads. The trade-off: searching in Start and File Explorer is much slower.
- **Changes:**
  - service `WSearch` start = 4
- **Undo:** the previous start type is restored
- **Reboot needed:** yes
- **Evidence:** no official source yet; treat as unverified

### `update-control` — Windows Update doesn't restart while you're signed in

- **Status:** optional
- **Group:** Updates
- **What it does:** Windows waits until you sign out to restart after an update. Updates (driver ones included) keep arriving as usual.
- **Changes:**
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings>

### `wu-drivers-off` — Windows Update without drivers

- **Status:** experimental
- **Group:** Updates
- **What it does:** Windows Update stops installing ANY driver (graphics, sound, network, chipset, Bluetooth…), not just the graphics one. You'll have to update them yourself from the maker's website. Only useful if Windows replaces a driver that was working; by default, better not.
- **Changes:**
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\ExcludeWUDriversInQualityUpdate` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings>

### `gpu-high` — Games on the powerful graphics card

- **Status:** experimental
- **Group:** Games
- **What it does:** Asks Windows for the high-performance graphics card for every program. It only helps on machines with two cards (integrated and dedicated, typical on laptops); with a single one it doesn't apply. What you pick per game in Settings > System > Display > Graphics takes priority.
- **Changes:**
  - `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` → `DirectXUserGlobalSettings`: key `GpuPreference=2` inside the string
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `windowed-opt` — Optimizations for windowed games

- **Status:** optional
- **Group:** Games
- **What it does:** Games in borderless windowed mode get the same latency as fullscreen. It’s the Windows 11 option in Display > Graphics.
- **Changes:**
  - `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` → `DirectXUserGlobalSettings`: key `SwapEffectUpgradeEnable=1` inside the string
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `hags` — Hardware-accelerated GPU scheduling

- **Status:** optional
- **Group:** Games
- **What it does:** The graphics card manages its own video memory instead of Windows. It's required for DLSS frame generation; beyond that, the effect depends on the card and driver: sometimes nothing changes and with some drivers it stutters. Measure with Frametimes before and after. If the card or driver doesn't support it, Windows ignores it.
- **Changes:**
  - `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode` = `2` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** yes
- **Evidence:** <https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/>

### `game-mode` — Windows Game Mode

- **Status:** recommended
- **Group:** Games
- **What it does:** While you play, Windows doesn't install updates or restart, and gives the game priority.
- **Changes:**
  - `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` = `1` (dword)
  - `HKCU\Software\Microsoft\GameBar\AllowAutoGameMode` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** no official source yet; treat as unverified

### `animations-off` — Fewer Windows animations

- **Status:** optional
- **Group:** Windows
- **What it does:** Removes animations when opening, minimizing and on the taskbar. Text stays smooth. You'll notice it after signing in again.
- **Changes:**
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\VisualFXSetting` = `3` (dword)
  - `HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate` = `0` (string)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarAnimations` = `0` (dword)
  - `HKCU\Control Panel\Desktop\FontSmoothing` = `2` (string)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** yes
- **Evidence:** no official source yet; treat as unverified

### `startup-delay` — Startup programs without delay

- **Status:** experimental
- **Group:** Windows
- **What it does:** Removes the wait Windows applies before opening startup programs. These values are not documented by Microsoft: the desktop may take longer to respond if many programs start at once.
- **Changes:**
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize\StartupDelayInMSec` = `0` (dword)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize\WaitForIdleState` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** yes
- **Evidence:** no official source yet; treat as unverified

### `boot-info` — See what Windows does at startup and shutdown

- **Status:** optional
- **Group:** Windows
- **What it does:** Instead of “Please wait”, Windows tells you what it's loading. Handy to see where it gets stuck.
- **Changes:**
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\VerboseStatus` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/enable-verbose-startup-shutdown-logon-logoff-status-messages>

### `smb1-off` — Turn off the SMB1 server (old file sharing)

- **Status:** recommended
- **Group:** Windows
- **What it does:** A '90s protocol WannaCry used. This tweak turns off the SMB1 server; the client is part of Windows' “SMB 1.0/CIFS File Sharing Support” component, and Vigía tells you if it's still installed. Only very old NAS boxes or printers need it.
- **Changes:**
  - `HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\SMB1` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** yes
- **Evidence:** <https://learn.microsoft.com/en-us/windows-server/storage/file-server/troubleshoot/detect-enable-and-disable-smbv1-v2-v3>

### `browsers-bg` — Chrome and Edge actually close

- **Status:** optional
- **Group:** Performance
- **What it does:** When you close the window they don't stay in memory or start with Windows. Chrome will show “Managed by your organization”: that's normal.
- **Changes:**
  - `HKLM\SOFTWARE\Policies\Google\Chrome\BackgroundModeEnabled` = `0` (dword)
  - `HKLM\SOFTWARE\Policies\Microsoft\Edge\BackgroundModeEnabled` = `0` (dword)
  - `HKLM\SOFTWARE\Policies\Microsoft\Edge\StartupBoostEnabled` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies>

### `ad-id-off` — No advertising ID

- **Status:** optional
- **Group:** Privacy
- **What it does:** Apps stop sharing an ID of yours to show you ads.
- **Changes:**
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled` = `0` (dword)
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo\DisabledByGroupPolicy` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy>

### `firefox-telemetry` — Firefox without sending usage data

- **Status:** optional
- **Group:** Privacy
- **What it does:** Turns off telemetry and the agent that checks your default browser.
- **Changes:**
  - `HKLM\SOFTWARE\Policies\Mozilla\Firefox\DisableTelemetry` = `1` (dword)
  - `HKLM\SOFTWARE\Policies\Mozilla\Firefox\DisableDefaultBrowserAgent` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://mozilla.github.io/policy-templates/>

### `wer-off` — Don't send error reports to Microsoft

- **Status:** optional
- **Group:** Privacy
- **What it does:** When a program closes on its own, Windows does not prepare or upload the report. Vigía’s Diagnostics still sees the crashes. Note: without reports, Vigía and Windows have less data to explain why a program closed.
- **Changes:**
  - `HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\Disabled` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/win32/wer/wer-settings>

### `p2p-updates-off` — Updates without sharing your internet

- **Status:** optional
- **Group:** Updates
- **What it does:** Windows downloads updates only from Microsoft and doesn't use your connection to send them to other PCs.
- **Changes:**
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference>

### `usb-suspend-off` — USB without suspend

- **Status:** experimental
- **Group:** Mouse and keyboard
- **What it does:** Windows stops putting unused USB ports to sleep. It only helps if your mouse, keyboard or headset disconnects for a second or responds late; if that does not happen to you, nothing changes and it uses a little more power. Plugged in only.
- **Changes:**
  - power setting `2a737441-1930-4402-8d77-b2bebba308a3/48e6b7a6-50f5-4782-a5d4-53bb8f07e226` (AC) = 0
- **Undo:** the previous value is set again on the plan that was active
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-selective-suspend>

### `pcie-aspm-off` — Graphics card and drive without link power saving

- **Status:** experimental
- **Group:** Performance
- **What it does:** Windows stops putting the PCI Express link (graphics card, NVMe) into power saving between uses. It only helps if a PCIe device fails or stutters when waking; otherwise it uses more power at idle for no gain. Plugged in only.
- **Changes:**
  - power setting `501a4d13-42af-4429-9fd1-a8218c268e20/ee12f906-d277-404b-b6da-e5fa1a576df5` (AC) = 0
- **Undo:** the previous value is set again on the plan that was active
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/pci-express-settings-link-state-power-management>

### `vrr-windowed` — G-Sync/FreeSync in windowed games

- **Status:** optional
- **Group:** Games
- **What it does:** Your monitor's variable refresh rate also works in borderless games that don't ask for it. It's the Windows option in Display > Graphics.
- **Changes:**
  - `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` → `DirectXUserGlobalSettings`: key `VRROptimizeEnable=1` inside the string
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://devblogs.microsoft.com/directx/os-variable-refresh-rate/>

### `widgets-off` — No Widgets

- **Status:** optional
- **Group:** Performance
- **What it does:** Removes the news and weather panel from the taskbar and the processes it leaves running in the background.
- **Changes:**
  - `HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests` = `0` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests>

### `start-web-off` — Start searches only your PC

- **Status:** optional
- **Group:** Privacy
- **What it does:** What you type in the Start menu isn't sent to Bing and results show up faster.
- **Changes:**
  - `HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search>

### `recall-off` — No Recall snapshots

- **Status:** optional
- **Group:** Privacy
- **What it does:** Windows doesn't save snapshots of what you do on screen. Only matters on PCs that come with Recall.
- **Changes:**
  - `HKCU\Software\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis` = `1` (dword)
- **Undo:** the exact previous value (or its absence) is restored
- **Reboot needed:** no
- **Evidence:** <https://learn.microsoft.com/en-us/windows/client-management/manage-recall>
