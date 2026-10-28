# macOS source preview

Current source version: **0.1.6**. Target: macOS 14+, Apple Silicon (`arm64`) and Intel (`x64`) as separate bundles.

**This is not a verified, signed public Mac release.** Windows-based .NET/service tests do not validate Swift, CoreBluetooth, system privacy prompts, assistant behavior, Gatekeeper, notarization or physical hardware on a Mac. Windows UI/update features are not all implemented here.

The app uses Avalonia with a menu bar icon, shared session logic, integration setup/repair/removal, brightness controls and USB/Bluetooth links. The Swift CoreBluetooth helper is compiled into the bundle; end users do not install Swift or .NET.

## Development

On a Mac, install .NET 10 SDK and Xcode Command Line Tools:

```sh
dotnet run --project macos/AiLight.Mac.csproj -c Release -- --self-test
bash macos/build.sh arm64
# Intel: bash macos/build.sh x64
```

The script runs service/shared checks, builds the app and Bluetooth helper, signs with an ad-hoc identity by default and creates a development ZIP. A valid `AI_LIGHT_SIGN_IDENTITY` can select an existing signing identity; never put private certificates or passwords into the repo. Developer ID signing, notarization and real-Mac validation remain required before a trustworthy public distribution. See [release preparation](../docs/RELEASING.md).

Keep `windows/Core`, `windows/App/opencode-plugin.js` and `shared` beside this directory: the project deliberately shares these sources.

The installation helper expects a verified `VibeCodingLight.app` next to it, installs into `~/Applications`, preserves a previous app bundle and opens the new one. It does not remove Gatekeeper quarantine or bypass system security. Settings are preserved. Follow macOS permission prompts yourself.

## Current limitations

- No online update checker yet. Updates currently use a new local app bundle. Sparkle is not planned.
- USB, BLE handoff, Bluetooth permission, assistant hooks and live device behavior need real-Mac testing.
- Login startup is configured by the user in System Settings > General > Login Items.
- General Copilot/Antigravity limitations apply; see [integrations](../docs/INTEGRATIONS.md).
- The app cannot protect against arbitrary malicious processes running as the same user.
