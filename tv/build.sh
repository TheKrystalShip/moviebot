#!/usr/bin/env bash
# Builds Cinema for Android TV and Fire TV with the SDK's own tools: javac, d8, aapt2,
# zipalign and apksigner. No Gradle.
#
#   tv/build.sh [--debug] [--origin https://host] [--install] [--open [url]]
#
#   --debug     remote debugging on, plain http to the development machine's loopback, the address
#               taken from the launch's `url` extra; installs beside the release build as
#               com.thekrystalship.cinema.debug and is signed with a debug key made on first use
#   --origin    the site the app shows (https://movies.thekrystalship.com); a release build takes https only
#   --install   installs on the device `adb` reaches ($ANDROID_SERIAL picks one of several)
#   --open      launches it, a debug build on `url` when one is given
#
# The version is the newest heading in tv/CHANGELOG.md, and its versionCode major*10000 + minor*100 +
# patch. A release is signed with the key in ~/.config/krystal-cinema-tv/ (release.keystore, its
# password in release.password), which stays off every repository: Android updates an app only under
# the key it was installed with. The APK lands in tv/out/.
#
# The toolchain: JAVA_HOME (~/Android/jdk) and ANDROID_HOME (~/Android/Sdk, with build-tools;35.0.0,
# platforms;android-35 and platform-tools).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export JAVA_HOME="${JAVA_HOME:-$HOME/Android/jdk}"
sdk="${ANDROID_HOME:-$HOME/Android/Sdk}"
tools="$sdk/build-tools/35.0.0"
platform="$sdk/platforms/android-35/android.jar"
adb="$sdk/platform-tools/adb"
keys="$HOME/.config/krystal-cinema-tv"

debug=false origin="https://movies.thekrystalship.com" install=false open=false url=""
while (( $# )); do
  case "$1" in
    --debug) debug=true ;;
    --origin) origin="${2:?--origin needs an address}"; shift ;;
    --install) install=true ;;
    --open) open=true; if [[ "${2:-}" != --* && -n "${2:-}" ]]; then url="$2"; shift; fi ;;
    *) echo "Unknown option $1" >&2; exit 2 ;;
  esac
  shift
done

[[ -x "$JAVA_HOME/bin/javac" && -x "$tools/d8" && -f "$platform" ]] \
  || { echo "No Android toolchain: JAVA_HOME=$JAVA_HOME, ANDROID_HOME=$sdk" >&2; exit 1; }
origin="${origin%/}"
[[ "$origin" =~ ^https?://[A-Za-z0-9.-]+(:[0-9]+)?$ ]] || { echo "--origin is a scheme and a host, nothing more: $origin" >&2; exit 2; }
if ! $debug && [[ "$origin" != https://* ]]; then echo "A release build shows an https site." >&2; exit 2; fi

version="$(sed -n 's/^## \[\([0-9][^]]*\)\].*/\1/p' "$here/CHANGELOG.md" | head -n 1)"
IFS=. read -r major minor patch <<< "${version%%-*}"
code=$(( major * 10000 + minor * 100 + patch ))

kind=$($debug && echo debug || echo release)
out="$here/out/$kind"
rm -rf "$out"
mkdir -p "$out/gen/com/thekrystalship/cinema" "$out/classes" "$out/dex"

cat > "$out/gen/com/thekrystalship/cinema/Built.java" <<EOF
package com.thekrystalship.cinema;

/** What this build was made with, by tv/build.sh. */
final class Built {
    static final String ORIGIN = "$origin";
    static final String VERSION = "$version";
    static final boolean DEBUG = $debug;

    private Built() {}
}
EOF

"$JAVA_HOME/bin/javac" -source 8 -target 8 -nowarn -Xlint:-options -bootclasspath "$platform" \
  -d "$out/classes" "$here"/src/com/thekrystalship/cinema/*.java "$out"/gen/com/thekrystalship/cinema/*.java
"$tools/d8" --min-api 25 --lib "$platform" $($debug || echo --release) --output "$out/dex" \
  $(find "$out/classes" -name '*.class')

"$tools/aapt2" compile --dir "$here/res" -o "$out/res.zip"
link=(--manifest "$here/AndroidManifest.xml" -I "$platform" -A "$here/assets"
  --version-code "$code" --version-name "$version" "$out/res.zip")
if $debug; then
  "$tools/aapt2" compile --dir "$here/debug/res" -o "$out/debug-res.zip"
  link+=(-R "$out/debug-res.zip" --auto-add-overlay --debug-mode
    --rename-manifest-package com.thekrystalship.cinema.debug)
fi
"$tools/aapt2" link -o "$out/base.apk" "${link[@]}"
(cd "$out/dex" && zip -q "$out/base.apk" classes.dex)
"$tools/zipalign" -f 4 "$out/base.apk" "$out/aligned.apk"

if $debug; then
  store="$keys/debug.keystore" pass="pass:android"
  if [[ ! -f "$store" ]]; then
    mkdir -p "$keys" && chmod 700 "$keys"
    "$JAVA_HOME/bin/keytool" -genkeypair -keystore "$store" -storepass android -keypass android \
      -alias debug -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=Cinema Debug" >/dev/null 2>&1
  fi
  apk="$here/out/krystal-cinema-tv-$version-debug.apk"
else
  store="$keys/release.keystore" pass="file:$keys/release.password"
  [[ -f "$store" && -f "$keys/release.password" ]] || {
    echo "No release key in $keys. Make it once, and keep a copy somewhere safe:" >&2
    echo "  mkdir -p $keys && chmod 700 $keys && umask 077 && head -c 24 /dev/urandom | base64 > $keys/release.password" >&2
    echo "  keytool -genkeypair -keystore $keys/release.keystore -storepass:file $keys/release.password \\" >&2
    echo "    -alias release -keyalg RSA -keysize 4096 -validity 36500 -dname \"CN=Cinema\"" >&2
    exit 1
  }
  apk="$here/out/krystal-cinema-tv-$version.apk"
fi
"$tools/apksigner" sign --ks "$store" --ks-pass "$pass" --out "$apk" "$out/aligned.apk"
rm -f "$apk.idsig"
echo "$apk ($kind $version, code $code, $origin)"

package="com.thekrystalship.cinema$($debug && echo .debug || true)"
if $install; then "$adb" install -r "$apk" >/dev/null && echo "installed $package"; fi
if $open; then
  "$adb" shell am force-stop "$package"
  extra=(); [[ -n "$url" ]] && extra=(--es url "$url")
  "$adb" shell am start -n "$package/com.thekrystalship.cinema.CinemaActivity" "${extra[@]}" >/dev/null
  echo "opened $package"
fi
