#!/usr/bin/env bash
# Deploys MovieBot's four services to hotbox. Run it from hotrod, with no arguments:
#
#   deploy/deploy-services.sh
#
# It publishes the API, the bot, the hand-off and the assistant here, copies them to /opt/moviebot,
# installs hotbox's settings file, and restarts the units. A restart is safe while people are
# watching: rooms come back as they were and the player reconnects through the gap. The hand-off is
# the exception: a restart starts every transcode it is running again from the beginning, so while
# one runs, its restart waits on hotbox until the last has ended. The site is not part of this
# (deploy/deploy-site.sh); it is installed after the restart only when hotbox has none.
set -euo pipefail

host=hotbox
opt=/opt/moviebot
api=http://127.0.0.1:8099
units=(moviebot-api moviebot-bot moviebot-handoff moviebot-assistant)
handoff=moviebot-handoff
waiter=moviebot-handoff-restart

# Whether the hand-off is making anything: its ffmpeg and fpcalc run inside its unit's cgroup.
busy="for p in \$(cat /sys/fs/cgroup/system.slice/$handoff.service/cgroup.procs 2>/dev/null); do
          case \$(cat /proc/\$p/comm 2>/dev/null) in ffmpeg|fpcalc) exit 0 ;; esac
      done; exit 1"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

[[ -d "$root/../moviebot-acquire/src/MovieBot.Acquire" ]] \
    || { echo "moviebot-acquire must be checked out beside this repository" >&2; exit 1; }

out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

dotnet publish "$root/src/MovieBot.Api"       -c Release --nologo -o "$out/api"
dotnet publish "$root/src/MovieBot.Bot"       -c Release --nologo -o "$out/bot"
dotnet publish "$root/src/MovieBot.Handoff"   -c Release --nologo -o "$out/handoff"
dotnet publish "$root/src/MovieBot.Assistant" -c Release --nologo -r linux-x64 --self-contained false -o "$out/assistant"

# --delete keeps a native binary from running beside the assemblies of an older build. The API's
# web root is the site's, which deploy/deploy-site.sh installs: excluded, it is neither copied from
# this publish nor deleted on hotbox.
for service in api bot handoff assistant; do
    rsync -a --delete --exclude=/wwwroot/ "$out/$service/" "$host:$opt/$service/"
done

ssh "$host" 'mkdir -p ~/.config/moviebot'
rsync "$root/deploy/hotbox.settings.json" "$host:.config/moviebot/moviebot.settings.json"

# The API serves its web root only when that exists as it starts.
ssh "$host" "mkdir -p '$opt/api/wwwroot'"

# A restart still waiting from an earlier deploy is replaced by this one's.
rsudo "$host" systemctl stop "$waiter" </dev/null 2>/dev/null || true
if ssh "$host" "$busy"; then
    now=()
    for unit in "${units[@]}"; do [[ "$unit" == "$handoff" ]] || now+=("$unit"); done
    rsudo "$host" systemctl restart "${now[@]}" </dev/null
    rsudo "$host" systemd-run --unit="$waiter" --collect \
        --description="Restarts the MovieBot hand-off once it is making nothing" \
        /bin/sh -c "while ( $busy ); do sleep 30; done; systemctl restart $handoff" </dev/null
    echo "$handoff is transcoding: it restarts on hotbox once that ends ($waiter)."
else
    rsudo "$host" systemctl restart "${units[@]}" </dev/null
fi

healthy=""
for _ in $(seq 60); do
    if healthy="$(ssh "$host" "curl -sf '$api/health'")"; then break; fi
    healthy=""
    sleep 1
done
if [[ -z "$healthy" ]]; then
    echo "The API did not answer /health within a minute of the restart:" >&2
    ssh "$host" "journalctl -u moviebot-api -n 30 --no-pager -o cat" >&2
    exit 1
fi
ssh "$host" "systemctl is-active ${units[*]}" | paste -sd' ' | sed "s/^/${units[*]}: /"
echo "API healthy: $healthy"

if ! ssh "$host" "test -f '$opt/api/wwwroot/index.html'"; then
    "$root/deploy/deploy-site.sh"
fi
