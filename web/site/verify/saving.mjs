// Download, end to end in a real Chromium as a landscape tablet (a finger for a pointer, 1280 wide): a
// computer offered no Download on a film's page nor a Download quality; then a title downloaded
// through its page's sheet, the page closed mid-download and the download carried on by the next page
// from what was on disk (by `Range`, with `If-Range`), pausing and resuming from the sheet, every file
// on the device byte for byte the server's (each track the concatenation of its playlist's files on
// disk), the API told the save is complete, a device without room refused before a byte moves, and a
// save the API no longer lists removed from the device when the connection comes back.
//
//   node verify/saving.mjs
//
// Needs the scratch site verify/mobile/scratch.sh serves, over a library holding a title whose
// playlists name only files it has (`MOVIEBOT_VERIFY_SAVE_TITLE`, by default the first such title,
// `clip` first): a library hard-linked from a checkout's `media/` that holds only the first segments of
// each film offers nothing to save. The page reaches the scratch API through a proxy of this check's
// own that holds each track to `MOVIEBOT_VERIFY_SAVE_RATE` bytes a second (1 MB), so a save lasts
// long enough to be interrupted. Settings (environment): MOVIEBOT_VERIFY_DIR as scratch.sh; CHROME_PATH
// a Chromium. Screenshots are left in <dir>/runs/saving-<time>/. Exits 1 when any check fails, 2 when
// the scratch library has nothing to save.

import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, readdirSync } from "node:fs";
import { createServer, request } from "node:http";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { VIEWER, memberToken } from "./mobile/stage.mjs";
import { issuerIn } from "./issuer.mjs";

const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
if (!existsSync(join(dir, "api.env"))) {
  console.error(`No scratch site in ${dir}. Start verify/mobile/scratch.sh first.`);
  process.exit(2);
}
const readEnv = (file) => Object.fromEntries(
  readFileSync(join(dir, file), "utf8").split("\n").filter(Boolean).map((line) => [line.slice(0, line.indexOf("=")), line.slice(line.indexOf("=") + 1)]));
const site = readEnv("api.env");
const api = `http://127.0.0.1:${site.PORT}`;
const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
const token = memberToken(issuer, VIEWER);
const RATE = Number(process.env.MOVIEBOT_VERIFY_SAVE_RATE || 1_000_000);
const shots = join(dir, "runs", "saving-" + new Date().toISOString().replace(/[:.]/g, "-"));
mkdirSync(shots, { recursive: true });

function chromePath() {
  if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
  const cache = join(homedir(), ".cache", "ms-playwright");
  for (const entry of readdirSync(cache).filter((d) => d.startsWith("chromium-")).sort().reverse()) {
    const candidate = join(cache, entry, "chrome-linux64", "chrome");
    if (existsSync(candidate)) return candidate;
  }
  throw new Error("No Chromium found. Set CHROME_PATH to a Chrome or Chromium binary.");
}

const call = async (method, path, body) => {
  const headers = { Authorization: `Bearer ${token}` };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const response = await fetch(api + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: response.status, body: response.status === 204 ? null : await response.json().catch(() => null) };
};

let failed = 0;
const check = (ok, line) => { if (!ok) failed++; console.log(`${ok ? "✓" : "✗"} ${line}`); };

// ── The title to save ────────────────────────────────────────────────────────────────────────────

/** A title's directory in the scratch library, on either root. */
const titleDir = (id) => [join(dir, "library", "media", id), join(dir, "library", "cold", id)].find((d) => existsSync(d));

/** A rendition's track as the API joins it: its playlist's init segment and every segment, in order. */
function concatenation(id, rendition) {
  const root = join(titleDir(id), rendition);
  const playlist = readFileSync(join(root, "index.m3u8"), "utf8").split("\n").map((l) => l.trim()).filter(Boolean);
  const init = playlist.map((l) => /^#EXT-X-MAP:URI="([^"]+)"/.exec(l)).find(Boolean);
  const files = [...(init ? [init[1]] : []), ...playlist.filter((l) => !l.startsWith("#"))];
  const hash = createHash("sha256");
  let bytes = 0;
  for (const file of files) {
    const data = readFileSync(join(root, file));
    hash.update(data);
    bytes += data.length;
  }
  return { sha: hash.digest("hex"), bytes };
}

const titles = (await call("GET", "/api/titles")).body || [];
const wanted = process.env.MOVIEBOT_VERIFY_SAVE_TITLE;
const candidates = wanted ? [wanted] : ["clip", ...titles.filter((t) => t.status === "ready" && !t.episode).map((t) => t.id)];
let TITLE = null;
let OFFER = null;
for (const id of candidates) {
  const offer = await call("GET", `/api/titles/${encodeURIComponent(id)}/offline`);
  if (offer.status === 200 && offer.body.rungs.length > 0 && offer.body.audio.length > 0) {
    TITLE = id;
    OFFER = offer.body;
    break;
  }
}
if (!TITLE) {
  console.error("The scratch library holds no title whose files are all there to save. Link a whole one into it (MOVIEBOT_VERIFY_MEDIA).");
  process.exit(2);
}
console.log(`Saving ${TITLE}: ${OFFER.rungs.map((r) => `${r.rendition} ${r.bytes}`).join(", ")}`);

// ── A proxy holding each track to RATE ───────────────────────────────────────────────────────────

const transfers = [];
const proxy = createServer((req, res) => {
  const track = /\/track\.mp4$/.test(req.url.split("?")[0]);
  // Each track request is recorded as it arrives, and its status once the API answers.
  const seen = track && req.method === "GET"
    ? { url: req.url, range: req.headers.range || null, ifRange: req.headers["if-range"] || null, status: null,
      bearer: String(req.headers.authorization || "").slice(-16) }
    : null;
  if (seen) transfers.push(seen);
  const up = request({ host: "127.0.0.1", port: Number(site.PORT), path: req.url, method: req.method, headers: req.headers }, (answer) => {
    if (seen) seen.status = answer.statusCode;
    res.writeHead(answer.statusCode, answer.headers);
    if (!track) {
      answer.pipe(res);
      return;
    }
    answer.on("data", (chunk) => {
      res.write(chunk);
      answer.pause();
      setTimeout(() => answer.resume(), (chunk.length / RATE) * 1000);
    });
    answer.on("end", () => res.end());
  });
  up.on("error", () => res.destroy());
  res.on("close", () => up.destroy());
  req.pipe(up);
});
await new Promise((resolve) => proxy.listen(0, "127.0.0.1", resolve));
const base = `http://127.0.0.1:${proxy.address().port}`;

// ── The device ───────────────────────────────────────────────────────────────────────────────────

const name = (s) => encodeURIComponent(String(s)).replace(/\./g, "%2E");

/** The save on the device, read from the page: its meta.json without the package, and each file's length. */
const onDevice = (page) => page.evaluate(async ({ account, title }) => {
  try {
    const root = await navigator.storage.getDirectory();
    const dir = await (await root.getDirectoryHandle(account)).getDirectoryHandle(title);
    const meta = JSON.parse(await (await (await dir.getFileHandle("meta.json")).getFile()).text());
    const sizes = {};
    for (const f of meta.files) {
      try { sizes[f.name] = (await (await dir.getFileHandle(f.name)).getFile()).size; } catch { sizes[f.name] = -1; }
    }
    delete meta.package;
    return { meta, sizes };
  } catch {
    return null;
  }
}, { account: name(VIEWER.id), title: name(TITLE) });

/** A file of the save, hashed in the page. */
const hashOnDevice = (page, file) => page.evaluate(async ({ account, title, file }) => {
  const root = await navigator.storage.getDirectory();
  const dir = await (await root.getDirectoryHandle(account)).getDirectoryHandle(title);
  const data = await (await (await dir.getFileHandle(file)).getFile()).arrayBuffer();
  return [...new Uint8Array(await crypto.subtle.digest("SHA-256", data))].map((b) => b.toString(16).padStart(2, "0")).join("");
}, { account: name(VIEWER.id), title: name(TITLE), file });

const received = (state) => (state ? Object.values(state.sizes).reduce((n, s) => n + Math.max(0, s), 0) : 0);

async function waitFor(fn, timeout, every = 250) {
  const until = Date.now() + timeout;
  for (;;) {
    const value = await fn();
    if (value || Date.now() > until) return value;
    await new Promise((r) => setTimeout(r, every));
  }
}

const browser = await chromium.launch({ executablePath: chromePath() });

async function tab(context) {
  const page = await context.newPage();
  page.errors = [];
  page.on("pageerror", (e) => page.errors.push(e.message));
  return page;
}

// Signed in, the worker installed and in control, nothing saved.
for (const record of (await call("GET", "/api/me/offline")).body || []) await call("DELETE", `/api/me/offline/${record.id}`);
const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, hasTouch: true });
{
  const page = await context.newPage();
  await page.goto(base + "/manifest.webmanifest");
  const planted = issuer.planted(VIEWER, { refresh: "verify-saving-refresh" });
  await page.evaluate(({ key, value }) => localStorage.setItem(key, value), planted);
  await page.goto(base + "/", { waitUntil: "load" });
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.close();
}

// A computer is offered no Download: a film's page has Watch and nothing to download it with, and
// Playback has no Download quality.
{
  const computer = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const p = await computer.newPage();
  await p.goto(base + "/manifest.webmanifest");
  await p.evaluate(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER));
  await p.goto(base + `/movies/${TITLE}`, { waitUntil: "load" });
  await p.locator(".kc-tt .kc-actions .k-btn--primary").first().waitFor({ timeout: 15000 });
  await p.waitForTimeout(1500);
  const offered = await p.locator(".kc-tt .kc-actions button", { hasText: /^(Download|Queued|Paused|Waiting|Not downloaded)/ }).count();
  check(offered === 0, `a computer's film page offers no Download (${offered} found)`);
  await p.goto(base + "/profile/playback", { waitUntil: "load" });
  await p.locator(".kc-heights").first().waitFor({ timeout: 15000 });
  const quality = await p.getByText("Download quality").count();
  check(quality === 0, "a computer's Playback has no Download quality");
  await computer.close();
}

// The picker, and the save started from it.
let page = await tab(context);
await page.goto(base + `/movies/${TITLE}`, { waitUntil: "load" });
// The control's face changes with the download, so it is found by every face it has.
const FACES = /^(Download|Queued|Paused|Waiting|Not downloaded)/;
const filmButton = (p) => p.locator(".kc-tt .kc-actions button", { hasText: FACES });
const button = filmButton(page);
await button.waitFor({ timeout: 15000 });
check(true, "a film's page offers Download");
await button.click();
const sheet = page.locator(".kc-dl-pick");
await sheet.waitFor({ timeout: 10000 });
await sheet.locator(".k-optlist").first().waitFor({ timeout: 10000 });
const picker = await page.evaluate(() => {
  const [quality, audio] = document.querySelectorAll(".kc-dl-pick .k-optlist");
  const rows = (list) => [...list.querySelectorAll(".k-optlist__row")];
  return {
    options: rows(quality).map((r) => [r.querySelector("input").value, r.querySelector(".k-optlist__aside")?.textContent || ""]),
    chosen: rows(quality).map((r) => r.querySelector("input")).find((i) => i.checked)?.value,
    audio: rows(audio).filter((r) => r.querySelector("input").checked).map((r) => r.textContent),
    space: document.querySelector(".kc-dl-pick .k-meter__caption")?.textContent,
    start: document.querySelector(".kc-dl-pick .kc-dl-start")?.textContent,
  };
});
check(picker.options.length === OFFER.rungs.length && picker.options.every(([, size]) => /^[\d.]+ [KMG]?B$/.test(size)),
  `the sheet lists every rung with its size (${picker.options.map(([v, s]) => `${v} ${s}`).join("; ")})`);
const smallest = OFFER.rungs[OFFER.rungs.length - 1].rendition;
check(picker.chosen === smallest, `a tablet's default is the smallest (${picker.chosen}, wanted ${smallest})`);
check(picker.audio.length >= 1, `the default audio is picked (${picker.audio.join(", ")})`);
check(/free/.test(picker.space || "") && /^Download · [\d.]+ [KMG]?B$/.test(picker.start || ""),
  `the sheet says what is free and what the download takes (${picker.space}; ${picker.start})`);
await page.screenshot({ path: join(shots, "picker.png") });

await sheet.locator(".kc-dl-start").click();
await sheet.waitFor({ state: "detached", timeout: 15000 });
const label = await waitFor(async () => {
  const text = await button.textContent().catch(() => "");
  return /Downloading · \d+%/.test(text) ? text : null;
}, 15000);
check(!!label, `the button shows the download's progress (${label})`);

// Part way through, the page goes, and the worker with it. A file being written reads as empty from
// the page until its writer lets go, so how far the download has got is read from the control.
await waitFor(async () => {
  const match = /Downloading · (\d+)%/.exec(await button.textContent().catch(() => ""));
  return match && Number(match[1]) >= 25;
}, 60000);
const partway = await onDevice(page);
check(!!partway && partway.meta.completedAt === null && partway.meta.saveId,
  `meta.json is written first, listing every file, not complete (${partway && partway.meta.files.map((f) => f.name).join(", ")})`);
await page.screenshot({ path: join(shots, "saving.png") });
await page.close();
const left = await (async () => {
  const peek = await tab(context);
  await peek.goto(base + "/manifest.webmanifest");
  const state = await onDevice(peek);
  await peek.close();
  return state;
})();
check(!!left && received(left) > 0 && received(left) < left.meta.bytes,
  `closing the page mid-save leaves part of it on the device (${received(left)} of ${left && left.meta.bytes})`);

// The next page carries the download on from what is on disk, and its sheet shows it and pauses it.
page = await tab(context);
await page.goto(base + `/movies/${TITLE}`, { waitUntil: "load" });
const control = filmButton(page);
await control.waitFor({ timeout: 15000 });
const status = await waitFor(async () => {
  const text = await control.textContent().catch(() => "");
  return /Downloading · \d+%/.test(text) ? text : null;
}, 15000);
check(!!status, `the film's page shows the download carrying on with its progress (${status})`);
await control.click();
const underway = page.locator(".kc-dl-underway");
await underway.waitFor({ timeout: 10000 });
const parts = await underway.locator(".kc-dl-part").allTextContents();
check(/^\d+%$/.test(await underway.locator(".kc-dl-progress__percent").textContent()) && parts.some((t) => t.startsWith("Picture")),
  `the sheet shows the download's percent and each of its parts (${parts.join("; ")})`);
// The control says Saving as soon as the save is queued, ahead of its first request; the resumed
// request is waited for, and has to start where the track on disk ends.
const partial = left ? left.meta.files.find((f) => (f.role === "video" || f.role === "audio") && left.sizes[f.name] > 0 && left.sizes[f.name] < f.bytes) : null;
const from = partial ? `bytes=${left.sizes[partial.name]}-` : null;
const resumed = await waitFor(() => transfers.find((t) => partial && t.url.endsWith(`/${partial.rendition}/track.mp4`) && t.range === from && t.status), 30000);
check(!!resumed && resumed.status === 206 && !!resumed.ifRange,
  `the transfer resumed from what was on disk (${resumed ? `${resumed.range}, If-Range ${resumed.ifRange}, ${resumed.status}` : `no request for ${from || "a part-saved track"}: ${JSON.stringify(transfers.map((t) => [t.url, t.range, t.status]))}`})`);
await page.screenshot({ path: join(shots, "sheet-downloading.png") });

await underway.locator(".k-modal__foot button", { hasText: "Pause" }).click();
const paused = await waitFor(async () => {
  const text = await control.textContent().catch(() => "");
  return /Paused · \d+%/.test(text) ? text : null;
}, 10000);
const atPause = received(await onDevice(page));
await page.waitForTimeout(2500);
const afterPause = received(await onDevice(page));
check(!!paused && afterPause === atPause, `Pause stops the transfer (${paused}; ${atPause} then ${afterPause} bytes)`);
await page.screenshot({ path: join(shots, "sheet-paused.png") });

// A pause stands across a reload.
await page.reload({ waitUntil: "load" });
await control.waitFor({ timeout: 15000 });
await page.waitForTimeout(2500);
const stillPaused = await control.textContent().catch(() => "");
check(/Paused/.test(stillPaused) && received(await onDevice(page)) === afterPause, `a paused download stays paused across a reload (${stillPaused})`);

await control.click();
await underway.locator(".k-modal__foot button", { hasText: "Resume" }).click();
const done = await waitFor(async () => {
  const state = await onDevice(page);
  return state && state.meta.completedAt ? state : null;
}, 120000);
check(!!done, `Resume carries the download to the end, and meta.json is written again with completedAt (${done && done.meta.completedAt})`);
const savedStatus = await waitFor(async () => {
  const text = await control.textContent().catch(() => "");
  return /^Downloaded$/.test(text) ? text : null;
}, 10000);
check(!!savedStatus, `the film's page says it is downloaded (${savedStatus})`);
// The sheet left open follows the download, and is the downloaded one once it finishes.
const doneSheet = page.locator(".kc-dl-done");
if (!(await doneSheet.isVisible())) await control.click();
await doneSheet.waitFor({ timeout: 10000 });
const facts = await doneSheet.locator(".kc-dl-fact").allTextContents();
check(facts.length === 4 && await doneSheet.locator(".k-modal__foot a, .k-modal__foot button", { hasText: "Play" }).count() === 1,
  `the sheet shows what is downloaded, with Play (${facts.join("; ")})`);
await page.screenshot({ path: join(shots, "sheet-downloaded.png") });
await page.keyboard.press("Escape");

// Every byte the server's.
if (done) {
  for (const file of done.meta.files) {
    const have = done.sizes[file.name];
    if (file.role === "video" || file.role === "audio") {
      const want = concatenation(TITLE, file.rendition);
      const sha = await hashOnDevice(page, file.name);
      check(have === want.bytes && sha === want.sha, `${file.name} is byte for byte its playlist's files joined (${have} bytes)`);
    } else {
      check(have === file.bytes && have >= 0, `${file.name} (${file.role}) holds its listed ${file.bytes} bytes`);
    }
  }
  check(done.meta.bytes === Object.values(done.sizes).reduce((n, s) => n + s, 0), "meta.json's bytes are every file's, exactly");
}

// The API was told.
const told = await waitFor(async () => {
  const records = (await call("GET", "/api/me/offline")).body || [];
  const record = records.find((r) => r.titleId === TITLE);
  return record && record.completedAt ? record : null;
}, 10000);
check(!!told, `POST complete reached the API (${told && told.completedAt})`);
const tokens = new Set(transfers.map((t) => t.bearer));
console.log(`  track requests: ${transfers.length}, ${tokens.size} distinct tokens`);

// The film's page says it is here.
await page.goto(base + `/movies/${TITLE}`, { waitUntil: "load" });
const here = await waitFor(async () => {
  const text = await page.locator(".kc-tt .kc-actions button", { hasText: "Downloaded" }).textContent().catch(() => null);
  return text;
}, 10000);
check(!!here, `the film's page says it is downloaded after a reload (${here})`);
await page.screenshot({ path: join(shots, "film-downloaded.png") });

// A device without room is refused before anything moves.
{
  const small = await browser.newContext({ viewport: { width: 1280, height: 900 }, hasTouch: true });
  await small.addInitScript(() => {
    const estimate = navigator.storage.estimate.bind(navigator.storage);
    navigator.storage.estimate = async () => ({ ...(await estimate()), quota: 1_000_000, usage: 0 });
  });
  const p = await small.newPage();
  await p.goto(base + "/manifest.webmanifest");
  await p.evaluate(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER));
  const before = (await call("GET", "/api/me/offline")).body.length;
  const seen = transfers.length;
  await p.goto(base + `/movies/${TITLE}`, { waitUntil: "load" });
  await p.locator(".kc-tt .kc-actions button", { hasText: /^Download$/ }).click();
  await p.locator(".kc-dl-pick .k-optlist").first().waitFor({ timeout: 10000 });
  const refusal = await p.locator(".kc-dl-pick .k-notice", { hasText: "Needs" }).textContent().catch(() => null);
  const disabled = await p.locator(".kc-dl-pick .kc-dl-start").isDisabled();
  check(!!refusal && disabled, `a device without room is told how much it needs, and cannot start (${refusal})`);
  check((await call("GET", "/api/me/offline")).body.length === before && transfers.length === seen, "nothing was asked of the API or fetched");
  await p.screenshot({ path: join(shots, "no-room.png") });
  await small.close();
}

// A save the API no longer lists is removed from the device when the connection comes back.
{
  const records = (await call("GET", "/api/me/offline")).body || [];
  for (const r of records.filter((r) => r.titleId === TITLE)) await call("DELETE", `/api/me/offline/${r.id}`);
  await context.setOffline(true);
  await page.evaluate(() => window.dispatchEvent(new Event("offline")));
  await page.waitForTimeout(500);
  await context.setOffline(false);
  await page.evaluate(() => window.dispatchEvent(new Event("online")));
  const gone = await waitFor(async () => (await onDevice(page)) === null, 15000);
  check(gone, "a save the API no longer lists is removed from the device when the connection comes back");
}

check(page.errors.length === 0, `nothing raised${page.errors.length ? ": " + page.errors.join("; ") : ""}`);
await browser.close();
proxy.close();
console.log(`\nScreenshots in ${shots}`);
console.log(failed ? `${failed} failed` : "all passed");
process.exit(failed ? 1 : 0);
