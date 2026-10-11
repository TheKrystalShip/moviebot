// How quickly a phone's remote is live again after the page was away, measured headless against the
// scratch site (verify/mobile/scratch.sh, staged): from the moment the page is back to the first command
// the room accepts.
//
//   node verify/resume.mjs [--scenarios frozen-5,frozen-45,offline-5,offline-45,killed] [--film <id>]
//
// A television (a hub client in a room holding a film) is paired with a phone: a headless Chromium page of
// the site's origin running the site's own remote modules (verify/remoteProbe.js). Each scenario puts the
// phone's page away and brings it back, then asks for a command every 100 ms the way a person pressing
// the button again would, and reports:
//
//   live     the remote's own account of itself after the page is back: each change of whether it is connected
//            to the room, with the time it was first seen;
//   landed   until the first command the room accepted, with how many presses it took;
//   reached  until the television's own connection heard that command.
//
// A scenario named with a trailing "+wake" also has the page told it is visible again (a
// `visibilitychange`, which a headless page never raises by itself) the moment it is back, as unlocking a
// phone does; without it the page is left to notice by its own timers and sockets.
//
// The scenarios:
//   frozen-N   the page's script stopped for N seconds (CDP Debugger.pause, since a headless page is never
//              hidden and Page.setWebLifecycleState freezes nothing there), its network still running,
//              which is a frozen page as the connection sees it: no pings leave it, and nothing it is sent is read;
//   offline-N  the browser's network off for N seconds, then on;
//   killed     the TCP connections of the browser to the API destroyed by the kernel (ss -K), at once.
//
// What this cannot say is how a phone's operating system treats a page for two hours: whether it closes
// the socket, freezes the page or discards it. verify/resumePhone.mjs measures that on a real phone.

import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { chromium } from "playwright-core";
import { VIEWER } from "./mobile/stage.mjs";
import { TV_CLIENT, issuerIn } from "./issuer.mjs";
import { probeScript } from "./probeBundle.mjs";

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const at = args.indexOf("--" + name);
  return at >= 0 && args[at + 1] ? args[at + 1] : fallback;
};

const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
if (!existsSync(join(dir, "api.env"))) {
  console.error(`No scratch site in ${dir}. Start verify/mobile/scratch.sh first.`);
  process.exit(2);
}
const site = Object.fromEntries(readFileSync(join(dir, "api.env"), "utf8").split("\n").filter(Boolean)
  .map((line) => [line.slice(0, line.indexOf("=")), line.slice(line.indexOf("=") + 1)]));
const base = `http://127.0.0.1:${site.PORT}`;
const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
const scenarios = option("scenarios", "frozen-5,frozen-45,offline-5,offline-45,killed").split(",");
const film = option("film", process.env.MOVIEBOT_VERIFY_TV_FILM || "coherence-2013");
const stamp = Date.now().toString(36);
const room = `verify-resume-${stamp}`;
const tvSid = `verify-resume-tv-${stamp}`;
const phoneSid = `verify-resume-phone-${stamp}`;
const PhoneAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Mobile Safari/537.36";
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// The television: a hub client of the TV client's token, in a room holding the film.
const heard = [];
const tv = new HubConnectionBuilder()
  .withUrl(`${base}/hub/session`, { accessTokenFactory: () => issuer.mint(VIEWER, { client: TV_CLIENT, sid: tvSid, device: "Resume TV" }) })
  .configureLogging(LogLevel.Error).build();
tv.on("StateChanged", (push) => { heard.push({ at: performance.now(), revision: push.state.revision, paused: push.state.paused }); });
await tv.start();
await tv.invoke("Join", room);
await tv.invoke("LoadTitle", film, null);
const code = await tv.invoke("PairingCode");
const phoneToken = issuer.mint(VIEWER, { sid: phoneSid });
const paired = await fetch(base + "/api/me/screens", {
  method: "POST", headers: { Authorization: `Bearer ${phoneToken}`, "Content-Type": "application/json", "User-Agent": PhoneAgent },
  body: JSON.stringify({ code: code.path.slice("/p/".length) }),
});
if (!paired.ok) { console.error(`Pairing answered ${paired.status}`); process.exit(2); }

function chromePath() {
  if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
  const cache = join(homedir(), ".cache", "ms-playwright");
  for (const entry of readdirSync(cache).filter((d) => d.startsWith("chromium-")).sort().reverse()) {
    const candidate = join(cache, entry, "chrome-linux64", "chrome");
    if (existsSync(candidate)) return candidate;
  }
  throw new Error("No Chromium found. Set CHROME_PATH to a Chrome or Chromium binary.");
}

const browser = await chromium.launch({ executablePath: chromePath() });
const context = await browser.newContext({ userAgent: PhoneAgent, isMobile: true, hasTouch: true, bypassCSP: true });
await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER, { hours: 6, sid: phoneSid }));
const page = await context.newPage();
const cdp = await context.newCDPSession(page);
await page.goto(base + "/api/config", { waitUntil: "load" });
await page.addScriptTag({ content: await probeScript(), type: "module" });
await page.waitForFunction(() => window.__phone, null, { timeout: 10000 });
await page.evaluate(() => window.__phone.start());

const live = () => page.evaluate(() => window.__phone.room().live).catch(() => false);
const until = async (what, timeout) => {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    if (await what()) return true;
    await sleep(50);
  }
  return false;
};
if (!(await until(live, 10000))) { console.error("The remote did not come up."); process.exit(2); }

/** The ports the browser's sockets to the API use here, which are the only sockets of a chromium of mine to it. */
const browserPorts = () => execFileSync("ss", ["-tnpH", "state", "established", "dst", `127.0.0.1:${site.PORT}`], { encoding: "utf8" })
  .split("\n").filter((line) => /chrom/.test(line)).map((line) => line.trim().split(/\s+/)[2].split(":").pop());

const disruptions = {
  async frozen(seconds) {
    await cdp.send("Debugger.enable");
    await cdp.send("Debugger.pause");
    await sleep(seconds * 1000);
    await cdp.send("Debugger.resume");
  },
  async offline(seconds) {
    await context.setOffline(true);
    await sleep(seconds * 1000);
    await context.setOffline(false);
  },
  async killed() {
    const ports = browserPorts();
    console.log(`  killing ${ports.length} connection(s) of the browser`);
    for (const port of ports)
      execFileSync("sudo", ["-A", "ss", "-K", "state", "established", "dst", `127.0.0.1:${site.PORT}`, "sport", "=", `:${port}`], { stdio: "ignore", env: { ...process.env, SUDO_ASKPASS: join(homedir(), ".local/bin/tks-askpass") } });

  },
};

const rows = [];
for (const name of scenarios) {
  const [kind, seconds] = name.replace("+wake", "").split("-");
  await until(live, 20000);
  await sleep(500);
  const wasPaused = await page.evaluate(() => window.__phone.room().state.paused);
  await disruptions[kind](Number(seconds));
  const t0 = performance.now();
  if (name.endsWith("+wake")) await page.evaluate(() => document.dispatchEvent(new Event("visibilitychange")));

  const timeline = [];
  let seen = null;
  let landedAt = null;
  let presses = 0;
  let paused = wasPaused;
  let lastError = "";
  let revision = heard.length > 0 ? heard[heard.length - 1].revision : -1;
  while (performance.now() - t0 < 120_000 && landedAt === null) {
    const now = await live();
    if (now !== seen) timeline.push(`${now ? "live" : "not live"}@${Math.round(performance.now() - t0)}ms`);
    seen = now;
    presses++;
    const outcome = await page.evaluate(async (play) => {
      try { await (play ? window.__phone.commands.play() : window.__phone.commands.pause()); return null; } catch (error) { return error.message; }
    }, paused).catch((error) => error.message);
    if (outcome === null) landedAt = performance.now() - t0;
    else lastError = outcome;
    if (landedAt === null) await sleep(100);
  }
  await sleep(300);
  const reached = heard.find((h) => h.at > t0 && h.revision !== revision);
  paused = !paused;
  revision = null;
  rows.push({
    scenario: name,
    live: timeline.join(" "),
    landed: landedAt === null ? `never (${lastError})` : `${Math.round(landedAt)} ms`,
    presses,
    reached: reached ? `${Math.round(reached.at - t0)} ms` : "never",
  });
  console.log(rows[rows.length - 1]);
}

console.table(rows);
await browser.close();
await fetch(base + `/api/me/screens/${tvSid}`, { method: "DELETE", headers: { Authorization: `Bearer ${phoneToken}` } });
await tv.stop();
