#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'macOS required.' >&2; exit 1; }
source_dir="$(cd "$(dirname "$0")" && pwd -P)"
source_app="$source_dir/VibeCodingLight.app"
[[ -d "$source_app" ]] || { echo 'Place this installer next to VibeCodingLight.app.' >&2; exit 1; }
bundle_id=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$source_app/Contents/Info.plist")
[[ "$bundle_id" == com.vibecodinglight.desktop ]] || { echo 'Unexpected app bundle.' >&2; exit 1; }
/usr/bin/codesign --verify --deep --strict "$source_app"
destination="$HOME/Applications"
mkdir -p "$destination"
[[ ! -L "$destination" ]] || { echo 'Applications symlink is not supported.' >&2; exit 1; }
target="$destination/VibeCodingLight.app"
[[ ! -L "$target" ]] || { echo 'Existing app symlink is not supported.' >&2; exit 1; }
[[ "$source_app" != "$target" ]] || { /usr/bin/open "$target"; exit 0; }
staging="$(mktemp -d "$destination/.ai-light-install-XXXXXX")"
/usr/bin/ditto "$source_app" "$staging/VibeCodingLight.app"
if [[ -e "$target" ]]; then
  existing_id=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$target/Contents/Info.plist")
  [[ "$existing_id" == com.vibecodinglight.desktop ]] || { echo 'Existing unrelated app was preserved.' >&2; exit 1; }
  "$target/Contents/MacOS/VibeCodingLight" --quit || true
  sleep 2
  backup="$destination/VibeCodingLight.backup-$(date +%Y%m%d%H%M%S)-$$.app"
  mv "$target" "$backup"
  echo "Previous version preserved: $backup"
fi
mv "$staging/VibeCodingLight.app" "$target"
rmdir "$staging"
/usr/bin/open "$target"
echo 'Installed. Enable integrations in the Connections tab.'
