#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'macOS is required to verify and package this application.' >&2; exit 1; }
root="$(cd "$(dirname "$0")/.." && pwd -P)"
cd "$root"
[[ $# -eq 1 ]] || { echo 'Usage: bash scripts/package-macos.sh path/to/built-arm64.zip' >&2; exit 1; }
source_archive="$1"
[[ -f "$source_archive" ]] || { echo 'Built archive not found.' >&2; exit 1; }
[[ -z "$(git status --porcelain --untracked-files=normal)" ]] || { echo 'Commit source changes before preparing a release.' >&2; exit 1; }
version="$(dotnet msbuild macos/AiLight.Mac.csproj -getProperty:Version -nologo)"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid source version.' >&2; exit 1; }
commit="$(git rev-parse HEAD)"
out="$root/macos/dist/releases"
name="VibeCodingLight-macos-v$version-macos-arm64.zip"
mkdir -p "$out"
for file in "$out/$name" "$out/$name.sha256" "$out/macos-release.json"; do
  [[ ! -e "$file" ]] || { echo 'Release output already exists; use a fresh build directory.' >&2; exit 1; }
done
# Keep verification staging in ignored build output for inspection on failure.
stage="$(mktemp -d "$root/macos/dist/package-check-XXXXXX")"
/usr/bin/ditto -x -k "$source_archive" "$stage"
app="$stage/VibeCodingLight.app"
[[ -d "$app" && ! -L "$app" && -f "$stage/Install VibeCodingLight.command" ]] || { echo 'Incomplete application archive.' >&2; exit 1; }
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Contents/Info.plist")" == com.vibecodinglight.desktop ]] || exit 1
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$app/Contents/Info.plist")" == "$version" ]] || exit 1
[[ "$(/usr/libexec/PlistBuddy -c 'Print :VCLSourceRevision' "$app/Contents/Info.plist")" == "$commit" ]] || { echo 'Bundle source commit does not match this checkout.' >&2; exit 1; }
/usr/bin/codesign --verify --deep --strict "$app"
for binary in VibeCodingLight VibeCodingLight.Bluetooth; do
  /usr/bin/lipo -verify_arch arm64 "$app/Contents/MacOS/$binary"
done
[[ "$("$app/Contents/MacOS/VibeCodingLight" --version)" == "$version+$commit" ]] || { echo 'Application version/source does not match this checkout.' >&2; exit 1; }
for notice in LICENSE NOTICE THIRD_PARTY_NOTICES.md; do
  /usr/bin/cmp "$root/$notice" "$app/Contents/Resources/$notice"
done
/usr/bin/diff -qr "$root/licenses" "$app/Contents/Resources/licenses"
/usr/bin/cmp "$root/macos/install.command" "$stage/Install VibeCodingLight.command"
/usr/bin/cmp "$root/macos/install.command" "$app/Contents/Resources/install.command"
cp "$source_archive" "$out/$name"
hash="$(/usr/bin/shasum -a 256 "$out/$name" | cut -d ' ' -f 1)"
printf '%s  %s\n' "$hash" "$name" > "$out/$name.sha256"
cat > "$out/macos-release.json" <<EOF
{
  "version": "$version",
  "platform": "osx-arm64",
  "archiveSha256": "$hash",
  "sourceCommit": "$commit"
}
EOF
printf 'Verified release package: %s\nSHA256: %s\n' "$out/$name" "$hash"
