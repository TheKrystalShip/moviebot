#!/usr/bin/env bash
# Prints the repository's version: the newest release heading in CHANGELOG.md.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sed -n 's/^## \[\([0-9][^]]*\)\].*/\1/p' "$root/CHANGELOG.md" | head -n 1
