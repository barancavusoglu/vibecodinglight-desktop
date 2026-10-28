#!/bin/bash
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1
cd "$(dirname "$0")"
[[ "$(uname -s)" == Darwin ]] || { echo 'Final .app packaging requires macOS.' >&2; exit 1; }
arch="${1:-arm64}"
[[ "$arch" == arm64 || "$arch" == x64 ]] || { echo 'Use arm64 or x64.' >&2; exit 1; }
dotnet run --project AiLight.Mac.csproj -c Release -- --self-test
dotnet run --project ../windows/Tests/AiLight.Tests.csproj -c Release
stage="$(mktemp -d "$PWD/dist-stage-XXXXXX")"
bundle="$stage/VibeCodingLight.app"
mkdir -p "$bundle/Contents/MacOS"
dotnet publish AiLight.Mac.csproj -c Release -r "osx-$arch" --self-contained true -o "$bundle/Contents/MacOS"
swift_arch="$arch"
[[ "$arch" != x64 ]] || swift_arch=x86_64
xcrun swiftc -swift-version 5 -O -target "$swift_arch-apple-macos14.0" -framework CoreBluetooth \
  -Xlinker -sectcreate -Xlinker __TEXT -Xlinker __info_plist -Xlinker "$PWD/BluetoothHelper.plist" \
  BluetoothHelper.swift -o "$bundle/Contents/MacOS/VibeCodingLight.Bluetooth"
cp Info.plist "$bundle/Contents/Info.plist"
chmod +x "$bundle/Contents/MacOS/VibeCodingLight"
identity="${AI_LIGHT_SIGN_IDENTITY:--}"
sign_options=()
if [[ "$identity" != - ]]; then sign_options=(--timestamp --options runtime); fi
while IFS= read -r -d '' binary; do
  if /usr/bin/file "$binary" | /usr/bin/grep -q 'Mach-O'; then
    /usr/bin/codesign --force --sign "$identity" "${sign_options[@]}" --entitlements entitlements.plist "$binary"
  fi
done < <(/usr/bin/find "$bundle/Contents/MacOS" -type f -print0)
/usr/bin/codesign --force --sign "$identity" "${sign_options[@]}" --entitlements entitlements.plist "$bundle"
/usr/bin/codesign --verify --deep --strict "$bundle"
cp install.command "$stage/Install VibeCodingLight.command"
chmod +x "$stage/Install VibeCodingLight.command"
mkdir -p dist
archive="$PWD/dist/VibeCodingLight-macos-$arch-0.1.6-$(date +%Y%m%d%H%M%S).zip"
/usr/bin/ditto -c -k --sequesterRsrc "$stage" "$archive"
echo "Created: $archive"
echo 'Developer ID signing + notarization + real Mac hardware checks are required before public release.'
