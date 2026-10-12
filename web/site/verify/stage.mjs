// Nothing of a page being prepared out of sight is seen or reached: every frame of a move between
// pages, on a desktop and on a phone, against the scratch site verify/mobile/scratch.sh serves.
//
//   node verify/stage.mjs
//
// A page navigated to is mounted beside the one shown and prepared out of sight (`PageStage`). Each
// move here records every animation frame while it is, and fails on any frame where something inside
// it is drawn (visible, on screen, under no transparent ancestor) or could take focus (a control that
// is drawn or not, outside `inert`), naming what it was.
//
// A move holds back one read its page waits on, so the page is out of sight with the rest of it drawn.
// A move counts only if the page was seen out of sight holding a slide ready to show at the top: a run
// that never reaches that state has looked at nothing, and fails as inconclusive.
//
// Exits 1 on a frame that shows or reaches the page being prepared, or an inconclusive move; 2 when
// there is no scratch site.

import { existsSync, readFileSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { VIEWER, stage } from "./mobile/stage.mjs";
import { SITE_CLIENT, issuerIn } from "./issuer.mjs";

/** The read each move holds back, and how long: under PageStage's half-second cap, so the page is still waited for. */
const HOLD_MS = 400;
const MOVES = [
  // Discover's rows ask for their shelves; the spotlight's shelf comes with the page's index.
  { from: "/", to: "/discover", hold: /\/api\/discover\/shelves\// },
  { from: "/discover", to: "/", hold: /\/api\/lists\/later/ },
];
const SCREENS = [
  { name: "desktop", viewport: { width: 1280, height: 800 } },
  { name: "phone", viewport: { width: 412, height: 915 }, isMobile: true, hasTouch: true },
];

const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
if (!existsSync(join(dir, "api.env"))) {
  console.error(`No scratch site in ${dir}. Start verify/mobile/scratch.sh first.`);
  process.exit(2);
}
const site = Object.fromEntries(readFileSync(join(dir, "api.env"), "utf8").split("\n").filter(Boolean)
  .map((line) => [line.slice(0, line.indexOf("=")), line.slice(line.indexOf("=") + 1)]));
const origin = `http://127.0.0.1:${site.PORT}`;
const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
const staged = await stage(origin, issuer);

/** Installed before the site's scripts: each frame, what of the page being prepared is seen or reached. */
function recorder() {
  const FOCUSABLE = "a[href], button, input, select, textarea, [tabindex]";
  window.__stage = { frames: [], recording: false };
  const label = (el) => (el.getAttribute("aria-label") || el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 40);
  const name = (el) => `${el.tagName.toLowerCase()}${typeof el.className === "string" && el.className ? "." + el.className.trim().split(/\s+/).join(".") : ""}${label(el) ? ` "${label(el)}"` : ""}`;
  const transparent = (el, page) => {
    for (let a = el; a && a !== page.parentElement; a = a.parentElement) if (getComputedStyle(a).opacity === "0") return true;
    return false;
  };
  const tick = () => {
    const page = window.__stage.recording && document.querySelector(".kc-stage__page.is-incoming");
    if (page) {
      const seen = [];
      const reached = [];
      for (const el of page.querySelectorAll("*")) {
        const style = getComputedStyle(el);
        if (el.matches(FOCUSABLE) && !el.closest("[inert]") && style.visibility === "visible" && style.display !== "none") reached.push(el);
        if (style.visibility !== "visible") continue;
        const r = el.getBoundingClientRect();
        if (r.width === 0 || r.height === 0 || r.bottom <= 0 || r.top >= innerHeight || r.right <= 0 || r.left >= innerWidth) continue;
        if (!transparent(el, page)) seen.push(el);
      }
      // The outermost of what is seen, so one leak is named once.
      const outer = seen.filter((el) => !seen.includes(el.parentElement));
      window.__stage.frames.push({
        t: Math.round(performance.now()),
        ready: !!page.querySelector(".kc-featured__body.is-shown"),
        seen: outer.slice(0, 3).map(name), seenCount: seen.length,
        reached: reached.slice(0, 3).map(name), reachedCount: reached.length,
      });
    }
    requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}

function pageName(path) {
  return path === "/" ? "Home" : path.slice(1, 2).toUpperCase() + path.slice(2);
}

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
const failures = [];
try {
  for (const screen of SCREENS) {
    const context = await browser.newContext({ viewport: screen.viewport, isMobile: !!screen.isMobile, hasTouch: !!screen.hasTouch });
    await context.addInitScript(({ key, value }) => { if (!localStorage.getItem(key)) localStorage.setItem(key, value); },
      issuer.planted(VIEWER, { hours: 6, client: SITE_CLIENT }));
    await context.addInitScript(recorder);
    const page = await context.newPage();
    let hold = null;
    await page.route(/\/api\//, async (route) => {
      if (hold && hold.test(new URL(route.request().url()).pathname)) await new Promise((r) => setTimeout(r, HOLD_MS));
      await route.continue();
    });
    for (const move of MOVES) {
      const key = `${screen.name}, ${pageName(move.from)} to ${pageName(move.to)}`;
      await page.goto(origin + move.from);
      await page.waitForLoadState("networkidle");
      await page.waitForTimeout(1500);
      hold = move.hold;
      await page.evaluate(() => { window.__stage.frames = []; window.__stage.recording = true; });
      await page.evaluate((to) => { history.pushState(null, "", to); dispatchEvent(new PopStateEvent("popstate")); }, move.to);
      await page.waitForTimeout(HOLD_MS + 1000);
      hold = null;
      const frames = await page.evaluate(() => { window.__stage.recording = false; return window.__stage.frames; });
      const ready = frames.filter((f) => f.ready).length;
      const seen = frames.filter((f) => f.seenCount > 0);
      const reached = frames.filter((f) => f.reachedCount > 0);
      if (ready === 0) {
        failures.push(`${key}: inconclusive, the page was never out of sight with a slide ready (${frames.length} frames out of sight)`);
        console.log(`  ? ${key}: never out of sight with a slide ready`);
        continue;
      }
      const problems = [];
      if (seen.length) problems.push(`seen in ${seen.length} of ${frames.length} frames: ${seen[0].seen.join(", ")} (${seen[0].seenCount} elements)`);
      if (reached.length) problems.push(`reachable in ${reached.length} of ${frames.length} frames: ${reached[0].reached.join(", ")} (${reached[0].reachedCount} controls)`);
      if (problems.length) {
        for (const p of problems) failures.push(`${key}: ${p}`);
        console.log(`  ✗ ${key}\n      ${problems.join("\n      ")}`);
      } else {
        console.log(`  ✓ ${key}: out of sight for ${frames.length} frames, ${ready} with a slide ready, nothing seen or reachable`);
      }
    }
    await context.close();
  }
} finally {
  await browser.close();
  await staged.close();
}

if (failures.length) {
  console.log(`\n${failures.length} failure${failures.length === 1 ? "" : "s"}:`);
  for (const f of failures) console.log(`  ✗ ${f}`);
  process.exit(1);
}
console.log("\nNothing of a page being prepared was seen or reachable.");
