#!/usr/bin/env bash
#
# Waits for llama-server to answer, then sends one real request through it.
#
# Two different waits are happening here and only the first is obvious. The server answers
# /health as soon as the model is resident, but the Vulkan backend compiles its compute
# pipelines lazily, on the first request that needs them, and the prompt prefix is only in the
# cache once something has been prefilled through it. Both are one-off costs of roughly a
# second, and both are otherwise paid by whoever speaks first, in front of a room.
#
# The warm-up only warms the shape it sends. Measured after a restart: replaying the warm-up's
# own request takes 241 ms, while the first request carrying a different system prompt and tool
# catalog takes 1,093 ms and the second takes 244 ms. So the body is not a token request to prove
# the port works — it is the bot's own instructions and catalog, which the bot writes into its
# state directory every time it starts. The file installed beside this script stands in only on a
# host where the bot has never run with the assistant switched on.
#
# Run as the unit's ExecStartPost, so systemd does not call the service active until a request
# of the real shape has been all the way through the model.

set -euo pipefail

PORT="${1:-8190}"
BODY_FILE="${2:-}"
if [[ -z "$BODY_FILE" ]]; then
  BODY_FILE=/var/lib/moviebot-bot/llm-warmup.json
  [[ -f "$BODY_FILE" ]] || BODY_FILE=/opt/moviebot/llm/warmup.json
fi
DEADLINE=$(( SECONDS + ${WARM_TIMEOUT:-180} ))

[[ -f "$BODY_FILE" ]] || { echo "no warm-up body at $BODY_FILE" >&2; exit 1; }

until curl -sf "127.0.0.1:$PORT/health" >/dev/null 2>&1; do
  if (( SECONDS >= DEADLINE )); then
    echo "llama-server never answered /health on $PORT" >&2
    exit 1
  fi
  sleep 1
done

start=$(date +%s%N)
if ! curl -sf "127.0.0.1:$PORT/v1/chat/completions" \
     -H 'Content-Type: application/json' --data-binary "@$BODY_FILE" >/dev/null; then
  echo "llama-server answered /health but refused a request on $PORT" >&2
  exit 1
fi
echo "warm: $BODY_FILE through in $(( ($(date +%s%N) - start) / 1000000 )) ms"
