// The phone's remote dock under a finger, with real touch events (CDP Input.dispatchTouchEvent) on a
// Galaxy's Chromium, against the scratch site scratch.sh serves, once with nothing on the TV and once with
// a film paused on it. It is one dock whose bottom edge stays on the tab bar's top while a finger moves its
// top edge: measured mid-drag, its height is the finger's travel, the collapsed row gives way to the open
// remote as it grows, and let go it settles open or collapsed by distance and speed. A pull down on the open
// remote's head, or on its body scrolled to the top, collapses it; the tab bar stays uncovered and
// answers a tap while it is open.
//
//   node verify/mobile/dock.mjs [--out dir]      exits 1 when a check fails
//
// A screenshot of each held frame goes to --out (default: a run directory of its own).
import { join } from "node:path";
import { homedir } from "node:os";
import { readFileSync, readdirSync, existsSync, mkdirSync } from "node:fs";
import { chromium, devices } from "playwright-core";
const { PEOPLE, stage } = await import("./stage.mjs");
const { issuerIn } = await import("../issuer.mjs");
const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
const env = Object.fromEntries(readFileSync(join(dir, "api.env"), "utf8").split("\n").filter(Boolean).map(l => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1)]));
const base = `http://127.0.0.1:${env.PORT}`;
const issuer = issuerIn(dir, Number(env.ISSUER_PORT));
const at = process.argv.indexOf("--out");
const out = at >= 0 ? process.argv[at + 1] : join(dir, "runs", "dock-" + new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19)); mkdirSync(out, { recursive: true });
const cache = join(homedir(), ".cache", "ms-playwright");
const exe = readdirSync(cache).filter(d => d.startsWith("chromium-")).sort().reverse().map(d => join(cache, d, "chrome-linux64", "chrome")).find(existsSync);
const staged = await stage(base, issuer);
const browser = await chromium.launch({ executablePath: exe });
const results = [];
const check = (name, ok, detail = "") => { results.push({ name, ok }); console.log((ok ? "PASS " : "FAIL ") + name + (detail ? "  " + detail : "")); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const ramp = (y0, y1, n) => Array.from({ length: n + 1 }, (_, i) => Math.round(y0 + (y1 - y0) * i / n));

async function phone(tv) {
  const sid = `verify-phone-dock-${tv}`;
  await staged.screens.pair(sid, [tv]);
  const desc = { ...devices["Galaxy S24"] }; delete desc.defaultBrowserType;
  const context = await browser.newContext(desc);
  await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(PEOPLE.viewer, { sid }));
  await context.addInitScript(() => localStorage.setItem("cinema:remote", "1"));
  const page = await context.newPage();
  page.errors = []; page.on("pageerror", e => page.errors.push(e.message));
  await page.goto(base + "/", { waitUntil: "load" });
  await page.waitForFunction(() => document.querySelector(".kc-remote-strip .kc-dl-head__name")?.textContent, null, { timeout: 20000 });
  await page.waitForTimeout(1200);
  page.cdp = await context.newCDPSession(page);
  page.done = async () => { await context.close(); await staged.screens.unpair(sid, [tv]); };
  return page;
}

/** The dock as it stands: how many there are, its edges, how far open, and the row's and the remote's opacity. */
const geom = (page) => page.evaluate(() => {
  const docks = document.querySelectorAll(".kc-remote-dock");
  const d = docks[0], b = d.getBoundingClientRect();
  const op = (s) => { const n = document.querySelector(s); return n ? Number(getComputedStyle(n).opacity) : null; };
  return {
    docks: docks.length, top: b.top, bottom: b.bottom, h: b.height, open: d.classList.contains("is-open"),
    p: Number(getComputedStyle(d).getPropertyValue("--dock-p")), row: op(".kc-remote-strip"), remote: op(".kc-remote"),
    remoteDrawn: Boolean(document.querySelector(".kc-remote")), tabTop: document.querySelector(".tabbar").getBoundingClientRect().top,
  };
});

/**
 * One touch through `ys` at `stepMs` a step, its clock carried by the events (CDP `timestamp`) so its speed is
 * the one asked for; `hold(i)` runs after the i-th move with the finger still down.
 */
async function touch(page, x, ys, stepMs, hold) {
  const t0 = Date.now() / 1000;
  const t = (type, y, i) => page.cdp.send("Input.dispatchTouchEvent", { type, touchPoints: type === "touchEnd" ? [] : [{ x, y }], timestamp: t0 + i * stepMs / 1000 });
  await t("touchStart", ys[0], 0);
  let i = 1;
  for (const y of ys.slice(1)) {
    await sleep(stepMs); await t("touchMove", y, i);
    // A move is delivered on the next frame, so the dock is measured after it.
    if (hold) { await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)))); await hold(i, y); }
    i++;
  }
  await sleep(stepMs); await t("touchEnd", 0, i);
  await page.waitForTimeout(450);
}
const rowPoint = async (page) => page.evaluate(() => { const b = document.querySelector(".kc-remote-strip").getBoundingClientRect(); return { x: b.x + b.width * 0.6, y: b.y + b.height / 2 }; });
const headPoint = async (page) => page.evaluate(() => { const b = document.querySelector(".kc-remote .kc-drawer__top").getBoundingClientRect(); return { x: b.x + b.width * 0.5, y: b.y + 10 }; });
const bodyPoint = async (page) => page.evaluate(() => { const b = document.querySelector(".kc-remote__body").getBoundingClientRect(); return { x: b.x + b.width * 0.5, y: b.y + 30 }; });

async function run(tv, label) {
  const page = await phone(tv);
  const rest = await geom(page);
  check(`${label}: one dock at rest, standing on the tab bar`, rest.docks === 1 && Math.abs(rest.bottom - rest.tabTop) <= 0.5 && !rest.remoteDrawn, JSON.stringify(rest));

  // A slow push up, held at each step: one dock, the same bottom edge, the height the finger's travel.
  const row = await rowPoint(page);
  const frames = [];
  await touch(page, row.x, ramp(row.y, row.y - 360, 18), 40, async (i, y) => {
    const g = await geom(page);
    frames.push({ travel: row.y - y, ...g });
    if (i % 6 === 0) await page.screenshot({ path: join(out, `${label}-push-${i}.png`) });
  });
  const opened = await geom(page);
  const full = opened.h;
  const tracked = frames.filter((f) => f.travel > 12 && rest.h + f.travel < full - 4);
  const anchored = frames.every((f) => f.docks === 1 && Math.abs(f.bottom - f.tabTop) <= 0.5);
  const following = tracked.every((f) => Math.abs(f.h - (rest.h + f.travel)) <= 2);
  const handover = frames.some((f) => f.row < 0.5 && f.remote > 0.5) && frames[0].row > 0.5;
  check(`${label}: pushed up, the one dock grows from the tab bar with the finger`, anchored && following && tracked.length >= 4,
    JSON.stringify(tracked.map((f) => [f.travel, Math.round(f.h), Math.round(f.bottom)])));
  check(`${label}: the row gives way to the open remote as it grows`, handover, JSON.stringify(frames.map((f) => [f.row, f.remote].map((v) => v === null ? "-" : v.toFixed(2)))));
  check(`${label}: let go far up, it settles open on the tab bar`, opened.open && opened.p === 1 && Math.abs(opened.bottom - opened.tabTop) <= 0.5, JSON.stringify(opened));
  await page.screenshot({ path: join(out, `${label}-open.png`) });

  // The tab bar is uncovered and takes a tap while the dock is open.
  const tab = await page.evaluate(() => {
    const t = [...document.querySelectorAll(".tabbar__tab")].find((e) => /Library/.test(e.textContent));
    const b = t.getBoundingClientRect();
    return { x: b.x + b.width / 2, y: b.y + b.height / 2, hit: t.contains(document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2)) };
  });
  check(`${label}: the tab bar is uncovered while the dock is open`, tab.hit);

  // A short slow pull on the head comes back open; a long one collapses it, and the open remote goes.
  let head = await headPoint(page);
  await touch(page, head.x, ramp(head.y, head.y + 24, 6), 40);
  const back = await geom(page);
  check(`${label}: a short pull on the head settles open again`, back.open && Math.abs(back.h - full) <= 1, JSON.stringify(back));
  head = await headPoint(page);
  const pulled = [];
  await touch(page, head.x, ramp(head.y, head.y + 240, 12), 40, async (i) => { pulled.push(await geom(page)); if (i === 6) await page.screenshot({ path: join(out, `${label}-pull-6.png`) }); });
  await page.waitForTimeout(300);
  const shut = await geom(page);
  check(`${label}: pulled down by the head, the one dock shrinks onto the tab bar and collapses`,
    pulled.every((f) => f.docks === 1 && Math.abs(f.bottom - f.tabTop) <= 0.5) && !shut.open && Math.abs(shut.h - rest.h) <= 1 && !shut.remoteDrawn, JSON.stringify(shut));

  // A short slow push stays collapsed; a short flick opens it.
  let r = await rowPoint(page);
  await touch(page, r.x, ramp(r.y, r.y - 24, 6), 40);
  check(`${label}: a short slow push settles collapsed again`, !(await geom(page)).open);
  r = await rowPoint(page);
  await touch(page, r.x, ramp(r.y, r.y - 40, 4), 10);
  check(`${label}: a short flick up opens it`, (await geom(page)).open);

  // A pull down on the body, scrolled to its top, collapses it.
  await page.evaluate(() => { document.querySelector(".kc-remote__body").scrollTop = 0; });
  const body = await bodyPoint(page);
  await touch(page, body.x, ramp(body.y, body.y + 220, 11), 40, async (i) => { if (i === 5) await page.screenshot({ path: join(out, `${label}-body-5.png`) }); });
  await page.waitForTimeout(300);
  const fromBody = await geom(page);
  check(`${label}: a pull down on the body at its top collapses it`, !fromBody.open && Math.abs(fromBody.h - rest.h) <= 1, JSON.stringify(fromBody));

  // A tab tapped while the dock is open goes to its page and the dock collapses onto it.
  await page.click(".kc-remote-strip__open");
  await page.waitForTimeout(500);
  await page.cdp.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x: tab.x, y: tab.y }] });
  await page.cdp.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
  await page.waitForTimeout(700);
  const away = await page.evaluate(() => location.pathname);
  check(`${label}: a tab tapped over the open dock goes to its page, and the dock collapses`, away !== "/" && !(await geom(page)).open, away);

  check(`${label}: the page raised nothing`, page.errors.length === 0, page.errors.join(" | "));
  await page.done();
}

await run("bedroom", "idle");
await run("living", "playing");
await browser.close();
await staged.close();
console.log(`\nScreenshots: ${out}`);
const failed = results.filter((r) => !r.ok).length;
console.log(`${results.length - failed} of ${results.length} checks pass.`);
process.exit(failed ? 1 : 0);
