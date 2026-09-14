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
#
# --checksum compares content rather than timestamp and size, which is what makes an unchanged
# build a genuine no-op. Every binary and library here is rewritten by patchelf on its way into the
# staging tree, so all of them carry a fresh mtime on every run even when nothing about them
# changed; by mtime alone rsync would replace every inode each time, under whatever is running.
#
# Timestamps are not the way to check that this worked. -a implies -t, so a skipped file still has
# the staging mtime written onto it in place: the metadata changes and the inode does not. The
# property that matters is inode identity — compare stat against what /proc/<pid>/maps has open.
for tree in llm whisper; do
  ssh "$HOST" "mkdir -p '$PREFIX/$tree'"
  rsync -a --checksum --delete --exclude 'models/***' \
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

# A running service goes on using the files it started with. The sync replaces them rather than
# writing through them, so the old inodes stay alive, mapped, and invisible — the unit is active,
# the binary on disk is the new one, and the process is still the old one. It shows up only as
# "(deleted)" against /proc/<pid>/exe, which nothing looks at.
#
# Only paths under the install prefix count. A process carries other mappings that read as deleted
# and never mean anything is wrong: the NVIDIA driver's /memfd:/.glXXXXXX, and /memfd:doublemapper
# in every .NET process, which is the runtime's write-xor-execute mapping for JIT code. A bare
# grep for "(deleted)" reports those on every run, and a warning that is always on is one people
# learn to scroll past.
echo
echo "== services running on replaced files"
for unit in moviebot-llm moviebot-speech; do
  stale=$(ssh "$HOST" "
    systemctl is-active --quiet $unit.service || exit 0
    pid=\$(systemctl show -p MainPID --value $unit.service)
    [ -n \"\$pid\" ] && [ \"\$pid\" != 0 ] || exit 0
    if readlink /proc/\$pid/exe 2>/dev/null | grep -q '(deleted)' \
       || grep -q '$PREFIX/.*(deleted)' /proc/\$pid/maps 2>/dev/null; then
      echo stale
    fi")
  if [[ "$stale" == "stale" ]]; then
    echo "  $unit is running code this deploy replaced — restart it:"
    echo "      ssh $HOST sudo systemctl restart $unit.service"
  else
    echo "  $unit: current"
  fi
done

echo
echo "next: install the unit files (needs root on $HOST)"
