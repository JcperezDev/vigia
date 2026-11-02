# Vigia

A monitor and optimizer for Windows 10/11 PCs, built for gaming. Free, no ads, no accounts.

**Download:** https://vigiapc.vercel.app · [Latest release](https://github.com/JcperezDev/vigia/releases/latest)

The app is available in English, Spanish, Portuguese, French and German. [Leer en español](README.es.md).

This repository holds the installers and updates of Vigia, and it's where you report problems or suggest ideas (Issues). The code is kept in a private repository.

## What it does

- **Monitor:** CPU, GPU, memory and disks, using the same counters as Task Manager, plus the CPU temperature when there is a compatible source and a valid reading (not every processor is supported), and which program is using more than it should.
- **Simple mode:** answer a few questions (printer, network, Windows apps…) and Vigia does the rest.
- **Tweaks, Games and Memory:** Windows tweaks explained one by one, real FPS before and after, a Fortnite profile, stretched resolution with an FPS estimate, and automatic memory release.
- **Windows apps, Windows startup, Storage:** remove what you don't need, turn off what starts on its own, and clean temporary files and the Recycle Bin.
- **Diagnostics, Disk health, BIOS:** checks your PC and explains what's worth changing. The BIOS is read only; Vigia never writes to it.
- **Windows ISO:** builds a clean ISO from the official one (no ads, local account, no TPM or Secure Boot check, no data collection, no automatic BitLocker) and writes it to a USB drive.

## Safety

- **A backup before every change.** The first time, it saves how your system was. Every tweak can be undone, and «Go back to how it was» restores everything.
- **Sends nothing.** No telemetry. It only asks GitHub whether there's a new version (you can turn that off).
- **Signed updates.** The updater checks the signature before installing.
- **No risky stuff:** it doesn't turn off Defender, UAC or CPU protections, and doesn't touch anti-cheat or game processes. The only driver it can add is PawnIO (open source, signed; bundled unmodified and checked by SHA-256) to read CPU temperatures, and only if you say yes; uninstalling Vigia removes it only if Vigia installed it. For games it only edits config files, with the game closed.

## Support

Vigia is free. If it helps you, give it a star, share it, or [donate with PayPal](https://www.paypal.com/donate/?hosted_button_id=6N3GP8FABMQ9U). Something broken, or an idea to make it better? Open an [issue](https://github.com/JcperezDev/vigia/issues).

## License

[PolyForm Noncommercial 1.0.0](LICENSE). You can use and share Vigia for free. What's not allowed is selling it, charging for it, or putting it in a paid product or service.

Versions up to 0.20.0 were released under the MIT license, and anyone who has them keeps that permission for those versions. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
