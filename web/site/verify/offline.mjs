// The site with no connection, in a real Chromium: the installed app opened cold in a second tab with
// the network cut, reaching a film saved on the device from Home and from Profile's Offline tab, and
// every address drawn in its offline state; then the same with the network up and the API not
// answering (blocked, and a gateway's 502 for an API that is down), which has to read the same; a
// session whose token ran out offline still standing; a device with nothing held saying a first
// sign-in needs a connection; and the connection coming back.
//
//   node verify/offline.mjs
//
// Needs the scratch site verify/mobile/scratch.sh serves. The saves are written into the Origin
// Private File System by the check itself, in the layout `src/offline/CLAUDE.md` sets out, from the
// scratch library's own listing and artwork; their tracks are placeholder bytes, since nothing here
// plays them. Settings (environment): MOVIEBOT_VERIFY_DIR as scratch.sh; CHROME_PATH a Chromium, when
// Playwright's cached one is not wanted. A screenshot of each offline state is left in
// <dir>/runs/offline-<time>/. Exits 1 when any check fails.

import { existsSync, mkdirSync, readFileSync, readdirSync } from "node:fs";
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
const base = `http://127.0.0.1:${site.PORT}`;
const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
const token = memberToken(issuer, VIEWER);
const shots = join(dir, "runs", "offline-" + new Date().toISOString().replace(/[:.]/g, "-"));
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

// The saves: a film finished, an episode finished, and a film under way.
const titles = await (await fetch(base + "/api/titles", { headers: { Authorization: `Bearer ${token}` } })).json();
const byId = new Map(titles.map((t) => [t.id, t]));
const FILM = "the-matrix-1999";
const EPISODE = titles.find((t) => t.episode && t.status === "ready");
const UNDERWAY = titles.find((t) => !t.episode && t.status === "ready" && t.id !== FILM);
const UNSAVED = titles.find((t) => !t.episode && t.id !== FILM && t.id !== (UNDERWAY && UNDERWAY.id));
if (!byId.has(FILM) || !EPISODE || !UNDERWAY || !UNSAVED) {
  console.error(`The scratch library needs ${FILM}, a ready episode and two more films.`);
  process.exit(2);
}

/** What the saver writes for a title, in the layout src/offline/CLAUDE.md sets out. */
function metaFor(t, { complete }) {
  const film = t.film || {};
  const d = t.details || {};
  const video = { name: "v0.mp4", role: "video", rendition: "v0", bytes: 4096 };
  const audio = { name: "a0.mp4", role: "audio", rendition: "a0", language: "eng", label: "English", bytes: 2048 };
  const poster = { name: "poster.jpg", role: "poster", type: "image/jpeg", bytes: 0 };
  const files = [video, audio, ...(t.poster ? [poster] : [])];
  return {
    format: 1,
    titleId: t.id,
    account: VIEWER.id,
    kind: t.episode ? "episode" : "film",
    name: film.name || t.title,
    year: film.year || null,
    imdbId: film.imdbId || null,
    show: t.episode ? { imdbId: film.imdbId, name: film.name } : null,
    episode: t.episode ? { season: t.episode.season, number: t.episode.number, name: t.episode.name || null } : null,
    overview: d.overview || null,
    genres: d.genres || [],
    durationSeconds: t.durationSeconds,
    quality: { rendition: "v0", width: 1280, height: 720, label: "720p" },
    files,
    bytes: 0,
    saveId: null,
    startedAt: new Date(Date.now() - (complete ? 3600e3 : 60e3)).toISOString(),
    completedAt: complete ? new Date().toISOString() : null,
    package: {},
  };
}

/** Writes the saves into this origin's private file system, from the page, with the poster fetched while online. */
const seed = (page, saves) => page.evaluate(async ({ saves, sub }) => {
  const name = (s) => encodeURIComponent(String(s)).replace(/\./g, "%2E");
  const root = await navigator.storage.getDirectory();
  const account = await root.getDirectoryHandle(name(sub), { create: true });
  for (const { meta, poster, partial } of saves) {
    const dir = await account.getDirectoryHandle(name(meta.titleId), { create: true });
    for (const file of meta.files) {
      let bytes;
      if (file.role === "poster") {
        bytes = new Uint8Array(await (await fetch(poster)).arrayBuffer());
        file.bytes = bytes.length;
      } else {
        bytes = new Uint8Array(partial ? Math.floor(file.bytes / 2) : file.bytes);
      }
      const w = await (await dir.getFileHandle(file.name, { create: true })).createWritable();
      await w.write(bytes);
      await w.close();
    }
    meta.bytes = meta.files.reduce((sum, f) => sum + f.bytes, 0);
    const w = await (await dir.getFileHandle("meta.json", { create: true })).createWritable();
    await w.write(JSON.stringify(meta));
    await w.close();
  }
}, { saves, sub: VIEWER.id });

const posterOf = (t) => `/media/${encodeURIComponent(t.id)}/${encodeURIComponent(t.poster)}`;

let failed = 0;
const check = (ok, line) => { if (!ok) failed++; console.log(`${ok ? "✓" : "✗"} ${line}`); };

const browser = await chromium.launch({ executablePath: chromePath() });

/** A signed-in context with the worker installed and in control, and the saves written. */
async function prepared() {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const planted = issuer.planted(VIEWER, { refresh: "verify-offline-refresh" });
  const page = await context.newPage();
  // The session is put in storage once, from a file of the origin's that is not a page, so a check
  // that changes it later is not undone by the next tab.
  await page.goto(base + "/manifest.webmanifest");
  await page.evaluate(({ key, value }) => localStorage.setItem(key, value), planted);
  await page.goto(base + "/", { waitUntil: "load" });
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.reload({ waitUntil: "load" });
  await page.waitForFunction(() => !!navigator.serviceWorker.controller, null, { timeout: 15000 });
  // Profile's Playback, online, keeps the person's preferences on the device.
  await page.goto(base + "/profile/playback", { waitUntil: "load" });
  await page.waitForSelector(".kc-segmented", { timeout: 15000 });
  await seed(page, [
    { meta: metaFor(byId.get(FILM), { complete: true }), poster: posterOf(byId.get(FILM)) },
    { meta: metaFor(EPISODE, { complete: true }), poster: posterOf(EPISODE) },
    { meta: metaFor(UNDERWAY, { complete: false }), poster: posterOf(UNDERWAY), partial: true },
  ]);
  return { context, page, planted };
}

/** A tab opened cold in `context`, collecting what the page raises. */
async function tab(context) {
  const page = await context.newPage();
  page.errors = [];
  page.on("pageerror", (e) => page.errors.push(e.message));
  return page;
}

const where = (page) => page.url().replace(base, "");

/** What an offline page drew. */
const drawn = (page) => page.evaluate(() => ({
  banner: !!document.querySelector(".kc-offline-banner"),
  h1: (document.querySelector("h1") || {}).textContent || null,
  empty: [...document.querySelectorAll(".kc-empty__title")].map((e) => e.textContent.trim()),
  cards: [...document.querySelectorAll(".showcase-card__name")].map((e) => e.textContent.trim()),
  navOff: [...document.querySelectorAll(".kc-nav-off .nav-item__label")].map((e) => e.textContent.trim()),
  navOn: [...document.querySelectorAll(".sidebar__nav > .sidebar__group > .nav-item .nav-item__label")].map((e) => e.textContent.trim()),
  subtab: [...document.querySelectorAll(".subtab--active")].map((e) => e.textContent.trim()),
  subtabs: [...document.querySelectorAll(".subtab")].map((e) => e.textContent.trim()),
}));

async function settle(page, ms = 1200) {
  await page.waitForLoadState("load").catch(() => {});
  await page.waitForTimeout(ms);
}

/**
 * Every address with the server out of reach, in tab `page`: each drawn from the device or saying it
 * needs a connection, under the banner, raising nothing. `label` names the way the server is out.
 */
async function walk(context, label) {
  const page = await tab(context);
  const film = byId.get(FILM);

  await page.goto(base + "/", { waitUntil: "domcontentloaded" });
  await settle(page, 2500);
  let d = await drawn(page);
  check(d.banner, `${label}: the banner is across Home`);
  check(d.h1 === "On this device", `${label}: Home leads with the films on this device (${d.h1})`);
  const name = film.film.name;
  check(d.cards.includes(name) && d.cards.length === 3, `${label}: Home lists the three saves (${d.cards.join(", ")})`);
  check(["Discover", "Saved", "Movies", "TV Shows"].every((n) => d.navOff.includes(n)), `${label}: Discover, Saved, Movies and TV Shows are drawn disabled (${d.navOff.join(", ")})`);
  check(d.navOn.includes("Home") && d.navOn.includes("Offline"), `${label}: Home and Offline stay live (${d.navOn.join(", ")})`);
  await page.screenshot({ path: join(shots, `${label}-home.png`) });

  await page.locator(".showcase-card", { hasText: name }).first().click();
  await settle(page);
  d = await drawn(page);
  check(where(page) === `/movies/${FILM}` && d.h1 === name, `${label}: Home leads to the saved film's page (${where(page)}, ${d.h1})`);
  const play = await page.locator(".kc-actions a", { hasText: "Play" }).getAttribute("href").catch(() => null);
  check(!!play && /^\/r\/[0-9a-f]{8}\?title=the-matrix-1999&offline=1$/.test(play), `${label}: the film's Play opens the player from the device (${play})`);
  await page.screenshot({ path: join(shots, `${label}-film.png`) });

  await page.goto(base + "/profile", { waitUntil: "domcontentloaded" });
  await settle(page, 2000);
  d = await drawn(page);
  check(where(page) === "/profile/offline", `${label}: Profile opens on its Offline tab (${where(page)})`);
  check(d.subtabs.join() === "Offline,Playback,Subtitles,Appearance,Account", `${label}: Profile's tabs are the device's (${d.subtabs.join(", ")})`);
  check(d.cards.length === 3, `${label}: the Offline tab lists the three saves`);
  const space = await page.locator(".kc-offline-space").textContent().catch(() => "");
  check(/saved · .* free/.test(space), `${label}: the Offline tab says what is saved and free (${space})`);
  const underway = await page.locator(".showcase-card", { hasText: UNDERWAY.film ? UNDERWAY.film.name : UNDERWAY.title }).locator(".showcase-card__status").textContent().catch(() => "");
  check(/^Waiting · \d+%/.test(underway), `${label}: a save under way waits for the connection and shows how far it has got (${underway})`);
  await page.screenshot({ path: join(shots, `${label}-profile-offline.png`) });

  await page.locator(".showcase-card", { hasText: name }).locator(".showcase-card__cta--act").click();
  await settle(page, 600);
  check(where(page).includes("offline=1") && where(page).includes(FILM), `${label}: Play on the Offline tab opens the player from the device (${where(page)})`);

  await page.goto(base + "/profile/playback", { waitUntil: "domcontentloaded" });
  await settle(page, 2000);
  const playback = await page.evaluate(() => ({
    drawn: !!document.querySelector(".kc-segmented"),
    fixed: !!document.querySelector("fieldset.kc-fieldset:disabled .kc-segmented"),
  }));
  check(playback.drawn && playback.fixed, `${label}: Playback is drawn from the device and changes nothing (${JSON.stringify(playback)})`);
  await page.screenshot({ path: join(shots, `${label}-playback.png`) });

  const show = EPISODE.film.imdbId;
  await page.goto(base + `/tv/${show}`, { waitUntil: "domcontentloaded" });
  await settle(page);
  d = await drawn(page);
  check(d.h1 === EPISODE.film.name && d.cards.length === 1, `${label}: a show with a saved episode lists it (${d.h1}, ${d.cards.join(", ")})`);
  await page.screenshot({ path: join(shots, `${label}-show.png`) });

  const NEEDS = ["/movies", "/tv", "/discover", "/discover?q=matrix", "/discover/film/tt0234215", "/saved", "/saved/later",
    "/notify", `/movies/${UNSAVED.id}`, "/r/abcd1234", "/profile/requests"];
  for (const path of NEEDS) {
    await page.goto(base + path, { waitUntil: "domcontentloaded" });
    await settle(page);
    d = await drawn(page);
    check(d.banner && d.empty.includes("Needs a connection"), `${label}: ${path} says it needs a connection`);
    await page.screenshot({ path: join(shots, `${label}-${path.replace(/[^a-z0-9]+/gi, "_")}.png`) });
  }
  const link = await page.locator(".kc-empty a", { hasText: "On this device" }).getAttribute("href").catch(() => null);
  check(link === "/profile/offline", `${label}: a page that needs the server leads to the films on the device (${link})`);

  await page.goto(base + "/about", { waitUntil: "domcontentloaded" });
  await settle(page);
  d = await drawn(page);
  check(d.h1 === "About", `${label}: About is drawn (${d.h1})`);

  check(page.errors.length === 0, `${label}: nothing raised${page.errors.length ? ": " + page.errors.join("; ") : ""}`);
  await page.close();
}

// The network cut.
{
  const { context, page, planted } = await prepared();
  await context.setOffline(true);
  await walk(context, "offline");

  // A phone: the tab bar's places that need the server are disabled, and Me leads to the device.
  const phone = await tab(context);
  await phone.setViewportSize({ width: 390, height: 844 });
  await phone.goto(base + "/", { waitUntil: "domcontentloaded" });
  await settle(phone, 2500);
  const tabs = await phone.$$eval(".tabbar__tab", (els) => els.map((b) => [b.textContent.trim(), b.disabled]));
  const off = tabs.filter(([, d]) => d).map(([n]) => n);
  check(off.join() === "Library,Discover,Assistant", `a phone's tab bar draws Library, Discover and the Assistant disabled (${off.join(", ")})`);
  await phone.screenshot({ path: join(shots, "offline-phone-home.png") });
  await phone.locator(".tabbar__tab", { hasText: /^Me$/ }).click();
  await settle(phone);
  check(where(phone) === "/profile/offline", `a phone's Me leads to the Offline tab (${where(phone)})`);
  await phone.screenshot({ path: join(shots, "offline-phone-me.png") });
  await phone.close();

  // A session whose token ran out with no connection stands, and nothing goes to the provider.
  await page.evaluate(({ key }) => {
    const user = JSON.parse(localStorage.getItem(key));
    user.expires_at = Math.floor(Date.now() / 1000) - 600;
    localStorage.setItem(key, JSON.stringify(user));
  }, planted);
  const stale = await tab(context);
  await stale.goto(base + "/profile", { waitUntil: "domcontentloaded" });
  await settle(stale, 2500);
  const d = await drawn(stale);
  const who = await stale.locator(".kc-head h1").textContent().catch(() => null);
  check(stale.url().startsWith(base) && d.banner && who === VIEWER.name, `an expired session offline still stands for ${VIEWER.name} (${who}, ${where(stale)})`);
  await stale.screenshot({ path: join(shots, "offline-expired.png") });
  await stale.close();

  // Nothing held: a first sign-in needs a connection.
  await page.evaluate(({ key }) => localStorage.removeItem(key), planted);
  const cold = await tab(context);
  await cold.goto(base + "/", { waitUntil: "domcontentloaded" });
  await settle(cold, 2500);
  const text = await cold.locator(".kc-signin").textContent().catch(() => "");
  check(cold.url().startsWith(base) && /Signing in needs a connection/.test(text), `a device with no session says a first sign-in needs a connection (${where(cold)})`);
  await cold.screenshot({ path: join(shots, "offline-first-sign-in.png") });
  await cold.close();
  await page.evaluate(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER));

  // The connection back: the banner goes and a page showing its offline state loads again.
  const back = await tab(context);
  await back.goto(base + "/", { waitUntil: "domcontentloaded" });
  await settle(back, 2000);
  await context.setOffline(false);
  await back.evaluate(() => window.dispatchEvent(new Event("online")));
  await back.waitForFunction(() => !document.querySelector(".kc-offline-banner"), null, { timeout: 15000 }).catch(() => {});
  await settle(back, 2500);
  const after = await drawn(back);
  check(!after.banner && after.h1 !== "On this device" && after.navOff.length === 0,
    `the connection back: the banner goes, Home loads from the server, every place is live (${after.h1})`);
  await back.screenshot({ path: join(shots, "back-online.png") });

  // Losing the connection on a page: the page stays, and its controls that ask the server are
  // disabled. Watch on a film not saved here is one of them; on a film saved here it plays from the
  // device instead.
  const lose = async (path) => {
    await context.setOffline(false);
    await back.evaluate(() => window.dispatchEvent(new Event("online")));
    await back.goto(base + path, { waitUntil: "load" });
    await back.waitForSelector(".kc-tt h1", { timeout: 15000 });
    await settle(back, 1500);
    await context.setOffline(true);
    await back.evaluate(() => window.dispatchEvent(new Event("offline")));
    await settle(back, 1000);
    return back.evaluate(() => {
      const watch = document.querySelector(".kc-tt .kc-actions .k-btn--primary");
      return {
        banner: !!document.querySelector(".kc-offline-banner"),
        h1: document.querySelector(".kc-tt h1")?.textContent,
        watchDisabled: !!watch && watch.tagName === "BUTTON" && watch.disabled,
        watchHref: watch && watch.tagName === "A" ? watch.getAttribute("href") : null,
        save: [...document.querySelectorAll(".kc-tt .kc-actions button")].find((b) => /^Save/.test(b.textContent.trim()))?.disabled,
      };
    });
  };

  const unsaved = await lose(`/movies/${UNSAVED.id}`);
  check(unsaved.banner && !!unsaved.h1 && unsaved.watchDisabled && unsaved.save !== false,
    `losing the connection on a film not saved here keeps its page, with Watch and Save disabled (${JSON.stringify(unsaved)})`);
  await back.screenshot({ path: join(shots, "lost-on-unsaved.png") });

  const saved = await lose(`/movies/${FILM}`);
  const offlineHref = new RegExp(`^/r/[0-9a-f]{8}\\?title=${FILM}&offline=1(&at=\\d+)?$`);
  check(saved.banner && !!saved.h1 && offlineHref.test(saved.watchHref || ""),
    `losing the connection on a film saved here turns Watch into playing it from the device (${JSON.stringify(saved)})`);
  await back.screenshot({ path: join(shots, "lost-on-saved.png") });
  await back.locator(".kc-tt .kc-actions .k-btn--primary").click();
  await settle(back, 600);
  check(offlineHref.test(where(back)), `Watch there leads to the offline player address (${where(back)})`);
  await context.close();
}

// The network up and the API not answering: blocked outright, then a gateway's 502 for an API that is down.
for (const [label, answer] of [
  ["blocked", (route) => route.abort("connectionrefused")],
  ["gateway", (route) => route.fulfill({ status: 502, contentType: "text/html", body: "<html><body>502 Bad Gateway</body></html>" })],
]) {
  const { context } = await prepared();
  await context.route(/\/(api|hub|media)\//, answer);
  await walk(context, label);
  await context.close();
}

// A server that answered with a 500 is not offline.
{
  const { context } = await prepared();
  await context.route(/\/api\/titles$/, (route) => route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ error: "Broken." }) }));
  const page = await tab(context);
  await page.goto(base + "/", { waitUntil: "load" });
  await settle(page, 2500);
  const d = await drawn(page);
  check(!d.banner && d.h1 !== "On this device", `a 500 from a server that answered is not offline (${d.h1})`);
  await context.close();
}

await browser.close();
console.log(`\nScreenshots in ${shots}`);
console.log(failed ? `${failed} failed` : "all passed");
process.exit(failed ? 1 : 0);
