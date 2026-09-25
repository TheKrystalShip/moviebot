#!/usr/bin/env bash
# Installs or upgrades MovieBot from an extracted release.
#
#   sudo ./install.sh
#
# Safe to run again: binaries, service files and the nginx example are replaced, while the
# configuration in /etc/moviebot and everything under /srv/moviebot are kept. Services that are
# already running are restarted onto the new binaries.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

user=moviebot
prefix=/opt/moviebot
data=/srv/moviebot
etc=/etc/moviebot
env_file="$etc/moviebot.env"
services=(moviebot-api moviebot-bot moviebot-handoff)

say()  { printf '\033[1m==>\033[0m %s\n' "$*"; }
fail() { printf 'error: %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || fail "run this as root: sudo ./install.sh"
[[ "$(uname -m)" == x86_64 ]] || fail "this release is built for x86_64 Linux"
command -v systemctl >/dev/null || fail "systemd is required"
for dir in api bot handoff ingest packaging; do
    [[ -d "$here/$dir" ]] || fail "run this from the extracted release directory ($dir/ is missing)"
done

say "Service account '$user'"
if id "$user" >/dev/null 2>&1; then
    echo "    exists"
else
    useradd --system --home-dir "$data" --create-home --shell /usr/sbin/nologin "$user"
    echo "    created"
fi

say "Library, download and incoming directories under $data"
install -d -o "$user" -g "$user" -m 0755 "$data" "$data/media" "$data/downloads" "$data/incoming"

say "Programs in $prefix"
install -d -m 0755 "$prefix"
for dir in api bot handoff ingest; do
    rm -rf "${prefix:?}/$dir"
    cp -a "$here/$dir" "$prefix/$dir"
done
chown -R root:root "$prefix"
ln -sf "$prefix/ingest/moviebot-ingest" /usr/local/bin/moviebot-ingest

say "Configuration in $env_file"
install -d -m 0755 "$etc"
if [[ -f "$env_file" ]]; then
    echo "    kept (already present)"
else
    signing_key="$(head -c 48 /dev/urandom | base64 | tr -d '\n/+=')"
    service_key="$(head -c 48 /dev/urandom | base64 | tr -d '\n/+=')"
    sed -e "s|^Auth__SigningKey=.*|Auth__SigningKey=$signing_key|" \
        -e "s|^Auth__ServiceKey=.*|Auth__ServiceKey=$service_key|" \
        -e "s|^Api__ServiceKey=.*|Api__ServiceKey=$service_key|" \
        "$here/packaging/config/moviebot.env.example" > "$env_file"
    echo "    created, with fresh internal keys"
fi
chown root:"$user" "$env_file"
chmod 0640 "$env_file"

say "qBittorrent settings"
qb_conf="$data/.config/qBittorrent/qBittorrent.conf"
if [[ -f "$qb_conf" ]]; then
    echo "    kept (already present)"
else
    install -d -o "$user" -g "$user" -m 0755 "$data/.config" "$data/.config/qBittorrent"
    install -o "$user" -g "$user" -m 0644 "$here/packaging/qbittorrent/qBittorrent.conf" "$qb_conf"
    echo "    created"
fi

say "systemd services"
for service in "${services[@]}"; do
    install -m 0644 "$here/packaging/systemd/$service.service" /etc/systemd/system/
done
systemctl daemon-reload
for service in "${services[@]}"; do
    systemctl try-restart "$service"
done

say "nginx example"
install -d -m 0755 "$etc/examples"
install -m 0644 "$here/packaging/nginx/moviebot.conf" "$etc/examples/nginx-moviebot.conf"
echo "    $etc/examples/nginx-moviebot.conf"

echo
say "Installed $(cat "$here/VERSION")."
if ! grep -q '^MOVIEBOT_TOKEN=.' "$env_file"; then
    echo "    Next: fill in $env_file, then follow the deployment guide from there."
fi
