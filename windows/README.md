# Windows beta

Current source version: **0.1.51**. Windows x64, Windows 10 2004 (build 19041) or later. Bluetooth requires a compatible adapter and permission. USB requires a data-capable cable; charge-only cables provide power but no USB data connection.

The WPF app lives in the system tray. It supports local assistant integrations, six themes, centered adaptive cards, brightness controls, device connection details, per-session light inclusion and startup/update-check preferences. The UI is currently Turkish.

## Build and test

Install .NET 10 SDK and Node.js 24, then run from the repository root:

```powershell
pwsh -File windows/build.ps1
```

The script runs shared regression tests, publishes a self-contained executable, runs integration smoke tests in isolated test directories and tests the packaged OpenCode transport. It produces `windows/dist/VibeCodingLight-Setup.exe` and a portable build. It does not install the app or modify real assistant configuration.

```powershell
# Regression tests only
dotnet run --project windows/Tests/AiLight.Tests.csproj -c Release

# Offline UI preview (isolated data directory; no robot or IPC connection)
$env:AI_LIGHT_DATA_DIR = Join-Path $PWD 'windows/dist/preview-data'
$env:AI_LIGHT_TEST_PIPE = [guid]::NewGuid().ToString('N')
& './windows/dist/portable/AI Light.exe' --render-preview './windows/dist/settings.png' 2 --theme=night --preview-size=1440x900
```

Preview tabs: `0` sessions, `1` integrations, `2` settings. Optional `--responsive-check`, `--theme-check` and `--connection-cards-check` use synthetic fixtures. Do not publish screenshots of your real sessions.

## Installation and updates

Run the verified `VibeCodingLight-Setup.exe` to install per user. The installer stops the existing app, replaces its executable, preserves user settings and reopens the app. End users do not need .NET, Node.js or Arduino IDE.

The optional startup check or **Check for updates** button reads:

`https://www.vibecodinglight.com/releases/windows/stable.json`

The UTF-8 JSON format is `{"version":"0.1.51"}`. Only stable numeric versions are accepted, with a 32 KB limit. Equal/older versions are not offered. An absent release or network failure is not reported as “up to date.” New versions open the fixed official page `https://www.vibecodinglight.com/#setup`; download/installation is manual. Metadata cannot set a URL or command. The remote release file and website download links require a separate publication step.

Per-user state is in `%LOCALAPPDATA%\AI Light`. Installed binaries use `%LOCALAPPDATA%\Programs\AI Light`. The internal executable remains `AI Light.exe` to preserve existing hook paths and upgrades. Backups can include original assistant settings; do not share them publicly.

Firmware 0.3.0 with `FX1` is required. USB data connection takes priority over Bluetooth. The six-second firmware watchdog remains in effect if the desktop connection stops.
