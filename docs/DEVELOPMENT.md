# Development and validation

## Layout

`windows/App` is the WPF application, installer and local transports. `windows/Core` is shared session/protocol/hook logic. `windows/Tests` is an executable regression suite, not a test-framework project; run it with `dotnet run`, not only `dotnet test`. `macos` is the Avalonia/native-helper preview. `shared` contains common serial transport code.

Keep relative project references intact. The firmware and physical product are separate; a desktop-only change should not silently require new firmware.

## Useful commands

`global.json` pins .NET SDK 10.0.400 so implicit linker/runtime dependencies remain consistent with the lock files. Install that exact SDK; update the SDK and lock files together. Avalonia's build-time telemetry is disabled by the Mac packaging script and CI. For direct Mac builds, set `AVALONIA_TELEMETRY_OPTOUT=1` in your developer shell.

```powershell
dotnet restore windows/Tests/AiLight.Tests.csproj --configfile NuGet.Config
dotnet run --project windows/Tests/AiLight.Tests.csproj -c Release
pwsh -File windows/build.ps1
```

```sh
dotnet run --project macos/AiLight.Mac.csproj -c Release -- --self-test
bash macos/build.sh arm64
```

The Windows build runs actual packaged subprocess/IPC checks for Codex and Claude plus a Node-based OpenCode transport check. Test configuration lives in isolated directories. Never run the public installer or enable real assistant hooks as a routine test.

For UI changes, use synthetic offline preview arguments documented in `windows/README.md`. Check narrow/wide windows and logical display scaling. The existing Windows manifest enables PerMonitorV2, but a rendered DPI scenario is not proof of behavior when moving a real window between monitors.

For core changes, check state priority, duplicate/stale events, async questions, child-session lifetime, exclusion, privacy, disconnects and compatibility for each affected assistant. Do not substitute a green shared test for a real harness or physical-device claim.

## Dependencies and notices

Direct NuGet versions are pinned in project files. Lock files record resolved dependencies. CI restores in locked mode. Regenerate them intentionally when changing dependencies, review transitive updates, and update `THIRD_PARTY_NOTICES.md` and `licenses/`. The .NET runtime is redistributed with self-contained builds and has its own notices.

After restoring both application projects, `node scripts/generate-notices.mjs` rebuilds the package inventory and copies notice files from the local NuGet cache. Review the generated diff and retain/update the curated upstream license texts in `licenses/upstream`; this local helper does not fetch or verify new upstream licenses automatically.

Build output, user preferences, diagnostics, hook backups, signing material and executable files are ignored. A source change is not authorization to update a running installation.
