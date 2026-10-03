#!/usr/bin/env bash
# Builds Krystal Cinema, whose player is also the Discord Activity, and installs it where the API
# serves it from: the root of its wwwroot.
#
# One origin serves the site, the player, the API, the media and the hub, so a Discord Activity needs
# one URL mapping rather than several that drift apart. Discord launches the Activity at the root; the
# site's index takes such a load to /embed/.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target="$root/src/MovieBot.Api/wwwroot"

cd "$root/web/site"
npm ci --silent
npm run check
npm run build

rm -rf "$target"
mkdir -p "$target"
cp -r "$root/web/site/dist/." "$target/"

echo "Site and player installed into $target"
