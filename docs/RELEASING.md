# Release preparation

Source publication, CI artifacts and public installers are separate milestones. This initial repository publishes source and CI configuration; it does not certify a Mac distribution or create a signed binary release automatically.

## Versioning

- Windows version: `windows/Directory.Build.props`.
- Mac version: `macos/AiLight.Mac.csproj`; keep `Info.plist` and bundle packaging consistent.
- Use distinct tags: `windows-v0.1.51` and `macos-v0.1.6`. Platform versions are independent. Do not assume GitHub's repository-wide `latest` points to the right OS.
- English commit subjects, tags/release titles and release notes. Explain the concrete behavior and validation, including anything not tested.

## Windows

1. Run the full Windows build/tests from a clean checkout and inspect the installer version.
2. Review current NuGet/native-runtime licenses and ship `LICENSE`, `NOTICE`, `THIRD_PARTY_NOTICES.md` and `licenses/` with the downloadable package.
3. Sign production binaries with an appropriate trusted Windows code-signing identity, then run `pwsh -File scripts/package-windows.ps1`. See [Windows signing](WINDOWS_SIGNING.md) for provider eligibility, signing order and verification. A checksum is not a publisher signature; signing does not guarantee immediate SmartScreen reputation.
4. Test a new installation and an upgrade from the previous version on a real Windows account, including settings and hook ownership preservation. Never publish real test-account data.
5. Push a `windows-v...` tag matching the source version and add `docs/releases/windows-v<version>.md` first. The release workflow reruns the build, creates a complete `unsigned-review` ZIP plus SHA256, and uploads a **draft prerelease** for review. It does not install anything or publish it to users. Local review packaging uses `pwsh -File scripts/package-windows.ps1 -AllowUnsigned`.
6. Verify signing, notices and package behavior, then publish the release when ready. The initial workflow builds unsigned packages; signing must be added/configured before production use. Do not label the result signed.

## macOS

Build on real macOS for both supported architectures. The source build uses an ad-hoc signing identity unless configured otherwise. Validate the Swift helper, CoreBluetooth permissions, USB/BLE behavior, local assistant hooks and app quit/restart. A Windows-based .NET build is insufficient.

Before a public Mac package, use Developer ID signing with the appropriate entitlements, notarize the distribution and verify Gatekeeper behavior. Never remove quarantine or bypass system security in installation instructions. Online update checks have not yet been implemented on Mac. The initial public-release workflow intentionally does not publish Mac packages.

## Website and update metadata

Do not announce a version before its download works. First publish the verified package, then connect/test the website's OS-specific download link, and only then publish Windows `https://www.vibecodinglight.com/releases/windows/stable.json`:

```json
{"version":"0.1.51"}
```

The Windows app checks only version metadata and opens `https://www.vibecodinglight.com/#setup`. It does not download/install updates. This repository's CI does not deploy the website or metadata. Keep prerelease/development artifacts out of the stable channel.

## References

- [GitHub releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)
- [Windows SmartScreen reputation](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)
- [Apple notarization](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
