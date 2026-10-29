# Release signing

How Vigía releases are signed, where the private key lives, and how to rotate it without breaking the chain of trust.

## What is signed

| Artifact | Signature | Checked by |
| --- | --- | --- |
| `Vigia-<ver>-win-x64.zip` | `.sig` (ECDSA P-256, SHA-256) | Puente, before extracting |
| `Puente.exe` | `.sig` | Vigía (before replacing Puente) and Puente (self-update) |
| `Vigia-Setup.exe` | `.sig` | the user / Vigía before running it |
| `vigia-manifest.json` | `.sig` | Puente: binds **version, minimum safe version, size and SHA-256 of every piece**. A valid old signature cannot be replayed as a newer version, and nothing below the minimum safe version is accepted. |
| `SHA256SUMS.txt` | — | humans |

The public key is compiled into `app/Puente/Program.cs` (`PublicKey`) and `app/Vigia/Updates.cs`. Release builds of Puente read **no** environment variables that change the key, paths or downloads; the test key (`PUENTE_TEST_PUBKEY`) only exists in the `PuenteTest=true` build (`Puente-test.exe`), which is never published.

## Where the private key lives

`%USERPROFILE%\.vigia\release-key.dpapi` (preferred) or `%USERPROFILE%\.vigia\release-key.pem` (legacy, plaintext).

* `.dpapi` is the PEM encrypted with Windows DPAPI for the **current user account** plus a fixed application entropy. Another Windows account, another PC, or a copy of the file on its own cannot decrypt it. It is created with its own ACL (only your account, no inherited entries).
* The key is never copied: `Puente.exe --sign` reads it directly, decrypts in memory and wipes the buffers after signing. `release.sh` passes only the path.
* `release.sh` refuses a key located inside the repository, so it can never be committed or packed by accident.
* The repository history was checked: no `.pem`/`.key` file and no `BEGIN … PRIVATE KEY` block in any commit.

### Moving from `.pem` to `.dpapi` (manual, needs your OK)

This does not change the key, only how it is stored at rest. Nothing is deleted automatically.

```bash
# 1. build Puente (Release)
dotnet build app/Puente/Puente.csproj -c Release -r win-x64
P=app/Puente/bin/Release/net10.0/win-x64/Puente.exe
# 2. encrypt (refuses to overwrite an existing .dpapi)
$P --protect-key "$USERPROFILE/.vigia/release-key.pem" "$USERPROFILE/.vigia/release-key.dpapi"
# 3. test: sign a scratch file with the .dpapi and verify it with the published public key
echo test > "$TMPDIR/sig-test.txt"
$P --sign "$TMPDIR/sig-test.txt" "$USERPROFILE/.vigia/release-key.dpapi" && $P --verify "$TMPDIR/sig-test.txt"
```

4. Make an **offline backup** of the `.pem` (encrypted USB / password manager attachment). DPAPI ties the `.dpapi` to this Windows account: if the account or Windows is reinstalled, only the offline backup can recover the key.
5. Only then delete the plaintext `.pem` (`release.sh` will pick the `.dpapi` automatically).

### Current state found (2026-10-29)

* `release-key.pem` is plaintext. Its folder `%USERPROFILE%\.vigia` has an **explicit** (not inherited) entry `CodexSandboxUsers:(OI)(CI)(RX)`, so the key inherits *read* for that local group. Its members are the Codex sandbox accounts `CodexSandboxOffline` and `CodexSandboxOnline` — the second one has network access, so a sandboxed process could read the key and send it out. Everything else is SYSTEM, Administrators and your account (full).
* Recommended, in this order: (1) remove that entry — `icacls "%USERPROFILE%\.vigia" /remove:g CodexSandboxUsers /t` — and check with `icacls`; (2) the `.dpapi` steps above. If Codex needs to read something in `.vigia` (e.g. `vm-cred.txt`), move that file elsewhere first rather than keeping the key readable. **Not changed** — it touches the key's permissions and possibly how Codex works, so it needs your OK.
* `%USERPROFILE%\.vigia` also holds other files (`vm-cred.txt`, `host-backup`); they were not opened.

## Signing a release without exposing the key

`tools/release.sh "notes"` does, in order: build → tests (Vigía selftest, Puente zip/ACL tests, translations) → package → manifest → sign → **verify every signature and hash locally with the freshly built Puente** → create the GitHub release as a **draft** → upload → re-check names, sizes and the manifest byte by byte from GitHub → push the tag → publish. If anything fails, the release stays a draft (invisible to users and to Puente). Running the script again resumes the same draft; a version that is already published is never modified, and the "Latest" mark of existing releases is never touched.

The script is tested against a local fake GitHub (`node tools/release-test.js`, 127.0.0.1 only, test key in `%TEMP%`).

The GitHub token comes from the git credential helper at run time; it is not stored in the repository or printed (only `***` in logs).

## Dedicated signing environment (recommended next step)

The strongest practical option for a solo project is to sign on a dedicated, offline Windows account or VM:

1. A separate local Windows account (or VM) that only builds and signs; the `.dpapi` key lives there.
2. Build artifacts are copied in, signed, verified, copied out; the account has no browser, no mail, no dev tools that run third-party code.
3. Upload from the normal account with `release.sh` (the signing steps can be skipped once the `.sig` files exist — future work).

A hardware token (smart card / YubiKey PIV with a P-256 key) would keep the key non-exportable; `Puente --sign` would need a CNG/PKCS#11 path. Not implemented.

CI signing (GitHub Actions secrets) is **not** recommended here: anyone who can change a workflow could exfiltrate the secret.

## Rotating the key (procedure, needs explicit authorization)

Installed copies of Vigía and Puente only trust the compiled-in public key, so a rotation must be bridged:

1. Generate the new key on the signing environment: `Puente.exe --genkey new.pem` (prints the new public key). Protect it with `--protect-key`.
2. Release **N** (bridge), signed with the **old** key, ships Puente/Vigía that trust **both** keys (old and new) and a `vigia-manifest.json` that names the new key fingerprint. Users update normally.
3. Wait until most installs are on N (several weeks).
4. Release **N+1** signed with the **new** key; its binaries trust only the new key. From then on the old key is retired (kept offline for emergency re-signing of N only).
5. If the old key is **compromised** (not just rotated): publish N as soon as possible, raise `minVersion` in the manifest to N so Puente refuses anything older, and announce it in the README / release notes so manual installs re-download Puente.

Multi-key trust is not implemented yet; it is needed only at step 2.
