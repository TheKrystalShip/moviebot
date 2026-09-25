#!/usr/bin/env bash
# Builds the release archive: every service published for linux-x64, the player inside the API,
# and the packaging kit (install.sh, systemd units, configuration examples, the deployment guide).
#
#   scripts/package-release.sh [output-dir]
#
# The version is the newest entry in CHANGELOG.md. Needs the .NET 10 SDK, node and npm, clang
# for the native builds, and moviebot-acquire checked out beside this repository.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$(realpath -m "${1:-$root/artifacts}")"
version="$("$root/scripts/version.sh")"
# The archive's name carries no version, so the newest release is always at the same address;
# the directory inside it and the VERSION file say which one it is.
archive="moviebot-linux-x64.tar.gz"
name="moviebot-$version"
stage="$out/$name"

[[ -d "$root/../moviebot-acquire/src/MovieBot.Acquire" ]] \
    || { echo "moviebot-acquire must be checked out beside this repository" >&2; exit 1; }

rm -rf "${stage:?}" "${out:?}/$archive" "$out/$archive.sha256"
mkdir -p "$stage"

"$root/scripts/build-player.sh"

publish() {
    dotnet publish "$root/src/$1" -c Release -r linux-x64 --nologo -o "$stage/$2" "${@:3}"
}
publish MovieBot.Api     api
publish MovieBot.Handoff handoff
publish MovieBot.Bot     bot    --self-contained
publish MovieBot.Ingest  ingest --self-contained -p:PublishSingleFile=true

# Debug symbols are for the build machine, not for a deployment.
find "$stage" -name '*.dbg' -delete -o -name '*.pdb' -delete

mkdir -p "$stage/packaging"
cp -r "$root/packaging/systemd" "$root/packaging/config" "$root/packaging/nginx" \
      "$root/packaging/qbittorrent" "$stage/packaging/"
cp "$root/packaging/install.sh" "$stage/install.sh"
cp "$root/docs/deploying.md" "$stage/DEPLOYING.md"
cp "$root/LICENSE" "$root/README.md" "$stage/"
printf '%s\n' "$version" > "$stage/VERSION"

tar -C "$out" -czf "$out/$archive" "$name"
(cd "$out" && sha256sum "$archive" > "$archive.sha256")
echo "$out/$archive"
