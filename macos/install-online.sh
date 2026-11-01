#!/bin/bash
set -euo pipefail
export PATH='/usr/bin:/bin:/usr/sbin:/sbin'
umask 077

download_only=false
case "${1:-}" in
  '') ;;
  --download-only) download_only=true ;;
  *) echo 'Usage: bash install.sh [--download-only]' >&2; exit 1 ;;
esac
[[ $# -le 1 ]] || exit 1
[[ "$(uname -s)" == Darwin ]] || { echo 'VibeCodingLight for Mac requires macOS.' >&2; exit 1; }
if [[ "$(uname -m)" != arm64 && "$(sysctl -n hw.optional.arm64 2>/dev/null || true)" != 1 ]]; then
  echo 'This package requires an Apple silicon Mac (M1 or later).' >&2; exit 1
fi
os_version="$(sw_vers -productVersion)"
major="${os_version%%.*}"
[[ "$major" =~ ^[0-9]+$ && "$major" -ge 14 ]] || { echo 'macOS 14 or later is required.' >&2; exit 1; }

temp_parent="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
stage="$(mktemp -d "$temp_parent/VibeCodingLight-install.XXXXXX")"
retain=false
cleanup() {
  [[ "$retain" == false ]] || return 0
  case "$stage" in
    "$temp_parent"/VibeCodingLight-install.??????)
      [[ -d "$stage" && ! -L "$stage" ]] && rm -rf -- "$stage" ;;
    *) echo 'Unexpected temporary path; cleanup stopped.' >&2 ;;
  esac
}
trap cleanup EXIT
manifest="$stage/release.json"
curl --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
  --connect-timeout 15 --max-time 30 --max-filesize 16384 \
  'https://www.vibecodinglight.com/releases/macos/installer.json' --output "$manifest"
version="$(plutil -extract version raw -o - "$manifest")"
platform="$(plutil -extract platform raw -o - "$manifest")"
expected="$(plutil -extract archiveSha256 raw -o - "$manifest")"
commit="$(plutil -extract sourceCommit raw -o - "$manifest")"
[[ "$version" =~ ^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$ ]] || { echo 'Invalid release version.' >&2; exit 1; }
[[ "$platform" == osx-arm64 && "$expected" =~ ^[a-fA-F0-9]{64}$ && "$commit" =~ ^[a-f0-9]{40}$ ]] || { echo 'Invalid release metadata.' >&2; exit 1; }
name="VibeCodingLight-macos-v$version-macos-arm64.zip"
url="https://github.com/barancavusoglu/vibecodinglight-desktop/releases/download/macos-v$version/$name"
archive="$stage/$name"
echo "Downloading VibeCodingLight $version for Mac..."
curl --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
  --connect-timeout 15 --max-time 300 "$url" --output "$archive"
actual="$(shasum -a 256 "$archive" | cut -d ' ' -f 1)"
[[ "$actual" == "$(printf '%s' "$expected" | tr 'A-F' 'a-f')" ]] || { echo 'Checksum mismatch. Installation stopped.' >&2; exit 1; }
unzip -Z -1 "$archive" > "$stage/entries.txt"
while IFS= read -r entry; do
  case "$entry" in
    /*|../*|*/../*|*/..|*\\*) echo 'Unsafe archive path. Installation stopped.' >&2; exit 1 ;;
  esac
  case "$entry" in
    VibeCodingLight.app/|VibeCodingLight.app/*|'Install VibeCodingLight.command'|__MACOSX/|__MACOSX/*) ;;
    *) echo 'Unexpected archive contents. Installation stopped.' >&2; exit 1 ;;
  esac
done < "$stage/entries.txt"
package="$stage/package"
ditto -x -k "$archive" "$package"
app="$package/VibeCodingLight.app"
installer="$package/Install VibeCodingLight.command"
[[ -d "$app" && ! -L "$app" && -f "$installer" && ! -L "$installer" ]] || { echo 'Incomplete application package.' >&2; exit 1; }
codesign --verify --deep --strict "$app"
[[ "$(plutil -extract CFBundleIdentifier raw -o - "$app/Contents/Info.plist")" == com.vibecodinglight.desktop ]] || exit 1
[[ "$(plutil -extract CFBundleShortVersionString raw -o - "$app/Contents/Info.plist")" == "$version" ]] || exit 1
[[ "$(plutil -extract VCLSourceRevision raw -o - "$app/Contents/Info.plist")" == "$commit" ]] || { echo 'Application source does not match the release.' >&2; exit 1; }
for notice in LICENSE NOTICE THIRD_PARTY_NOTICES.md; do
  [[ -f "$app/Contents/Resources/$notice" ]] || { echo 'License material is missing.' >&2; exit 1; }
done
[[ -d "$app/Contents/Resources/licenses" ]] || exit 1
if [[ "$download_only" == true ]]; then
  retain=true
  printf 'Verified package: %s\n' "$package"
  exit 0
fi
bash "$installer"
echo "VibeCodingLight $version is installed. Choose your assistants under Connections."
