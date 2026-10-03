#!/usr/bin/env bash
# Builds Krystal Cinema and the player and installs them where the API serves them from: the site at
# the root, the player under /activity/.
#
# One origin serves the site, the player, the API, the media and the hub, so a Discord Activity needs
# one URL mapping rather than several that drift apart. Discord launches the Activity at the root; the
# site's index sends such a load on to /activity/.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target="$root/src/MovieBot.Api/wwwroot"

cd "$root/web/site"
npm ci --silent
npm run build

cd "$root/web/activity"
npm ci --silent
npm run build -- --base /activity/

rm -rf "$target"
mkdir -p "$target/activity"
cp -r "$root/web/site/dist/." "$target/"
cp -r "$root/web/activity/dist/." "$target/activity/"

echo "Site and player installed into $target"
