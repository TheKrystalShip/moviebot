#!/usr/bin/env bash
# Builds the player and installs it where the API serves it from.
#
# One origin serves the page, the API, the media and the hub, so a Discord Activity needs one URL
# mapping rather than several that drift apart.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target="$root/src/MovieBot.Api/wwwroot"

cd "$root/web/activity"
npm ci --silent
npm run build

rm -rf "$target"
mkdir -p "$target"
cp -r dist/. "$target/"

echo "Player installed into $target"
