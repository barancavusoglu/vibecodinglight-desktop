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
# Bundle managed assemblies so codesign only sees native code in Contents/MacOS.
# Keep native libraries alongside the executable for signing without extraction.
dotnet publish AiLight.Mac.csproj -c Release -r "osx-$arch" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false \
  -p:DebugType=embedded -p:CopyOutputSymbolsToPublishDirectory=false -o "$bundle/Contents/MacOS"
swift_arch="$arch"
[[ "$arch" != x64 ]] || swift_arch=x86_64
xcrun swiftc -swift-version 5 -O -target "$swift_arch-apple-macos14.0" -framework CoreBluetooth \
  -Xlinker -sectcreate -Xlinker __TEXT -Xlinker __info_plist -Xlinker "$PWD/BluetoothHelper.plist" \
  BluetoothHelper.swift -o "$bundle/Contents/MacOS/VibeCodingLight.Bluetooth"
version="$(dotnet msbuild AiLight.Mac.csproj -getProperty:Version -nologo)"
cp Info.plist "$bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$bundle/Contents/Info.plist"
mkdir -p "$bundle/Contents/Resources"
iconset="$PWD/obj/VibeCodingLight.iconset"
mkdir -p "$iconset"
/usr/bin/sips -s format png ../windows/App/Assets/VibeCodingLight.ico --out "$PWD/obj/VibeCodingLight-source.png" >/dev/null
for size in 16 32 128 256 512; do
  /usr/bin/sips -z "$size" "$size" "$PWD/obj/VibeCodingLight-source.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  /usr/bin/sips -z "$((size * 2))" "$((size * 2))" "$PWD/obj/VibeCodingLight-source.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
/usr/bin/iconutil -c icns "$iconset" -o "$bundle/Contents/Resources/VibeCodingLight.icns"
cp install.command "$bundle/Contents/Resources/install.command"
cp ../LICENSE ../NOTICE ../THIRD_PARTY_NOTICES.md "$bundle/Contents/Resources/"
/usr/bin/ditto ../licenses "$bundle/Contents/Resources/licenses"
chmod +x "$bundle/Contents/MacOS/VibeCodingLight"
identity="${AI_LIGHT_SIGN_IDENTITY:--}"
sign_options=(--force --sign "$identity" --entitlements entitlements.plist)
if [[ "$identity" != - ]]; then sign_options+=(--timestamp --options runtime); fi
while IFS= read -r -d '' binary; do
  if /usr/bin/file "$binary" | /usr/bin/grep -q 'Mach-O'; then
    /usr/bin/codesign "${sign_options[@]}" "$binary"
  fi
done < <(/usr/bin/find "$bundle/Contents/MacOS" -type f -print0)
/usr/bin/codesign "${sign_options[@]}" "$bundle"
/usr/bin/codesign --verify --deep --strict "$bundle"
cp install.command "$stage/Install VibeCodingLight.command"
chmod +x "$stage/Install VibeCodingLight.command"
mkdir -p dist
archive="$PWD/dist/VibeCodingLight-macos-$arch-$version-$(date +%Y%m%d%H%M%S).zip"
/usr/bin/ditto -c -k --sequesterRsrc "$stage" "$archive"
echo "Created: $archive"
echo 'Developer ID signing + notarization + real Mac hardware checks are required before public release.'
