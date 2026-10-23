#!/usr/bin/env bash
# Deploys Cinema's Android apps to hotbox. Run it from hotrod:
#
#   deploy/deploy-app.sh            # the phone app and the television app
#   deploy/deploy-app.sh phone      # one of them: phone or tv
#
# Each is built as a release here (phone/build.sh, tv/build.sh) and installed in the API's state
# directory, as phone.apk and tv.apk, where /phone.apk and /tv.apk serve it and no deploy of the
# services touches it. The API reads the file per request, so nothing is restarted. Each lands under
# a temporary name and is renamed, so a download in progress never reads half of one release and half
# of the next. The release keys live in ~/.config/krystal-cinema-phone/ and
# ~/.config/krystal-cinema-tv/ (phone/README.md, tv/README.md).
set -euo pipefail

host=hotbox
state=/var/lib/moviebot
api=http://127.0.0.1:8099

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

apps=("$@")
(( ${#apps[@]} )) || apps=(phone tv)

for app in "${apps[@]}"; do
    [[ "$app" == phone || "$app" == tv ]] || { echo "Unknown app $app: phone or tv." >&2; exit 2; }

    apk="$("$root/$app/build.sh" | tail -n 1 | cut -d' ' -f1)"
    [[ -f "$apk" ]] || { echo "The $app build named no APK." >&2; exit 1; }

    rsync "$apk" "$host:$state/.$app.apk.part"
    ssh "$host" "mv -f '$state/.$app.apk.part' '$state/$app.apk'"

    built="$(md5sum < "$apk")"
    served="$(ssh "$host" "curl -sf '$api/$app.apk' | md5sum")"
    if [[ "$built" != "$served" ]]; then
        echo "The API is not serving the $app app just installed at /$app.apk." >&2
        exit 1
    fi
    echo "$(basename "$apk") deployed to $host:$state/$app.apk and served at /$app.apk."
done
