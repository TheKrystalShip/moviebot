// A film saved on the device and played from it, in a real Chromium against the scratch site: saved
// through the API as the saver saves it (the package, each track whole from `track.mp4`, the subtitle
// and the artwork) into the Origin Private File System, then played alone with the network cut (it
// starts, plays, pauses, seeks by key and by the bar, resumes where it was left, reaches its end and
// asks the network for nothing), its progress reaching the API once the connection is back; then in a
// room beside a viewer who streams, the saved copy answering from the device while the hub keeps the
// timeline, and the room lost with the film saved here carrying on alone from where it was, and with
// it not saved saying the room cannot be reached.
//
//   node verify/device.mjs
//
// Needs the scratch site verify/mobile/scratch.sh serves, over a library holding a ready film every one
// of whose segments is on disk, which is what the API offers to save (`MOVIEBOT_VERIFY_SAVED`, by
// default the first such film, `clip` first), and a Chromium that decodes H.264 and AAC. Settings
// (environment): MOVIEBOT_VERIFY_DIR as scratch.sh; CHROME_PATH a Chromium, when Playwright's cached
// one is not wanted. A screenshot of each state is left in <dir>/runs/device-<time>/. Exits 1 when
// any check fails, 2 when the scratch library has nothing to save.

import { existsSync, mkdirSync, readFileSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { VIEWER } from "./mobile/stage.mjs";
import { issuerIn } from "./issuer.mjs";

const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
if (!existsSync(join(dir, "api.env"))) {
  console.error(`No scratch site in ${dir}. Start verify/mobile/scratch.sh first.`);
  process.exit(2);
}
const readEnv = (file) => Object.fromEntries(
  readFileSync(join(dir, file), "utf8").split("\n").filter(Boolean).map((line) => [line.slice(0, line.indexOf("=")), line.slice(line.indexOf("=") + 1)]));
const site = readEnv("api.env");
const base = `http://127.0.0.1:${site.PORT}`;
const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
const OTHER = { id: "verify-device-other", name: "Ada" };
const shots = join(dir, "runs", "device-" + new Date().toISOString().replace(/[:.]/g, "-"));
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

let failed = 0;
const check = (ok, line) => { if (!ok) failed++; console.log(`${ok ? "✓" : "✗"} ${line}`); };
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(token, method, path, body) {
  const headers = { Authorization: `Bearer ${token}` };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const response = await fetch(base + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

const token = issuer.mint(VIEWER);
// A title the API offers to save is one whose every segment is on disk.
const candidates = process.env.MOVIEBOT_VERIFY_SAVED
  ? [process.env.MOVIEBOT_VERIFY_SAVED]
  : ["clip", ...(await call(token, "GET", "/api/titles")).filter((t) => t.status === "ready" && !t.episode).map((t) => t.id)];
let TITLE = null;
let offer = null;
for (const id of candidates) {
  offer = await call(token, "GET", `/api/titles/${encodeURIComponent(id)}/offline`).catch(() => null);
  if (offer && offer.rungs.length > 0 && offer.audio.length > 0) {
    TITLE = id;
    break;
  }
}
if (!TITLE) {
  console.error(`The scratch library on ${base} offers nothing to save: it needs a ready title every one of whose segments is on disk.`);
  process.exit(2);
}
const rung = offer.rungs.at(-1);
// The person's language is English; the English track with the most in it is the one saved.
const subtitle = offer.subtitles.filter((s) => /^en/.test(s.language)).sort((a, b) => b.bytes - a.bytes)[0];
const pkg = await call(token, "POST", "/api/me/offline", {
  titleId: TITLE, rung: rung.rendition, audio: [offer.audio[0].rendition], subtitles: subtitle ? [subtitle.id] : [], device: "verify-device",
});
const duration = pkg.manifest.durationSeconds;
console.log(`Saving ${TITLE} at ${rung.name} (${(pkg.bytes / 1e6).toFixed(1)} MB) on ${base}`);

/** The saver's work, done from the page: every file fetched whole and written where the store keeps it. */
const save = (page) => page.evaluate(async ({ pkg, sub, token }) => {
  const name = (s) => encodeURIComponent(String(s)).replace(/\./g, "%2E");
  const root = await navigator.storage.getDirectory();
  const account = await root.getDirectoryHandle(name(sub), { create: true });
  const dir = await account.getDirectoryHandle(name(pkg.save.titleId), { create: true });
  const files = [
    ...pkg.tracks.map((t) => ({ name: `${t.rendition}.mp4`, role: t.kind, rendition: t.rendition, bytes: t.bytes, url: t.url, auth: true })),
    ...pkg.subtitles.map((s) => ({ name: `${s.id}.vtt`, role: "subtitle", id: s.id, language: s.language, label: s.label, bytes: s.bytes, url: s.url, auth: true })),
    ...(pkg.artwork.poster ? [{ name: "poster.jpg", role: "poster", type: "image/jpeg", url: pkg.artwork.poster }] : []),
    ...(pkg.artwork.thumbnails ? [{ name: "thumbs.jpg", role: "thumbnails", type: "image/jpeg", url: pkg.artwork.thumbnails, auth: true }] : []),
  ];
  const write = async (file, body) => {
    const w = await (await dir.getFileHandle(file, { create: true })).createWritable();
    await w.write(body);
    await w.close();
  };
  const meta = {
    format: 1, titleId: pkg.save.titleId, account: sub, kind: "film", name: pkg.manifest.film?.name || pkg.manifest.title,
    year: pkg.manifest.film?.year ?? null, imdbId: pkg.manifest.film?.imdbId ?? null, show: null, episode: null,
    overview: null, genres: [], durationSeconds: pkg.manifest.durationSeconds,
    quality: { rendition: pkg.tracks[0].rendition, width: pkg.manifest.video.renditions[0].width, height: pkg.manifest.video.renditions[0].height, label: pkg.manifest.video.renditions[0].name },
    files: [], bytes: 0, saveId: pkg.save.id, startedAt: new Date().toISOString(), completedAt: null, package: pkg,
  };
  for (const f of files) {
    const response = await fetch(f.url, f.auth ? { headers: { Authorization: `Bearer ${token}` } } : undefined);
    if (!response.ok) throw new Error(`${f.url} answered ${response.status}`);
    const blob = await response.blob();
    await write(f.name, blob);
    const { url, auth, ...entry } = f;
    meta.files.push({ ...entry, bytes: blob.size });
  }
  meta.bytes = meta.files.reduce((sum, f) => sum + f.bytes, 0);
  meta.completedAt = new Date().toISOString();
  await write("meta.json", JSON.stringify(meta));
  return meta.files.map((f) => `${f.name} ${f.bytes}`);
}, { pkg, sub: VIEWER.id, token });

const browser = await chromium.launch({
  executablePath: chromePath(),
  args: ["--autoplay-policy=no-user-gesture-required", "--mute-audio"],
});

/** A signed-in context for `person` with the worker installed and in control, the session in storage. */
async function prepared(person, viewport = { width: 1280, height: 720 }) {
  const context = await browser.newContext({ viewport });
  const planted = issuer.planted(person);
  await context.addInitScript(() => {
    window.__sent = [];
    const send = WebSocket.prototype.send;
    WebSocket.prototype.send = function (data) {
      try { window.__sent.push(String(data)); } catch { /* binary */ }
      return send.call(this, data);
    };
  });
  const page = await context.newPage();
  await page.goto(base + "/manifest.webmanifest");
  await page.evaluate(({ key, value }) => localStorage.setItem(key, value), planted);
  await page.goto(base + "/", { waitUntil: "load" });
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.reload({ waitUntil: "load" });
  await page.waitForFunction(() => !!navigator.serviceWorker.controller, null, { timeout: 15000 });
  // Profile's Playback, online, keeps the person's preferences on the device.
  await page.goto(base + "/profile/playback", { waitUntil: "load" });
  await page.waitForSelector(".kc-segmented", { timeout: 15000 });
  const requests = [];
  context.on("request", (r) => requests.push(r.url()));
  return { context, page, requests };
}

/** A tab collecting what the page raises. */
async function tab(context) {
  const page = await context.newPage();
  page.errors = [];
  page.on("pageerror", (e) => page.errors.push(e.message));
  return page;
}

const video = (page) => page.evaluate(() => {
  const v = document.querySelector("video");
  return v ? { at: v.currentTime, paused: v.paused, ended: v.ended, ready: v.readyState, seeking: v.seeking } : null;
});
const playing = (page, timeout = 30000) => page.waitForFunction(() => {
  const v = document.querySelector("video");
  return v && v.readyState >= 3 && !v.paused;
}, null, { timeout });
const logged = (page) => page.evaluate((sub) => JSON.parse(localStorage.getItem("cinema.progress." + sub) || "[]"), VIEWER.id);
async function reach(page) {
  const { width, height } = page.viewportSize();
  await page.mouse.move(width / 2, height / 3);
  await page.mouse.move(width / 2 + 4, height / 3 + 4);
  await wait(200);
}
const mediaAsked = (requests, from) => requests.slice(from).filter((u) => /\/media\/|\/hub\//.test(u));
const room = (what) => `verify-device-${what}-${Date.now().toString(36)}`;
const backOnline = async (context, page) => {
  await context.setOffline(false);
  await page.evaluate(() => window.dispatchEvent(new Event("online")));
};

// ── Saving, and playing alone with no connection ────────────────────────────────────────────────

await call(token, "PUT", "/api/me/preferences", { subtitleLanguage: "en" });
const phone = await prepared(VIEWER);
const written = await save(phone.page);
check(written.length >= 3, `the save is written to the device (${written.join(", ")})`);

await phone.context.setOffline(true);
let page = await tab(phone.context);
let from = phone.requests.length;
await page.goto(`${base}/r/0a1b2c3d?title=${TITLE}&offline=1`, { waitUntil: "domcontentloaded" });
await page.evaluate(() => window.dispatchEvent(new Event("offline")));
await playing(page).catch(() => null);
let v = await video(page);
check(v !== null && !v.paused && v.ready >= 3, `with the network cut, the saved film plays from the device (${JSON.stringify(v)})`);
const startedAt = v ? v.at : 0;
await wait(2500);
v = await video(page);
check(v.at - startedAt > 1.5, `and its playhead moves (${startedAt.toFixed(2)}s to ${v.at.toFixed(2)}s)`);
const top = await page.evaluate(() => ({
  room: !!document.querySelector(".mp-top__room"),
  invite: !!document.querySelector(".mp-invite"),
  quality: [...document.querySelectorAll(".mp-menu-button")].some((b) => /Quality/.test(b.getAttribute("aria-label") || "")),
  subtitles: document.querySelector('.mp-menu-button[aria-label^="Subtitles"]')?.getAttribute("aria-label"),
  name: document.querySelector(".mp-top__name")?.textContent,
}));
await reach(page);
await page.screenshot({ path: join(shots, "alone-playing.png") });
check(!top.room && !top.invite, `alone, the room's viewers and invite are not drawn (${JSON.stringify(top)})`);
check(!top.quality, "alone, there is no quality to choose: the saved rung is the film");
check(/English/.test(top.subtitles || ""), `the person's subtitle language, kept on the device, is on (${top.subtitles})`);
const cues = await page.evaluate(() => { const t = [...document.querySelector("video").textTracks].find((x) => x.mode !== "disabled"); return t && t.cues ? t.cues.length : 0; });
check(cues > 0, `the saved subtitle file is read from the device (${cues} cues)`);

// A seek by key: ten seconds on, at once, and the film goes on.
await reach(page);
let before = (await video(page)).at;
let t0 = Date.now();
await page.keyboard.press("l");
await page.waitForFunction((b) => { const x = document.querySelector("video"); return !x.seeking && x.currentTime > b + 8; }, before, { timeout: 10000 }).catch(() => null);
v = await video(page);
check(v.at > before + 8 && v.at < before + 13, `a key seek moves the film ten seconds (${before.toFixed(1)}s to ${v.at.toFixed(1)}s, ${Date.now() - t0} ms)`);

// A seek on the bar, far into the film.
await reach(page);
const bar = await page.locator(".mp-scrub").boundingBox();
t0 = Date.now();
await page.mouse.click(bar.x + bar.width * 0.6, bar.y + bar.height / 2);
await page.waitForFunction((d) => { const x = document.querySelector("video"); return !x.seeking && Math.abs(x.currentTime - d * 0.6) < 4 && x.readyState >= 3; }, duration, { timeout: 10000 }).catch(() => null);
v = await video(page);
check(Math.abs(v.at - duration * 0.6) < 4, `a seek on the bar lands where it was asked, unclamped (${v.at.toFixed(1)}s of ${duration.toFixed(1)}s, ready in ${Date.now() - t0} ms)`);
await wait(1500);
check(!(await video(page)).paused, "and plays on from there");

// Pausing writes down where the person got to.
await reach(page);
await page.click(".mp-play");
await wait(500);
v = await video(page);
const pausedAt = v.at;
const log = await logged(page);
const entry = log.find((e) => e.titleId === TITLE);
check(v.paused && entry && Math.abs(entry.positionSeconds - pausedAt) < 1, `a pause stops the film and writes down where it was (${pausedAt.toFixed(1)}s, logged ${entry && entry.positionSeconds}s)`);
await page.screenshot({ path: join(shots, "alone-paused.png") });
const asked = mediaAsked(phone.requests, from);
check(asked.length === 0, `nothing asked the network for the film or a room (${asked.length}${asked.length ? ": " + asked.slice(0, 3).join(", ") : ""})`);
check(page.errors.length === 0, `nothing raised${page.errors.length ? ": " + page.errors.join("; ") : ""}`);
await page.close();

// Opened again with no position named: it starts where it was left.
page = await tab(phone.context);
await page.goto(`${base}/r/0a1b2c3e?title=${TITLE}&offline=1`, { waitUntil: "domcontentloaded" });
await page.evaluate(() => window.dispatchEvent(new Event("offline")));
await playing(page).catch(() => null);
v = await video(page);
check(v && Math.abs(v.at - pausedAt) < 3, `opened again, it resumes where it was left (${v && v.at.toFixed(1)}s, left at ${pausedAt.toFixed(1)}s)`);

// A browser with no Media Source Extensions plays HLS itself, and nothing can hand it a saved file.
const native = await tab(phone.context);
await native.addInitScript(() => {
  Object.defineProperty(window, "MediaSource", { value: undefined });
  Object.defineProperty(window, "ManagedMediaSource", { value: undefined });
});
await native.goto(`${base}/r/0a1b2c40?title=${TITLE}&offline=1`, { waitUntil: "domcontentloaded" });
await native.waitForSelector(".mp-message", { timeout: 15000 }).catch(() => null);
const refusal = await native.locator(".mp-message").textContent().catch(() => "");
check(/cannot play a film saved on the device/.test(refusal), `a browser that plays HLS itself says it cannot play a saved film (${refusal})`);
await native.close();

// The connection back: the log reaches the API, and leaves the device.
await backOnline(phone.context, page);
await page.waitForFunction((sub) => !localStorage.getItem("cinema.progress." + sub), VIEWER.id, { timeout: 20000 }).catch(() => null);
let progress = (await call(token, "GET", "/api/me/progress")).find((p) => p.titleId === TITLE);
check(progress && Math.abs(progress.positionSeconds - pausedAt) < 15,
  `with the connection back, where the film got to is on the API (${progress && progress.positionSeconds}s) and off the device (${JSON.stringify(await logged(page))})`);
await page.close();

// To the end, with no connection, and the end reaching the API as the film watched.
await phone.context.setOffline(true);
page = await tab(phone.context);
await page.goto(`${base}/r/0a1b2c3f?title=${TITLE}&offline=1&at=${Math.floor(duration - 4)}`, { waitUntil: "domcontentloaded" });
await page.evaluate(() => window.dispatchEvent(new Event("offline")));
await page.waitForFunction(() => document.querySelector("video")?.ended, null, { timeout: 30000 }).catch(() => null);
v = await video(page);
check(v && v.ended, `played from a position on the address, it reaches its end (${v && v.at.toFixed(1)}s of ${duration.toFixed(1)}s)`);
await page.screenshot({ path: join(shots, "alone-ended.png") });
await backOnline(phone.context, page);
await page.waitForFunction((sub) => !localStorage.getItem("cinema.progress." + sub), VIEWER.id, { timeout: 20000 }).catch(() => null);
const summary = (await call(token, "GET", "/api/titles")).find((t) => t.id === TITLE);
check(summary && summary.progress && summary.progress.finished, `and the API has it as watched (${JSON.stringify(summary && summary.progress)})`);
await page.close();

// ── In a room: one viewer from the device, one streaming ────────────────────────────────────────

const name = room("together");
const ada = await prepared(OTHER);
const adaPage = await tab(ada.context);
await adaPage.goto(`${base}/r/${name}?title=${TITLE}`, { waitUntil: "domcontentloaded" });
await adaPage.waitForFunction(() => document.querySelector("video")?.readyState >= 2, null, { timeout: 30000 }).catch(() => null);
from = phone.requests.length;
page = await tab(phone.context);
await page.goto(`${base}/r/${name}?title=${TITLE}`, { waitUntil: "domcontentloaded" });
await page.waitForFunction(() => document.querySelector("video")?.readyState >= 2, null, { timeout: 30000 }).catch(() => null);
await reach(adaPage);
await adaPage.click(".mp-play");
await playing(page).catch(() => null);
await playing(adaPage).catch(() => null);
await wait(3000);
let [mine, theirs] = [await video(page), await video(adaPage)];
check(!mine.paused && !theirs.paused && Math.abs(mine.at - theirs.at) < 1,
  `in a room, a play by the other viewer plays both, together (${mine.at.toFixed(2)}s from the device, ${theirs.at.toFixed(2)}s streamed)`);
await reach(page);
await page.keyboard.press("l");
await wait(2500);
[mine, theirs] = [await video(page), await video(adaPage)];
check(Math.abs(mine.at - theirs.at) < 1, `a seek by the viewer playing from the device moves the room (${mine.at.toFixed(2)}s and ${theirs.at.toFixed(2)}s)`);
const segments = phone.requests.slice(from).filter((u) => u.includes(`/media/${TITLE}/`) && /\.(m4s|mp4|m3u8|vtt)/.test(u));
check(segments.length === 0, `the viewer with the film saved asks the server for none of it (${segments.length}${segments.length ? ": " + segments.slice(0, 3).join(", ") : ""})`);
const streamed = ada.requests.filter((u) => u.includes(`/media/${TITLE}/`) && u.includes(".m4s"));
check(streamed.length > 0, `while the other viewer streams it (${streamed.length} segments)`);
const report = (await page.evaluate(() => window.__sent.join("\u001e"))).split("\u001e")
  .map((frame) => { try { return JSON.parse(frame); } catch { return null; } })
  .filter((m) => m && m.target === "ReportQuality")
  .map((m) => m.arguments);
check(report.length > 0 && report.every(([playing, wanted]) => playing === rung.height && wanted === rung.height),
  `it reports the saved rung as the one it plays and wants (${JSON.stringify(report)})`);
const hasQuality = await page.evaluate(() => [...document.querySelectorAll(".mp-menu-button")].some((b) => /Quality/.test(b.getAttribute("aria-label") || "")));
check(!hasQuality, "and offers no other quality");
await reach(page);
await page.screenshot({ path: join(shots, "room-from-device.png") });

// The connection lost: the film saved here carries on alone, from where the room was.
await phone.context.setOffline(true);
await page.evaluate(() => window.dispatchEvent(new Event("offline")));
await page.waitForSelector(".mp-lost__go", { timeout: 40000 }).catch(() => null);
const lostText = await page.locator(".mp-lost").textContent().catch(() => "");
check(/can't be reached/.test(lostText) && /Carry on/.test(lostText), `with the connection lost, the room says so and offers to carry on from the device (${lostText})`);
await page.screenshot({ path: join(shots, "room-lost-saved.png") });
const at = (await video(page)).at;
await page.click(".mp-lost__go");
await page.waitForURL(/offline=1/, { timeout: 10000 }).catch(() => null);
await playing(page).catch(() => null);
v = await video(page);
check(/offline=1/.test(page.url()) && v && !v.paused && Math.abs(v.at - at) < 4,
  `carrying on plays it alone from where the room was (${page.url().replace(base, "")}, ${v && v.at.toFixed(1)}s, room at ${at.toFixed(1)}s)`);
const alone = await page.evaluate(() => !document.querySelector(".mp-top__room") && !document.querySelector(".mp-lost"));
check(alone, "and the room's viewers and the lost room are gone from it");
await page.screenshot({ path: join(shots, "room-carried-on.png") });

// The other viewer, with nothing saved, loses the room and is only told so.
await ada.context.setOffline(true);
await adaPage.evaluate(() => window.dispatchEvent(new Event("offline")));
await adaPage.waitForSelector(".mp-lost", { timeout: 40000 }).catch(() => null);
const theirsLost = await adaPage.evaluate(() => ({ text: document.querySelector(".mp-lost")?.textContent, go: !!document.querySelector(".mp-lost__go") }));
check(/can't be reached/.test(theirsLost.text || "") && !theirsLost.go, `a viewer with nothing saved is told the room cannot be reached, and offered nothing (${JSON.stringify(theirsLost)})`);
await adaPage.screenshot({ path: join(shots, "room-lost-unsaved.png") });

await browser.close();
await call(token, "DELETE", `/api/me/offline/${pkg.save.id}`);
console.log(`\nScreenshots in ${shots}`);
console.log(failed ? `${failed} failed` : "all passed");
process.exit(failed ? 1 : 0);
