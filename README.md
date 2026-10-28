# VibeCodingLight Desktop

Desktop companions for the [VibeCodingLight](https://www.vibecodinglight.com) desk robot. See whether your local AI coding sessions are working, waiting for you, or idle through a Windows tray app or a macOS menu bar app and an RGB status light.

[![CI](https://github.com/barancavusoglu/vibecodinglight-desktop/actions/workflows/ci.yml/badge.svg)](https://github.com/barancavusoglu/vibecodinglight-desktop/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

## Status and downloads

| Platform | Source version | Status |
| --- | --- | --- |
| Windows x64 | 0.1.51 | Tested local beta; Windows 10 2004 or later |
| macOS Apple Silicon / Intel | 0.1.6 | Source preview targeting macOS 14+; real Mac, Bluetooth, signing and notarization validation pending |

Use the [Releases page](https://github.com/barancavusoglu/vibecodinglight-desktop/releases) for published packages. **A source push or a successful CI build is not a signed public release.** There may be no release assets yet. CI artifacts are development builds. This repository does not claim Windows/Mac feature parity or production-ready Mac support.

Windows packages are self-contained: end users do not need .NET, Node.js or Arduino IDE. Mac packaging is also self-contained when built on macOS. The current UI is in Turkish. Download and run only packages from a source you trust; open source is not a malware-free certification.

## Features

- Windows system tray and macOS menu bar operation.
- Local coding-session status aggregation with user-controlled inclusion in the light calculation.
- USB data connection or Bluetooth LE with separate USB power, including power-bank use. USB takes priority when a USB host data connection exists.
- Brightness controls and firmware-rendered light effects.
- Windows responsive cards, six themes and update availability checks that open the official download page. Updates are installed manually.
- Install/repair/remove integration wiring while preserving unrelated user-owned hooks and settings.

| Integration | Scope |
| --- | --- |
| Codex | Local CLI and desktop sessions; hook approval remains the user's choice |
| Claude Code | CLI and local desktop Code sessions; excludes Chat, Cowork and remote sessions |
| OpenCode | Local plugin integration; excludes remote/WSL sessions |
| GitHub Copilot | Limited beta: working/idle lifecycle; permission/question waiting is not tracked |
| Antigravity | Limited beta: working/idle and `ask_question`; general permission dialogs are not tracked |

Behavior depends on the assistant version and execution environment. See [integration scope](docs/INTEGRATIONS.md).

## Light states

| Light | Meaning |
| --- | --- |
| Red | An included session is waiting for user input or permission |
| Green | Included sessions are working, with none idle or waiting |
| Yellow | Working and idle sessions coexist |
| Blue breathing | Only idle/open sessions remain, subject to the idle timeout |
| Off | No included open sessions, or idle timeout reached |

Firmware 0.3.0 with the `FX1` capability is required for current light control. Firmware, physical designs, the website, commerce services and private operations are outside this repository.

## Build from source

Developer prerequisites: [.NET SDK 10.0.400](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json`), Git, and Node.js 24 for Windows transport tests. Windows application builds require Windows. Mac bundles require macOS and Xcode Command Line Tools.

```powershell
# Windows: tests, publish, isolated integration smoke and OpenCode transport tests
pwsh -File windows/build.ps1
```

```sh
# Shared tests; no robot required
dotnet run --project windows/Tests/AiLight.Tests.csproj -c Release

# macOS service tests and local preview bundle
export AVALONIA_TELEMETRY_OPTOUT=1
dotnet run --project macos/AiLight.Mac.csproj -c Release -- --self-test
bash macos/build.sh arm64  # or x64
```

See [Windows](windows/README.md), [macOS](macos/README.md), [development](docs/DEVELOPMENT.md), and [release preparation](docs/RELEASING.md).

## Privacy and safety

Session processing takes place locally. The event transport intentionally omits prompt/answer bodies and command/tool arguments; it can retain local metadata such as session titles, project folder names, identifiers and diagnostics. Integration setup modifies the selected assistant's local hook/plugin configuration, with local backups. Those backups may contain your original configuration: do not upload them publicly.

The optional Windows startup update check contacts the official website for version metadata. It does not upload session contents and does not download or execute updates. The web host can still see ordinary HTTP connection information. There is no account requirement for the app. See [SECURITY.md](SECURITY.md).

## Contribution policy and license

This repository is a public source publication maintained by its owner. **External code contributions are not accepted, and pull requests are disabled.** You can read, download and use the source under its license; changes to this repository are maintained by the owner. See [the contribution policy](CONTRIBUTING.md). Maintainer commit messages must be in English.

Code and project documentation are licensed under [Apache License 2.0](LICENSE); preserve [NOTICE](NOTICE) and the separate [third-party notices](THIRD_PARTY_NOTICES.md). Disabling pull requests does not restrict the rights granted by that license. The license does not grant trademark rights. Product names of other vendors are used only to describe integrations; no affiliation or endorsement is implied.
