#!/usr/bin/env bash
# Keeps a television awake while it is measured, then puts its own settings back.
#
#   verify/tvsweep/awake.sh <host:port> [hours]     (3 hours unless given)
#
# A panel that sleeps stops drawing, and a sweep step taken then has no frames. This turns on stay-on
# and turns off the screensaver and the sleep timeout, without pressing anything, so no key lands in a
# measurement; at the end, or when stopped, it restores the values it read first. Run it in the
# background and stop it by its pid.
set -u
serial="${1:?the television, host:port}"
hours="${2:-3}"
a() { adb -s "$serial" shell "$@"; }
adb connect "$serial" >/dev/null 2>&1
stay="$(a settings get global stay_on_while_plugged_in)"
saver="$(a settings get secure screensaver_enabled)"
sleep_after="$(a settings get secure sleep_timeout)"
restore() {
  adb connect "$serial" >/dev/null 2>&1
  a settings put global stay_on_while_plugged_in "$stay"
  a settings put secure screensaver_enabled "$saver"
  a settings put secure sleep_timeout "$sleep_after"
  echo "$(date -Is) restored stay_on=$stay screensaver=$saver sleep_timeout=$sleep_after"
}
trap restore EXIT
a settings put global stay_on_while_plugged_in 7
a settings put secure screensaver_enabled 0
a settings put secure sleep_timeout -1
a input keyevent KEYCODE_WAKEUP
echo "$(date -Is) awake for ${hours}h (was stay_on=$stay screensaver=$saver sleep_timeout=$sleep_after)"
end=$(( $(date +%s) + hours * 3600 ))
# The settings hold it awake; this only reconnects adb if the link drops, so the restore reaches it.
while (( $(date +%s) < end )); do
  sleep 300
  adb connect "$serial" >/dev/null 2>&1
done
