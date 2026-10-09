#!/usr/bin/env bash
#
# Puts the model the assistant's llama-server needs into the staged tree.
#
# It is large and does not change, so this prefers a copy already on the machine over a
# download, and verifies by digest either way. The digests are the contract: the
# upstream URLs point at a branch, and a branch moves.
#
# Run: ./fetch-models.sh   (on hotrod before install.sh, or on hotbox against /opt/moviebot)

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STAGE="${STAGE:-$HERE/stage}"

# name | sha256 | url
MODELS=(
  "llm/models/gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf|e531007218dfab990486a5de7676a6932d6ea8dea233d1f698d7c21cf8a16889|https://huggingface.co/unsloth/gemma-4-E2B-it-qat-GGUF/resolve/main/gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf"
)

# Places a copy may already exist, searched before anything is downloaded.
LOCAL_SEARCH=(
  "$HOME/gemma-test/models"
  "/opt/moviebot/llm/models"
)

digest() { sha256sum "$1" | cut -d' ' -f1; }

for entry in "${MODELS[@]}"; do
  IFS='|' read -r rel want url <<< "$entry"
  dest="$STAGE/$rel"
  name="$(basename "$rel")"
  mkdir -p "$(dirname "$dest")"

  if [[ -f "$dest" ]] && [[ "$(digest "$dest")" == "$want" ]]; then
    echo "ok       $name (already staged)"
    continue
  fi

  found=""
  for dir in "${LOCAL_SEARCH[@]}"; do
    if [[ -f "$dir/$name" ]] && [[ "$(digest "$dir/$name")" == "$want" ]]; then
      found="$dir/$name"; break
    fi
  done

  if [[ -n "$found" ]]; then
    echo "copy     $name  <- $found"
    cp -f "$found" "$dest"
  else
    echo "download $name"
    curl -fL --progress-bar "$url" -o "$dest.part"
    mv "$dest.part" "$dest"
  fi

  got="$(digest "$dest")"
  if [[ "$got" != "$want" ]]; then
    echo "DIGEST MISMATCH for $name" >&2
    echo "  expected $want" >&2
    echo "  got      $got" >&2
    echo "  upstream serves this from a branch, so it may have moved; confirm before trusting it." >&2
    rm -f "$dest"
    exit 1
  fi
  echo "ok       $name"
done

du -sh "$STAGE"/*/models 2>/dev/null || true
