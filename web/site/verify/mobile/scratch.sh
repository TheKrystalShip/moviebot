#!/usr/bin/env bash
# A scratch Krystal Cinema for the mobile harness: the site's production build served by a MovieBot
# API of its own, over a library of its own, so nothing the harness does reaches a library people use.
#
# The library is the checkout's media/ hard-linked into two roots — most films on the media root, the
# first few on a cold root, the way a host with both disks holds them. A hard link is free and the API
# replaces a manifest rather than editing it, so the source library is never written to. The state
# directory is made fresh on every start.
#
# Runs in the foreground; stop it with Ctrl-C. run.mjs reads the keys this writes.
#
#   ./scratch.sh             build the site and the API, then serve
#   ./scratch.sh --no-build  serve what was built last
#
# Settings (environment):
#   MOVIEBOT_VERIFY_DIR         where everything goes          (~/.cache/moviebot-verify)
#   MOVIEBOT_VERIFY_MEDIA       the library to link from       (<repo>/media); same filesystem as the dir
#   MOVIEBOT_VERIFY_COLD        films put on the cold root     (5)
#   MOVIEBOT_VERIFY_PORT        the API's port                 (8299)
#   MOVIEBOT_VERIFY_TMDB_TOKEN_FILE  a file holding a TMDB read token; without one, films carry no
#                                    ratings, genres or backdrops
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
site="$(cd "$here/../.." && pwd)"
repo="$(cd "$site/../.." && pwd)"
dir="${MOVIEBOT_VERIFY_DIR:-$HOME/.cache/moviebot-verify}"
source_media="${MOVIEBOT_VERIFY_MEDIA:-$repo/media}"
cold_count="${MOVIEBOT_VERIFY_COLD:-5}"
port="${MOVIEBOT_VERIFY_PORT:-8299}"

mkdir -p "$dir"

if [[ "${1:-}" != "--no-build" ]]; then
  (cd "$site" && npm run build --silent)
  dotnet build "$repo/src/MovieBot.Api" -c Release -o "$dir/api" -v quiet -nologo
fi
[[ -x "$dir/api/moviebot-api" ]] || { echo "No API build in $dir/api; run without --no-build." >&2; exit 1; }
[[ -f "$site/dist/index.html" ]] || { echo "No site build in $site/dist; run without --no-build." >&2; exit 1; }

# The library: hard links, so a film costs nothing and the source is never touched.
rm -rf "$dir/library"
mkdir -p "$dir/library/media" "$dir/library/cold"
touch "$dir/library/cold/.moviebot-cold"
i=0
for film in "$source_media"/*/; do
  [[ -f "$film/manifest.json" ]] || continue
  name="$(basename "$film")"
  if (( i < cold_count )); then target="$dir/library/cold"; else target="$dir/library/media"; fi
  if ! cp -al "$film" "$target/$name" 2>/dev/null; then
    echo "Cannot hard-link $film into $dir: they must be on the same filesystem (set MOVIEBOT_VERIFY_DIR)." >&2
    exit 1
  fi
  i=$((i + 1))
done
(( i > 0 )) || { echo "No films under $source_media." >&2; exit 1; }

# A fresh state directory.
rm -rf "$dir/state"
mkdir -p "$dir/state/subtitles"

# Keys of the scratch API's own, made once and kept beside it for run.mjs.
if [[ ! -f "$dir/keys.env" ]] || ! grep -q '^MEDIA_TICKET_KEY=' "$dir/keys.env"; then
  umask 077
  {
    echo "MEDIA_TICKET_KEY=$(head -c 32 /dev/urandom | base64 | tr -d '\n')"
    echo "SERVICE_KEY=$(head -c 24 /dev/urandom | base64 | tr -d '\n=+/')"
  } > "$dir/keys.env"
fi
# shellcheck disable=SC1091
source "$dir/keys.env"
issuer_port=$((port + 1))
printf 'PORT=%s\nISSUER_PORT=%s\n' "$port" "$issuer_port" > "$dir/api.env"

# The identity provider the scratch API trusts, standing in for tks-auth; the checks mint tokens with
# its key (verify/issuer.mjs). It goes when the API does.
node "$site/verify/issuer.mjs" serve "$dir" "$issuer_port" &
issuer_pid=$!
trap 'kill "$issuer_pid" 2>/dev/null' EXIT

tmdb=()
if [[ -n "${MOVIEBOT_VERIFY_TMDB_TOKEN_FILE:-}" ]]; then
  tmdb=("Tmdb__ReadAccessToken=$(cat "$MOVIEBOT_VERIFY_TMDB_TOKEN_FILE")")
fi

echo "Scratch Krystal Cinema on http://127.0.0.1:$port: $i films, $cold_count of them on the cold root."
cd "$dir/api"
# The API refuses to start without a tracker category; the harness reaches no tracker.
env \
  STATE_DIRECTORY="$dir/state" \
  ASPNETCORE_WEBROOT="$site/dist" \
  Media__Root="$dir/library/media" \
  Media__ColdRoot="$dir/library/cold" \
  Subtitles__Root="$dir/state/subtitles" \
  Tmdb__Directory="$dir/films" \
  Auth__MediaTicketKey="$MEDIA_TICKET_KEY" \
  Auth__ServiceKey="$SERVICE_KEY" \
  Oidc__Issuer="http://127.0.0.1:$issuer_port" \
  Selection__AllowedCategories__0=1 \
  Kestrel__Endpoints__Http__Url="http://127.0.0.1:$port" \
  "${tmdb[@]}" \
  ./moviebot-api
