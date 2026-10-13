// The reference television over adb, reached and held awake for as long as a harness drives it.
//
// A panel that sleeps stops drawing and drops its adb link, so a run that outlasts the television's
// own timers measures nothing. `awake` connects (remaking a connection a sleep left dead), wakes the
// panel, and turns on stay-on and turns off the screensaver and the sleep timeout, without pressing
// any key a page would see. When the process exits, however it exits, it puts back the values it
// found. Those values are kept in <dir>/awake-<serial>.json until they are put back, so a run that
// was killed before restoring hands the television's own settings to the next one rather than its
// overrides.

import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const HELD = {
  "global stay_on_while_plugged_in": "7",
  "secure screensaver_enabled": "0",
  "secure sleep_timeout": "-1",
};

/** Connects to the television, wakes it and holds it awake until exit; returns its adb runner. */
export async function awake(serial, dir) {
  const adb = (...a) => execFileSync("adb", ["-s", serial, ...a], { encoding: "utf8" }).trim();
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const state = () => { try { return adb("get-state"); } catch { return "offline"; } };
  try { execFileSync("adb", ["connect", serial], { stdio: "ignore" }); } catch { /* answered below */ }
  if (state() !== "device") {
    // A television that slept keeps a dead connection until adb is asked to make it again.
    try { execFileSync("adb", ["reconnect", "offline"], { stdio: "ignore" }); } catch { /* answered below */ }
    await wait(3000);
  }
  if (state() !== "device") {
    console.error(`${serial} is not an authorized adb device: is the television on, and its debugging prompt accepted?`);
    process.exit(2);
  }

  const file = join(dir, `awake-${serial.replace(/[^\w.-]/g, "_")}.json`);
  const found = existsSync(file) ? JSON.parse(readFileSync(file, "utf8"))
    : Object.fromEntries(Object.keys(HELD).map((k) => [k, adb("shell", "settings", "get", ...k.split(" "))]));
  writeFileSync(file, JSON.stringify(found));
  for (const [k, v] of Object.entries(HELD)) adb("shell", "settings", "put", ...k.split(" "), v);

  let restored = false;
  const restore = () => {
    if (restored) return;
    restored = true;
    try {
      for (const [k, v] of Object.entries(found)) {
        // `settings get` answers "null" for a setting the television never had.
        if (v === "null") adb("shell", "settings", "delete", ...k.split(" "));
        else adb("shell", "settings", "put", ...k.split(" "), v);
      }
      rmSync(file, { force: true });
    } catch (e) {
      console.error(`The television's own settings were not put back (${e.message.split("\n")[0]}); the next run restores them from ${file}.`);
    }
  };
  process.on("exit", restore);
  for (const signal of ["SIGINT", "SIGTERM", "SIGHUP"]) process.once(signal, () => process.exit(130));

  if (!/mWakefulness=Awake/.test(adb("shell", "dumpsys", "power"))) {
    adb("shell", "input", "keyevent", "KEYCODE_WAKEUP");
    for (let i = 0; i < 20 && !/mWakefulness=Awake/.test(adb("shell", "dumpsys", "power")); i++) await wait(250);
    if (!/mWakefulness=Awake/.test(adb("shell", "dumpsys", "power"))) {
      console.error(`${serial} answered adb but did not wake.`);
      process.exit(2);
    }
    // The panel lights a moment after the system reports it awake.
    await wait(2000);
  }
  return adb;
}
