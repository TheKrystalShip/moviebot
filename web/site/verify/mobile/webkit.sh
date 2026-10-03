#!/usr/bin/env bash
# Safari's engine for the harness, in Playwright's own image: WebKit wants a set of system libraries a
# rolling distribution does not ship, and the image carries them. It serves the browser on a socket
# run.mjs connects to, and shares the host's network so the browser reaches the scratch site at
# 127.0.0.1. The image's version is the site's Playwright version, since a client and a browser
# server of different versions refuse each other.
#
# Runs in the foreground; stop it with Ctrl-C.
#
#   MOVIEBOT_VERIFY_WEBKIT_PORT  the socket's port (3123)
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
version="$(node -p "require('$here/../../node_modules/playwright-core/package.json').version")"
port="${MOVIEBOT_VERIFY_WEBKIT_PORT:-3123}"

exec docker run --rm --name moviebot-verify-webkit --network host --init --ipc=host \
  "mcr.microsoft.com/playwright:v$version-noble" \
  /bin/sh -c "cd /home/pwuser && npx -y playwright@$version run-server --port $port --host 127.0.0.1"
