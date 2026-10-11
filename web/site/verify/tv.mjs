// Krystal Cinema driven by a remote alone: every page in TV mode at the reference television's size,
// walked with the arrow keys from where focus starts, against the scratch site verify/mobile/scratch.sh
// serves (staged first, by its run or `node verify/mobile/run.mjs`; an unstaged site stops this run).
//
//   node verify/tv.mjs [--checks pages,overlays,back,player,signin] [--pages home,title] [--out dir]
//
// Each page is checked for:
// - focus starting on a control when it opens, and on screen;
// - every control on it reached from there with arrows alone (Menu into a card's own controls and
//   Back out of them counted too), and each one on screen when it is reached;
// - focus never falling to nothing after a key.
// Then what the keys do: an overlay taking focus when it opens and handing it back when it closes,
// Back going back a page to the control it was left on, Back on Home going to the bar and then
// being left to the app, and the player: the play button focused, OK playing, presses gathered into
// one skip, a held key scrubbing and sending one seek, Menu opening the subtitles, and Back leaving.
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
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { VIEWER, memberToken } from "./mobile/stage.mjs";
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

const PROFILE_TABS = ["", "/requests", "/following", "/notifications", "/playback", "/subtitles", "/appearance", "/account"];
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

async function open(path) {
  const context = await browser.newContext({ viewport: Viewport, reducedMotion: "reduce" });
  await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER, { hours: 6, client: TV_CLIENT }));
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
const until = async (what, timeout) => {
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

const checks = option("checks", "pages,overlays,back,player,signin").split(",");
try {
  if (checks.includes("pages")) for (const p of PAGES) if (!pageNames || pageNames.includes(p.name)) await walk(p);
  if (checks.includes("overlays")) {
    await overlay("save", "/movies/" + encodeURIComponent(films[0].id), ".kc-follow .k-btn");
    await overlay("notifications", "/", '.tv-bar__button[aria-haspopup="dialog"]');
  }
  if (checks.includes("back")) await backs();
  if (checks.includes("player")) await player();
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
