# Tools (Herramientas)

The **Tools** view lists two kinds of entries. It never scans a folder for
`.exe`, `.msi`, `.lnk` or `.url` files and never runs a program it found on disk
by itself. The old per-machine tools folder (`ToolsRoot`) was removed.

## 1. Built into Windows

Task Manager, Resource Monitor, Device Manager, Services, Event Viewer, System
Information, DirectX Diagnostic, System Configuration, Registry Editor,
Programs and Features, Performance Options, System Protection and the
`ms-settings:` pages (power, graphics, Game Mode, startup apps, installed apps,
mouse, sound, network, Windows Update, Windows Security, temp folder).

Repairs (`sfc /scannow`, `DISM /RestoreHealth`, `chkdsk C: /scan`,
`ipconfig /flushdns`, Winsock/IP reset, `wsreset`, Memory Diagnostic, restore
point) show what they do and ask before running. `chkdsk` is read-only
(`/scan`); nothing here repairs or locks a disk, and nothing restarts the PC.

## 2. Well-known third-party programs (optional)

Each entry shows publisher, origin and whether it is installed. If it is not
installed, **Install** opens a visible console that runs
`winget install -e --id <ID> --source winget` after the user confirms. Nothing is
downloaded from arbitrary links; WinGet checks the installer hash from the
official `winget-pkgs` manifest. If WinGet is missing, Vigía says so and does
nothing.

IDs checked with `winget show --id <ID> -e --source winget` on 2026-10-29:

| Program | WinGet ID | Version seen | Publisher (manifest) | License | Homepage |
|---|---|---|---|---|---|
| CPU-Z | `CPUID.CPU-Z` | 3.01 | CPUID, Inc. | Freeware | https://www.cpuid.com/softwares/cpu-z.html |
| GPU-Z | `TechPowerUp.GPU-Z` | 2.71.0 | TechPowerUp | Freeware | https://www.techpowerup.com/gpuz/ |
| HWiNFO | `REALiX.HWiNFO` | 8.54 | Martin Malik, REALiX s.r.o. | Proprietary | https://www.hwinfo.com/download/ |
| Process Explorer | `Microsoft.Sysinternals.ProcessExplorer` | 17.2 | Sysinternals | Proprietary | https://learn.microsoft.com/en-us/sysinternals/downloads/process-explorer |
| Autoruns | `Microsoft.Sysinternals.Autoruns` | 14.3 | Sysinternals | Proprietary | https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns |
| MSI Afterburner | `Guru3D.Afterburner` | 4.6.6 | MSI Co., LTD | Freeware | (installer from a Guru3D mirror, hash-pinned by the manifest) |
| FanControl | `Rem0o.FanControl` | 284 | Remi Mercier Software Inc | Freeware | https://getfancontrol.com/ |
| Display Driver Uninstaller | `Wagnardsoft.DisplayDriverUninstaller` | 18.1.5.3 | Wagnardsoft | MIT | https://www.wagnardsoft.com/display-driver-uninstaller-ddu |
| NVIDIA Profile Inspector | `Orbmu2k.nvidiaProfileInspector` | 3.0.2.3 | Orbmu2k | MIT | https://github.com/Orbmu2k/nvidiaProfileInspector |

Versions change over time; only the ID is stored in the code.

## Privileges

Vigía runs as administrator. Third-party programs are started with the token of
the desktop shell (Explorer) via `CreateProcessWithTokenW`, so they do not
inherit Vigía's elevation. If the shell cannot be found, the program is **not**
started elevated as a fallback; Vigía shows an error instead. WinGet installs
start the same way; if an installer needs admin rights, Windows asks.

**Not yet validated on a PC with UAC enabled.** On the development PC UAC is
off, so every process is elevated anyway. To validate: open a program from
Tools, then in Task Manager → Details add the *Elevated* column; it must say
*No*.
