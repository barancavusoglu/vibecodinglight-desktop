# Security policy

## Reporting

Report vulnerabilities privately to **info@vibecodinglight.com** with the subject `VibeCodingLight Desktop security report`. Include the platform, version and minimal reproduction, with secrets and personal content removed. You may also use GitHub private vulnerability reporting if enabled for this repository. Please do not publish exploitable details or real customer/session data in an issue.

This is a beta project. Fixes target the newest applicable platform version; there is no LTS support promise or guaranteed response time. macOS is a source preview and is not a verified public distribution.

## Boundaries

- No cloud session-processing service is part of this repository.
- Hook/plugin setup changes local assistant configuration only after the user selects an integration. Trust approvals stay with the user.
- Windows uses a current-user IPC channel; macOS uses a private local socket directory. This is not a security boundary against arbitrary malicious code running as the same user.
- Diagnostics and backups can contain local metadata or original configuration. Redact them before sharing.
- Windows checks version metadata over HTTPS and opens a fixed official download page. It does not run commands or download executables supplied by metadata. HTTPS checks are not code-signing certificates.
- Unsigned development artifacts and successful CI are not a substitute for platform signing, notarization, antivirus analysis or real device testing.

Never commit credentials, certificates with private keys, signing secrets, device-owner data or production configuration. Follow [release preparation](docs/RELEASING.md) before distributing installers.
