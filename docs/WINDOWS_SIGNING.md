# Windows code signing

No signing provider or certificate is configured yet. Current draft packages are unsigned review builds. Never describe them as signed or place them in the stable update channel.

## Choosing a publisher identity

- [SignPath Foundation](https://signpath.org/terms) offers free signing for accepted open-source projects. Acceptance requires project review and verifiable reputation; a new repository is not automatically eligible. The certificate publisher is **SignPath Foundation**, not the maintainer's own company. Its roles, public signing/privacy policy and manual release approval requirements must be implemented after acceptance. No application has been submitted and no sponsorship is claimed here.
- A certificate authority can supply a code-signing certificate in a validated individual or legal business name, using a supported hardware token or cloud signing service. Confirm Turkish applicant eligibility, the exact publisher name, identity documents and full token/cloud costs before purchasing. A website TLS certificate is not a code-signing certificate.
- [Microsoft Artifact Signing eligibility](https://learn.microsoft.com/en-gb/azure/artifact-signing/quickstart#prerequisites) currently does not include Turkey for Public Trust identities. Selecting an Azure resource in another region does not change identity eligibility. Private Trust is not a substitute for publicly trusted consumer downloads.

Reviewed on 2026-10-29. Provider requirements can change. Do not buy EV solely to remove SmartScreen warnings: [Microsoft documents that EV no longer bypasses them](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation).

## Build, sign, then package

1. Commit the source and release notes. Run `pwsh -File windows/build.ps1` from a clean checkout. Keep its regression and smoke output for the release review. Do not rebuild after signing.
2. Sign the final single-file `windows/dist/VibeCodingLight-Setup.exe` with the selected provider and an RFC 3161 timestamp. The installer copies this same executable into the install location, preserving its signature. No separate bootstrapper is produced.
3. Verify the signature and timestamp, then run `pwsh -File scripts/package-windows.ps1`. It requires a clean checkout, matching embedded source/version, a valid signature and a timestamp. It packages the installer, notices, release notes and metadata, then computes SHA256 over the final ZIP. A checksum is not a publisher signature.
4. Validate a fresh installation and upgrade on Windows, including the installed executable's signature, settings and hook ownership. Publish only after release approval. Connect the website download first, then publish stable version metadata as described in [RELEASING.md](RELEASING.md).

For a provider whose token exposes the certificate through the current user's Windows certificate store, the Windows SDK [SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool) can be used as follows. Use the provider's actual timestamp service and your certificate's thumbprint; cloud providers may require their own signing client instead.

```powershell
$installer = 'windows/dist/VibeCodingLight-Setup.exe'
$thumbprint = '<validated code-signing certificate thumbprint>'
$timestampUrl = '<provider RFC 3161 timestamp URL>'
signtool sign /sha1 $thumbprint /fd SHA256 /tr $timestampUrl /td SHA256 $installer
if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }
signtool verify /pa /all /v $installer
if ($LASTEXITCODE -ne 0) { throw 'Signature verification failed.' }
Get-AuthenticodeSignature -LiteralPath $installer |
    Format-List Status, SignerCertificate, TimeStamperCertificate
pwsh -File scripts/package-windows.ps1
```

The `/sha1` argument selects a certificate by thumbprint; `/fd SHA256` selects the actual file digest algorithm. Keep private keys in the provider's token or managed signing service. Never commit private keys, token PINs, signing credentials or exported key files. Do not replace publisher trust with a self-signed certificate or ask users to disable Windows security.

## Unsigned review packages

Before an identity is available, explicitly use `pwsh -File scripts/package-windows.ps1 -AllowUnsigned`. The archive is marked `unsigned-review`, includes `UNSIGNED_REVIEW.txt`, and records `NotSigned` in `release.json`. Invalid or untrusted signatures are rejected even with this switch. The tag workflow currently uses this mode and creates a **draft prerelease** only. Selecting a signing provider still requires integrating its signing step before packaging; this guide does not configure one.
