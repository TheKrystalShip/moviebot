// Where focus goes on the reference television, key by key, against where it should go.
//
//   node verify/tvfocus/focus.mjs --serial <host:port> [--only walk,walk] [--shots] [--out dir] [--cdp-keys]
//
// It drives the debug build of tv/ on the production site with the remote's own keys, sent over adb as
// key events, so every key takes the path a person's press takes: the arrows and OK reach the page as
// the WebView delivers them, and Back and Menu reach the wrapper app, which asks the page. `--cdp-keys`
// types them into the page over DevTools instead (Back as Escape), which is faster and skips the
// wrapper.
//
// Each step is one key. The step records where focus was, every element focus passed through after the
// key until it stayed put, and where it came to rest, each named by `where` below; then it compares the
// resting place with the step's `expect`. A step without `expect` is recorded and reported, not judged,
// which is how a new walk is written: run it, read where focus went, write down where it should have.
// Whatever the expectation, a step is flagged when focus ends against the arrow (Down landing above
// where it was), when it passes through somewhere else on the way (it moved twice for one press), or
// when it ends on nothing.
//
// With --shots each step is also a picture: the screen after the key, with where focus was outlined in
// red, where it went in green, and where it should have gone, when that is elsewhere, in yellow.
//
// Output goes to `~/.cache/moviebot-verify/tvfocus/<time>/`: results.json, and with --shots one JPEG
// per step. Exit code 1 when a step missed its expectation or was flagged.

import { execFileSync, spawn } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { WALKS } from "./walks.mjs";

const args = process.argv.slice(2);
const flag = (n) => args.includes("--" + n);
const opt = (n) => { const i = args.indexOf("--" + n); return i >= 0 ? args[i + 1] : null; };
const serial = opt("serial") || process.env.ADB_SERIAL;
if (!serial) { console.error("--serial <host:port> (or ADB_SERIAL) names the television."); process.exit(2); }
const only = opt("only") ? opt("only").split(",") : null;
const shots = flag("shots");
const cdpKeys = flag("cdp-keys");
const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
const out = opt("out") || join(dir, "tvfocus", new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19));
mkdirSync(out, { recursive: true });
const Debug = "com.thekrystalship.cinema.debug";
const origin = "https://movies.thekrystalship.com";
const adb = (...a) => execFileSync("adb", ["-s", serial, ...a], { encoding: "utf8" }).trim();
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

/** The remote's keys: Android key codes over adb, page keys over DevTools. */
const KEYS = {
  Up: [19, "ArrowUp"], Down: [20, "ArrowDown"], Left: [21, "ArrowLeft"], Right: [22, "ArrowRight"],
  OK: [23, "Enter"], Back: [4, "Escape"], Menu: [82, "ContextMenu"],
};
const ARROW = { Up: "up", Down: "down", Left: "left", Right: "right" };

/**
 * Installed before the site's scripts: every focusin with the element named, and the name of whatever
 * is focused now. An element is named by where it is, so a walk can say where focus should be:
 * - `bar/<label>`: the bar's places and buttons;
 * - `hero/<label>`: the slides at the top of Home, and anything else marked `data-tv-top`;
 * - `row/<row title>/<n>`: the n-th card or tile of a row, from 0;
 * - `side/<label>`: a side column, such as the profile's sections;
 * - `dialog/<label>`: inside an open dialog or menu;
 * - `main/<label>`: anything else on the page; `none` is nothing focused.
 */
const recorder = () => {
  if (window.__focus) return;
  const Focusable = 'a[href], button, input, select, textarea, [tabindex], [contenteditable="true"]';
  const text = (el) => (el.getAttribute("aria-label") || el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 40);
  const where = (el) => {
    if (!el || el === document.body || el === document.documentElement) return { id: "none" };
    const r = el.getBoundingClientRect();
    // Where it is on the page rather than the screen: every scroll around it added back, so a column
    // scrolling under the move does not turn Down into Up.
    let sx = 0;
    let sy = 0;
    for (let n = el.parentElement; n; n = n.parentElement) { sx += n.scrollLeft; sy += n.scrollTop; }
    const base = {
      label: text(el),
      rect: { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) },
      pos: { x: Math.round(r.left + sx), y: Math.round(r.top + sy), w: Math.round(r.width), h: Math.round(r.height) },
    };
    const overlay = el.closest('[role="dialog"], [role="menu"], [aria-modal="true"]');
    if (overlay) return { ...base, id: "dialog/" + base.label };
    if (el.closest(".tv-bar")) return { ...base, id: "bar/" + base.label };
    const row = el.closest(".tv-row");
    if (row) {
      const head = row.querySelector(".tv-row__head");
      const title = head ? (head.firstChild && head.firstChild.nodeType === 3 ? head.firstChild.textContent : [...head.childNodes].filter((n) => !(n.classList && n.classList.contains("tv-row__count"))).map((n) => n.textContent).join("")).trim() : "?";
      const track = row.querySelector(".tv-row__track") || row;
      const items = [...track.querySelectorAll(Focusable)].filter((x) => !x.parentElement.closest(".tv-card") || x === el);
      return { ...base, id: `row/${title}/${items.indexOf(el)}` };
    }
    if (el.closest("[data-tv-top], .kc-tvh__hero")) return { ...base, id: "hero/" + base.label };
    if (el.closest("aside")) return { ...base, id: "side/" + base.label };
    return { ...base, id: "main/" + base.label };
  };
  const F = window.__focus = { log: [], keys: [], where, now: () => ({ ...where(document.activeElement), path: location.pathname }) };
  document.addEventListener("focusin", (e) => F.log.push({ t: performance.now(), path: location.pathname, ...where(e.target) }));
  // When each key arrived: the page's own keys as they come, Back and Menu as the wrapper asks the page.
  addEventListener("keydown", (e) => F.keys.push({ t: performance.now(), key: e.key }), true);
  const hook = setInterval(() => {
    const tv = window.krystalTv;
    if (!tv) return;
    for (const m of ["back", "menu"]) {
      const f = tv[m];
      tv[m] = (...a) => { F.keys.push({ t: performance.now(), key: m }); return f.apply(tv, a); };
    }
    clearInterval(hook);
  }, 100);
  // The outlines a picture is taken with: outside the body, which the navigator watches for changes.
  // The outlines a picture is taken with, from where the elements are drawn now: where focus was (the
  // element focused when the key was sent, `F.prev`), where it is, and the expected element when it
  // is elsewhere. They go outside the body, which the navigator watches for changes.
  F.mark = (expected) => {
    let layer = document.getElementById("__focus-marks");
    if (!layer) {
      layer = document.createElement("div");
      layer.id = "__focus-marks";
      layer.style.cssText = "position:fixed;inset:0;pointer-events:none;z-index:2147483647";
      document.documentElement.appendChild(layer);
    }
    const box = (el, color) => {
      if (!el || !el.isConnected || el === document.body) return null;
      const r = el.getBoundingClientRect();
      const d = document.createElement("div");
      d.style.cssText = `position:absolute;left:${r.left - 3}px;top:${r.top - 3}px;width:${r.width + 6}px;height:${r.height + 6}px;border:3px solid ${color};border-radius:6px;box-sizing:border-box`;
      return d;
    };
    let want = null;
    if (expected) {
      const re = new RegExp("^" + expected.split("*").map((s) => s.replace(/[.+?^${}()|[\]\\]/g, "\\$&")).join(".*") + "$");
      want = [...document.querySelectorAll(Focusable)].find((el) => re.test(where(el).id)) || null;
    }
    layer.replaceChildren(...[box(F.prev, "#ff3030"), box(want, "#ffd000"), box(document.activeElement, "#30ff60")].filter(Boolean));
  };
  F.unmark = () => { const l = document.getElementById("__focus-marks"); if (l) l.remove(); };
};

/** A step's expectation as a test: a name with `*` for anything, or a list of names any of which will do. */
function matches(expect, id) {
  const one = (e) => new RegExp("^" + e.split("*").map((s) => s.replace(/[.+?^${}()|[\]\\]/g, "\\$&")).join(".*") + "$").test(id);
  return Array.isArray(expect) ? expect.some(one) : one(expect);
}

/** Whether `to` sits the arrow's way from `from`, by their centres. */
function along(direction, from, to) {
  if (!from || !to) return true;
  const cx = (r) => r.x + r.w / 2;
  const cy = (r) => r.y + r.h / 2;
  switch (direction) {
    case "down": return cy(to) > cy(from);
    case "up": return cy(to) < cy(from);
    case "right": return cx(to) > cx(from);
    case "left": return cx(to) < cx(from);
    default: return true;
  }
}

adb("shell", "am", "force-stop", Debug);
adb("shell", "am", "start", "-W", "-n", `${Debug}/com.thekrystalship.cinema.CinemaActivity`, "--es", "url", origin);
let socket = null;
for (let i = 0; i < 40 && !socket; i++) {
  const pid = adb("shell", "pidof", Debug).split(/\s+/)[0];
  if (pid && adb("shell", "cat", "/proc/net/unix").includes(`webview_devtools_remote_${pid}`)) socket = `webview_devtools_remote_${pid}`;
  else await wait(250);
}
if (!socket) { console.error("The debug app's WebView offered no DevTools socket: is tv/ installed with --debug?"); process.exit(2); }
const port = adb("forward", "tcp:0", `localabstract:${socket}`);
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
const context = browser.contexts()[0];
let page = context.pages().find((p) => p.url().startsWith("http"));
for (let i = 0; i < 40 && !page; i++) { await wait(250); page = context.pages().find((p) => p.url().startsWith("http")); }
await context.addInitScript(recorder);
const cdp = await context.newCDPSession(page);

// One shell held open for the keys: a fresh `adb shell` per key costs a connection each time.
const shell = cdpKeys ? null : spawn("adb", ["-s", serial, "shell"], { stdio: ["pipe", "ignore", "inherit"] });
async function press(key) {
  const [code, name] = KEYS[key] || [null, key];
  if (shell && code !== null) shell.stdin.write(`input keyevent ${code}\n`);
  else await page.keyboard.press(name);
}

/** Waits until focus has stayed on one element, in one place, for `quiet` ms, or `max` ms have gone. */
async function settled(since, { quiet = 700, min = 900, max = 5000 } = {}) {
  // An adb key event takes the better part of a second to arrive: nothing is measured before it has.
  if (since > 0) {
    const sent = Date.now();
    while (Date.now() - sent < 5000) {
      const arrived = await page.evaluate((t) => window.__focus && window.__focus.keys.some((k) => k.t >= t), since).catch(() => false);
      if (arrived) break;
      await wait(50);
    }
  }
  const start = Date.now();
  let lastKey = null;
  let stable = Date.now();
  while (Date.now() - start < max) {
    await wait(100);
    const s = await page.evaluate((t) => {
      const F = window.__focus;
      if (!F) return null;
      const n = F.now();
      return { key: `${n.path}|${n.id}|${n.rect ? Object.values(n.rect).join(",") : ""}`, moves: F.log.filter((e) => e.t >= t).length };
    }, since).catch(() => null);
    const key = s ? s.key + "|" + s.moves : "loading";
    if (key !== lastKey) { lastKey = key; stable = Date.now(); }
    if (s && Date.now() - start >= min && Date.now() - stable >= quiet) return;
  }
}

const results = [];
let failures = 0;
try {
  for (const [name, walk] of Object.entries(WALKS)) {
    if (only ? !only.includes(name) : walk.optional) continue;
    console.log(`\n${name}${walk.about ? " — " + walk.about : ""}`);
    await page.goto(origin + walk.start, { waitUntil: "commit" });
    await page.waitForFunction(() => window.__focus && document.activeElement && document.activeElement !== document.body, null, { timeout: 15000 }).catch(() => {});
    await settled(0, { quiet: 1500, min: walk.settle || 4000, max: 15000 });
    if (walk.ready) {
      const now = await page.evaluate(() => window.__focus.now());
      const ok = matches(walk.ready, now.id);
      if (!ok) failures++;
      console.log(`  ${ok ? "ok  " : "MISS"} ${"(open)".padEnd(10)} ${"".padEnd(38)} -> ${now.id}${ok ? "" : `   expected ${walk.ready}`}`);
      results.push({ walk: name, step: 0, key: "(open)", to: now, expect: walk.ready, ok });
    }
    for (let i = 0; i < walk.steps.length; i++) {
      const step = walk.steps[i];
      const before = await page.evaluate(() => {
        window.__focus.prev = document.activeElement;
        return { ...window.__focus.now(), t: performance.now() };
      });
      await press(step.key);
      await settled(before.t, step.open ? { quiet: 1200, min: 2500, max: 10000 } : {});
      const after = await page.evaluate((t) => ({ now: window.__focus.now(), passed: window.__focus.log.filter((e) => e.t >= t) }), before.t);
      const to = after.now;
      // Every element focus rested on between the key and the end, the end itself left out.
      const via = after.passed.slice(0, -1).filter((e, j, a) => e.id !== to.id && (j === 0 || e.id !== a[j - 1].id)).map((e) => e.id);
      const flags = [];
      if (to.id === "none") flags.push("focus on nothing");
      if (ARROW[step.key] && to.path === before.path && to.id !== before.id && !along(ARROW[step.key], before.pos, to.pos)) flags.push("against the arrow");
      if (via.length > 0) flags.push("moved more than once");
      const ok = step.expect ? matches(step.expect, to.id) : null;
      const pathOk = step.path ? to.path === step.path : true;
      const bad = ok === false || !pathOk || (flags.length > 0 && !step.allow);
      if (bad) failures++;
      const record = { walk: name, step: i + 1, key: step.key, note: step.note, from: before, to, via, expect: step.expect, path: step.path, ok, pathOk, flags };
      if (shots) {
        await page.evaluate((e) => window.__focus.mark(e), step.expect && !ok ? [step.expect].flat()[0] : null);
        const file = `${name}-${String(i + 1).padStart(2, "0")}-${step.key}.jpg`;
        const shot = await cdp.send("Page.captureScreenshot", { format: "jpeg", quality: 70, clip: { x: 0, y: 0, width: 960, height: 540, scale: 0.5 } });
        writeFileSync(join(out, file), Buffer.from(shot.data, "base64"));
        await page.evaluate(() => window.__focus.unmark());
        record.shot = file;
      }
      results.push(record);
      const verdict = bad ? (ok === false || !pathOk ? "MISS" : "FLAG") : ok ? "ok  " : "    ";
      const moved = before.path !== to.path ? `  [${before.path} -> ${to.path}]` : "";
      const why = [
        ok === false ? `expected ${[step.expect].flat().join(" | ")}` : "",
        !pathOk ? `expected page ${step.path}` : "",
        via.length ? `via ${via.join(" > ")}` : "",
        ...flags.filter((f) => f !== "moved more than once"),
      ].filter(Boolean).join("; ");
      console.log(`  ${verdict} ${step.key.padEnd(5)} ${before.id.slice(0, 40).padEnd(40)} -> ${to.id}${moved}${why ? "   " + why : ""}${step.note ? `   (${step.note})` : ""}`);
    }
  }
} finally {
  writeFileSync(join(out, "results.json"), JSON.stringify(results, null, 1));
  if (shell) { shell.stdin.end(); shell.kill(); }
  await browser.close().catch(() => {});
  try { adb("forward", "--remove", `tcp:${port}`); } catch { /* gone with the app */ }
  adb("shell", "am", "force-stop", Debug);
  adb("shell", "am", "start", "-n", "com.thekrystalship.cinema/.CinemaActivity");
  console.log(`\n${failures} step(s) missed or flagged. Results: ${join(out, "results.json")}`);
}
process.exit(failures > 0 ? 1 : 0);
