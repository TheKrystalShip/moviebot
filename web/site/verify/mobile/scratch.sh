#!/usr/bin/env bash
# A scratch Cinema for the mobile harness: the site's production build served by a MovieBot
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
#   MOVIEBOT_VERIFY_DUB         the film given a second audio track, for the audio menu (coherence-2013)
#   MOVIEBOT_VERIFY_CATALOGUE   a catalogue directory, catalogue.db and images/, as moviebot-catalogue
#                               keeps it ($MOVIEBOT_VERIFY_DIR/catalogue); without one, no title has a
#                               card, Discover is empty and nothing can be wanted
#
# The API only reads the catalogue, so one copied from a host (sqlite3's VACUUM INTO, and its images/)
# serves every start unchanged, and what a page fetches stays what the TV performance baseline measured.
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

# The audio menu needs a film with a choice; the one the checks play is given a second track.
dub="${MOVIEBOT_VERIFY_DUB:-coherence-2013}"
for root in media cold; do
  [[ -d "$dir/library/$root/$dub" ]] && node "$here/secondAudio.mjs" "$dir/library/$root/$dub"
done

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

catalogue="${MOVIEBOT_VERIFY_CATALOGUE:-$dir/catalogue}"
[[ -f "$catalogue/catalogue.db" ]] || echo "No catalogue at $catalogue: no title has a card." >&2

echo "Scratch Cinema on http://127.0.0.1:$port: $i films, $cold_count of them on the cold root."
cd "$dir/api"
# The API refuses to start without a tracker category; the harness reaches no tracker.
env \
  STATE_DIRECTORY="$dir/state" \
  ASPNETCORE_WEBROOT="$site/dist" \
  Media__Root="$dir/library/media" \
  Media__ColdRoot="$dir/library/cold" \
  Subtitles__Root="$dir/state/subtitles" \
  Catalogue__Directory="$catalogue" \
  Auth__MediaTicketKey="$MEDIA_TICKET_KEY" \
  Auth__ServiceKey="$SERVICE_KEY" \
  Oidc__Issuer="http://127.0.0.1:$issuer_port" \
  Selection__AllowedCategories__0=1 \
  Kestrel__Endpoints__Http__Url="http://127.0.0.1:$port" \
  ./moviebot-api
