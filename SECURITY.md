# Security policy

## Reporting a vulnerability

Please report security problems privately through GitHub: **Security → Report a vulnerability** on
<https://github.com/JcperezDev/vigia>. If that option is not available, open an issue that only says you have
a security report (no details) and you will get a private contact.

Include the Vigía version, Windows version and build, and the steps to reproduce. Please give a reasonable time
to fix the problem before you publish it. Vigía is free and run by one maintainer, so there is no bug bounty.

## Supported versions

Only the latest release gets security fixes. Vigía updates itself through Puente, so updating is the fix.

## What Vigía promises

- It does not send your data anywhere. The only network requests are the update check and the download of
  releases from GitHub, and the support links you open yourself.
- Updates are signed with ECDSA P-256. Puente checks the signature of every file before it installs anything,
  refuses ZIP entries that point outside the install folder, never installs an older version than the one you
  have, and keeps the previous version so you can go back.
- Changes to Windows are saved first (exact previous value plus a restore point) and can be undone. Deleting
  files (temporary files, the Recycle Bin) cannot be undone, and Vigía says so before it deletes.
- It never turns off Defender, Firewall, SmartScreen, UAC, Secure Boot, TPM, memory protections or Windows
  Update, never writes to the BIOS, and never touches game processes, game memory or anti-cheat software.
- The only driver it can add is PawnIO (open source, signed, bundled unmodified and checked by SHA-256), and only
  after you say yes.

The design, the threat model and the known gaps are in [docs/SECURITY-DESIGN.md](docs/SECURITY-DESIGN.md).
