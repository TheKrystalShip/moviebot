// Download of a film the server is still making, end to end in a real Chromium as a landscape tablet:
// Download on the page of a film being transcoded asks for its quality alone, the server answers the
// want as ready at once, the device downloads what has been made, waits, and carries on from what it
// holds once the film is whole (by `Range`, with `If-Range` naming the validator its bytes arrived
// under); every track ends byte for byte the server's, the API is told the save is complete, and the
// want is gone. With the page out of view a notification says how far the download has got, another
// says it is downloaded, and the first closes once the page is in view again. The show page offers
// Download season.
//
//   node verify/growing.mjs
//
// Needs the scratch site verify/mobile/scratch.sh serves, over a library holding a whole title to make
// the growing copy of (`MOVIEBOT_VERIFY_GROW_TITLE`, by default `clip`). The copy is hard-linked
// beside it with its playlists cut short and its manifest saying it is being transcoded, then made
// whole by writing the playlists and the manifest back; it is removed at the end. Settings
// (environment): MOVIEBOT_VERIFY_DIR as scratch.sh; CHROME_PATH a Chromium. Screenshots are left in
// <dir>/runs/growing-<time>/. Exits 1 when any check fails, 2 when the scratch library cannot serve.

import { createHash } from "node:crypto";
import { existsSync, linkSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, unlinkSync, writeFileSync } from "node:fs";
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
const shots = join(dir, "runs", "growing-" + new Date().toISOString().replace(/[:.]/g, "-"));
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

async function waitFor(fn, timeout, every = 250) {
  const until = Date.now() + timeout;
  for (;;) {
    const value = await fn();
    if (value || Date.now() > until) return value;
    await new Promise((r) => setTimeout(r, every));
  }
}

// ── The growing copy ─────────────────────────────────────────────────────────────────────────────

const SOURCE = process.env.MOVIEBOT_VERIFY_GROW_TITLE || "clip";
const sourceDir = [join(dir, "library", "media", SOURCE), join(dir, "library", "cold", SOURCE)].find((d) => existsSync(d));
if (!sourceDir) {
  console.error(`The scratch library holds no ${SOURCE} to make a growing copy of.`);
  process.exit(2);
}
const TITLE = `${SOURCE}-growing`;
const IMDB = "tt9999001";
const copy = join(dir, "library", "media", TITLE);
const manifest = JSON.parse(readFileSync(join(sourceDir, "manifest.json"), "utf8"));
const renditions = [...manifest.video.renditions, ...manifest.audio].map((r) => r.uri.split("/")[0]);
const whole = Object.fromEntries(renditions.map((r) => [r, readFileSync(join(sourceDir, r, "index.m3u8"), "utf8")]));

/** A playlist cut to its first `keep` segments, unended, as a transcode part way through writes it. */
function cut(playlist, keep) {
  const out = [];
  let segments = 0;
  for (const line of playlist.split("\n")) {
    if (line === "#EXT-X-ENDLIST") continue;
    if (line && !line.startsWith("#")) {
      if (segments >= keep) break;
      segments++;
    }
    out.push(line);
  }
  // A cut ends after a segment's name, never on its #EXTINF.
  while (out.length && out[out.length - 1].startsWith("#EXTINF")) out.pop();
  return out.join("\n").replace(/#EXT-X-PLAYLIST-TYPE:VOD/, "#EXT-X-PLAYLIST-TYPE:EVENT") + "\n";
}

/** Replaces a file of the copy, which is a hard link to the source's, with one of its own. */
function rewrite(path, text) {
  if (existsSync(path)) unlinkSync(path);
  writeFileSync(path, text);
}

function linkTree(from, to) {
  mkdirSync(to, { recursive: true });
  for (const entry of readdirSync(from)) {
    const source = join(from, entry);
    if (statSync(source).isDirectory()) linkTree(source, join(to, entry));
    else linkSync(source, join(to, entry));
  }
}

const KEEP = Math.max(1, Math.floor(whole[renditions[0]].split("\n").filter((l) => l && !l.startsWith("#")).length / 2));
rmSync(copy, { recursive: true, force: true });
linkTree(sourceDir, copy);
for (const r of renditions) rewrite(join(copy, r, "index.m3u8"), cut(whole[r], KEEP));
const growingManifest = {
  ...manifest, id: TITLE, title: "Growing Clip", status: "transcoding", headSeconds: KEEP * 4,
  film: { imdbId: IMDB, name: "Growing Clip", year: 2026 },
};
rewrite(join(copy, "manifest.json"), JSON.stringify(growingManifest));

/** The copy made whole: every playlist as the source's and the manifest ready. */
function finish() {
  for (const r of renditions) rewrite(join(copy, r, "index.m3u8"), whole[r]);
  const ready = { ...growingManifest, status: "ready" };
  delete ready.headSeconds;
  rewrite(join(copy, "manifest.json"), JSON.stringify(ready));
}

/** A rendition's track as the API joins it now: its playlist's init segment and every segment. */
function concatenation(rendition) {
  const root = join(copy, rendition);
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

// ── A proxy that records each track request ──────────────────────────────────────────────────────

const transfers = [];
const proxy = createServer((req, res) => {
  const track = /\/track\.mp4$/.test(req.url.split("?")[0]);
  const seen = track && req.method === "GET"
    ? { url: req.url, range: req.headers.range || null, ifRange: req.headers["if-range"] || null, status: null }
    : null;
  if (seen) transfers.push(seen);
  const up = request({ host: "127.0.0.1", port: Number(site.PORT), path: req.url, method: req.method, headers: req.headers }, (answer) => {
    if (seen) seen.status = answer.statusCode;
    res.writeHead(answer.statusCode, answer.headers);
    answer.pipe(res);
  });
  up.on("error", () => res.destroy());
  res.on("close", () => up.destroy());
  req.pipe(up);
});
await new Promise((resolve) => proxy.listen(0, "127.0.0.1", resolve));
const base = `http://127.0.0.1:${proxy.address().port}`;

// ── The device ───────────────────────────────────────────────────────────────────────────────────

const name = (s) => encodeURIComponent(String(s)).replace(/\./g, "%2E");

/** The save on the device: its meta.json, the package's tracks' lengths, and each file's length. */
const onDevice = (page) => page.evaluate(async ({ account, title }) => {
  try {
    const root = await navigator.storage.getDirectory();
    const dir = await (await root.getDirectoryHandle(account)).getDirectoryHandle(title);
    const meta = JSON.parse(await (await (await dir.getFileHandle("meta.json")).getFile()).text());
    const sizes = {};
    for (const f of meta.files) {
      try { sizes[f.name] = (await (await dir.getFileHandle(f.name)).getFile()).size; } catch { sizes[f.name] = -1; }
    }
    const tracks = meta.package.tracks.map((t) => ({ rendition: t.rendition, bytes: t.bytes, segments: t.segmentBytes.length }));
    delete meta.package;
    return { meta, sizes, tracks };
  } catch {
    return null;
  }
}, { account: name(VIEWER.id), title: name(TITLE) });

const hashOnDevice = (page, file) => page.evaluate(async ({ account, title, file }) => {
  const root = await navigator.storage.getDirectory();
  const dir = await (await root.getDirectoryHandle(account)).getDirectoryHandle(title);
  const data = await (await (await dir.getFileHandle(file)).getFile()).arrayBuffer();
  return [...new Uint8Array(await crypto.subtle.digest("SHA-256", data))].map((b) => b.toString(16).padStart(2, "0")).join("");
}, { account: name(VIEWER.id), title: name(TITLE), file });

/** Puts the page out of view, or back in it, as far as the page can tell. */
const hide = (page, hidden) => page.evaluate((h) => {
  Object.defineProperty(document, "visibilityState", { configurable: true, get: () => (h ? "hidden" : "visible") });
  Object.defineProperty(document, "hidden", { configurable: true, get: () => h });
  document.dispatchEvent(new Event("visibilitychange"));
}, hidden);

/** The site's notifications showing. */
const notes = (page) => page.evaluate(async () =>
  (await (await navigator.serviceWorker.ready).getNotifications()).map((n) => ({ tag: n.tag, title: n.title, body: n.body })));

const tracksHeld = (state) => !!state && state.meta.files.filter((f) => f.role === "video" || f.role === "audio")
  .every((f) => state.sizes[f.name] === f.bytes);

const browser = await chromium.launch({ executablePath: chromePath() });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, hasTouch: true });
await context.grantPermissions(["notifications"], { origin: base });
for (const record of (await call("GET", "/api/me/offline")).body || []) await call("DELETE", `/api/me/offline/${record.id}`);
for (const want of (await call("GET", "/api/me/offline/wants")).body || []) await call("DELETE", `/api/me/offline/wants/${want.id}`);
{
  const page = await context.newPage();
  await page.goto(base + "/manifest.webmanifest");
  const planted = issuer.planted(VIEWER, { refresh: "verify-growing-refresh" });
  await page.evaluate(({ key, value }) => localStorage.setItem(key, value), planted);
  await page.goto(base + "/", { waitUntil: "load" });
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.close();
}

const page = await context.newPage();
page.errors = [];
page.on("pageerror", (e) => page.errors.push(e.message));
await page.goto(base + `/movies/${TITLE}`, { waitUntil: "load" });
const control = page.locator(".kc-tt .kc-actions button", { hasText: /^(Download|Preparing|Downloading|Downloaded)/ });
await control.waitFor({ timeout: 15000 });
check((await control.textContent()) === "Download", "the page of a film being made offers Download");

// The sheet asks for the quality alone: nothing else is known before the server has the film.
await control.click();
const sheet = page.locator(".kc-dl-pick");
await sheet.locator(".k-optlist").first().waitFor({ timeout: 10000 });
const picker = await page.evaluate(() => ({
  lists: document.querySelectorAll(".kc-dl-pick .k-optlist").length,
  options: [...document.querySelectorAll(".kc-dl-pick .k-optlist__row input")].map((i) => i.value),
  chosen: [...document.querySelectorAll(".kc-dl-pick .k-optlist__row input")].find((i) => i.checked)?.value,
  start: document.querySelector(".kc-dl-pick .kc-dl-start")?.textContent,
  languages: [...document.querySelectorAll(".kc-dl-pick select")].map((s) => s.getAttribute("aria-label")),
  original: document.querySelector('.kc-dl-pick select[aria-label="Audio language"] option[value=""]')?.textContent,
}));
check(picker.lists === 1 && picker.options.join() === "1080,720,480" && picker.chosen === "480" && /^Download( · ~[\d.]+[KMGT]?B)?$/.test(picker.start),
  `the sheet asks for its quality, 480p on a tablet (${picker.options.join(", ")}; ${picker.chosen}; ${picker.start})`);
check(picker.languages.join() === "Audio language,Subtitle language" && picker.original === "Original",
  `and an audio and a subtitle language, the original first (${picker.languages.join(", ")}; ${picker.original})`);
// Subtitles in Spanish: the save takes the title's Spanish subtitles as the server lists them.
await page.locator('.kc-dl-pick select[aria-label="Subtitle language"]').selectOption("es");
await page.screenshot({ path: join(shots, "want-sheet.png") });
await sheet.locator(".kc-dl-start").click();
await sheet.waitFor({ state: "detached", timeout: 15000 });

// The want is ready at once, and the device's download of what has been made begins.
const first = await waitFor(async () => {
  const state = await onDevice(page);
  return state && state.meta.growing && tracksHeld(state) ? state : null;
}, 30000);
check(!!first && first.meta.completedAt === null && first.tracks.every((t) => t.segments === KEEP),
  `the device holds every track as far as it has been made, and is not done (${first && first.tracks.map((t) => `${t.rendition} ${t.segments} segments`).join(", ")})`);
const label = await control.textContent();
const percent = Number((/· (\d+)%/.exec(label) || [])[1]);
check(/^Downloading · \d+%$/.test(label) && percent > 0 && percent < 100,
  `the button counts what is made of the whole film (${label})`);
check(((await call("GET", "/api/me/offline/wants")).body || []).length === 0, "the want went when the device saved the film");
const spanish = manifest.subtitles.filter((s) => s.available && s.language === "spa").map((s) => s.id);
const kept = first ? first.meta.files.filter((f) => f.role === "subtitle").map((f) => f.id) : [];
check(spanish.length > 0 && spanish.every((id) => kept.includes(id)),
  `the download takes the Spanish subtitles asked for (${kept.join(", ") || "none"})`);
check(first && first.meta.quality.height === manifest.video.renditions.find((r) => r.uri.startsWith(first.meta.quality.rendition)).height,
  `the rung is the one 480p picks (${first && first.meta.quality.label})`);
await page.screenshot({ path: join(shots, "growing.png") });

// The page out of view: a notification says what is downloading and how far it has got.
await hide(page, true);
const progress = await waitFor(async () => (await notes(page)).find((n) => n.tag === "kc-downloads") || null, 10000);
check(!!progress && progress.title === "Growing Clip" && /^Downloading · \d+%$/.test(progress.body),
  `a hidden page's download is in a notification (${progress ? `${progress.title}: ${progress.body}` : "none"})`);

// The film is finished; the next stretch carries on from what the device holds.
const before = transfers.length;
finish();
const done = await waitFor(async () => {
  const state = await onDevice(page);
  return state && state.meta.completedAt ? state : null;
}, 60000);
check(!!done && done.meta.growing === false, `once the film is whole the download finishes (${done && done.meta.completedAt})`);
const after = transfers.slice(before);
const resumed = after.filter((t) => t.range && !t.range.startsWith("bytes=0-"));
check(resumed.length > 0 && resumed.every((t) => t.status === 206 && t.ifRange) && !after.some((t) => t.status === 200),
  `each track carried on from what was held (${after.map((t) => `${t.url.split("/").slice(-2, -1)} ${t.range} ${t.status}`).join("; ")})`);
if (done) {
  for (const file of done.meta.files.filter((f) => f.role === "video" || f.role === "audio")) {
    const want = concatenation(file.rendition);
    const sha = await hashOnDevice(page, file.name);
    check(done.sizes[file.name] === want.bytes && sha === want.sha, `${file.name} is byte for byte the whole film's (${want.bytes} bytes)`);
  }
}
const told = await waitFor(async () => {
  const record = ((await call("GET", "/api/me/offline")).body || []).find((r) => r.titleId === TITLE);
  return record && record.completedAt ? record : null;
}, 10000);
check(!!told, "the API was told the save is complete");
const face = await waitFor(async () => ((await control.textContent()) === "Downloaded" ? "Downloaded" : null), 10000);
check(!!face, "the film's page says it is downloaded");
const finished = await waitFor(async () => (await notes(page)).find((n) => n.tag === `kc-downloaded-${TITLE}`) || null, 10000);
check(!!finished && finished.title === "Growing Clip" && finished.body === "Downloaded",
  `finishing while hidden is a notification of its own (${finished ? `${finished.title}: ${finished.body}` : "none"})`);
await hide(page, false);
const left = await waitFor(async () => {
  const now = await notes(page);
  return now.some((n) => n.tag === "kc-downloads") ? null : now;
}, 10000);
check(!!left && left.some((n) => n.tag === `kc-downloaded-${TITLE}`),
  `the page in view closes the progress and leaves Downloaded (${left ? left.map((n) => n.tag).join(", ") : "progress still shown"})`);
await page.screenshot({ path: join(shots, "downloaded.png") });

// A show's season offers Download season.
const shows = (await call("GET", "/api/shows")).body || [];
const show = shows.find((s) => s.imdbId);
if (show) {
  await page.goto(base + `/tv/${show.imdbId}`, { waitUntil: "load" });
  const season = page.locator(".kc-season__head .kc-dl-season");
  const seen = await season.waitFor({ timeout: 15000 }).then(() => true, () => false);
  check(seen && /^Download (season|\d+ more)$/.test(await season.textContent()), `a show's season offers Download season (${seen ? await season.textContent() : "none"})`);
  await page.screenshot({ path: join(shots, "season.png"), fullPage: true });
}

check(page.errors.length === 0, `nothing raised${page.errors.length ? ": " + page.errors.join("; ") : ""}`);
for (const record of ((await call("GET", "/api/me/offline")).body || []).filter((r) => r.titleId === TITLE)) {
  await call("DELETE", `/api/me/offline/${record.id}`);
}
await browser.close();
proxy.close();
rmSync(copy, { recursive: true, force: true });
console.log(`\nScreenshots in ${shots}`);
console.log(failed ? `${failed} failed` : "all passed");
process.exit(failed ? 1 : 0);
