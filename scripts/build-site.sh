#!/usr/bin/env bash
# Builds Cinema, whose player is also the Discord Activity, into web/site/dist.
#
# scripts/build-player.sh installs that build into the checkout's API wwwroot for a release;
# deploy/deploy-site.sh installs it into the API's wwwroot on hotbox.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$root/web/site"
npm ci --silent
npm run check
npm run build

echo "Site and player built into $root/web/site/dist"
