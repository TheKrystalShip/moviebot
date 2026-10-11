// How quickly a real phone's remote is live again after the phone was away: the part of
// docs/remote-mode-plan.md, "Step 4", that headless Chromium cannot say. The phone's Chrome is reached
// over USB debugging; a tab on the site's origin holds the site's own remote modules (verify/remoteProbe.js)
// and, once the page is hidden and shown again, presses play or pause every 100 ms until the room accepts
// one, keeping what it saw. Nothing needs the laptop at the moment the phone comes back.
//
//   adb forward tcp:9222 localabstract:chrome_devtools_remote
//   node verify/resumePhone.mjs arm  [--cdp http://127.0.0.1:9222] [--origin https://movies.thekrystalship.com]
//   ... lock the phone and leave it, then unlock it, and leave Chrome on that tab ...
//   node verify/resumePhone.mjs read [--cdp http://127.0.0.1:9222] [--origin https://movies.thekrystalship.com]
//
// The phone's Chrome must be signed in to the site, paired with a television that is in a room, with the
// phone's own browser (not the installed app, whose storage is its own) holding the session. The press
// plays or pauses that television's film.

import { chromium } from "playwright-core";
import { probeScript } from "./probeBundle.mjs";

const [mode] = process.argv.slice(2).filter((arg) => !arg.startsWith("--"));
const option = (name, fallback) => {
  const at = process.argv.indexOf("--" + name);
  return at >= 0 && process.argv[at + 1] ? process.argv[at + 1] : fallback;
};
const cdp = option("cdp", "http://127.0.0.1:9222");
const origin = option("origin", "https://movies.thekrystalship.com");
const url = origin + "/api/config";

const browser = await chromium.connectOverCDP(cdp);
const context = browser.contexts()[0];
const probe = () => context.pages().find((page) => page.url() === url);

if (mode === "arm") {
  const page = probe() || (await context.newPage());
  await page.goto(url, { waitUntil: "load" });
  await page.addScriptTag({ content: await probeScript(), type: "module" });
  await page.waitForFunction(() => window.__phone, null, { timeout: 15000 });
  await page.evaluate(() => window.__phone.start());
  const live = await page.waitForFunction(() => window.__phone.room().live, null, { timeout: 15000 }).then(() => true, () => false);
  if (!live) {
    console.error("The remote did not come up: is this Chrome signed in, paired with a television that is in a room?");
    process.exit(1);
  }
  await page.evaluate(() => window.__phone.watchReturn());
  console.log(`Armed on ${url}. Put the phone away, bring it back, and leave Chrome on this tab; then run: read`);
} else if (mode === "read") {
  const page = probe();
  const result = page && (await page.evaluate(() => (window.__phone ? window.__phone.result : null)).catch(() => null));
  if (!result) console.log("The tab has no result: it was closed or reloaded while the phone was away, which is itself what happened.");
  else console.log(JSON.stringify(result, null, 2));
} else {
  console.error("Usage: resumePhone.mjs arm|read [--cdp <url>] [--origin <origin>]");
  process.exit(2);
}
await browser.close();
