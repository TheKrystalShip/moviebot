#!/usr/bin/env bash
# Deploys Cinema, the site and its player, to hotbox. Run it from hotrod, with no arguments:
#
#   deploy/deploy-site.sh
#
# It builds the site here and installs it in the API's web root, /opt/moviebot/api/wwwroot. The API
# reads the site from disk on every request, so nothing is restarted and nobody watching notices.
set -euo pipefail

host=hotbox
site=/opt/moviebot/api/wwwroot
api=http://127.0.0.1:8099

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dist="$root/web/site/dist"

"$root/scripts/build-site.sh"

ssh "$host" "mkdir -p '$site'"

# The hashed assets land first and the files that name them after, so a page never loads an index
# whose scripts are not there yet. An earlier build's assets stay for a page still open on it, and go
# once they are a fortnight old.
rsync -a --exclude=/index.html --exclude=/sw.js --exclude=/manifest.webmanifest "$dist/" "$host:$site/"
rsync -a "$dist/" "$host:$site/"
ssh "$host" "find '$site/assets' -type f -mtime +14 -delete"

built="$(md5sum < "$dist/index.html")"
served="$(ssh "$host" "curl -sf '$api/' | md5sum")"
if [[ "$built" != "$served" ]]; then
    echo "The API is not serving the site just installed. It serves its web root only when that" >&2
    echo "existed as it started: deploy/deploy-services.sh restarts it." >&2
    exit 1
fi
echo "Site deployed to $host:$site and served."
