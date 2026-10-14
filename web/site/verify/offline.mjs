// The site with no connection, in a real Chromium as a landscape tablet: the installed app opened cold
// in a second tab with the network cut, drawing the same pages from what is downloaded — Home, the
// Library, a downloaded film's page and a show's page drawn whole from the show kept on the device —
// with nothing saying it is offline, every place and control that needs the server disabled, and
// Discover, the assistant and the pages only the server can give saying they are out of reach; then the
// same with the network up and the API not answering (blocked, and a gateway's 502 for an API that is
// down), which has to read the same; a session whose token ran out offline still standing; a device
// with nothing held saying a first sign-in needs a connection; and the connection coming back.
//
//   node verify/offline.mjs
//
// Needs the scratch site verify/mobile/scratch.sh serves, with a show the API can describe. The
// downloads are written into the Origin Private File System by the check itself, in the layout
// `src/offline/CLAUDE.md` sets out, from the scratch library's own listing and artwork; their tracks
// are placeholder bytes, since nothing here plays them. The show is kept by the site, from the API,
// when the page loads with the downloads on the device. Settings (environment): MOVIEBOT_VERIFY_DIR as
// scratch.sh; CHROME_PATH a Chromium, when Playwright's cached one is not wanted. A screenshot of each
// state is left in <dir>/runs/offline-<time>/. Exits 1 when any check fails.

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

const ask = async (path) => {
  const response = await fetch(base + path, { headers: { Authorization: `Bearer ${token}` } });
  return response.ok ? response.json() : null;
};

// The downloads: a film finished, an episode finished, and a film under way.
const titles = await ask("/api/titles");
const byId = new Map(titles.map((t) => [t.id, t]));
const FILM = "the-matrix-1999";
const UNDERWAY = titles.find((t) => !t.episode && t.status === "ready" && t.id !== FILM);
const UNSAVED = titles.find((t) => !t.episode && t.id !== FILM && t.id !== (UNDERWAY && UNDERWAY.id));
let EPISODE = null;
let SHOW = null;
for (const t of titles.filter((x) => x.episode && x.status === "ready" && x.film && x.film.imdbId)) {
  const show = await ask(`/api/shows/${encodeURIComponent(t.film.imdbId)}`);
  if (show && show.seasons.some((x) => x.episodes.length > 1)) {
    EPISODE = t;
    SHOW = show;
    break;
  }
}
if (!byId.has(FILM) || !EPISODE || !UNDERWAY || !UNSAVED) {
  console.error(`The scratch library needs ${FILM}, a ready episode of a show the API describes, and two more films.`);
  process.exit(2);
}
const SHOW_EPISODES = SHOW.seasons.find((x) => x.number === EPISODE.episode.season).episodes.length;

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
    completedAt: complete ? new Date(Date.now() - 26 * 3600e3).toISOString() : null,
    package: {},
  };
}

/** Writes the downloads into this origin's private file system, from the page, with the poster fetched while online. */
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

/** The show the site kept on the device for an account: its `show.json` and how many artwork files beside it, or null. */
const keptShow = (page, imdbId) => page.evaluate(async ({ sub, imdbId }) => {
  const name = (s) => encodeURIComponent(String(s)).replace(/\./g, "%2E");
  try {
    const root = await navigator.storage.getDirectory();
    const dir = await (await (await root.getDirectoryHandle(name(sub))).getDirectoryHandle(".shows")).getDirectoryHandle(name(imdbId));
    const kept = JSON.parse(await (await (await dir.getFileHandle("show.json")).getFile()).text());
    let files = 0;
    for await (const [entry] of dir.entries()) if (entry !== "show.json") files += 1;
    return { seasons: kept.show.seasons.length, listed: kept.files.length, files };
  } catch {
    return null;
  }
}, { sub: VIEWER.id, imdbId });

const posterOf = (t) => `/media/${encodeURIComponent(t.id)}/${encodeURIComponent(t.poster)}`;

let failed = 0;
const check = (ok, line) => { if (!ok) failed++; console.log(`${ok ? "✓" : "✗"} ${line}`); };

const browser = await chromium.launch({ executablePath: chromePath() });

/**
 * A signed-in context with the worker installed and in control, the downloads written, where the
 * person got to in the film logged on the device, and the show kept by the site.
 */
async function prepared() {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, hasTouch: true });
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
  await page.waitForSelector(".kc-heights", { timeout: 15000 });
  await seed(page, [
    { meta: metaFor(byId.get(FILM), { complete: true }), poster: posterOf(byId.get(FILM)) },
    { meta: metaFor(EPISODE, { complete: true }), poster: posterOf(EPISODE) },
    { meta: metaFor(UNDERWAY, { complete: false }), poster: posterOf(UNDERWAY), partial: true },
  ]);
  await page.evaluate(({ sub, film }) => localStorage.setItem("cinema.progress." + sub,
    JSON.stringify([{ titleId: film, positionSeconds: 600, watchedAt: new Date().toISOString() }])), { sub: VIEWER.id, film: FILM });
  // Loading with a connection keeps every show a download belongs to.
  await page.goto(base + "/", { waitUntil: "load" });
  let kept = null;
  for (let i = 0; i < 30 && !(kept && kept.files > 0); i++) {
    await page.waitForTimeout(500);
    kept = await keptShow(page, SHOW.imdbId);
  }
  return { context, page, planted, kept };
}

/** A tab opened cold in `context`, collecting what the page raises. */
async function tab(context) {
  const page = await context.newPage();
  page.errors = [];
  page.on("pageerror", (e) => page.errors.push(e.message));
  return page;
}

const where = (page) => page.url().replace(base, "");

/** What a page drew. */
const drawn = (page) => page.evaluate(() => ({
  says: /offline|connection/i.test(document.body.innerText),
  h1: (document.querySelector("h1") || {}).textContent || null,
  eyebrow: (document.querySelector(".kc-featured .kc-eyebrow") || {}).textContent || null,
  unreachable: [...document.querySelectorAll(".kc-unreachable__title, .kc-unreachable__note")].map((e) => e.textContent.trim()),
  cards: [...document.querySelectorAll(".showcase-card__name")].map((e) => e.textContent.trim()),
  navOff: [...document.querySelectorAll(".kc-nav-off .nav-item__label")].map((e) => e.textContent.trim()),
  navOn: [...document.querySelectorAll(".sidebar__nav > .sidebar__group > .nav-item .nav-item__label")].map((e) => e.textContent.trim()),
  bellOff: !!document.querySelector(".kc-nav-off .kc-bell-item"),
  fab: !!document.querySelector(".dock-fab, .dockfab, [class*='dock-fab']"),
  subtab: [...document.querySelectorAll(".subtab--active")].map((e) => e.textContent.trim()),
  subtabs: [...document.querySelectorAll(".subtab")].map((e) => [e.textContent.trim(), e.disabled]),
}));

async function settle(page, ms = 1200) {
  await page.waitForLoadState("load").catch(() => {});
  await page.waitForTimeout(ms);
}

/** Says `page` draws "You're offline" with `note`, leading to the library. */
async function unreachable(page, label, path, note) {
  const d = await drawn(page);
  const library = await page.locator(".kc-unreachable .k-btn", { hasText: "Library" }).getAttribute("href").catch(() => null);
  check(d.unreachable[0] === "You're offline" && d.unreachable[1] === note && library === "/movies",
    `${label}: ${path} says it is out of reach and leads to the library (${d.unreachable.join(" / ")}, ${library})`);
}

/**
 * Every address with the server out of reach, in tab `page`: the pages the device can draw drawn from
 * it with nothing saying so, the rest out of reach, raising nothing. `label` names the way the server
 * is out.
 */
async function walk(context, label) {
  const page = await tab(context);
  const film = byId.get(FILM);
  const name = film.film.name;
  const showName = SHOW.title;

  await page.goto(base + "/", { waitUntil: "domcontentloaded" });
  await settle(page, 2500);
  let d = await drawn(page);
  check(!d.says && !(await page.locator(".kc-offline-banner").count()), `${label}: nothing on Home says it is offline`);
  check(d.h1 === name && /^Downloaded /.test(d.eyebrow || ""), `${label}: Home's hero is the downloaded film (${d.h1}, ${d.eyebrow})`);
  check(d.cards.includes(name) && d.cards.includes(showName) && d.cards.length === 2,
    `${label}: Home's rails hold the downloaded film and show and nothing else (${d.cards.join(", ")})`);
  const continuing = await page.locator(".kc-continue").count();
  check(continuing === 1, `${label}: Continue watching holds the film started on the device (${continuing})`);
  check(["Discover", "Saved", "Notify me"].every((n) => d.navOff.includes(n)) && d.bellOff,
    `${label}: Discover, Saved, Notify me and the bell are drawn disabled (${d.navOff.join(", ")})`);
  check(["Home", "Movies", "TV Shows"].every((n) => d.navOn.includes(n)) && !d.navOn.includes("Offline") && !d.navOff.includes("Offline"),
    `${label}: Home, Movies and TV Shows stay live, and there is no Offline place (${d.navOn.join(", ")})`);
  check(!d.fab, `${label}: the assistant cannot be opened`);
  await page.screenshot({ path: join(shots, `${label}-home.png`) });

  await page.locator(".showcase-card", { hasText: name }).first().click();
  await settle(page);
  d = await drawn(page);
  check(where(page) === `/movies/${FILM}` && d.h1 === name, `${label}: Home leads to the downloaded film's page (${where(page)}, ${d.h1})`);
  const play = await page.locator(".kc-tt .kc-actions .k-btn--primary").getAttribute("href").catch(() => null);
  check(!!play && /^\/r\/[0-9a-f]{8}\?title=the-matrix-1999&offline=1(&at=600)?$/.test(play), `${label}: the film's Watch plays it from the device (${play})`);
  check(!d.says, `${label}: nothing on the film's page says it is offline`);
  await page.screenshot({ path: join(shots, `${label}-film.png`) });

  await page.goto(base + "/movies", { waitUntil: "domcontentloaded" });
  await settle(page, 2000);
  d = await drawn(page);
  check(d.h1 === "Movies" && d.cards.join() === name, `${label}: the Library lists the downloaded film alone (${d.cards.join(", ")})`);
  await page.locator(".toolbar input, input[type='search']").first().fill("zzzz").catch(() => {});
  await settle(page, 800);
  d = await drawn(page);
  check(d.cards.length === 0, `${label}: the Library's search narrows what is downloaded (${d.cards.length} for "zzzz")`);
  await page.screenshot({ path: join(shots, `${label}-movies.png`) });

  await page.goto(base + "/tv", { waitUntil: "domcontentloaded" });
  await settle(page, 2000);
  d = await drawn(page);
  check(d.h1 === "TV Shows" && d.cards.join() === showName, `${label}: the shows' Library lists the show once (${d.cards.join(", ")})`);

  await page.goto(base + `/tv/${SHOW.imdbId}/season/${EPISODE.episode.season}`, { waitUntil: "domcontentloaded" });
  await settle(page, 2000);
  d = await drawn(page);
  const rows = await page.evaluate(() => ({
    all: document.querySelectorAll(".kc-episode").length,
    playable: [...document.querySelectorAll("a.kc-episode")].map((a) => a.getAttribute("href")),
    dimmed: document.querySelectorAll(".kc-episode.is-off").length,
    stills: [...document.querySelectorAll(".kc-episode__still img")].filter((i) => i.src.startsWith("blob:") && i.naturalWidth > 0).length,
    seasons: document.querySelectorAll(".kc-season-chip").length,
    overview: !!document.querySelector(".kc-tt__overview"),
  }));
  check(d.h1 === showName && rows.seasons === SHOW.seasons.length && rows.all === SHOW_EPISODES,
    `${label}: the show's page draws every season and every episode from the device (${rows.seasons} seasons, ${rows.all} episodes)`);
  check(rows.playable.length === 1 && rows.playable[0].includes(`title=${encodeURIComponent(EPISODE.id)}&offline=1`) && rows.dimmed === SHOW_EPISODES - 1,
    `${label}: the downloaded episode plays from the device and the rest are dimmed (${rows.playable.join(", ")}, ${rows.dimmed} dimmed)`);
  check(rows.stills > 1 && (rows.overview || !SHOW.overview), `${label}: the episodes' stills and the show's description are the device's (${rows.stills} stills)`);
  await page.screenshot({ path: join(shots, `${label}-show.png`), fullPage: true });

  await page.goto(base + "/discover", { waitUntil: "domcontentloaded" });
  await settle(page);
  await unreachable(page, label, "/discover", "Discover isn't available offline.");
  await page.screenshot({ path: join(shots, `${label}-discover.png`) });

  for (const path of ["/discover?q=matrix", "/discover/film/tt0234215"]) {
    await page.goto(base + path, { waitUntil: "domcontentloaded" });
    await settle(page);
    await unreachable(page, label, path, "Discover isn't available offline.");
  }
  for (const path of ["/saved", "/saved/later", "/notify", `/movies/${UNSAVED.id}`, "/r/abcd1234"]) {
    await page.goto(base + path, { waitUntil: "domcontentloaded" });
    await settle(page);
    await unreachable(page, label, path, "This page isn't available offline.");
  }

  await page.goto(base + "/profile", { waitUntil: "domcontentloaded" });
  await settle(page, 2000);
  d = await drawn(page);
  const off = d.subtabs.filter(([, disabled]) => disabled).map(([n]) => n);
  check(where(page) === "/profile/playback" && d.subtab.join() === "Playback", `${label}: Profile opens on Playback (${where(page)})`);
  check(off.join() === "History,Requests,Following,Notifications" && !d.subtabs.some(([n]) => n === "Offline"),
    `${label}: Profile's tabs that need the server are disabled and there is no Offline tab (${off.join(", ")})`);
  const playback = await page.evaluate(() => ({
    drawn: !!document.querySelector(".kc-heights"),
    fixed: !!document.querySelector("fieldset.kc-fieldset:disabled .kc-heights"),
  }));
  check(playback.drawn && playback.fixed, `${label}: Playback is drawn from the device and changes nothing (${JSON.stringify(playback)})`);
  await page.screenshot({ path: join(shots, `${label}-profile.png`) });

  await page.goto(base + "/profile/offline", { waitUntil: "domcontentloaded" });
  await settle(page, 1500);
  check(where(page) === "/profile/playback", `${label}: /profile/offline is no tab, and lands where the profile does (${where(page)})`);

  await page.goto(base + "/about", { waitUntil: "domcontentloaded" });
  await settle(page);
  d = await drawn(page);
  check(d.h1 === "About", `${label}: About is drawn (${d.h1})`);

  check(page.errors.length === 0, `${label}: nothing raised${page.errors.length ? ": " + page.errors.join("; ") : ""}`);
  await page.close();
}

// The network cut.
{
  const { context, page, planted, kept } = await prepared();
  check(!!kept && kept.seasons === SHOW.seasons.length && kept.files === kept.listed && kept.files > 1,
    `loading with a connection keeps the downloaded episode's show on the device (${JSON.stringify(kept)})`);
  await context.setOffline(true);
  await walk(context, "offline");

  // A phone: the tab bar's Discover and Assistant are disabled, and Me leads to the profile.
  const phone = await tab(context);
  await phone.setViewportSize({ width: 390, height: 844 });
  for (const [path, shot] of [["/", "home"], ["/movies", "movies"], [`/tv/${SHOW.imdbId}/season/${EPISODE.episode.season}`, "show"], ["/discover", "discover"]]) {
    await phone.goto(base + path, { waitUntil: "domcontentloaded" });
    await settle(phone, 2500);
    await phone.screenshot({ path: join(shots, `offline-phone-${shot}.png`) });
  }
  await phone.goto(base + "/", { waitUntil: "domcontentloaded" });
  await settle(phone, 2000);
  const tabs = await phone.$$eval(".k-tabbar__tab", (els) => els.map((b) => [b.textContent.trim(), b.disabled]));
  const offTabs = tabs.filter(([, d]) => d).map(([n]) => n);
  check(offTabs.join() === "Discover,Assistant", `a phone's tab bar draws Discover and the Assistant disabled (${offTabs.join(", ")})`);
  const bell = await phone.locator(".kc-phonehead .kc-nav-off .kc-bell-button").count();
  check(bell === 1, "a phone's bell is drawn disabled");
  await phone.locator(".k-tabbar__tab", { hasText: /^Library$/ }).click();
  await settle(phone);
  check(where(phone) === "/movies", `a phone's Library leads to the downloaded films (${where(phone)})`);
  await phone.locator(".k-tabbar__tab", { hasText: /^Me$/ }).click();
  await settle(phone);
  check(where(phone) === "/profile/playback", `a phone's Me leads to the profile (${where(phone)})`);
  await phone.screenshot({ path: join(shots, "offline-phone-me.png") });
  check(phone.errors.length === 0, `a phone offline raises nothing${phone.errors.length ? ": " + phone.errors.join("; ") : ""}`);
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
  const who = await stale.locator(".kc-head h1").textContent().catch(() => null);
  check(stale.url().startsWith(base) && who === VIEWER.name, `an expired session offline still stands for ${VIEWER.name} (${who}, ${where(stale)})`);
  await stale.screenshot({ path: join(shots, "offline-expired.png") });
  await stale.close();

  // Nothing held: a first sign-in needs a connection.
  await page.evaluate(({ key }) => localStorage.removeItem(key), planted);
  const cold = await tab(context);
  await cold.goto(base + "/", { waitUntil: "domcontentloaded" });
  await settle(cold, 2500);
  const text = await cold.locator(".k-cardscreen").textContent().catch(() => "");
  check(cold.url().startsWith(base) && /Signing in needs a connection/.test(text), `a device with no session says a first sign-in needs a connection (${where(cold)})`);
  await cold.screenshot({ path: join(shots, "offline-first-sign-in.png") });
  await cold.close();
  await page.evaluate(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER));

  // The connection back: Home is drawn again from the server and every place is live.
  const back = await tab(context);
  await back.goto(base + "/", { waitUntil: "domcontentloaded" });
  await settle(back, 2000);
  await context.setOffline(false);
  await back.evaluate(() => window.dispatchEvent(new Event("online")));
  await back.waitForFunction(() => !document.querySelector(".kc-nav-off"), null, { timeout: 15000 }).catch(() => {});
  await settle(back, 2500);
  const after = await drawn(back);
  check(!/^Downloaded /.test(after.eyebrow || "") && after.navOff.length === 0 && after.cards.length > 2,
    `the connection back: Home loads from the server and every place is live (${after.eyebrow}, ${after.cards.length} cards)`);
  await back.screenshot({ path: join(shots, "back-online.png") });

  // Losing the connection on a page: the page stays, and its controls that ask the server are
  // disabled. Watch on a film not downloaded here is one of them; on a film downloaded here it plays
  // from the device instead.
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
  check(!unsaved.banner && !!unsaved.h1 && unsaved.watchDisabled && unsaved.save !== false,
    `losing the connection on a film not downloaded here keeps its page, with Watch and Save disabled (${JSON.stringify(unsaved)})`);
  await back.screenshot({ path: join(shots, "lost-on-unsaved.png") });

  const saved = await lose(`/movies/${FILM}`);
  const offlineHref = new RegExp(`^/r/[0-9a-f]{8}\\?title=${FILM}&offline=1(&at=\\d+)?$`);
  check(!saved.banner && !!saved.h1 && offlineHref.test(saved.watchHref || ""),
    `losing the connection on a film downloaded here turns Watch into playing it from the device (${JSON.stringify(saved)})`);
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
  await context.route(/\/(api|hub|media|shows)\//, answer);
  await walk(context, label);
  await context.close();
}

// The show's last episode removed takes the kept show with it: the episode's download is given a
// record the API does not hold, so the next load with a connection removes it from the device.
{
  const { context, page, kept } = await prepared();
  check(!!kept, "the show is kept before its last episode goes");
  await page.evaluate(async ({ sub, titleId }) => {
    const name = (s) => encodeURIComponent(String(s)).replace(/\./g, "%2E");
    const root = await navigator.storage.getDirectory();
    const dir = await (await root.getDirectoryHandle(name(sub))).getDirectoryHandle(name(titleId));
    const meta = JSON.parse(await (await (await dir.getFileHandle("meta.json")).getFile()).text());
    meta.saveId = "verify-no-such-save";
    const w = await (await dir.getFileHandle("meta.json", { create: true })).createWritable();
    await w.write(JSON.stringify(meta));
    await w.close();
  }, { sub: VIEWER.id, titleId: EPISODE.id });
  await page.goto(base + "/", { waitUntil: "load" });
  let gone = null;
  for (let i = 0; i < 30 && gone !== true; i++) {
    await page.waitForTimeout(500);
    gone = (await keptShow(page, SHOW.imdbId)) === null;
  }
  check(gone, "removing the show's last episode removes the kept show");
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
  check(d.navOff.length === 0 && !/^Downloaded /.test(d.eyebrow || ""), `a 500 from a server that answered is not offline (${d.eyebrow})`);
  await context.close();
}

await browser.close();
console.log(`\nScreenshots in ${shots}`);
console.log(failed ? `${failed} failed` : "all passed");
process.exit(failed ? 1 : 0);
