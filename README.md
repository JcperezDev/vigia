# Vigía

A monitor and optimizer for Windows 10/11 PCs, built for gaming. Free, no ads, no accounts, and the source code is public.

**Download:** https://vigiapc.vercel.app · [Latest release](https://github.com/JcperezDev/vigia/releases/latest)

The app is available in English, Spanish, Portuguese, French and German. [Leer en español](README.es.md).

## What it does

- **Monitor:** CPU, GPU, memory and disks, using the same counters as Task Manager, plus the CPU temperature when there is a compatible source and a valid reading (not every processor is supported), and which program is using more than it should.
- **Simple mode:** answer a few questions (printer, network, Windows apps…) and Vigía does the rest.
- **Tweaks, Games and Memory:** Windows tweaks explained one by one, real FPS before and after, a Fortnite profile, stretched resolution with an FPS estimate, and automatic memory release.
- **Windows apps, Windows startup, Storage:** remove what you don't need, turn off what starts on its own, and clean temporary files and the Recycle Bin.
- **Diagnostics, Disk health, BIOS:** checks your PC and explains what's worth changing. The BIOS is read only; Vigía never writes to it.
- **Windows ISO:** builds a clean ISO from the official one (no ads, local account, no TPM or Secure Boot check, no data collection, no automatic BitLocker) and writes it to a USB drive.

## Safety

- **A backup before every change.** The first time, it saves how your system was. Every tweak can be undone, and «Go back to how it was» restores everything.
- **Sends nothing.** No telemetry. It only asks GitHub whether there's a new version (you can turn that off).
- **Signed updates.** The updater checks an ECDSA signature before installing.
- **No risky stuff:** it doesn't turn off Defender, UAC or CPU protections, and doesn't touch anti-cheat or game processes. The only driver it can add is PawnIO (open source, signed; bundled unmodified and checked by SHA-256) to read CPU temperatures, and only if you say yes; uninstalling Vigía removes it only if Vigía installed it. For games it only edits config files, with the game closed.

## Build

You need the .NET 10 SDK on Windows.

```
cd app/Vigia
dotnet build -c Release
bin/Release/net10.0-windows/win-x64/Vigia.exe
```

Vigía asks for administrator rights because it changes Windows settings.

## Layout

| Folder | Contents |
|---|---|
| `app/Vigia` | The app (.NET 10, WinForms + WebView2; the UI lives in `wwwroot`) |
| `app/Puente` | Installer and updater |
| `landing` | The website (https://vigiapc.vercel.app) |
| `tools` | Publishing, translations, icons, wallpaper and virtual machine tests |

## Support

Vigía is free. If it helps you, give it a star, share it, or [buy me a coffee on Ko-fi](https://ko-fi.com/vigiapc). Something broken, or an idea to make it better? Open an [issue](https://github.com/JcperezDev/vigia/issues).

## License

[PolyForm Noncommercial 1.0.0](LICENSE). You can use, read, modify and share Vigía for free. What's not allowed is selling it, charging for it, or putting it in a paid product or service.

Versions up to 0.20.0 were released under the MIT license, and anyone who has them keeps that permission for those versions. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
