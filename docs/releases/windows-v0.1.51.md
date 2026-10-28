# VibeCodingLight for Windows 0.1.51

The first public-source Windows release candidate brings the existing desktop companion to the standalone desktop repository.

- Session, connection and settings cards adapt to available window space and stay centered. Connection health matches integration-card widths.
- Six appearance themes: Light, Dark, Smoke, Sand, Forest and Night. Application preferences and About information share one card.
- Updates check the official version endpoint and open the download page when a newer version is available. Download and installation remain manual.
- Apache-2.0 source and third-party license notices are included with the distribution.

## Installation

Windows x64. Extract the entire ZIP, retain the license notices and run `VibeCodingLight-Setup.exe`. The application includes its .NET runtime and installs for the current user. An existing installation retains its settings. Connect the robot with your own USB-C data cable, or power it with your own charging cable and use Bluetooth. A cable and powerbank are not included with the robot.

## Release status and validation

This is a release candidate. Check `release.json` for the exact source commit, installer SHA256 and signature status. An archive named `unsigned-review` has no trusted publisher signature and is for review only; it must not enter the stable update channel.

The Windows build runs the shared regression suite, packaged integration smoke checks and OpenCode transport checks. Previous local upgrades preserved settings; a fresh Windows account and a signed download from the public distribution channel still require validation. macOS and physical USB/Bluetooth validation are separate milestones.

Code signing identifies a publisher and detects changes to the signed file. It does not certify that software is free of bugs or malware, or guarantee that a new file avoids SmartScreen warnings.
