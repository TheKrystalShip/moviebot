// Krystal Cinema driven by a remote alone: every page in TV mode at the reference television's size,
// walked with the arrow keys from where focus starts, against the scratch site verify/mobile/scratch.sh
// serves (staged first, by its run or `node verify/mobile/run.mjs`; an unstaged site stops this run).
//
//   node verify/tv.mjs [--checks pages,heads,empty,overlays,back,player,remote,driving,remoteui,settings,signin] [--pages home,title] [--out dir]
//
// Each page is checked for:
// - focus starting on a control when it opens, and on screen;
// - every control on it reached from there with arrows alone (Menu into a card's own controls and
//   Back out of them counted too), and each one on screen when it is reached;
// - focus never falling to nothing after a key.
// Then a list's page headed by the list's name, visible, with its count of titles; and Saved and a
// list with nothing on them, for a person with nothing saved: the empty state's one
// Discover button focused, the empty list headed by its name without a count, the page centred inside
// the margins, Up and Back reaching the bar from it.
// Then what the keys do: an overlay taking focus when it opens and handing it back when it closes,
// Back going back a page to the control it was left on, Back on Home going to the bar and then
// being left to the app, and the player: the play button focused, OK playing, presses gathered into
// one skip, a held key scrubbing and sending one seek, Menu opening the subtitles, and Back leaving.
// Then a phone, a second browser context of the same person on a session of its own, paired with the
// television by reading its QR code from the screenshot's pixels at the scale a camera across a room
// has, unpaired from each side, and told the television's room once it plays and pauses a film.
// Then the phone driving the film: the site's own remote modules on a blank page of a phone paired with
// the television, which play, pause and seek the room and move the television's player, stay out of the
// room's roster, follow the television into another room, and lose the room when the television leaves.
// Then the phone changing the television's own settings, which the API holds and pushes: the drawers
// for Subtitles, Style, Audio and Quality aimed at the television, each change showing on the
// television's player (the track on the stream, the style on the cues, the audio rendition fetched,
// the quality label), the television's own menus moving the phone's labels, and the room's revision
// and roster standing still throughout. The film has two audio tracks and two subtitle tracks, which
// the check puts in the scratch library when it is not there.
// Last, signing in through the test provider's device grant, where the check decides for the person:
// the code and its QR, approval, a restart, signing out, a refusal, an expired code, a provider that
// cannot be reached, and a session that ends under a page.
//
// A control is what the page draws that takes focus by Tab: not anything hidden or being prepared out
// of sight, not a container that is focusable only to be scrolled, and not a control inside a card
// that is one itself, which Menu reaches. The navigator's own rules are not consulted: this is what a
// person could press with a pointer, and the question is whether a remote reaches it too.
//
// Screenshots of each page with its first focus land in ~/.cache/moviebot-verify/tv/<time>/. Exits 1
// on any failure.

import { existsSync, mkdirSync, readFileSync, readdirSync } from "node:fs";
import { createRequire } from "node:module";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { probeScript } from "./probeBundle.mjs";
import { PEOPLE, VIEWER, memberToken } from "./mobile/stage.mjs";
import { TV_CLIENT, issuerIn } from "./issuer.mjs";

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
const token = memberToken(issuer, VIEWER);
const out = option("out", join(dir, "tv", new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19)));
mkdirSync(out, { recursive: true });

/** The reference television: a Fire TV's WebView draws a 960×540 page. */
const Viewport = { width: 960, height: 540 };
/** How long a page has to load and settle its first focus. */
const SettleMs = 3000;
/** How long one key has to land. Scrolling is instant here: the browser reduces motion. */
const KeyMs = 70;

const api = async (path) => {
  const response = await fetch(base + path, { headers: { Authorization: `Bearer ${token}` } });
  if (!response.ok) throw new Error(`GET ${path}: ${response.status}`);
  return response.json();
};

const titles = await api("/api/titles");
const films = titles.filter((t) => t.status === "ready" && !t.episode);
if (films.length === 0) {
  console.error("The scratch library holds no ready film.");
  process.exit(2);
}
// Saved's pages and the Save menu need what staging puts there: the first film on the viewer's Watch
// later. Without it Saved is an empty page with nothing to focus, and a film's Save is a button that
// saves on a press rather than a menu.
const saved = await api("/api/me/saved");
if (!films[0].film || !saved.includes(films[0].film.imdbId)) {
  console.error("The scratch library is not staged: the first film is not on the viewer's Watch later. Run\n"
    + "`node verify/mobile/run.mjs` once after scratch.sh starts, which needs scratch.sh started with\n"
    + "MOVIEBOT_VERIFY_TMDB_TOKEN_FILE, since saving a title asks the film database about it.");
  process.exit(2);
}
/** A film the scratch library holds video for, which the player checks play. */
const playable = process.env.MOVIEBOT_VERIFY_TV_FILM || "coherence-2013";

const PROFILE_TABS = ["", "/requests", "/following", "/notifications", "/playback", "/remote", "/subtitles", "/appearance", "/account"];
const PAGES = [
  { name: "home", path: "/" },
  { name: "movies", path: "/movies" },
  { name: "tv", path: "/tv" },
  { name: "title", path: "/movies/" + encodeURIComponent(films[0].id) },
  { name: "saved", path: "/saved" },
  { name: "list", path: "/saved/later" },
  { name: "discover", path: "/discover" },
  { name: "notify", path: "/notify" },
  ...PROFILE_TABS.map((tab) => ({ name: "profile" + (tab ? "-" + tab.slice(1) : ""), path: "/profile" + tab })),
  { name: "about", path: "/about" },
];
const pageNames = option("pages", "") ? option("pages").split(",") : null;

function chromePath() {
  if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
  const cache = join(homedir(), ".cache", "ms-playwright");
  for (const entry of readdirSync(cache).filter((d) => d.startsWith("chromium-")).sort().reverse()) {
    const candidate = join(cache, entry, "chrome-linux64", "chrome");
    if (existsSync(candidate)) return candidate;
  }
  throw new Error("No Chromium found. Set CHROME_PATH to a Chrome or Chromium binary.");
}

const failures = [];
const fail = (where, what) => { failures.push(`${where}: ${what}`); console.log(`  ✗ ${what}`); };
const pass = (what) => console.log(`  ✓ ${what}`);

const browser = await chromium.launch({ executablePath: chromePath(), args: ["--autoplay-policy=no-user-gesture-required"] });

async function open(path, person = VIEWER, session = { hours: 6, client: TV_CLIENT }) {
  const context = await browser.newContext({ viewport: Viewport, reducedMotion: "reduce" });
  await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(person, session));
  const page = await context.newPage();
  page.on("pageerror", (e) => failures.push(`${path}: page error ${e.message}`));
  await page.goto(base + path + (path.includes("?") ? "&" : "?") + "tv=1", { waitUntil: "load" });
  await page.waitForTimeout(SettleMs);
  return { page, close: () => context.close() };
}

/** Marks every control on the page with an id of its own, and answers them with the focused one's. */
function tagControls() {
  const Focusable = 'a[href], button, input, select, textarea, [tabindex], [contenteditable="true"]';
  const Native = new Set(["A", "BUTTON", "INPUT", "SELECT", "TEXTAREA", "SUMMARY"]);
  const Card = 'a[href], button, [role="link"][tabindex], [role="button"][tabindex]';
  const Interactive = new Set(["button", "link", "menuitem", "menuitemradio", "menuitemcheckbox", "option", "tab", "slider", "switch", "checkbox", "radio", "combobox", "textbox", "searchbox"]);
  window.__tvNext = window.__tvNext || 1;
  const drawn = (el) => {
    if (el.closest('[aria-hidden="true"], [inert]')) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width >= 1 && r.height >= 1 && s.visibility !== "hidden" && s.display !== "none";
  };
  const controls = [];
  const nested = [];
  for (const el of document.querySelectorAll(Focusable)) {
    if (el.tabIndex < 0 || el.disabled || el.getAttribute("aria-disabled") === "true" || !drawn(el)) continue;
    if (el.tagName === "INPUT" && el.type === "hidden") continue;
    if (!Native.has(el.tagName) && !Interactive.has(el.getAttribute("role")) && el.querySelector(Focusable)) continue;
    if (!el.dataset.tvid) el.dataset.tvid = String(window.__tvNext++);
    const card = el.parentElement && el.parentElement.closest(Card);
    (card ? nested : controls).push(el.dataset.tvid);
  }
  return { controls, nested };
}

/** The focused element: its id, a name for it, and whether it is on screen. */
function focused() {
  const el = document.activeElement;
  if (!el || el === document.body) return null;
  window.__tvNext = window.__tvNext || 1;
  if (!el.dataset.tvid) el.dataset.tvid = String(window.__tvNext++);
  const r = el.getBoundingClientRect();
  const name = (el.getAttribute("aria-label") || el.textContent || el.tagName).trim().replace(/\s+/g, " ").slice(0, 50);
  return {
    id: el.dataset.tvid, name,
    onScreen: r.top >= -1 && r.left >= -1 && r.bottom <= innerHeight + 1 && r.right <= innerWidth + 1,
    rect: `${Math.round(r.left)},${Math.round(r.top)} ${Math.round(r.width)}×${Math.round(r.height)}`,
  };
}

async function press(page, key) {
  await page.keyboard.press(key);
  await page.waitForTimeout(KeyMs);
  return page.evaluate(focused);
}

const nameOf = (page, id) => page.evaluate((tvid) => {
  const el = document.querySelector(`[data-tvid="${tvid}"]`);
  return el ? (el.getAttribute("aria-label") || el.textContent || el.tagName).trim().replace(/\s+/g, " ").slice(0, 50) : `#${tvid}`;
}, id);

/** Every control a page draws, reached with the arrows from where focus starts. */
async function walk({ name, path }) {
  console.log(`${name} (${path})`);
  const { page, close } = await open(path);
  try {
    await page.screenshot({ path: join(out, `${name}.png`) });
    const start = await page.evaluate(focused);
    if (!start) { fail(name, "nothing is focused when the page opens"); return; }
    if (!start.onScreen) fail(name, `the first focus, "${start.name}", is off screen`);
    const { controls, nested } = await page.evaluate(tagControls);

    const reached = new Set([start.id]);
    const queue = [start.id];
    const offScreen = new Set();
    let lost = 0;
    while (queue.length > 0) {
      const id = queue.shift();
      for (const key of ["ArrowDown", "ArrowRight", "ArrowUp", "ArrowLeft"]) {
        const ok = await page.evaluate((tvid) => {
          const el = document.querySelector(`[data-tvid="${tvid}"]`);
          if (!el) return false;
          el.focus({ preventScroll: false });
          return document.activeElement === el;
        }, id);
        if (!ok) break;
        const now = await press(page, key);
        if (!now) { lost++; if (lost <= 3) fail(name, `${key} from "${await nameOf(page, id)}" left nothing focused`); continue; }
        // Where a key moved focus is the navigator's placing; where it stayed is the harness's own focus().
        if (now.id !== id && !now.onScreen) offScreen.add(`${now.name} at ${now.rect}, by ${key} from "${await nameOf(page, id)}"`);
        if (!reached.has(now.id)) { reached.add(now.id); queue.push(now.id); }
      }
    }
    const missed = controls.filter((id) => !reached.has(id));
    if (missed.length > 0) {
      const names = await Promise.all(missed.slice(0, 8).map((id) => nameOf(page, id)));
      fail(name, `${missed.length} of ${controls.length} controls never reached: ${names.map((n) => `"${n}"`).join(", ")}${missed.length > 8 ? ", …" : ""}`);
    } else {
      pass(`all ${controls.length} controls reached`);
    }
    if (offScreen.size > 0) fail(name, `focus landed off screen on ${[...offScreen].slice(0, 5).map((n) => `"${n}"`).join(", ")}`);

    // A card holding controls of its own: Menu goes in among them, Back comes out to the card.
    const cards = await page.evaluate((ids) => {
      const Card = 'a[href], button, [role="link"][tabindex], [role="button"][tabindex]';
      const found = new Map();
      for (const id of ids) {
        const el = document.querySelector(`[data-tvid="${id}"]`);
        const card = el && el.parentElement && el.parentElement.closest(Card);
        if (card && card.dataset.tvid && !found.has(card.dataset.tvid)) found.set(card.dataset.tvid, `${el.tagName}.${el.className}`);
      }
      return [...found.keys()];
    }, nested);
    for (const id of cards.slice(0, 3)) {
      await page.evaluate((tvid) => document.querySelector(`[data-tvid="${tvid}"]`).focus(), id);
      const inside = await press(page, "ContextMenu");
      const within = inside && await page.evaluate(([card, at]) => {
        const c = document.querySelector(`[data-tvid="${card}"]`);
        return c !== null && c.contains(document.querySelector(`[data-tvid="${at}"]`)) && card !== at;
      }, [id, inside.id]);
      if (!within) { fail(name, `Menu on "${await nameOf(page, id)}" did not go into its own controls`); continue; }
      const outAgain = await press(page, "Escape");
      if (!outAgain || outAgain.id !== id) fail(name, `Back from inside "${await nameOf(page, id)}" did not come back to it`);
    }
    if (cards.length > 0) pass(`Menu into a card's own controls and Back out, on ${Math.min(3, cards.length)} of ${cards.length}`);
  } finally {
    await close();
  }
}

/** An overlay takes focus when OK opens it, keeps it while it is open, and hands it back when it closes. */
async function overlay(name, path, trigger) {
  console.log(`${name} (${path})`);
  const { page, close } = await open(path);
  try {
    const found = await page.evaluate((selector) => {
      const el = [...document.querySelectorAll(selector)].find((e) => e.getBoundingClientRect().width > 0);
      if (!el) return false;
      el.dataset.tvid = "opener";
      el.focus();
      return true;
    }, trigger);
    if (!found) { fail(name, `no ${trigger} to open`); return; }
    await page.keyboard.press("Enter");
    await page.waitForTimeout(500);
    const inside = await page.evaluate(() => {
      const open = [...document.querySelectorAll('[role="dialog"], [role="menu"], [aria-modal="true"]')]
        .filter((e) => e.getBoundingClientRect().width > 0 && getComputedStyle(e).visibility !== "hidden");
      const top = open[open.length - 1];
      return top ? top.contains(document.activeElement) : null;
    });
    if (inside === null) { fail(name, "OK opened nothing"); return; }
    if (!inside) fail(name, "focus stayed outside the overlay OK opened");
    else pass("focus moves into the overlay");
    for (const key of ["ArrowDown", "ArrowDown", "ArrowRight", "ArrowUp", "ArrowLeft", "ArrowDown"]) {
      await press(page, key);
      const still = await page.evaluate(() => {
        const open = [...document.querySelectorAll('[role="dialog"], [role="menu"], [aria-modal="true"]')]
          .filter((e) => e.getBoundingClientRect().width > 0);
        return open.length > 0 && open[open.length - 1].contains(document.activeElement);
      });
      if (!still) { fail(name, `${key} took focus out of the open overlay`); break; }
    }
    // Back, as the wrapper sends it.
    const used = await page.evaluate(() => window.krystalTv.back());
    await page.waitForTimeout(400);
    const after = await page.evaluate(() => document.activeElement && document.activeElement.dataset.tvid);
    if (!used) fail(name, "Back with an overlay open was not used");
    else if (after !== "opener") fail(name, "closing the overlay did not hand focus back to what opened it");
    else pass("Back closes it and hands focus back");
  } finally {
    await close();
  }
}

/** Back goes back a page to the control it was left on; on Home it goes to the bar, then is the app's. */
async function backs() {
  console.log("back");
  const { page, close } = await open("/");
  try {
    // Home is drawn anew when it is come back to, so the film is known again by its name.
    const named = (el) => (el.getAttribute("aria-label") || el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 80);
    const card = await page.evaluate((nameOf) => {
      const name = new Function("el", `return (${nameOf})(el)`);
      const el = [...document.querySelectorAll('a[href^="/movies/"], [role="link"]')].find((e) => e.getBoundingClientRect().width > 0 && e.closest("main"));
      if (!el) return null;
      el.focus();
      return name(el);
    }, named.toString());
    if (!card) { fail("back", "Home has no film to open"); return; }
    await page.keyboard.press("Enter");
    await page.waitForTimeout(2000);
    const there = page.url();
    if (!/\/movies\//.test(there)) { fail("back", `OK on a film opened ${there}`); return; }
    const opened = await page.evaluate(focused);
    if (!opened) fail("back", "the film's page opened with nothing focused");
    await page.evaluate(() => window.krystalTv.back());
    await page.waitForTimeout(2000);
    const home = await page.evaluate((nameOf) => {
      const name = new Function("el", `return (${nameOf})(el)`);
      const el = document.activeElement;
      return { path: location.pathname, name: el && el !== document.body ? name(el) : null };
    }, named.toString());
    if (home.path !== "/") fail("back", `Back from the film went to ${home.path}`);
    else if (home.name !== card) fail("back", `Back from the film put focus on "${home.name}", not on "${card}" it was opened from`);
    else pass("Back goes back a page, to the control it was left on");
    const first = await page.evaluate(() => window.krystalTv.back());
    const inNav = await page.evaluate(() => Boolean(document.activeElement && document.activeElement.closest("nav")));
    const second = await page.evaluate(() => window.krystalTv.back());
    if (!first || !inNav) fail("back", "Back on Home did not go to the bar");
    else if (second) fail("back", "Back from the bar on Home was used, so the app would never close");
    else pass("Back on Home goes to the bar, then is left to the app");
  } finally {
    await close();
  }
}

/** The player, as a remote drives it. */
async function player() {
  console.log(`player (${playable})`);
  const room = `verify-tv-${Date.now().toString(36)}`;
  const { page, close } = await open(`/r/${room}?title=${encodeURIComponent(playable)}`);
  const time = () => page.evaluate(() => { const v = document.querySelector("video"); return v ? v.currentTime : null; });
  const paused = () => page.evaluate(() => { const v = document.querySelector("video"); return v ? v.paused : null; });
  const shown = () => page.evaluate(() => Boolean(document.querySelector(".mp-stage.is-shown")));
  try {
    await page.waitForFunction(() => { const v = document.querySelector("video"); return v && v.readyState >= 3; }, null, { timeout: 30000 })
      .catch(() => {});
    if (await page.evaluate(() => !document.querySelector("video") || document.querySelector("video").readyState < 3)) {
      fail("player", `${playable} did not load; the scratch library needs its segments (verify/README)`);
      return;
    }
    const start = await page.evaluate(() => document.activeElement && document.activeElement.className);
    if (!/mp-play|mp-gate__join/.test(start || "")) fail("player", `the player opened with "${start || "nothing"}" focused, not play`);
    await page.keyboard.press("Enter");
    await page.waitForTimeout(1500);
    if (await paused()) { fail("player", "OK on play did not start the film"); return; }
    pass("play is focused, and OK plays");

    await page.waitForTimeout(3500);
    if (await shown()) fail("player", "the controls did not go away while the film played");
    const before = await time();
    await page.keyboard.press("ArrowRight");
    if (!(await shown()) && await page.evaluate(() => document.querySelector(".mp-scrub-row__hint") !== null)) fail("player", "a skip scrubbed");
    await page.waitForTimeout(150);
    await page.keyboard.press("ArrowRight");
    await page.waitForTimeout(1300);
    const jumped = (await time()) - before;
    if (jumped < 17 || jumped > 25) fail("player", `two presses of Right moved the film ${jumped.toFixed(1)}s, not one skip of 20`);
    else pass(`two presses of Right are one skip (${jumped.toFixed(1)}s)`);

    // Pressed slower than presses are gathered: two skips, the second from the bar the first focused.
    await page.waitForTimeout(3500);
    const slow = await time();
    await page.keyboard.press("ArrowRight");
    await page.waitForTimeout(900);
    await page.keyboard.press("ArrowRight");
    await page.waitForTimeout(1300);
    const slowJump = (await time()) - slow;
    const onBar = await page.evaluate(() => document.activeElement && document.activeElement.classList.contains("mp-scrub"));
    if (slowJump < 18 || slowJump > 26 || !onBar) fail("player", `two slow presses of Right moved the film ${slowJump.toFixed(1)}s${onBar ? "" : ", and left focus off the scrub bar"}`);
    else pass(`two slow presses of Right are two skips, from the scrub bar (${slowJump.toFixed(1)}s)`);

    await page.waitForTimeout(3500);
    await page.keyboard.press("ArrowUp");
    await page.waitForTimeout(300);
    if (!(await shown())) fail("player", "Up with the controls away did not bring them back");
    else pass("Up brings the controls back");

    await page.keyboard.press("ContextMenu");
    await page.waitForTimeout(500);
    const menuOpen = await page.evaluate(() => {
      const menu = document.querySelector('.mp-menu[role="menu"]');
      return menu ? menu.contains(document.activeElement) : null;
    });
    if (menuOpen === null) fail("player", "Menu did not open the subtitles");
    else if (!menuOpen) fail("player", "the subtitles opened without focus in them");
    else pass("Menu opens the subtitles, with focus in them");
    await page.evaluate(() => window.krystalTv.back());
    await page.waitForTimeout(400);
    if (await page.evaluate(() => document.querySelector('.mp-menu[role="menu"]') !== null)) fail("player", "Back did not close the subtitles");

    // Held: the first press, then repeats every 50 ms with no keyup between, then one keyup.
    await page.waitForTimeout(3500);
    const held = await time();
    await page.keyboard.down("ArrowRight");
    let previewed = false;
    for (let i = 0; i < 24; i++) {
      await page.waitForTimeout(50);
      await page.keyboard.down("ArrowRight");
      if (!previewed) previewed = await page.evaluate(() => document.querySelector(".mp-scrub-row__hint") !== null);
    }
    const during = (await time()) - held;
    await page.keyboard.up("ArrowRight");
    await page.waitForTimeout(1500);
    const scrubbed = (await time()) - held;
    if (!previewed) fail("player", "holding Right showed no preview of where letting go lands");
    if (during > 4) fail("player", `the film moved ${during.toFixed(1)}s while the key was still held`);
    if (scrubbed < 25) fail("player", `letting go of a held Right moved the film only ${scrubbed.toFixed(1)}s`);
    if (previewed && during <= 4 && scrubbed >= 25) pass(`holding Right previews, and letting go moves the film once (${scrubbed.toFixed(0)}s)`);

    await page.evaluate(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "MediaPlayPause", bubbles: true, cancelable: true })));
    await page.waitForTimeout(800);
    if (!(await paused())) fail("player", "Play/Pause did not pause");
    else pass("Play/Pause pauses");

    await page.evaluate(() => window.krystalTv.back());
    await page.waitForTimeout(1500);
    if (/\/r\//.test(page.url())) fail("player", "Back on a paused film did not leave the room");
    else pass("Back leaves the room");
  } finally {
    await close();
  }
}

/** The test provider's own doors (verify/issuer.mjs), where a check decides for the person. */
const provider = async (path, method = "POST") => (await fetch(issuer.url + path, { method })).json();
const waitingCodes = async () => (await provider("/verify/devices", "GET")).map((d) => d.userCode);

/** Opens `path` on a television holding `planted`, or nothing: the page signs itself in. */
async function openSignedOut(path, planted) {
  const context = await browser.newContext({ viewport: Viewport, reducedMotion: "reduce" });
  if (planted) await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), planted);
  const page = await context.newPage();
  page.on("pageerror", (e) => failures.push(`${path}: page error ${e.message}`));
  await page.goto(base + path + "?tv=1", { waitUntil: "load" });
  return { page, close: () => context.close() };
}

const codeOn = (page, timeout = 8000) =>
  page.locator(".kc-tv-signin__code").textContent({ timeout }).catch(() => null);
const focusedText = (page) => page.evaluate(() => document.activeElement && document.activeElement.textContent.trim());
const until = async (what, timeout = 8000) => {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    if (await what()) return true;
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  return false;
};

/**
 * Signing a television in: the code and its QR shown with nothing held, approval opening the page that
 * was asked for, the session surviving a restart, signing out revoking it, a refusal, an expired code,
 * a provider that cannot be reached, and a session that ends under a page.
 */
async function signIn() {
  console.log("\nsign-in");
  {
    const { page, close } = await openSignedOut("/movies");
    try {
      const code = await codeOn(page);
      const waiting = await waitingCodes();
      if (!code || !waiting.includes(code)) fail("sign-in", `the screen shows ${code}, the provider is waiting on ${waiting.join(", ") || "nothing"}`);
      else pass(`a television with nothing held shows the code the provider issued (${code})`);
      const drawn = await page.locator(".kc-tv-signin__qr svg path").getAttribute("d", { timeout: 5000 }).catch(() => null);
      if (!drawn) fail("sign-in", "no QR code drawn");
      else pass("the QR code is drawn");
      await page.screenshot({ path: join(out, "signin.png") });
      if (await page.evaluate(() => window.krystalTv.back()) !== false) fail("sign-in", "Back on the code screen was kept by the page");
      else pass("Back on the code screen is left to the app");

      // An access token of seventy seconds, which the page renews ten seconds in.
      const alive = async () => (await provider("/verify/sessions", "GET")).filter((s) => s.person.id === VIEWER.id).length;
      const before = await alive();
      await provider("/verify/access?seconds=70");
      await provider(`/verify/decide?code=${code}&decision=approve&person=${VIEWER.id}&name=${encodeURIComponent(VIEWER.name)}`);
      const inside = await until(() => page.locator(".tv-bar").isVisible(), 15000);
      await provider("/verify/access?seconds=300");
      const path = new URL(page.url()).pathname;
      if (!inside || path !== "/movies") fail("sign-in", `approval left ${path}${inside ? "" : " with no bar"}`);
      else pass("approval opens the page that was asked for");

      const stored = () => page.evaluate((client) => {
        const key = Object.keys(localStorage).find((k) => k.startsWith("cinema.oidc.user:") && k.endsWith(":" + client));
        return key ? JSON.parse(localStorage.getItem(key)) : null;
      }, TV_CLIENT);
      const first = await stored();
      const renewed = await until(async () => {
        const now = await stored();
        return now && first && now.access_token !== first.access_token && now.refresh_token !== first.refresh_token;
      }, 30000);
      const sessions = await alive() - before;
      if (!first || !renewed || sessions !== 1 || !(await page.locator(".tv-bar").isVisible())) {
        fail("sign-in", `the session ${renewed ? "renewed" : "did not renew"} on its own, ${sessions} new at the provider`);
      } else pass("the session renews on its own, rotating its refresh token");

      await page.reload({ waitUntil: "load" });
      await page.waitForTimeout(SettleMs);
      if (await page.locator(".kc-tv-signin").count() > 0 || !(await page.locator(".tv-bar").isVisible())) fail("sign-in", "the session did not survive a restart");
      else pass("the session survives a restart");

      await page.goto(base + "/profile/account", { waitUntil: "load" });
      await page.waitForTimeout(SettleMs);
      await page.getByRole("button", { name: "Sign out" }).first().click();
      const back = await codeOn(page);
      const left = await alive() - before;
      if (!back || left !== 0) fail("sign-in", `signing out left ${left} session(s) at the provider${back ? "" : " and no code shown"}`);
      else pass("signing out revokes the session and shows a new code");
    } finally {
      await close();
    }
  }
  {
    const { page, close } = await openSignedOut("/");
    try {
      const code = await codeOn(page);
      await provider(`/verify/decide?code=${code}&decision=deny&person=${VIEWER.id}`);
      const refused = await until(() => page.getByText("Sign-in refused").isVisible(), 15000);
      const focused = await focusedText(page);
      if (!refused || focused !== "New code") fail("sign-in", `a refusal shows ${refused ? "itself" : "nothing"} with focus on ${focused}`);
      else pass("a refusal is shown with New code focused");
      await page.keyboard.press("Enter");
      const next = await codeOn(page);
      if (!next || next === code) fail("sign-in", "New code drew no new code");
      else pass("New code draws a new code");
    } finally {
      await close();
    }
  }
  {
    await provider("/verify/lifetime?seconds=6");
    const { page, close } = await openSignedOut("/");
    try {
      const first = await codeOn(page);
      await provider("/verify/lifetime?seconds=600");
      const replaced = await until(async () => {
        const now = await codeOn(page, 500);
        return now && now !== first;
      }, 20000);
      if (!replaced) fail("sign-in", "an expired code was not replaced");
      else pass("an expired code is replaced unasked");
    } finally {
      await provider("/verify/lifetime?seconds=600");
      await close();
    }
  }
  {
    await provider("/verify/down?on=1");
    const { page, close } = await openSignedOut("/");
    try {
      const shown = await until(() => page.getByText("Can't reach sign-in").isVisible(), 15000);
      const focused = await focusedText(page);
      if (!shown || focused !== "Retry") fail("sign-in", `a provider that cannot be reached shows ${shown ? "itself" : "nothing"} with focus on ${focused}`);
      else pass("a provider that cannot be reached is shown with Retry focused");
      await page.screenshot({ path: join(out, "signin-unreachable.png") });
      await provider("/verify/down?on=0");
      if (!(await codeOn(page, 15000))) fail("sign-in", "no code once the provider answered again");
      else pass("a code is drawn once the provider answers again, unasked");
    } finally {
      await provider("/verify/down?on=0");
      await close();
    }
  }
  {
    // Fifteen seconds after opening, the renewal presents a refresh token the provider never issued.
    const planted = issuer.planted(VIEWER, { seconds: 75, client: TV_CLIENT, refresh: "ended" });
    const { page, close } = await openSignedOut("/movies", planted);
    try {
      await page.waitForTimeout(SettleMs);
      await page.evaluate(() => { document.querySelector(".kc-tv-main").dataset.kept = "1"; });
      const covered = await until(() => page.locator(".kc-tv-signin.is-over").isVisible(), 30000);
      const under = await page.evaluate(() => {
        const main = document.querySelector('.kc-tv-main[data-kept="1"]');
        return !!main && !!main.closest("[inert]");
      });
      if (!covered || !under) fail("sign-in", `an ended session ${covered ? "covered" : "did not cover"} the page${under ? "" : ", which was not kept out of reach beneath"}`);
      else pass("an ended session puts the code over the page, out of reach beneath it");
      await page.screenshot({ path: join(out, "signin-over.png") });
      if (await page.evaluate(() => window.krystalTv.back()) !== false) fail("sign-in", "Back over an ended session was kept by the page");
      else pass("Back over an ended session is left to the app");
      const code = await codeOn(page);
      await provider(`/verify/decide?code=${code}&decision=approve&person=${VIEWER.id}&name=${encodeURIComponent(VIEWER.name)}`);
      const gone = await until(async () => await page.locator(".kc-tv-signin").count() === 0, 15000);
      const kept = await page.evaluate(() => {
        const main = document.querySelector('.kc-tv-main[data-kept="1"]');
        return !!main && !main.closest("[inert]");
      });
      if (!gone || !kept) fail("sign-in", `approval ${gone ? "" : "did not take the code away and "}${kept ? "" : "did not return to the page it covered"}`);
      else pass("approval returns to the page the code covered");
    } finally {
      await close();
    }
  }
}

/**
 * A list page's heading: the list's name as the page's h1, drawn with a size, inside the screen, in a
 * colour that reads on the canvas; and the count of titles beside it only when there is a title.
 */
async function checkHead(name, page, expected, counted) {
  const head = await page.evaluate(() => {
    const h = document.querySelector("main h1");
    if (!h) return null;
    const r = h.getBoundingClientRect();
    const colour = getComputedStyle(h).color.match(/[\d.]+/g).map(Number);
    let opacity = colour.length > 3 ? colour[3] : 1;
    for (let el = h; el; el = el.parentElement) opacity *= Number(getComputedStyle(el).opacity);
    const facts = document.querySelector("main .kc-tvsv__head .kc-tvsv__facts");
    return {
      text: h.textContent, width: r.width, height: r.height, left: r.left, top: r.top, right: r.right, bottom: r.bottom,
      visibility: getComputedStyle(h).visibility, opacity, facts: facts && facts.textContent,
    };
  });
  if (!head) return fail(name, "the page draws no heading");
  if (head.text !== expected) fail(name, `the heading reads "${head.text}", not "${expected}"`);
  if (head.width < 1 || head.height < 1 || head.visibility !== "visible") fail(name, "the heading has no size or is hidden");
  if (head.left < 0 || head.top < 0 || head.right > Viewport.width || head.bottom > Viewport.height) fail(name, "the heading is outside the screen");
  if (head.opacity < 0.9) fail(name, `the heading is drawn at ${head.opacity.toFixed(2)} opacity`);
  if (counted && !/^\d+ titles?$/.test(head.facts || "")) fail(name, `the heading's count reads "${head.facts}"`);
  if (!counted && head.facts) fail(name, `an empty list shows "${head.facts}" beside its name`);
}

/** A list with titles, as a person sees it: Watch later and one of their own, each headed by its name. */
async function heads() {
  const made = await fetch(base + "/api/lists", {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ name: "Friday night" }),
  }).then((r) => r.json());
  await fetch(base + `/api/lists/${made.list.id}/entries/${films[0].film.imdbId}`, {
    method: "PUT",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ kind: "film" }),
  });
  for (const [name, path, expected] of [["head-later", "/saved/later", "Watch later"], ["head-list", "/saved/" + made.list.id, "Friday night"]]) {
    console.log(`${name} (${path})`);
    const { page, close } = await open(path);
    try {
      await page.screenshot({ path: join(out, `${name}.png`) });
      const before = failures.length;
      await checkHead(name, page, expected, true);
      if (failures.length === before) pass("list heading: its name, visible, with the count of titles");
    } finally {
      await close();
    }
  }
}

/**
 * Saved and a list with nothing on them, as a person with nothing saved sees them: Discover is the one
 * control, it holds focus when the page opens, and Up and Back reach the bar from it.
 */
async function empties() {
  // The first has nothing at all; the second owns a list with nothing in it, and so has a Saved page of its own.
  const nobody = PEOPLE.empty;
  const mine = await fetch(base + "/api/lists", {
    method: "POST",
    headers: { Authorization: `Bearer ${issuer.mint(PEOPLE.lister)}`, "Content-Type": "application/json" },
    body: JSON.stringify({ name: "Nothing yet" }),
  }).then((r) => r.json());
  const headed = { "empty-later": "Watch later", "empty-list": "Nothing yet" };
  for (const [name, path, person] of [["empty-saved", "/saved", nobody], ["empty-later", "/saved/later", nobody], ["empty-list", "/saved/" + mine.list.id, PEOPLE.lister]]) {
    console.log(`${name} (${path})`);
    const { page, close } = await open(path, person);
    try {
      await page.screenshot({ path: join(out, `${name}.png`) });
      if (headed[name]) await checkHead(name, page, headed[name], false);
      const start = await page.evaluate(focused);
      const shape = await page.evaluate(() => {
        const main = document.querySelector("main");
        const box = (el) => { const r = el.getBoundingClientRect(); return { left: r.left, top: r.top, right: r.right, bottom: r.bottom }; };
        const empty = main.querySelector(".k-empty");
        const button = main.querySelector(".k-empty__actions a, .k-empty__actions button");
        return {
          title: empty && empty.querySelector(".k-empty__title") && empty.querySelector(".k-empty__title").textContent,
          prose: empty ? empty.querySelectorAll(".k-empty__body, p").length : 0,
          controls: main.querySelectorAll("a[href], button").length,
          box: empty && box(empty),
          button: button && button.textContent.trim(),
          inMain: Boolean(button && document.activeElement === button),
        };
      });
      if (!shape.title) { fail(name, "the page draws no empty state"); continue; }
      if (shape.title !== "Nothing saved yet") fail(name, `the title reads "${shape.title}"`);
      if (shape.prose > 0) fail(name, "the empty state carries prose under its title");
      if (shape.controls !== 1 || shape.button !== "Discover") fail(name, `the page holds ${shape.controls} control(s), the button reading "${shape.button}"; wanted Discover alone`);
      if (!start || !shape.inMain) { fail(name, `focus starts on "${start && start.name}", not on Discover`); continue; }
      const margin = { x: 48, y: 27 };
      const b = shape.box;
      if (b.left < margin.x || b.right > Viewport.width - margin.x || b.bottom > Viewport.height - margin.y) fail(name, "the empty state leaves the television's safe margins");
      const centre = (b.left + b.right) / 2;
      if (Math.abs(centre - Viewport.width / 2) > 2) fail(name, `the empty state is centred at ${Math.round(centre)}, not ${Viewport.width / 2}`);
      const pill = await page.evaluate(() => getComputedStyle(document.activeElement).backgroundColor);
      if (!/^rgb\(2[45]\d, 2[45]\d, 2[45]\d\)$|^rgb\(255, 255, 255\)$/.test(pill)) fail(name, `the focused Discover is ${pill}, not a light pill`);
      await page.screenshot({ path: join(out, `${name}-focused.png`) });
      await page.keyboard.press("ArrowUp");
      await page.waitForTimeout(KeyMs);
      const up = await page.evaluate(() => Boolean(document.activeElement && document.activeElement.closest("nav")));
      await page.keyboard.press("ArrowDown");
      await page.waitForTimeout(KeyMs);
      const down = await page.evaluate(() => document.activeElement && document.activeElement.textContent.trim());
      const used = await page.evaluate(() => window.krystalTv.back());
      await page.waitForTimeout(KeyMs);
      const back = await page.evaluate(() => Boolean(document.activeElement && document.activeElement.closest("nav")));
      if (!up) fail(name, "Up from Discover did not reach the bar");
      if (down !== "Discover") fail(name, `Down from the bar landed on "${down}", not Discover`);
      if (!used || !back) fail(name, "Back from Discover did not move focus to the bar");
      await page.keyboard.press("ArrowDown");
      await page.keyboard.press("Enter");
      await page.waitForTimeout(1500);
      if (!new URL(page.url()).pathname.startsWith("/discover")) fail(name, `OK on Discover opened ${page.url()}`);
      else pass("empty state: Discover focused and led to; Up and Back reach the bar");
    } finally {
      await close();
    }
  }
}

/**
 * How a phone's camera across a room sees the television's page: a 55" set is 1.21 m wide, so a CSS
 * pixel of its 960-wide page is 1.26 mm; a phone at 3 m with a 67° field of view spans 4 m, analysed
 * 1920 pixels wide. One page pixel is then 0.6 of a camera pixel. A code is checked decoded from a
 * screenshot scaled to that, which is the pixel budget a scanner has from the sofa, and nothing of its
 * optics.
 */
const SofaScale = 0.6;
const PhoneAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Mobile Safari/537.36";
const jsQrPath = createRequire(import.meta.url).resolve("jsqr/dist/jsQR.js");

/** The text of the QR code drawn in `selector`, read from its pixels at `scale`, or null when nothing reads. */
async function readQr(page, selector, scale) {
  const shot = await page.locator(selector).screenshot();
  const reader = await browser.newPage();
  try {
    await reader.addScriptTag({ path: jsQrPath });
    return await reader.evaluate(async ({ png, scale: s }) => {
      const img = new Image();
      img.src = "data:image/png;base64," + png;
      await img.decode();
      const w = Math.round(img.width * s);
      const h = Math.round(img.height * s);
      const canvas = document.createElement("canvas");
      canvas.width = w;
      canvas.height = h;
      const g = canvas.getContext("2d");
      g.imageSmoothingQuality = "high";
      g.drawImage(img, 0, 0, w, h);
      const read = window.jsQR(g.getImageData(0, 0, w, h).data, w, h);
      return read ? read.data : null;
    }, { png: shot.toString("base64"), scale });
  } finally {
    await reader.close();
  }
}

/** A phone of the viewer's, signed in on the site in a session of its own. */
async function phoneOf(sid) {
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 }, userAgent: PhoneAgent, isMobile: true, hasTouch: true, reducedMotion: "reduce",
  });
  await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER, { hours: 6, sid }));
  const page = await context.newPage();
  page.on("pageerror", (e) => failures.push(`phone: page error ${e.message}`));
  return { page, close: () => context.close() };
}

/** The screens the phone's session is paired with, as the API answers it. */
const pairedWith = (sid) => fetch(base + "/api/me/screens", { headers: { Authorization: `Bearer ${issuer.mint(VIEWER, { sid })}` } })
  .then((r) => r.json());

/**
 * Pairing a phone with the television by its QR code: the code drawn in Profile's Remote and read from
 * its pixels at the sofa's scale, the phone opening it and landing Home paired, with Undo and remote mode
 * on; the television listing the phone by its browser, the remote reaching its Unpair, and Unpair and
 * Undo each ending the pairing on both sides; then the code beside the paused player's card, read the
 * same way, and the paired phone told which room the television is in and that it is paused.
 */
async function remote() {
  console.log("remote");
  const stamp = Date.now().toString(36);
  const tvSession = { hours: 6, client: TV_CLIENT, sid: `verify-tv-${stamp}`, device: "Verify TV" };
  const phoneSid = `verify-phone-${stamp}`;
  const tv = await open("/profile/remote", VIEWER, tvSession);
  const phone = await phoneOf(phoneSid);
  try {
    await tv.page.waitForSelector(".kc-tvr__qr svg", { timeout: 10000 }).catch(() => {});
    await tv.page.screenshot({ path: join(out, "remote.png") });
    const drawn = await tv.page.evaluate(() => {
      const svg = document.querySelector(".kc-tvr__qr svg");
      const title = document.querySelector(".kc-tvp__title");
      return { modules: svg && Number(svg.dataset.modules), width: svg && svg.getBoundingClientRect().width, title: title && title.textContent };
    });
    if (!drawn.modules) { fail("remote", "Profile's Remote draws no code"); return; }
    if (drawn.modules > 33) fail("remote", `the code is ${drawn.modules} modules across, past version 4`);
    if (!/Verify TV/.test(drawn.title || "")) fail("remote", `the title reads "${drawn.title}", without the television's name`);
    const url = await readQr(tv.page, ".kc-tvr__code", SofaScale);
    if (!url || !url.startsWith(base + "/p/")) { fail("remote", `the code read from the sofa's scale is ${JSON.stringify(url)}`); return; }
    pass(`Profile's Remote draws a ${drawn.modules}-module code ${Math.round(drawn.width)} px wide, read at the sofa's scale: ${url.length} characters`);

    await phone.page.goto(url, { waitUntil: "load" });
    await phone.page.waitForFunction(() => location.pathname === "/" && document.querySelector(".toast"), null, { timeout: 15000 }).catch(() => {});
    const landed = await phone.page.evaluate(() => ({
      path: location.pathname,
      toast: document.querySelector(".toast__title") && document.querySelector(".toast__title").textContent,
      undo: document.querySelector(".toast__action") && document.querySelector(".toast__action").textContent,
      mode: localStorage.getItem("cinema:remote"),
    }));
    await phone.page.screenshot({ path: join(out, "remote-phone-paired.png") });
    if (landed.path !== "/" || landed.toast !== "Paired with Verify TV" || landed.undo !== "Undo" || landed.mode !== "1")
      fail("remote", `the phone landed on ${landed.path} with "${landed.toast}" (${landed.undo}), remote mode ${landed.mode}`);
    else pass("the phone pairs at once and lands Home with the toast and Undo, remote mode on");

    await tv.page.waitForSelector(".kc-tvr__phone", { timeout: 5000 }).catch(() => {});
    const listed = await tv.page.evaluate(() => [...document.querySelectorAll(".kc-tvr__phone")].map((row) => row.textContent));
    if (listed.length !== 1 || !/Chrome on Android/.test(listed[0]) || !/Paired \d+ \w+/.test(listed[0]))
      fail("remote", `the television lists ${JSON.stringify(listed)}`);
    else pass(`the television lists the phone: "${listed[0]}"`);

    // Focus follows the page's first control as its content arrives, so the phone's Unpair takes it, and
    // its row lifts; Left goes to the section's place in the list and Right comes back.
    const first = await tv.page.evaluate(focused);
    const lifted = () => tv.page.evaluate(() => {
      const row = document.activeElement && document.activeElement.closest(".kc-tvr__phone");
      return row ? getComputedStyle(row).transform !== "none" : false;
    });
    await tv.page.screenshot({ path: join(out, "remote-unpair-focused.png") });
    const firstLifted = await lifted();
    const left = await press(tv.page, "ArrowLeft");
    const right = await press(tv.page, "ArrowRight");
    if (!first || first.name !== "Unpair" || !first.onScreen || !firstLifted)
      fail("remote", `focus rests on "${first && first.name}" once the phone is listed${firstLifted ? "" : ", its row not lifted"}`);
    else if (!left || left.name !== "Remote" || !right || right.name !== "Unpair")
      fail("remote", `Left from Unpair reached "${left && left.name}" and Right from there "${right && right.name}"`);
    else pass("Unpair takes focus once the phone is listed, its row lifts, and Left and Right go to Remote and back");
    await tv.page.keyboard.press("Enter");
    await tv.page.waitForFunction(() => !document.querySelector(".kc-tvr__phone"), null, { timeout: 5000 }).catch(() => {});
    await tv.page.waitForTimeout(300);
    const afterUnpair = await tv.page.evaluate(() => ({
      rows: document.querySelectorAll(".kc-tvr__phone").length,
      focused: document.activeElement && document.activeElement !== document.body ? document.activeElement.textContent.trim() : null,
    }));
    const phoneSees = await pairedWith(phoneSid);
    if (afterUnpair.rows !== 0 || phoneSees.length !== 0) fail("remote", `Unpair on the television left ${afterUnpair.rows} row(s) and the phone paired with ${phoneSees.length}`);
    else if (afterUnpair.focused !== "Remote") fail("remote", `unpairing the last phone left focus on "${afterUnpair.focused}", not the section's place in the list`);
    else pass("Unpair on the television ends the pairing for the phone too, and focus goes back to Remote in the list");

    // Paired again by a fresh scan, and undone from the phone's toast.
    const again = await readQr(tv.page, ".kc-tvr__code", SofaScale);
    await phone.page.goto(again, { waitUntil: "load" });
    await phone.page.waitForSelector(".toast__action", { timeout: 15000 }).catch(() => {});
    await tv.page.waitForSelector(".kc-tvr__phone", { timeout: 5000 }).catch(() => {});
    await phone.page.click(".toast__action").catch(() => {});
    await tv.page.waitForFunction(() => !document.querySelector(".kc-tvr__phone"), null, { timeout: 5000 }).catch(() => {});
    const undone = await tv.page.evaluate(() => document.querySelectorAll(".kc-tvr__phone").length);
    await phone.page.waitForFunction(() => localStorage.getItem("cinema:remote") === "0", null, { timeout: 5000 }).catch(() => {});
    const mode = await phone.page.evaluate(() => localStorage.getItem("cinema:remote"));
    if (undone !== 0 || (await pairedWith(phoneSid)).length !== 0 || mode !== "0")
      fail("remote", `Undo left ${undone} row(s) on the television, remote mode ${mode}`);
    else pass("Undo on the phone ends the pairing, and the television's list empties");

    // Paired once more, then the television plays a film and pauses it: the code beside the card.
    await phone.page.goto(await readQr(tv.page, ".kc-tvr__code", SofaScale), { waitUntil: "load" });
    await phone.page.waitForSelector(".toast__action", { timeout: 15000 }).catch(() => {});
    const room = `verify-remote-${stamp}`;
    await tv.page.goto(`${base}/r/${room}?title=${encodeURIComponent(playable)}`, { waitUntil: "load" });
    await tv.page.waitForFunction(() => { const v = document.querySelector("video"); return v && v.readyState >= 3; }, null, { timeout: 30000 }).catch(() => {});
    await tv.page.keyboard.press("Enter");
    await tv.page.waitForTimeout(1500);
    await tv.page.evaluate(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "MediaPlayPause", bubbles: true, cancelable: true })));
    await tv.page.waitForSelector(".kc-tvr-paused__qr svg", { timeout: 10000 }).catch(() => {});
    await tv.page.waitForTimeout(700);
    await tv.page.screenshot({ path: join(out, "remote-paused.png") });
    const beside = await tv.page.evaluate(() => {
      const card = document.querySelector(".mp-pausecard__text");
      const code = document.querySelector(".kc-tvr-paused__figure");
      if (!card || !code) return null;
      const a = card.getBoundingClientRect();
      const b = code.getBoundingClientRect();
      return { cardMiddle: (a.top + a.bottom) / 2, codeMiddle: (b.top + b.bottom) / 2, right: b.right, overlaps: b.left < a.right };
    });
    if (!beside) { fail("remote", "the paused player draws no code beside its card"); return; }
    if (Math.abs(beside.cardMiddle - beside.codeMiddle) > 2 || beside.overlaps || beside.right > Viewport.width - 47)
      fail("remote", `the paused code sits at ${Math.round(beside.codeMiddle)} beside a card at ${Math.round(beside.cardMiddle)}${beside.overlaps ? ", over it" : ""}`);
    const pausedUrl = await readQr(tv.page, ".kc-tvr-paused__qr", SofaScale);
    if (!pausedUrl || !pausedUrl.startsWith(base + "/p/")) fail("remote", `the paused code read from the sofa's scale is ${JSON.stringify(pausedUrl)}`);
    else pass("the paused player shows the code beside its card, centred with it, read at the sofa's scale");

    const screens = await pairedWith(phoneSid);
    const screen = screens.find((s) => s.id === tvSession.sid);
    if (!screen || screen.room !== room || screen.paused !== true || screen.titleId !== playable)
      fail("remote", `the phone is told ${JSON.stringify(screens)}`);
    else pass("the paired phone is told the television's room, its film and that it is paused");
  } finally {
    await fetch(base + `/api/me/screens/${tvSession.sid}`, { method: "DELETE", headers: { Authorization: `Bearer ${issuer.mint(VIEWER, { sid: phoneSid })}` } });
    await phone.close();
    await tv.close();
  }
}

/**
 * A phone with nothing on screen driving the television's room. The television plays a film and pauses it
 * to show its code, which is read and paired through the API; the phone's remote modules then hold a room
 * connection as a remote of that screen. Its play, pause and seek move the television's player, the room's
 * roster holds the television's person once, the television opening another room carries the remote to it,
 * and the television leaving rooms leaves it in none.
 */
async function driving() {
  console.log("driving");
  const stamp = Date.now().toString(36);
  const tvSession = { hours: 6, client: TV_CLIENT, sid: `verify-drive-tv-${stamp}`, device: "Verify TV" };
  const phoneSid = `verify-drive-phone-${stamp}`;
  const first = `verify-drive-a-${stamp}`;
  const second = `verify-drive-b-${stamp}`;
  const tv = await open("/profile/remote", VIEWER, tvSession);
  const phoneContext = await browser.newContext({ viewport: { width: 390, height: 844 }, userAgent: PhoneAgent, isMobile: true, hasTouch: true, bypassCSP: true });
  await phoneContext.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER, { hours: 6, sid: phoneSid }));
  const phone = await phoneContext.newPage();
  phone.on("pageerror", (e) => { failures.push(`driving: phone page error ${e.message}`); if (process.env.DEBUG_PROBE) console.log("pageerror", e.message); });
  phone.on("console", (m) => { if (process.env.DEBUG_PROBE) console.log("phone console", m.type(), m.text()); });
  const phoneAuth = { Authorization: `Bearer ${issuer.mint(VIEWER, { sid: phoneSid })}` };
  const video = (page, read) => page.evaluate(read, null);
  const tvPaused = () => video(tv.page, () => { const v = document.querySelector("video"); return v ? v.paused : null; });
  const tvTime = () => video(tv.page, () => { const v = document.querySelector("video"); return v ? v.currentTime : null; });
  const roster = async (room) => (await (await fetch(`${base}/api/sessions/${room}/participants`, { headers: { Authorization: `Bearer ${token}` } })).json());
  const loaded = (page) => page.waitForFunction(() => { const v = document.querySelector("video"); return v && v.readyState >= 3; }, null, { timeout: 30000 }).catch(() => {});
  try {
    await tv.page.goto(`${base}/r/${first}?title=${encodeURIComponent(playable)}&tv=1`, { waitUntil: "load" });
    await loaded(tv.page);
    await tv.page.keyboard.press("Enter");
    await tv.page.waitForTimeout(1500);
    await tv.page.evaluate(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "MediaPlayPause", bubbles: true, cancelable: true })));
    await tv.page.waitForSelector(".kc-tvr-paused__qr svg", { timeout: 10000 }).catch(() => {});
    await tv.page.waitForTimeout(700);
    const url = await readQr(tv.page, ".kc-tvr-paused__qr", SofaScale);
    if (!url) { fail("driving", "the paused player drew no code to pair with"); return; }
    const paired = await fetch(base + "/api/me/screens", {
      method: "POST", headers: { ...phoneAuth, "Content-Type": "application/json", "User-Agent": PhoneAgent },
      body: JSON.stringify({ code: new URL(url).pathname.slice("/p/".length) }),
    });
    if (!paired.ok) { fail("driving", `pairing from the television's code answered ${paired.status}`); return; }

    await phone.goto(base + "/api/config", { waitUntil: "load" });
    await phone.addScriptTag({ content: await probeScript(), type: "module" });
    await phone.waitForFunction(() => window.__phone, null, { timeout: 10000 });
    await phone.evaluate(() => window.__phone.start());
    const live = await until(() => phone.evaluate(() => { const r = window.__phone.room(); return r.live && r.state !== null; }), 10000);
    const held = await phone.evaluate(() => window.__phone.room());
    if (!live || held.state.sessionId !== first || held.state.titleId !== playable || held.state.paused !== true)
      fail("driving", `the remote holds ${JSON.stringify(held)}, not the paused room ${first}`);
    else pass("the phone is a remote of the television, in its paused room, holding its state");

    const people = await roster(first);
    if (people.length !== 1) fail("driving", `the room's roster is ${JSON.stringify(people)} with a remote attached, not the television's person alone`);
    else pass("the roster holds the television's person alone with the remote attached");

    await phone.evaluate(() => window.__phone.commands.play());
    const played = await until(async () => (await tvPaused()) === false, 6000);
    if (!played) fail("driving", "the remote's play did not start the television's player");
    else pass("the remote's play starts the television's player");

    await phone.evaluate(() => window.__phone.commands.seek(60));
    const sought = await until(async () => Math.abs((await tvTime()) - 61) < 8, 6000);
    if (!sought) fail("driving", `the remote's seek to 60 left the television at ${await tvTime()}`);
    else pass("the remote's seek moves the television's player");

    await phone.evaluate(() => window.__phone.commands.skip(-15));
    const skipped = await until(async () => { const at = await tvTime(); return at > 40 && at < 52; }, 6000);
    if (!skipped) fail("driving", `the remote's back fifteen left the television at ${await tvTime()}`);
    else pass("the remote's back fifteen moves the television's player back");

    await phone.evaluate(() => window.__phone.commands.pause());
    const stopped = await until(async () => (await tvPaused()) === true, 6000);
    if (!stopped) fail("driving", "the remote's pause did not stop the television's player");
    else pass("the remote's pause stops the television's player");

    // The television opens another room: the remote is carried to it, and acts on it.
    await tv.page.goto(`${base}/r/${second}?title=${encodeURIComponent(playable)}&tv=1`, { waitUntil: "load" });
    await loaded(tv.page);
    const followed = await until(() => phone.evaluate((room) => window.__phone.room().state?.sessionId === room, second), 10000);
    if (!followed) fail("driving", `the remote did not follow the television into ${second}: ${JSON.stringify(await phone.evaluate(() => window.__phone.room()))}`);
    else {
      await phone.evaluate(() => window.__phone.commands.play());
      const movedOn = await until(async () => (await tvPaused()) === false, 6000);
      const leftBehind = (await roster(first)).length === 0;
      if (!movedOn || !leftBehind) fail("driving", `after following, play moved the television: ${movedOn}; the first room is empty: ${leftBehind}`);
      else pass("the remote follows the television into another room and drives that one");
    }

    // The television leaves rooms for Home: the remote is in none, and refuses to act on a room that is not there.
    await tv.page.goto(`${base}/?tv=1`, { waitUntil: "load" });
    const emptied = await until(() => phone.evaluate(() => window.__phone.room().state === null), 10000);
    const refused = await phone.evaluate(() => window.__phone.commands.play().then(() => null, (e) => e.message));
    if (!emptied || !refused) fail("driving", `the television left its room and the remote holds ${emptied ? "no room, and play was accepted" : "a room"}`);
    else pass(`the remote leaves with the television, and play is refused ("${refused}")`);
  } finally {
    await fetch(base + `/api/me/screens/${tvSession.sid}`, { method: "DELETE", headers: phoneAuth });
    await phoneContext.close();
    await tv.close();
  }
}


/** A finger on a phone page, as the browser's own input pipeline gets one (a synthesized scroll gesture scrolls nothing). */
function fingerOn(cdp) {
  const send = (type, x, y) => cdp.send("Input.dispatchTouchEvent", { type, touchPoints: type === "touchEnd" ? [] : [{ x, y, id: 1 }] });
  const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
  return {
    async tap(x, y) { await send("touchStart", x, y); await wait(40); await send("touchEnd", x, y); },
    /** Presses at `from`, moves in steps of `stepMs`, calls `during` at the end, then lifts unless told not to. */
    async drag(from, to, { steps = 10, stepMs = 16, during = null, lift = true } = {}) {
      await send("touchStart", from.x, from.y);
      for (let i = 1; i <= steps; i += 1) {
        await send("touchMove", from.x + ((to.x - from.x) * i) / steps, from.y + ((to.y - from.y) * i) / steps);
        await wait(stepMs);
      }
      if (during) await during();
      if (lift) await send("touchEnd", to.x, to.y);
    },
    async lift() { await send("touchEnd", 0, 0); },
  };
}

/** The colours of the pixel rows of a thin screenshot, top to bottom, as [r, g, b]. */
async function readRows(png) {
  const reader = await browser.newPage();
  try {
    return await reader.evaluate(async (data) => {
      const img = new Image();
      img.src = "data:image/png;base64," + data;
      await img.decode();
      const c = document.createElement("canvas");
      c.width = img.width; c.height = img.height;
      const g = c.getContext("2d");
      g.drawImage(img, 0, 0);
      const rows = [];
      for (let y = 0; y < img.height; y += 1) rows.push([...g.getImageData(1, y, 1, 1).data].slice(0, 3));
      return rows;
    }, png);
  } finally {
    await reader.close();
  }
}

/**
 * The phone's remote as a person uses it: the site itself on a phone paired with the television, the
 * strip let down over the tab bar, the drawer it opens, the film driven by buttons and by a finger on the
 * still, the drawer pulled down and its strip swiped away and brought back, a title put on the television
 * from its page (the television on Home, then mid-film), and the idle drawer's Continue watching.
 */
async function remoteUi() {
  console.log("remoteui");
  const stamp = Date.now().toString(36);
  const tvSession = { hours: 6, client: TV_CLIENT, sid: `verify-ui-tv-${stamp}`, device: "Verify TV" };
  const phoneSid = `verify-ui-phone-${stamp}`;
  const first = `verify-ui-a-${stamp}`;
  const tv = await open("/profile/remote", VIEWER, tvSession);
  const phoneContext = await browser.newContext({ viewport: { width: 390, height: 844 }, userAgent: PhoneAgent, isMobile: true, hasTouch: true, bypassCSP: true });
  await phoneContext.addInitScript(({ key, value }) => {
    localStorage.setItem(key, value);
    if (localStorage.getItem("cinema:remote") === null) localStorage.setItem("cinema:remote", "1");
  }, issuer.planted(VIEWER, { hours: 6, sid: phoneSid }));
  const phone = await phoneContext.newPage();
  phone.on("pageerror", (e) => failures.push(`remoteui: phone page error ${e.message}`));
  phone.on("console", (m) => { if (process.env.DEBUG_PROBE) console.log("phone console", m.type(), m.text()); });
  const cdp = await phoneContext.newCDPSession(phone);
  const finger = fingerOn(cdp);
  const phoneAuth = { Authorization: `Bearer ${issuer.mint(VIEWER, { sid: phoneSid })}` };
  const tvVideo = (read) => tv.page.evaluate(read).catch(() => null);
  const tvPaused = () => tvVideo(() => { const v = document.querySelector("video"); return v ? v.paused : null; });
  const tvTime = () => tvVideo(() => { const v = document.querySelector("video"); return v ? v.currentTime : null; });
  const tvAddress = () => tv.page.evaluate(() => location.pathname + location.search);
  const loaded = (page) => page.waitForFunction(() => { const v = document.querySelector("video"); return v && v.readyState >= 3; }, null, { timeout: 30000 }).catch(() => {});
  const centre = async (selector) => {
    const box = await phone.locator(selector).first().boundingBox();
    if (!box) throw new Error(`no ${selector} on the phone`);
    return { x: box.x + box.width / 2, y: box.y + box.height / 2, box };
  };
  const shot = (name) => phone.screenshot({ path: join(out, name) });
  const stripName = () => phone.evaluate(() => document.querySelector(".kc-remote-strip .kc-dl-head__name")?.textContent || "");
  const goTo = async (path, name) => {
    await phone.evaluate((to) => { history.pushState({}, "", to); window.dispatchEvent(new PopStateEvent("popstate")); }, path);
    await phone.waitForFunction((film) => { const h = document.querySelector(".kc-title h1"); return h && h.textContent.includes(film); }, name, { timeout: 10000 });
    await phone.waitForTimeout(600);
  };
  const closeRemote = async () => { await phone.keyboard.press("Escape"); await phone.waitForSelector(".kc-remote-strip", { timeout: 5000 }); };
  const openedControls = async () => {
    const strip = await centre(".kc-remote-strip__open");
    await finger.tap(strip.x, strip.y);
    await phone.waitForSelector(".kc-remote .mp-controls", { timeout: 10000 });
    await phone.waitForTimeout(1200);
    return phone.evaluate(() => [...document.querySelectorAll(".kc-remote .mp-controls button")].map((b) => b.getAttribute("aria-label")));
  };
  const watchOnTvButton = () => phone.locator(".kc-title .kc-actions .k-btn", { hasText: "Watch on TV" }).last();
  try {
    await tv.page.goto(`${base}/r/${first}?title=${encodeURIComponent(playable)}&tv=1`, { waitUntil: "load" });
    await loaded(tv.page);
    await tv.page.keyboard.press("Enter");
    await tv.page.waitForTimeout(1500);
    await tv.page.evaluate(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "MediaPlayPause", bubbles: true, cancelable: true })));
    await tv.page.waitForSelector(".kc-tvr-paused__qr svg", { timeout: 10000 }).catch(() => {});
    await tv.page.waitForTimeout(700);
    const url = await readQr(tv.page, ".kc-tvr-paused__qr", SofaScale);
    if (!url) { fail("remoteui", "the paused player drew no code to pair with"); return; }
    const paired = await fetch(base + "/api/me/screens", {
      method: "POST", headers: { ...phoneAuth, "Content-Type": "application/json", "User-Agent": PhoneAgent },
      body: JSON.stringify({ code: new URL(url).pathname.slice("/p/".length) }),
    });
    if (!paired.ok) { fail("remoteui", `pairing from the television's code answered ${paired.status}`); return; }

    // The strip, over the tab bar.
    await phone.goto(`${base}/`, { waitUntil: "load" });
    const strip = await phone.waitForSelector(".kc-remote-strip", { timeout: 15000 }).catch(() => null);
    if (!strip) { fail("remoteui", "the phone paired with a connected television drew no strip"); return; }
    await phone.waitForFunction(() => document.querySelector(".kc-remote-strip .kc-dl-head__name")?.textContent, null, { timeout: 10000 }).catch(() => {});
    await phone.waitForTimeout(600);
    await shot("remoteui-04-strip.png");
    const stripFacts = await phone.evaluate(() => {
      const el = document.querySelector(".kc-remote-strip");
      const bar = document.querySelector(".tabbar");
      const r = el.getBoundingClientRect();
      return {
        eyebrow: el.querySelector(".kc-dl-head__eyebrow")?.textContent, name: el.querySelector(".kc-dl-head__name")?.textContent,
        label: el.querySelector(".kc-remote-strip__open")?.getAttribute("aria-label"), pause: el.querySelector(".mp-play")?.getAttribute("aria-label"),
        gap: bar ? Math.round(bar.getBoundingClientRect().top - r.bottom) : null,
      };
    });
    if (stripFacts.eyebrow !== "Verify TV" || !stripFacts.name || stripFacts.label !== "Open the remote: Verify TV" || stripFacts.pause !== "Play" || stripFacts.gap !== 0)
      fail("remoteui", `the strip says ${JSON.stringify(stripFacts)}`);
    else if (/\(\d{4}\)/.test(stripFacts.name)) fail("remoteui", `the strip names the film with its year: ${stripFacts.name}`);
    else pass(`the strip stands on the tab bar naming the TV and what it holds (${stripFacts.name}), with Play for the paused film`);

    // The strip sits flush on the tab bar and is as far under its handle as above the bar, at the pixel ratios
    // phones have: measured from the rects and from the screenshot's pixel rows.
    for (const dpr of [1, 2, 2.625, 3]) {
      const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: dpr, userAgent: PhoneAgent, isMobile: true, hasTouch: true, bypassCSP: true });
      await context.addInitScript(({ key, value }) => { localStorage.setItem(key, value); localStorage.setItem("cinema:remote", "1"); }, issuer.planted(VIEWER, { hours: 6, sid: phoneSid }));
      const page = await context.newPage();
      await page.goto(`${base}/`, { waitUntil: "load" });
      await page.waitForSelector(".kc-remote-strip .kc-dl-head__name", { timeout: 15000 });
      await page.waitForTimeout(500);
      const geo = await page.evaluate(() => {
        const s = document.querySelector(".kc-remote-strip").getBoundingClientRect();
        const b = document.querySelector(".tabbar").getBoundingClientRect();
        const row = document.querySelector(".kc-remote-strip__open").getBoundingClientRect();
        return { stripBottom: s.bottom, tabTop: b.top, above: row.top - s.top, below: s.bottom - row.bottom, x: 24 };
      });
      const shotPng = await page.screenshot({ clip: { x: 20, y: geo.tabTop - 4, width: 4, height: 8 } });
      const rows = await readRows(shotPng.toString("base64"));
      await context.close();
      const bad = Math.abs(geo.stripBottom - geo.tabTop) > 0.01 || Math.abs(geo.above - geo.below) > 1.01;
      // Every pixel row of the four CSS pixels above the bar is the strip's own colour: no stripe of the page shows between them.
      const above = rows.slice(0, Math.round(4 * dpr));
      const seam = above.every((c) => c.every((v, i) => Math.abs(v - above[0][i]) <= 2));
      if (bad || !seam) fail("remoteui", `at ${dpr}x the strip's bottom is ${geo.stripBottom} and the bar's top ${geo.tabTop}; gaps above ${geo.above} and below ${geo.below} the row; seam ${JSON.stringify(rows)}`);
      else pass(`at ${dpr}x the strip stands flush on the tab bar (${geo.stripBottom}) and its row has ${geo.above}px above and ${geo.below}px below`);
    }

    // Nothing interactive sits under the strip, and every page scrolls clear of it.
    const label = process.env.CLEARANCE_LABEL || "after";
    const under = () => phone.evaluate(() => {
      const strip = document.querySelector(".kc-remote-strip").getBoundingClientRect();
      const main = document.querySelector(".app__main");
      const last = main ? [...main.querySelectorAll("*")].filter((e) => e.getBoundingClientRect().height > 0).reduce((a, e) => (e.getBoundingClientRect().bottom > a ? e.getBoundingClientRect().bottom : a), 0) : 0;
      const field = document.querySelector(".assistant-dock--open textarea, .assistant-dock--open input, .assistant-dock textarea");
      const f = field ? field.getBoundingClientRect() : null;
      return { stripTop: strip.top, last, field: f && { top: f.top, bottom: f.bottom } };
    });
    const scrollEnd = () => phone.evaluate(() => { const m = document.querySelector(".app__main"); if (m) m.scrollTop = m.scrollHeight; });
    for (const [name, path, film] of [["home", "/", null], ["movies", "/movies", null], ["discover", "/discover", null], ["profile", "/profile", null], ["title", `/movies/${playable}`, "Coherence"]]) {
      if (film) await goTo(path, film);
      else { await phone.evaluate((to) => { history.pushState({}, "", to); window.dispatchEvent(new PopStateEvent("popstate")); }, path); await phone.waitForTimeout(1500); }
      await scrollEnd();
      await phone.waitForTimeout(400);
      const m = await under();
      await shot(`clearance-${label}-${name}.png`);
      if (m.last > m.stripTop + 0.5) fail("remoteui", `the end of ${name} is at ${Math.round(m.last)} under the strip, which starts at ${Math.round(m.stripTop)}`);
      else pass(`the end of ${name} scrolls clear of the strip (${Math.round(m.last)} against ${Math.round(m.stripTop)})`);
    }
    await phone.locator(".tabbar__tab", { hasText: "Assistant" }).click();
    await phone.waitForSelector(".assistant-dock--open", { timeout: 8000 });
    await phone.waitForTimeout(800);
    const assistant = await under();
    await shot(`clearance-${label}-assistant.png`);
    if (!assistant.field) fail("remoteui", "the assistant drew no input to measure");
    else if (assistant.field.bottom > assistant.stripTop + 0.5) fail("remoteui", `the assistant's input ends at ${Math.round(assistant.field.bottom)} under the strip, which starts at ${Math.round(assistant.stripTop)}`);
    else pass(`the assistant's input stands clear of the strip (${Math.round(assistant.field.bottom)} against ${Math.round(assistant.stripTop)})`);
    await phone.locator(".tabbar__tab", { hasText: "Home" }).click();
    await phone.waitForTimeout(800);

    // A tap opens the drawer.
    const open1 = await centre(".kc-remote-strip__open");
    await finger.tap(open1.x, open1.y);
    const opened = await phone.waitForSelector(".kc-remote", { timeout: 5000 }).catch(() => null);
    if (!opened) { fail("remoteui", "a tap on the strip did not open the remote"); return; }
    await phone.waitForSelector(".kc-remote .mp-scrub", { timeout: 10000 }).catch(() => {});
    await phone.waitForTimeout(700);
    await shot("remoteui-05-open.png");
    const drawer = await phone.evaluate(() => {
      const d = document.querySelector(".kc-remote");
      const controls = [...d.querySelectorAll(".mp-controls button")].map((b) => ({ label: b.getAttribute("aria-label"), w: Math.round(b.getBoundingClientRect().width), h: Math.round(b.getBoundingClientRect().height) }));
      return {
        tv: d.querySelector(".kc-remote__tv")?.textContent, mode: d.querySelector(".k-toggle")?.getAttribute("aria-checked"),
        scrub: Boolean(d.querySelector(".mp-scrub")), controls, still: Boolean(d.querySelector(".kc-remote__still img")),
      };
    });
    const gaps = await phone.evaluate(() => {
      const d = document.querySelector(".kc-remote").getBoundingClientRect();
      const toggle = document.querySelector(".kc-remote .k-toggle").getBoundingClientRect();
      const play = document.querySelector(".kc-remote .mp-controls .mp-play").getBoundingClientRect();
      return { above: Math.round((toggle.top - d.top) * 10) / 10, below: Math.round((d.bottom - play.bottom) * 10) / 10 };
    });
    if (Math.abs(gaps.above - gaps.below) > 2) fail("remoteui", `the open drawer has ${gaps.above}px above its head and ${gaps.below}px under its controls`);
    else pass(`the open drawer has ${gaps.above}px above its head and ${gaps.below}px under its controls`);
    const labels = drawer.controls.map((c) => c.label);
    const small = drawer.controls.filter((c) => c.w < 44 || c.h < 44).map((c) => c.label);
    if (drawer.tv !== "Verify TV" || drawer.mode !== "true" || !drawer.scrub || !drawer.still || small.length > 0
      || !labels.includes("Play") || !labels.includes("Back 10 seconds") || !labels.includes("Forward 10 seconds"))
      fail("remoteui", `the open drawer holds ${JSON.stringify(drawer)}`);
    else pass(`the drawer opens with the TV, the switch, the still, the scrub bar and ${labels.length} controls, each at least 44px`);
    if (!labels.some((l) => l.startsWith("Subtitles")) || !labels.some((l) => l.startsWith("Quality")) || labels.some((l) => l.startsWith("Audio")))
      fail("remoteui", `a film with one audio track and two rungs offers Subtitles and Quality and no Audio: ${labels}`);
    else pass("the control row offers Subtitles and Quality, and no Audio for a film with one audio track");

    // The television shows a phone glyph while it is paused and a remote drives it, and not otherwise.
    const glyph = () => tv.page.evaluate(() => Boolean(document.querySelector(".mp-top__remote")));
    const shownPaused = await until(async () => (await tvPaused()) === true && (await glyph()), 6000);
    if (!shownPaused) fail("remoteui", `the paused television shows no phone glyph with a remote attached (paused ${await tvPaused()})`);
    else {
      await tv.page.screenshot({ path: join(out, "remoteui-09-tv-mark.png") });
      pass("the paused television shows the phone glyph while a remote drives it");
    }
    await phone.click('.kc-remote .mp-controls button[aria-label="Play"]');
    const goneWhilePlaying = await until(async () => (await tvPaused()) === false && !(await glyph()), 6000);
    await phone.click('.kc-remote .mp-controls button[aria-label="Pause"]');
    const backWhenPaused = await until(async () => (await tvPaused()) === true && (await glyph()), 6000);
    if (!goneWhilePlaying || !backWhenPaused) fail("remoteui", `the glyph while playing gone ${goneWhilePlaying}, back when paused ${backWhenPaused}`);
    else pass("the glyph is shown only while the television is paused");
    await phone.click(".kc-remote .k-toggle");
    const leftGone = await until(async () => !(await glyph()), 6000);
    await phone.waitForSelector(".toast__action", { timeout: 5000 });
    await phone.click(".toast__action");
    const rejoined = await until(async () => await glyph(), 8000);
    if (!leftGone || !rejoined) fail("remoteui", `the glyph goes when the remote leaves ${leftGone} and returns when it rejoins ${rejoined}`);
    else pass("the glyph goes when the phone leaves as a remote and returns when it rejoins");
    const reopen = await centre(".kc-remote-strip__open");
    await finger.tap(reopen.x, reopen.y);
    await phone.waitForSelector(".kc-remote .mp-scrub", { timeout: 10000 });

    // Buttons move the television.
    await phone.click('.kc-remote .mp-controls button[aria-label="Play"]');
    const playing = await until(async () => (await tvPaused()) === false);
    await phone.click('.kc-remote .mp-controls button[aria-label="Pause"]');
    const stopped = await until(async () => (await tvPaused()) === true);
    const before = await tvTime();
    await phone.click('.kc-remote .mp-controls button[aria-label="Forward 10 seconds"]');
    const forward = await until(async () => (await tvTime()) - before >= 9);
    await phone.click('.kc-remote .mp-controls button[aria-label="Back 10 seconds"]');
    const back = await until(async () => Math.abs((await tvTime()) - before) < 2);
    if (!playing || !stopped || !forward || !back) fail("remoteui", `buttons: play ${playing}, pause ${stopped}, forward ${forward}, back ${back} (from ${before} to ${await tvTime()})`);
    else pass("Play, Pause, Forward and Back 10 seconds move the television's player");

    // A finger on the still, as on the film.
    const still = await centre(".kc-remote__still");
    await finger.tap(still.x, still.y);
    const tapped = await until(async () => (await tvPaused()) === false);
    await finger.tap(still.x, still.y);
    const tappedAgain = await until(async () => (await tvPaused()) === true);
    if (!tapped || !tappedAgain) fail("remoteui", `a tap in the middle of the still: play ${tapped}, pause ${tappedAgain}`);
    else pass("a tap in the middle of the still plays, and another pauses");
    const base0 = await tvTime();
    const right = { x: still.box.x + still.box.width * 0.85, y: still.y };
    await finger.tap(right.x, right.y);
    await new Promise((r) => setTimeout(r, 90));
    await finger.tap(right.x, right.y);
    const skipped = await until(async () => Math.abs((await tvTime()) - base0 - 10) < 2, 4000);
    if (!skipped) fail("remoteui", `a double tap at the right left the television at ${await tvTime()} from ${base0}`);
    else pass("a double tap at the right of the still skips ten seconds, once, when the taps stop");
    // A sweep sideways scrubs and the room moves when the finger lifts.
    const from = await tvTime();
    await finger.drag({ x: still.box.x + still.box.width * 0.3, y: still.y }, { x: still.box.x + still.box.width * 0.7, y: still.y }, {
      lift: false,
      during: async () => {
        await phone.waitForTimeout(300);
        await shot("remoteui-06-scrub.png");
        const preview = await phone.evaluate(() => Boolean(document.querySelector(".kc-remote .mp-scrub__preview")));
        const held = await tvTime();
        if (!preview) fail("remoteui", "sweeping the still showed no preview on the scrub bar");
        if (Math.abs(held - from) > 1) fail("remoteui", `the television moved to ${held} before the finger lifted (from ${from})`);
      },
    });
    await finger.lift();
    const swept = await until(async () => (await tvTime()) - from > 100);
    if (!swept) fail("remoteui", `a sweep across the still left the television at ${await tvTime()} from ${from}`);
    else pass("a sweep across the still scrubs, the television moving only when the finger lifts");
    let level = false;
    await finger.drag({ x: still.box.x + still.box.width * 0.85, y: still.y + 40 }, { x: still.box.x + still.box.width * 0.85, y: still.y - 40 }, {
      during: async () => { level = await phone.evaluate(() => Boolean(document.querySelector(".mp-gesture-level"))); },
    });
    if (level) fail("remoteui", "a vertical sweep drew a volume or brightness level");
    else pass("vertical sweeps on the still draw no level: the TV's volume is not the remote's");

    // Pulling the handle down collapses to the strip.
    const top = await centre(".kc-remote .kc-drawer__top");
    await finger.drag({ x: top.x - 120, y: top.y }, { x: top.x - 120, y: top.y + 150 }, { steps: 10, stepMs: 20, during: async () => { await shot("remoteui-15-pulling.png"); } });
    const collapsed = await until(() => phone.evaluate(() => !document.querySelector(".kc-remote") && Boolean(document.querySelector(".kc-remote-strip"))));
    if (!collapsed) fail("remoteui", "pulling the handle down did not leave the strip");
    else pass("pulling the handle down collapses the remote to its strip");
    // Pushing the strip up opens it again.
    const strip2 = await centre(".kc-remote-strip__open");
    await finger.drag({ x: strip2.x, y: strip2.y }, { x: strip2.x, y: strip2.y - 80 });
    const pushed = await until(() => phone.evaluate(() => Boolean(document.querySelector(".kc-remote"))));
    if (!pushed) fail("remoteui", "pushing the strip up did not open the remote");
    else pass("pushing the strip up opens the remote");
    // Collapse again, then swipe the strip away.
    const top2 = await centre(".kc-remote .kc-drawer__top");
    await finger.drag({ x: top2.x - 120, y: top2.y }, { x: top2.x - 120, y: top2.y + 150 });
    await phone.waitForSelector(".kc-remote-strip", { timeout: 5000 }).catch(() => {});
    const strip3 = await centre(".kc-remote-strip__open");
    await finger.drag({ x: strip3.x - 60, y: strip3.y }, { x: strip3.x + 60, y: strip3.y }, { lift: false, steps: 6, during: async () => { await shot("remoteui-16-swiping.png"); } });
    await finger.drag({ x: strip3.x + 60, y: strip3.y }, { x: strip3.x + 150, y: strip3.y }, { steps: 3 }).catch(() => {});
    const away = await until(() => phone.evaluate(() => localStorage.getItem("cinema:remote") === "0" && !document.querySelector(".kc-remote-strip")));
    await phone.waitForSelector(".toast__action", { timeout: 5000 }).catch(() => {});
    await phone.waitForTimeout(300);
    await shot("remoteui-17-off.png");
    const toast = await phone.evaluate(() => ({ title: document.querySelector(".toast__title")?.textContent, undo: document.querySelector(".toast__action")?.textContent }));
    if (!away || toast.title !== "Remote off · Verify TV" || toast.undo !== "Undo") fail("remoteui", `swiping the strip away: off ${away}, toast ${JSON.stringify(toast)}`);
    else pass("swiping the strip sideways turns remote mode off with an Undo toast");
    const stillPaired = (await pairedWith(phoneSid)).length;
    await phone.click(".toast__action");
    const back2 = await until(() => phone.evaluate(() => localStorage.getItem("cinema:remote") === "1" && Boolean(document.querySelector(".kc-remote-strip"))));
    if (!back2 || stillPaired !== 1) fail("remoteui", `Undo: remote mode back ${back2}, pairings before it ${stillPaired}`);
    else pass("nothing is unpaired, and Undo brings the strip back");

    // Watch on TV from a page, the television on Home. Only the first film has video in the scratch library;
    // the others are titles the television holds without playing.
    await tv.page.goto(`${base}/?tv=1`, { waitUntil: "load" });
    await until(async () => /Nothing playing/.test(await stripName()));
    const film = playable;
    const filmName = /^coherence/.test(playable) ? "Coherence" : playable;
    await goTo(`/movies/${film}`, filmName);
    await phone.evaluate(() => document.querySelector(".kc-title .kc-actions").scrollIntoView({ block: "center" }));
    await phone.waitForTimeout(300);
    await shot("remoteui-10-film.png");
    const buttons = await phone.evaluate(() => [...document.querySelectorAll(".kc-title .kc-actions .k-btn")].map((b) => b.textContent.trim()));
    if (buttons[0] !== "Watch on TV" || buttons[1] !== "Watch") fail("remoteui", `the film page's buttons are ${JSON.stringify(buttons)}`);
    else pass(`the film page's main button is Watch on TV with Watch beside it (${buttons.join(", ")})`);
    await watchOnTvButton().click();
    const into = await until(async () => /^\/r\//.test(await tv.page.evaluate(() => location.pathname)), 12000);
    await loaded(tv.page);
    const onTv = await until(async () => new RegExp(filmName).test(await stripName()), 5000);
    const playingFilm = await until(async () => (await tvPaused()) === false, 12000);
    if (!playingFilm) await tv.page.screenshot({ path: join(out, "remoteui-tv-not-playing.png") });
    if (!into || !onTv || !playingFilm) fail("remoteui", `Watch on TV with the television on Home: in a room ${into}, holding the title ${onTv}, playing ${playingFilm} (${await tvAddress()})`);
    else pass("Watch on TV with the television on Home takes it into a room that plays the title");
    const room1 = await tv.page.evaluate(() => location.pathname);
    await tv.page.goBack({ waitUntil: "load" }).catch(() => {});
    const returned = (await tv.page.evaluate(() => location.pathname)) === "/";
    if (!returned) fail("remoteui", "Back on the television did not return to the page it was on");
    else pass("Back on the television returns to the page it was on");
    await tv.page.goto(`${base}${room1}?tv=1`, { waitUntil: "load" });
    await loaded(tv.page);
    await until(async () => new RegExp(filmName).test(await stripName()));

    // The same title again leaves the television alone; another title asks first.
    const filmTime = await tvTime();
    await watchOnTvButton().click();
    await phone.waitForTimeout(1200);
    const noSheet = await phone.evaluate(() => !document.querySelector(".kc-dl-sheet"));
    const shownRemote = await phone.evaluate(() => Boolean(document.querySelector(".kc-remote")));
    if (!noSheet || !shownRemote || Math.abs((await tvTime()) - filmTime) > 5) fail("remoteui", `Watch on TV for the title already playing: asked ${!noSheet}, remote shown ${shownRemote}, television moved to ${await tvTime()} from ${filmTime}`);
    else pass("Watch on TV for the title already on the television leaves it alone and shows its remote");
    await phone.keyboard.press("Escape");
    await phone.waitForSelector(".kc-remote-strip", { timeout: 5000 });
    const heat = "heat-1995";
    await goTo(`/movies/${heat}`, "Heat");
    await watchOnTvButton().click();
    const sheet = await phone.waitForSelector(".kc-dl-sheet", { timeout: 5000 }).catch(() => null);
    await phone.waitForTimeout(500);
    await shot("remoteui-11-replace.png");
    if (!sheet) fail("remoteui", "Watch on TV over a film part way through did not ask");
    else {
      const asked = await phone.evaluate(() => ({
        title: document.querySelector(".kc-dl-sheet .kc-dl-head__name")?.textContent,
        names: [...document.querySelectorAll(".kc-replace__name")].map((n) => n.textContent),
        buttons: [...document.querySelectorAll(".kc-dl-sheet .kc-dl-foot .k-btn")].map((b) => b.textContent.trim()),
      }));
      if (asked.title !== "Watch on TV" || asked.names.length !== 2 || !new RegExp(filmName).test(asked.names[0]) || !/Heat/.test(asked.names[1]) || asked.buttons[1] !== "Keep watching")
        fail("remoteui", `the question says ${JSON.stringify(asked)}`);
      else pass(`Watch on TV over a film part way through asks first: ${asked.names.join(" -> ")}`);
      await phone.click(".kc-dl-sheet >> text=Keep watching");
      await phone.waitForTimeout(800);
      const stillThere = new RegExp(filmName).test(await stripName());
      if (!stillThere) fail("remoteui", "Keep watching changed what the television plays");
      else pass("Keep watching leaves the television as it was");
      await watchOnTvButton().click();
      await phone.waitForSelector(".kc-dl-sheet", { timeout: 5000 });
      await phone.click(".kc-dl-sheet .kc-dl-foot .k-btn >> nth=0");
      const replaced = await until(async () => /Heat/.test(await stripName()), 10000);
      if (!replaced) fail("remoteui", "Watch on the question did not put the title on the television");
      else pass("Watch on the question puts the title on the television in its room");
    }

    // The film that plays is put back, and in its closing stretch the next title replaces it at once: the
    // scrub bar is tapped near its end, and the drawer put away.
    await goTo(`/movies/${film}`, filmName);
    await watchOnTvButton().click();
    await phone.waitForSelector(".kc-dl-sheet", { timeout: 5000 });
    await phone.click(".kc-dl-sheet .kc-dl-foot .k-btn >> nth=0");
    await until(async () => new RegExp(filmName).test(await stripName()), 10000);
    await loaded(tv.page);
    const opener = await centre(".kc-remote-strip__open");
    await finger.tap(opener.x, opener.y);
    await phone.waitForSelector(".kc-remote .mp-scrub", { timeout: 10000 });
    await phone.waitForTimeout(500);
    const bar = await centre(".kc-remote .mp-scrub");
    await finger.tap(bar.box.x + bar.box.width * 0.985, bar.y);
    const ending = await until(async () => (await tvTime()) > (await tvVideo(() => document.querySelector("video").duration)) - 120);
    await phone.keyboard.press("Escape");
    await phone.waitForSelector(".kc-remote-strip", { timeout: 5000 });
    await goTo(`/movies/${heat}`, "Heat");
    await watchOnTvButton().click();
    const swapped = await until(async () => /Heat/.test(await stripName()), 10000);
    const asked2 = await phone.evaluate(() => Boolean(document.querySelector(".kc-dl-sheet")));
    if (!ending || !swapped || asked2) fail("remoteui", `in the closing stretch: reached it ${ending}, replaced ${swapped}, asked ${asked2}`);
    else pass("Watch on TV in the closing stretch of what plays replaces it without asking");

    // A film has no episode controls; an episode has them, and the player's own episodes list aimed at the television.
    const filmControls = await openedControls();
    await closeRemote();
    if (filmControls.some((l) => /episode/i.test(l))) fail("remoteui", `a film's remote has episode controls: ${filmControls}`);
    else pass("a film's remote shows no episode controls");
    const show = "superstore-s01e02";
    await tv.page.goto(`${base}/r/${first}?title=${show}&tv=1`, { waitUntil: "load" });
    await until(async () => /Superstore/.test(await stripName()) || /Pilot|Superstore/.test(await stripName()), 10000);
    const episodeControls = await openedControls();
    await shot("remoteui-05-episode.png");
    const wanted = ["Previous episode: S1 · E1", "Next episode: S1 · E3", "Episodes: S1 · E2"];
    if (!wanted.every((w) => episodeControls.some((l) => l.startsWith(w)))) fail("remoteui", `an episode's remote has ${JSON.stringify(episodeControls)}`);
    else pass("an episode's remote has previous and next episode and Episodes, each naming where it goes");
    await phone.click('.kc-remote .mp-controls button[aria-label^="Episodes"]');
    await phone.waitForSelector(".mp-episodes .kc-episode", { timeout: 10000 }).catch(() => {});
    await phone.waitForTimeout(800);
    await shot("remoteui-08-episodes.png");
    await phone.click('.mp-menu .kc-drawer__back[aria-label="Back to the remote"]');
    const episodesBack = await until(() => phone.evaluate(() => !document.querySelector(".mp-menu") && Boolean(document.querySelector(".kc-remote .mp-controls"))));
    if (!episodesBack) fail("remoteui", "the episodes list's back button did not return to the remote");
    else pass("the episodes list has a back button, which returns to the remote");
    await phone.click('.kc-remote .mp-controls button[aria-label^="Episodes"]');
    await phone.waitForSelector(".mp-episodes .kc-episode", { timeout: 10000 });

    const list = await phone.evaluate(() => ({
      count: document.querySelectorAll(".mp-episodes .kc-episode").length,
      current: document.querySelector(".mp-episodes .kc-episode.is-current .kc-episode__title")?.textContent,
      aim: document.querySelector(".mp-menu__aim")?.textContent,
    }));
    if (list.count < 6 || !list.current || list.aim !== "Verify TV") fail("remoteui", `the episodes list shows ${JSON.stringify(list)}`);
    else pass(`the episodes list is the player's, aimed at the TV (${list.aim}), with ${list.count} episodes and "${list.current}" marked`);
    await phone.locator(".mp-episodes .kc-episode").nth(3).click();
    const fourth = await until(async () => (await tv.page.evaluate(() => document.title + location.search + document.body.innerText)).includes("Superstore") && /S1 · E4|E4/.test(await phone.evaluate(() => document.querySelector('.kc-remote .mp-controls button[aria-label^="Episodes"]')?.getAttribute("aria-label") || "")), 10000);
    if (!fourth) fail("remoteui", "choosing an episode in the list did not put it on the television");
    else pass("choosing an episode puts it on the television");
    await phone.click('.kc-remote .mp-controls button[aria-label^="Next episode"]');
    const fifth = await until(async () => (await phone.evaluate(() => document.querySelector('.kc-remote .mp-controls button[aria-label^="Episodes"]')?.getAttribute("aria-label") || "")).includes("E5"), 10000);
    await phone.waitForTimeout(1000);
    await phone.click('.kc-remote .mp-controls button[aria-label^="Previous episode"]');
    const fourthAgain = await until(async () => (await phone.evaluate(() => document.querySelector('.kc-remote .mp-controls button[aria-label^="Episodes"]')?.getAttribute("aria-label") || "")).includes("E4"), 10000);
    if (!fifth || !fourthAgain) fail("remoteui", `Next episode reached E5 ${fifth}, Previous episode back to E4 ${fourthAgain}`);
    else pass("Next episode and Previous episode move the television by an episode each");
    await closeRemote();

    // Several televisions: the picker, with one off; then the idle drawer of a television on Home.
    const second = await open("/profile/remote", VIEWER, { hours: 6, client: TV_CLIENT, sid: `verify-ui-tv2-${stamp}`, device: "Bedroom TV" });
    const third = await open("/profile/remote", VIEWER, { hours: 6, client: TV_CLIENT, sid: `verify-ui-tv3-${stamp}`, device: "Office TV" });
    for (const other of [second, third]) {
      const code = await readQr(other.page, ".kc-tvr__code", SofaScale);
      const response = code && await fetch(base + "/api/me/screens", {
        method: "POST", headers: { ...phoneAuth, "Content-Type": "application/json", "User-Agent": PhoneAgent },
        body: JSON.stringify({ code: new URL(code).pathname.slice("/p/".length) }),
      });
      if (!response || !response.ok) { fail("remoteui", "a second television could not be paired"); return; }
    }
    await third.close();
    await until(async () => (await pairedWith(phoneSid)).filter((s) => !s.connected).length === 1, 40000);
    const open2 = await centre(".kc-remote-strip__open");
    await finger.tap(open2.x, open2.y);
    await phone.waitForSelector(".kc-remote", { timeout: 5000 });
    await phone.click(".kc-remote__tv");
    await phone.waitForSelector(".k-optlist", { timeout: 5000 });
    await phone.waitForTimeout(600);
    await shot("remoteui-12-screens.png");
    const rows = await phone.evaluate(() => [...document.querySelectorAll(".k-optlist__row")].map((r) => ({
      label: r.querySelector(".k-optlist__label")?.textContent, detail: r.querySelector(".k-optlist__detail")?.textContent, off: r.classList.contains("is-disabled"), on: r.classList.contains("is-on"),
    })));
    const byName = Object.fromEntries(rows.map((r) => [r.label, r]));
    if (rows.length !== 3 || !byName["Verify TV"]?.on || byName["Bedroom TV"]?.detail !== "Nothing playing" || byName["Office TV"]?.detail !== "Off" || !byName["Office TV"]?.off)
      fail("remoteui", `the picker lists ${JSON.stringify(rows)}`);
    else pass("the picker lists the paired televisions: the one driven, one with nothing playing, one off and not to be chosen");
    await phone.click('.kc-drawer__back[aria-label="Back to the remote"]');
    const backToRemote = await until(() => phone.evaluate(() => Boolean(document.querySelector(".kc-remote .kc-remote__tv")) && !document.querySelector(".k-optlist")));
    if (!backToRemote) fail("remoteui", "the picker's back button did not return to the remote");
    else pass("the TV picker has a back button, which returns to the remote");
    await phone.click(".kc-remote__tv");
    await phone.waitForSelector(".k-optlist", { timeout: 5000 });
    await phone.locator(".k-optlist__row", { hasText: "Bedroom TV" }).click();
    await phone.waitForSelector(".kc-remote .k-empty", { timeout: 5000 }).catch(() => {});
    await phone.waitForSelector(".kc-remote .kc-continue", { timeout: 10000 }).catch(() => {});
    await phone.waitForTimeout(800);
    await shot("remoteui-13-idle.png");
    const idle = await phone.evaluate(() => ({
      tv: document.querySelector(".kc-remote__tv")?.textContent, title: document.querySelector(".kc-remote .k-empty__title")?.textContent,
      cards: document.querySelectorAll(".kc-remote .kc-continue").length, glyph: Boolean(document.querySelector(".kc-remote .kc-continue__play .lucide-tv")),
    }));
    if (idle.tv !== "Bedroom TV" || idle.title !== "Nothing playing" || idle.cards === 0 || !idle.glyph) fail("remoteui", `the idle drawer shows ${JSON.stringify(idle)}`);
    else pass(`the idle drawer says Nothing playing and offers Continue watching (${idle.cards}), each card with the TV glyph`);
    await phone.locator(".kc-remote .kc-continue").first().click();
    const wentIn = await until(async () => /^\/r\//.test(await second.page.evaluate(() => location.pathname)), 12000);
    if (!wentIn) fail("remoteui", "a Continue watching card on the idle drawer did not take the second television into a room");
    else pass("a Continue watching card on the idle drawer is Watch on TV for that television");
    await second.close();
  } finally {
    await fetch(base + `/api/me/screens/${tvSession.sid}`, { method: "DELETE", headers: phoneAuth });
    await phoneContext.close();
    await tv.close();
  }
}


/** The film the settings check plays: coherence with a second audio track and a second subtitle track. */
const DualFilm = "dual-audio-2013";

/**
 * Puts a film with two audio tracks and two subtitle tracks into the scratch library, beside the film it is
 * made from: its files are hard links, its manifest a copy that lists a second audio rendition (a copy of
 * the first, in a directory of its own, so the stream's request for it is told apart) and a second subtitle
 * track, and lists no master, so the player composes one from the manifest. Waits for the listing to show it.
 */
async function stageDualFilm() {
  if ((await api("/api/titles")).some((t) => t.id === DualFilm)) return;
  const { execFileSync } = await import("node:child_process");
  const { existsSync: exists, renameSync, writeFileSync } = await import("node:fs");
  const source = ["media", "cold"].map((root) => join(dir, "library", root, playable)).find((d) => exists(d));
  if (!source) throw new Error(`The scratch library holds no ${playable} to make ${DualFilm} from.`);
  const target = join(dir, "library", "media", DualFilm);
  execFileSync("cp", ["-al", source, target]);
  execFileSync("cp", ["-al", join(target, "a0"), join(target, "a1")]);
  execFileSync("cp", ["-l", join(target, "s3.vtt"), join(target, "s4.vtt")]);
  const manifest = JSON.parse(readFileSync(join(source, "manifest.json"), "utf8"));
  manifest.id = DualFilm;
  manifest.title = "Dual Audio";
  delete manifest.film;
  delete manifest.master;
  manifest.audio = [
    { ...manifest.audio[0], default: true },
    { ...manifest.audio[0], id: "a1", language: "spa", label: "Español", default: false, uri: "a1/index.m3u8" },
  ];
  manifest.subtitles = [
    manifest.subtitles[0],
    { ...manifest.subtitles[0], id: "s4", language: "spa", label: "Español", uri: "s4.vtt" },
  ];
  writeFileSync(join(target, "manifest.json.tmp"), JSON.stringify(manifest));
  renameSync(join(target, "manifest.json.tmp"), join(target, "manifest.json"));
  if (!(await until(async () => (await api("/api/titles")).some((t) => t.id === DualFilm), 30000)))
    throw new Error(`${DualFilm} never appeared in the scratch library's listing.`);
}

/**
 * The phone changing the television's own settings: Subtitles, Style, Audio and Quality as the player's own
 * menus aimed at the television. Every change is written to the API and pushed to the television, which
 * shows it, and the television's own menus move the phone's labels; the room's state and roster are
 * never touched.
 */
async function settingsUi() {
  console.log("settings");
  await stageDualFilm();
  const stamp = Date.now().toString(36);
  const tvSession = { hours: 6, client: TV_CLIENT, sid: `verify-set-tv-${stamp}`, device: "Verify TV" };
  const phoneSid = `verify-set-phone-${stamp}`;
  const room = `verify-set-${stamp}`;
  const tv = await open("/profile/remote", VIEWER, tvSession);
  const phoneContext = await browser.newContext({ viewport: { width: 390, height: 844 }, userAgent: PhoneAgent, isMobile: true, hasTouch: true, bypassCSP: true });
  await phoneContext.addInitScript(({ key, value }) => {
    localStorage.setItem(key, value);
    if (localStorage.getItem("cinema:remote") === null) localStorage.setItem("cinema:remote", "1");
  }, issuer.planted(VIEWER, { hours: 6, sid: phoneSid }));
  const phone = await phoneContext.newPage();
  phone.on("pageerror", (e) => failures.push(`settings: phone page error ${e.message}`));
  const finger = fingerOn(await phoneContext.newCDPSession(phone));
  const phoneAuth = { Authorization: `Bearer ${issuer.mint(VIEWER, { sid: phoneSid })}` };
  const asked = [];
  tv.page.on("request", (r) => asked.push(r.url()));
  // What each page's hub connections are sent, counted by message name from the frames themselves.
  const heard = { tv: {}, phone: {} };
  for (const [name, page] of [["tv", tv.page], ["phone", phone]]) {
    page.on("websocket", (ws) => ws.on("framereceived", ({ payload }) => {
      for (const part of String(payload).split("\u001e")) {
        const target = /"target":"(\w+)"/.exec(part);
        if (target) heard[name][target[1]] = (heard[name][target[1]] || 0) + 1;
      }
    }));
  }
  const counts = (name, ...targets) => JSON.stringify(targets.map((t) => heard[name][t] || 0));
  const shot = (name) => phone.screenshot({ path: join(out, name) });
  const centre = async (selector) => {
    const box = await phone.locator(selector).first().boundingBox();
    if (!box) throw new Error(`no ${selector} on the phone`);
    return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  };
  const tap = async (selector) => { const c = await centre(selector); await finger.tap(c.x, c.y); };
  const label = (prefix) => phone.evaluate((p) => [...document.querySelectorAll(".kc-remote .mp-controls button")]
    .map((b) => b.getAttribute("aria-label")).find((l) => l && l.startsWith(p)) || null, prefix);
  const tvLabel = (prefix) => tv.page.evaluate((p) => [...document.querySelectorAll(".mp-controls button, .mp-bottom button")]
    .map((b) => b.getAttribute("aria-label")).find((l) => l && l.startsWith(p)) || null, prefix);
  const rows = () => phone.evaluate(() => [...document.querySelectorAll(".mp-menu .mp-option")]
    .map((o) => ({ label: o.querySelector(".mp-option__label")?.textContent, on: o.classList.contains("is-on") })));
  const tvTracks = () => tv.page.evaluate(() => [...document.querySelectorAll("video track")].map((t) => t.label));
  const mine = (path) => fetch(base + path, { headers: phoneAuth }).then((r) => r.json());
  try {
    // Start from a known place: no style, Auto, and the film's first audio track with subtitles off.
    const before = await mine("/api/me/preferences");
    const put = (path, body) => fetch(base + path, { method: "PUT", headers: { ...phoneAuth, "Content-Type": "application/json" }, body: JSON.stringify(body) });
    await put("/api/me/preferences", { ...before, qualityHeight: null, subtitleStyle: null });
    await put(`/api/me/tracks/${DualFilm}`, { subtitleOff: true, audioId: "a0" });

    let delayEchoes = false;
    await tv.page.routeWebSocket(/\/hub\/session/, (ws) => {
      const server = ws.connectToServer();
      ws.onMessage((m) => server.send(m));
      server.onMessage((m) => {
        if (delayEchoes && typeof m === "string" && m.includes("PreferencesChanged")) setTimeout(() => ws.send(m), 1500);
        else ws.send(m);
      });
    });
    await tv.page.goto(`${base}/r/${room}?title=${DualFilm}&tv=1`, { waitUntil: "load" });
    await tv.page.waitForFunction(() => { const v = document.querySelector("video"); return v && v.readyState >= 3; }, null, { timeout: 30000 }).catch(() => {});
    await tv.page.keyboard.press("Enter");
    await tv.page.waitForTimeout(1500);
    await tv.page.evaluate(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "MediaPlayPause", bubbles: true, cancelable: true })));
    await tv.page.waitForSelector(".kc-tvr-paused__qr svg", { timeout: 10000 }).catch(() => {});
    await tv.page.waitForTimeout(700);
    const url = await readQr(tv.page, ".kc-tvr-paused__qr", SofaScale);
    if (!url) { fail("settings", "the paused player drew no code to pair with"); return; }
    const paired = await fetch(base + "/api/me/screens", {
      method: "POST", headers: { ...phoneAuth, "Content-Type": "application/json", "User-Agent": PhoneAgent },
      body: JSON.stringify({ code: new URL(url).pathname.slice("/p/".length) }),
    });
    if (!paired.ok) { fail("settings", `pairing answered ${paired.status}`); return; }

    await phone.goto(`${base}/`, { waitUntil: "load" });
    await phone.waitForSelector(".kc-remote-strip", { timeout: 15000 });
    await phone.waitForTimeout(1500);
    await tap(".kc-remote-strip__open");
    await phone.waitForSelector(".kc-remote .mp-controls .mp-menu-button", { timeout: 10000 });
    await phone.waitForTimeout(1500);
    const quiet = { tv: counts("tv", "StateChanged", "ParticipantsChanged"), phone: counts("phone", "StateChanged", "ParticipantsChanged") };

    // The control row, for a film with two audio tracks (canvas board 21).
    const controls = await phone.evaluate(() => [...document.querySelectorAll(".kc-remote .mp-controls button, .kc-remote .mp-controls a")]
      .map((b) => ({ label: b.getAttribute("aria-label"), w: Math.round(b.getBoundingClientRect().width), h: Math.round(b.getBoundingClientRect().height) })));
    await shot("settings-21-film-remote.png");
    const names = controls.map((c) => c.label);
    const wanted = ["Subtitles: Off", "Audio track: English", "Quality: Auto"];
    if (!wanted.every((w) => names.includes(w))) fail("settings", `the control row says ${JSON.stringify(names)}, expected ${wanted}`);
    else if (names.indexOf("Subtitles: Off") > names.indexOf("Audio track: English") || names.indexOf("Audio track: English") > names.indexOf("Quality: Auto"))
      fail("settings", `Audio is not between Subtitles and Quality: ${names}`);
    else pass(`the control row has ${wanted.join(", ")} in that order, with Quality as "Auto" and no playing height`);
    const small = controls.filter((c) => c.w < 44 || c.h < 44).map((c) => `${c.label} ${c.w}x${c.h}`);
    if (small.length > 0) fail("settings", `controls under 44px: ${small}`);
    else pass("every control in the row is at least 44px");

    // Subtitles (board 19).
    await tap('.kc-remote .mp-menu-button[title="Subtitles"]');
    await phone.waitForSelector(".mp-aimed.mp-menu", { timeout: 5000 });
    await phone.waitForTimeout(800);
    await shot("settings-19-subtitles.png");
    const list = await rows();
    const head = await phone.evaluate(() => ({ title: document.querySelector(".mp-aimed .kc-drawer__title")?.textContent, aim: document.querySelector(".mp-aimed .mp-menu__aim")?.textContent,
      back: Boolean(document.querySelector(".mp-aimed .kc-drawer__back")) }));
    if (head.title !== "Subtitles" || head.aim !== "Verify TV" || !head.back || list[0]?.label !== "Off" || !list[0].on || !list.some((r) => r.label === "Español"))
      fail("settings", `the subtitles drawer shows ${JSON.stringify({ head, list })}`);
    else pass("the Subtitles drawer is aimed at the TV with Off checked, the film's tracks and a back arrow");

    await tap('.mp-aimed .mp-option[title="Español"]');
    const seen = await until(async () => (await tvTracks()).join() === "Español", 8000);
    const stored = await mine(`/api/me/tracks/${DualFilm}`);
    await phone.waitForTimeout(400);
    const checked = (await rows()).find((r) => r.on)?.label;
    if (!seen || stored.subtitleId !== "s4" || checked !== "Español") fail("settings", `choosing Español: the TV's subtitle track is ${JSON.stringify(await tvTracks())}, the API holds ${JSON.stringify(stored)}, the phone checks ${checked}`);
    else pass("choosing a subtitle track shows it on the TV's player and in the API, and checks it on the phone");

    // Style (board 07): each change restyles the TV's cues, whose sample line stands in while the film is silent.
    const cueStyle = () => tv.page.evaluate(() => {
      const t = document.querySelector(".mp-cue__text");
      if (!t) return null;
      const c = getComputedStyle(t);
      return { size: parseFloat(c.fontSize), weight: c.fontWeight, text: t.textContent };
    });
    const plain = await cueStyle();
    await tap(".mp-aimed .mp-option:has(.lucide-type)");
    await phone.waitForSelector(".mp-aimed.is-styling", { timeout: 5000 });
    await phone.waitForTimeout(500);
    await shot("settings-07-style.png");
    const editor = await phone.evaluate(() => ({ title: document.querySelector(".mp-aimed .kc-drawer__title")?.textContent,
      back: document.querySelector(".mp-aimed .kc-drawer__back")?.getAttribute("aria-label") }));
    if (editor.title !== "Subtitle style" || editor.back !== "Back to subtitles") fail("settings", `the style editor shows ${JSON.stringify(editor)}`);
    else pass("the style editor opens from Style with the back arrow to the subtitle list");
    await tap('.mp-aimed .mp-style__choice:has-text("150%")');
    await tap('.mp-aimed .mp-style__choice:has-text("200%")');
    await tap('.mp-aimed .mp-style__choice:has-text("Bold")');
    const restyled = await until(async () => {
      const c = await cueStyle();
      return c && c.weight >= 700 && (!plain || c.size > plain.size);
    }, 8000);
    await tv.page.screenshot({ path: join(out, "settings-tv-style-sample.png") });
    const mid = await cueStyle();
    const stylePut = await until(async () => (await mine("/api/me/preferences")).subtitleStyle?.size === 200, 6000);
    if (!restyled || !stylePut) fail("settings", `after three style changes the TV's cue is ${JSON.stringify(mid)} (was ${JSON.stringify(plain)}) and the API holds size ${(await mine("/api/me/preferences")).subtitleStyle?.size}`);
    else pass(`style changes restyle the TV's cues live (${plain ? plain.size : "none"}px to ${mid.size}px, bold) and are kept by the API`);
    const sampled = mid && mid.text === "I'll meet you at the station at nine.";
    const lapsed = await until(async () => { const c = await cueStyle(); return !c || c.text !== "I'll meet you at the station at nine."; }, 9000);
    if (sampled && !lapsed) fail("settings", "the TV's sample line stayed after the style stopped changing");
    else pass(sampled ? "the TV showed its sample line while the style changed and let it lapse after the last change" : "the TV restyled a line the film was saying, and showed no sample");

    await tap('.mp-aimed .kc-drawer__back');
    await phone.waitForFunction(() => !document.querySelector(".mp-aimed.is-styling"), null, { timeout: 5000 });
    const listAgain = await rows();
    if (!listAgain.some((r) => r.label === "Español" && r.on)) fail("settings", `the back arrow of the style editor did not return to the subtitle list: ${JSON.stringify(listAgain)}`);
    else pass("the style editor's back arrow returns to the subtitle list");
    await tap('.mp-aimed .kc-drawer__back');
    await phone.waitForFunction(() => !document.querySelector(".mp-aimed"), null, { timeout: 5000 });
    if (await label("Subtitles") !== "Subtitles: Español") fail("settings", `the Subtitles label says ${await label("Subtitles")}`);
    else pass("the drawer's back arrow returns to the remote, whose Subtitles button names the track");

    // Audio (board 22): a rendition fetched from the second directory.
    await tap('.kc-remote .mp-menu-button[title="Audio track"]');
    await phone.waitForSelector(".mp-aimed.mp-menu", { timeout: 5000 });
    await phone.waitForTimeout(500);
    await shot("settings-22-audio.png");
    const audio = await rows();
    if (audio.map((r) => r.label).join() !== "English,Español" || !audio[0].on) fail("settings", `the audio drawer shows ${JSON.stringify(audio)}`);
    asked.length = 0;
    await tap('.mp-aimed .mp-option[title="Español"]');
    const audioFetched = await until(async () => asked.some((u) => u.includes(`/media/${DualFilm}/a1/`)), 10000);
    const audioLabel = await until(async () => (await tvLabel("Audio track")) === "Audio track: Español", 6000);
    await phone.waitForSelector(".mp-aimed", { state: "detached", timeout: 5000 }).catch(() => {});
    if (!audioFetched || !audioLabel || (await mine(`/api/me/tracks/${DualFilm}`)).audioId !== "a1")
      fail("settings", `choosing Español audio: the TV fetched the second rendition ${audioFetched}, its button says ${await tvLabel("Audio track")}`);
    else pass("choosing an audio track switches the TV's stream to that rendition and is kept by the API");

    // Quality (board 20): the setting, not the height playing.
    await tap('.kc-remote .mp-menu-button[title="Quality"]');
    await phone.waitForSelector(".mp-aimed.mp-menu", { timeout: 5000 });
    await phone.waitForTimeout(500);
    await shot("settings-20-quality.png");
    const quality = await rows();
    const qualityHead = await phone.evaluate(() => [...document.querySelectorAll(".mp-aimed .mp-option__sub")].map((e) => e.textContent));
    if (quality.map((r) => r.label).join() !== "Auto,818p,546p" || !quality[0].on || qualityHead[0] !== "Adaptive")
      fail("settings", `the quality drawer shows ${JSON.stringify({ quality, qualityHead })}`);
    else pass("the Quality drawer lists Auto (checked, plain Adaptive) and the film's own rungs");
    asked.length = 0;
    await tap('.mp-aimed .mp-option[title="546p"]');
    const qualityLabel = await until(async () => /^Quality: (Up to )?546p/.test(await tvLabel("Quality") || ""), 8000);
    const qualityKept = await until(async () => (await mine("/api/me/preferences")).qualityHeight === 546, 6000);
    await phone.waitForSelector(".mp-aimed", { state: "detached", timeout: 5000 }).catch(() => {});
    const phoneQuality = await label("Quality");
    if (!qualityLabel || !qualityKept || phoneQuality !== "Quality: 546p") fail("settings", `choosing 546p: the TV's button says ${await tvLabel("Quality")}, the API holds ${(await mine("/api/me/preferences")).qualityHeight}, the phone says ${phoneQuality}`);
    else pass('choosing a fixed quality applies on the TV and is kept by the API; the phone says "546p", never a playing height');

    // The television's own menus move the phone's labels.
    await tv.page.click('.mp-menu-button[title="Subtitles"]');
    await tv.page.click('.mp-popover .mp-option[title="English"], .mp-menu .mp-option[title="English"]');
    const phoneSubtitle = await until(async () => (await label("Subtitles")) === "Subtitles: English", 8000);
    // The subtitles popover stays open over the control bar; a button under it is pressed as a script would.
    await tv.page.locator('.mp-menu-button[title="Quality"]').dispatchEvent("click");
    await tv.page.click('.mp-popover .mp-option[title="Auto"], .mp-menu .mp-option[title="Auto"]');
    const phoneAuto = await until(async () => (await label("Quality")) === "Quality: Auto", 8000);
    await shot("settings-21-after-tv-menu.png");
    if (!phoneSubtitle || !phoneAuto) fail("settings", `after the TV's own menus the phone says ${await label("Subtitles")} and ${await label("Quality")}`);
    else pass("a track and a quality chosen in the TV's own menus move the phone's labels");

    // Two quick choices in the TV's own menu: each is echoed back by the API, and the first never shows after the second.
    await tv.page.evaluate(() => {
      window.__quality = [];
      const read = () => [...document.querySelectorAll(".mp-menu-button")].map((b) => b.getAttribute("aria-label")).find((l) => l && l.startsWith("Quality"));
      new MutationObserver(() => { const l = read(); if (l && window.__quality[window.__quality.length - 1] !== l) window.__quality.push(l); })
        .observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["aria-label"] });
      const l = read(); if (l) window.__quality.push(l);
    });
    // The API's preferences pushes reach the television a second and a half late, so both choices are made before either echo.
    delayEchoes = true;
    await tv.page.locator('.mp-menu-button[title="Quality"]').dispatchEvent("click");
    await tv.page.locator('.mp-menu .mp-option[title="818p"], .mp-popover .mp-option[title="818p"]').first().dispatchEvent("click");
    await tv.page.locator('.mp-menu-button[title="Quality"]').dispatchEvent("click");
    await tv.page.locator('.mp-menu .mp-option[title="546p"], .mp-popover .mp-option[title="546p"]').first().dispatchEvent("click");
    await tv.page.waitForTimeout(5000);
    delayEchoes = false;
    const history = await tv.page.evaluate(() => window.__quality);
    // The setting a label names, apart from the height playing: "Auto · 818p" is Auto, "Up to 818p · 546p" is 818.
    const setting = (l) => { const m = /^Quality: (?:Up to )?(Auto|\d+)/.exec(l); return m ? m[1] : l; };
    const chosen = history.map(setting);
    const lastFirst = chosen.lastIndexOf("818");
    const firstSecond = chosen.indexOf("546");
    if (chosen[chosen.length - 1] !== "546" || lastFirst > firstSecond || (await mine("/api/me/preferences")).qualityHeight !== 546)
      fail("settings", `two quick quality choices on the TV showed ${JSON.stringify(history)}, the API holds ${(await mine("/api/me/preferences")).qualityHeight}`);
    else pass(`two quick quality choices on the TV showed ${JSON.stringify(history)}: the first never shows after the second, and the API holds 546`);
    await phone.waitForTimeout(500);
    if ((await label("Quality")) !== "Quality: 546p") fail("settings", `the phone says ${await label("Quality")} after the TV chose 546p`);
    else pass("and the phone's label follows the last choice");

    const after = { tv: counts("tv", "StateChanged", "ParticipantsChanged"), phone: counts("phone", "StateChanged", "ParticipantsChanged") };
    if (after.tv !== quiet.tv || after.phone !== quiet.phone)
      fail("settings", `the room was sent messages: StateChanged and ParticipantsChanged on the TV ${quiet.tv} became ${after.tv}, on the phone ${quiet.phone} became ${after.phone}`);
    else if ((heard.tv.TracksChanged || 0) < 3 || (heard.phone.TracksChanged || 0) < 3 || (heard.tv.PreferencesChanged || 0) < 3 || (heard.phone.PreferencesChanged || 0) < 3)
      fail("settings", `the settings were not pushed to both: TracksChanged ${heard.tv.TracksChanged}/${heard.phone.TracksChanged}, PreferencesChanged ${heard.tv.PreferencesChanged}/${heard.phone.PreferencesChanged} (tv/phone)`);
    else pass(`no StateChanged or ParticipantsChanged reached the TV or the phone for any of it, while ${heard.tv.TracksChanged} TracksChanged and ${heard.tv.PreferencesChanged} PreferencesChanged reached the TV and ${heard.phone.TracksChanged} and ${heard.phone.PreferencesChanged} the phone`);

    // A film starting afresh reads what the API holds: the audio and subtitle chosen above, on any device.
    await tv.page.goto(`${base}/r/${room}?title=${DualFilm}&tv=1`, { waitUntil: "load" });
    await tv.page.waitForFunction(() => { const v = document.querySelector("video"); return v && v.readyState >= 3; }, null, { timeout: 30000 }).catch(() => {});
    await tv.page.waitForTimeout(1500);
    const started = { audio: await tvLabel("Audio track"), subtitles: await tvLabel("Subtitles"), tracks: await tvTracks() };
    if (started.audio !== "Audio track: Español" || started.subtitles !== "Subtitles: English" || started.tracks.join() !== "English")
      fail("settings", `a film starting afresh on the TV read ${JSON.stringify(started)}, not the API's Español audio and English subtitles`);
    else pass("a film starting afresh on the TV starts on the tracks the API holds");
  } finally {
    await fetch(base + `/api/me/screens/${tvSession.sid}`, { method: "DELETE", headers: phoneAuth });
    await phoneContext.close();
    await tv.close();
  }
}

const checks = option("checks", "pages,heads,empty,overlays,back,player,remote,driving,remoteui,settings,signin").split(",");
try {
  if (checks.includes("pages")) for (const p of PAGES) if (!pageNames || pageNames.includes(p.name)) await walk(p);
  if (checks.includes("heads")) await heads();
  if (checks.includes("empty")) await empties();
  if (checks.includes("overlays")) {
    await overlay("save", "/movies/" + encodeURIComponent(films[0].id), ".kc-follow .k-btn");
    await overlay("notifications", "/", '.tv-bar__button[aria-haspopup="dialog"]');
  }
  if (checks.includes("back")) await backs();
  if (checks.includes("player")) await player();
  if (checks.includes("remote")) await remote();
  if (checks.includes("driving")) await driving();
  if (checks.includes("remoteui")) await remoteUi();
  if (checks.includes("settings")) await settingsUi();
  if (checks.includes("signin")) await signIn();
} finally {
  await browser.close();
}

console.log(`\nScreenshots in ${out}`);
if (failures.length > 0) {
  console.log(`${failures.length} failure(s)`);
  process.exit(1);
}
console.log("Every check passed.");
