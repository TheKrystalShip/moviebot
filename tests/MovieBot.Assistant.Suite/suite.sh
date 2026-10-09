#!/usr/bin/env bash
# The MovieBot assistant suite's one entry point, run on hotrod (where the harness is).
#
#   MOVIEBOT_ASSISTANT_TOKEN_FILE=/path/to/token suite.sh [--filter text] [--list] [--results dir]
#
# The harness answers web_search in the suite's conversations (suite-<case>-<run>) from the recordings in
# web-search/, so a run never spends the day's search budget. They are copied into the harness's replay
# directory first; the harness reads it on every search.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPLAY="${TKS_WEB_SEARCH_REPLAY_DIR:-/var/lib/tks-assistant/web-search-replay}"

mkdir -p "$REPLAY"
cp "$HERE"/web-search/*.json "$REPLAY/"

exec dotnet run --project "$HERE" -c Release --no-launch-profile -- "$@"
