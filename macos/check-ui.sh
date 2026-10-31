#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
export AVALONIA_TELEMETRY_OPTOUT=1
mkdir -p dist
for width in 420 755 1440; do
  for tab in sessions connections settings; do
    args=(--layout-check --theme=night --showcase)
    [[ "$tab" != connections ]] || args+=(--connections)
    [[ "$tab" != settings ]] || args+=(--settings)
    dotnet run --project AiLight.Mac.csproj -c Release --no-build -- --preview "$PWD/dist/ci-$width-$tab.png" --preview-width="$width" "${args[@]}"
  done
done
for theme in light dark mist sand forest night; do
  dotnet run --project AiLight.Mac.csproj -c Release --no-build -- --preview "$PWD/dist/ci-theme-$theme.png" --settings --theme="$theme" --theme-preview --layout-check
done
for scenario in session-check runtime-check review-ui-test verify-check update-available-test diagnostics-test notice-test; do
  dotnet run --project AiLight.Mac.csproj -c Release --no-build -- --preview "$PWD/dist/ci-$scenario.png" "--$scenario" --layout-check
done
dotnet run --project AiLight.Mac.csproj -c Release --no-build -- --preview "$PWD/dist/ci-brightness.png" --settings --empty --brightness-check --layout-check
dotnet run --project AiLight.Mac.csproj -c Release --no-build -- --preview "$PWD/dist/ci-wheel.png" --settings --color-wheel --layout-check
dotnet run --project AiLight.Mac.csproj -c Release --no-build -- --preview "$PWD/dist/ci-empty.png" --empty --layout-check
