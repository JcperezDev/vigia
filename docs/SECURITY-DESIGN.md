# Vigía security design and threat model

This document describes what Vigía protects, from whom, how, and what is still open. It is kept honest on
purpose: a gap listed here is a known gap, not a hidden one.

## Assets

1. **Admin rights.** Vigía changes registry keys, services, scheduled tasks and power settings, so it runs elevated.
   Anything that can make Vigía (or its updater) run code it did not ship gets admin rights.
2. **The user's Windows settings.** Each change must be reversible to the exact previous value.
3. **The user's files.** Cleanup must only delete what it showed, and nothing personal.
4. **The update channel.** A compromised update would reach every user.

## Attackers considered

| Attacker | Can do | In scope |
|---|---|---|
| Network (Wi-Fi, ISP, proxy) | Change or block downloads | Yes |
| GitHub release tampering without the signing key | Upload a different ZIP or Puente | Yes |
| Web content | Reach the WebView2 page through a link, iframe or redirect | Yes |
| A non-admin program already running as the user | Write to `%LOCALAPPDATA%`, send messages to windows | Partly (see gaps) |
| An admin program or kernel malware | Anything | No: it already owns the PC |

## Protections in place

### Updates (Puente)

- Every ZIP and every `Puente.exe` is signed with ECDSA P-256 (SHA-256). The public key is compiled into Puente;
  the private key never enters the repository. Nothing is extracted or run before the signature matches.
- A partially downloaded file that fails verification is deleted and downloaded once more from scratch.
- `SafeExtract` rejects absolute paths, `..`, `:` (NTFS alternate streams) and packages over 2 GB, and checks that
  every resolved path stays inside the target folder. Tested by `Puente.exe --zip-test [real.zip]`.
- No downgrade: Vigía is only replaced by a newer version, and Puente does not replace itself with an older
  signed Puente.
- The package must contain a `Vigia.exe` whose file version matches the release it claims to be.
- The previous version is kept; `Puente.exe --rollback` (or the "Go back" button) restores it.
- Authenticode signing is **not** used yet: there is no trusted code-signing certificate. Binaries are not
  presented as Authenticode-signed anywhere.

### Interface (WebView2)

- The page is served from the virtual host `https://app.vigia/` from files inside the install folder.
- Navigation and frame navigation outside that origin are cancelled; links open in the default browser only
  through a closed list of domains decided on the native side.
- Native code re-validates every message (`UiMessage.cs`): real source URL of the sender, 64 KB maximum, a JSON
  object at most 16 levels deep, and a `cmd` matching `^[A-Za-z][A-Za-z0-9]{0,39}$` that must be a known
  command. Rejections are written to `ui.log` (capped at 256 KB). JavaScript validation is not trusted.
- Host objects, autofill, password saving and browser shortcuts (DevTools included) are off outside a debugger.

### System changes

- The tweak catalog is embedded in `Vigia.exe`. A loose `tweaks.json` is ignored, so a non-admin program cannot
  turn an editable JSON file into an admin action.
- Before the first apply, the exact previous value of every operation is saved and a restore point is made.
  If an operation fails halfway, the operations done in that attempt are undone in reverse order.
- Retired tweaks are no longer offered, but their undo still works so older users can get their values back.
- The Simple mode asks yes/no for every change, and experimental tweaks never enter it or "Apply all".
- Defender, Firewall, SmartScreen, UAC, Secure Boot, TPM, memory protections and Windows Update are never turned
  off. The security module diagnoses and links to Windows settings; it does not bypass Tamper Protection.

### Driver (PawnIO)

- Only used to read CPU temperatures. Bundled unmodified; SHA-256 pinned
  (`1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`, PawnIO 2.2.0).
- Installed only after the user says yes in Monitor, never while a game is open, and only if it is missing.
  The UI cannot grant this consent through the settings message.
- The installer file is opened without write or delete sharing while it is hashed and run, so it cannot be swapped
  between the check and the launch.
- Uninstalling Vigía removes PawnIO only if Vigía installed it. Without PawnIO everything works except CPU
  temperature.

## Known gaps

| # | Severity | Gap | Plan |
|---|---|---|---|
| 1 | High (confirmed) | Vigía is installed per user in `%LOCALAPPDATA%\Programs\Vigia`, which any program running as the user can write. With "Open Vigía at Windows start" on (off by default), a scheduled task starts that `Vigia.exe` with highest privileges and no UAC prompt. A non-admin program could replace the file and get admin rights silently at the next logon. | Install per machine in `Program Files` (admin-only ACLs) or verify the binary from an admin-owned copy before the task runs it. Changes the installer and the update flow, so it needs the maintainer's approval. Until then the autostart option stays off by default. |
| 2 | High (design) | The whole UI runs elevated (`requireAdministrator`). A bug in the page or in a message handler runs with admin rights. | Split into a normal-rights UI and a small elevated helper with a named pipe (ACL limited to the user's SID, client process verified by path and signature, closed command list with typed parameters). Depends on gap 1: a helper in a user-writable folder adds nothing. |
| 3 | Medium | Not Authenticode-signed, so SmartScreen warns on first download. | Needs a real certificate. No self-signed or fake certificate will be used. |
| 4 | Low | Backups live in `%LOCALAPPDATA%\Vigia` and could be edited by a non-admin program to make "Revert" write chosen values. Values are limited to the keys of the embedded catalog, which limits the damage. | Sign or MAC backups with a per-install secret kept in an admin-only location once gap 1 is fixed. |
