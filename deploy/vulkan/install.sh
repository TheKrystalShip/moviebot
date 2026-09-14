#!/usr/bin/env bash
#
# Installs the staged trees onto hotbox, under /opt/moviebot.
#
# No sudo: /opt/moviebot is owned by heisen, so this is an ordinary rsync. The unit files are
# a separate, privileged step — see units/README or the parent README.
#
# The models are fetched ON hotbox rather than pushed from here. They are 3 GB, they are
# already on that machine, and copying them across the LAN to a directory that can copy them
# locally is a waste of the only link between the two hosts.
#
# Run: ./install.sh [host]        (default host: hotbox)

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STAGE="${STAGE:-$HERE/stage}"
HOST="${1:-hotbox}"
PREFIX="${PREFIX:-/opt/moviebot}"

[[ -d "$STAGE/llm/bin" && -d "$STAGE/whisper/bin" ]] || {
  echo "nothing staged — run ./build.sh first" >&2; exit 1; }

echo "== installing to $HOST:$PREFIX"

# --delete keeps a tree from accumulating libraries a later build stopped producing, which is
# how a stale .so ends up satisfying a NEEDED entry nobody meant to still be there. The models
# directory is excluded from that: it is populated on the far side and must survive a rebuild.
for tree in llm whisper; do
  ssh "$HOST" "mkdir -p '$PREFIX/$tree'"
  rsync -a --delete --exclude 'models/***' \
    "$STAGE/$tree/" "$HOST:$PREFIX/$tree/"
  echo "  $tree: $(ssh "$HOST" "du -sh '$PREFIX/$tree' | cut -f1")"
done

# The fetch script travels with the trees so the far side can re-run it without this repo.
rsync -a "$HERE/fetch-models.sh" "$HOST:$PREFIX/fetch-models.sh"
echo
echo "== models"
ssh "$HOST" "STAGE='$PREFIX' '$PREFIX/fetch-models.sh'"

echo
echo "== checking the install on $HOST"
ssh "$HOST" "'$PREFIX/llm/bin/llama-server' --list-devices 2>&1 | tail -4"
echo
echo "next: install the unit files (needs root on $HOST)"
