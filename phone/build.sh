#!/usr/bin/env bash
# Builds Cinema for Android phones: the TKS shell (shell/, vendored) as shell.conf, res/ and
# CHANGELOG.md make it. Every option is the shell's (shell/build.sh):
#
#   phone/build.sh [--debug] [--origin https://host] [--install] [--open [url]]
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$here/shell/build.sh" "$here" "$@"
