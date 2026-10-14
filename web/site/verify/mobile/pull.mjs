// A drawer pulled by its head, with real touch events (CDP Input.dispatchTouchEvent) on a Galaxy's
// Chromium, against the scratch site scratch.sh serves: its tab and corners, the distance and the speed
// that close it, a short pull and a twitch that do not, and a long body that still scrolls.
//
//   node verify/mobile/pull.mjs [--out dir]      exits 1 when a check fails
//
// Screenshots of the drawer at rest and held go to --out (default: a run directory of its own).
import { join } from "node:path";
import { homedir } from "node:os";
import { readFileSync, readdirSync, existsSync, mkdirSync } from "node:fs";
import { chromium, devices } from "playwright-core";
const { PEOPLE, stage } = await import("./stage.mjs");
const { issuerIn } = await import("../issuer.mjs");
const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
const env = Object.fromEntries(readFileSync(join(dir, "api.env"), "utf8").split("\n").filter(Boolean).map(l => l.split("=")));
const base = `http://127.0.0.1:${env.PORT}`;
const issuer = issuerIn(dir, Number(env.ISSUER_PORT));
const at = process.argv.indexOf("--out");
const out = at >= 0 ? process.argv[at + 1] : join(dir, "runs", "pull-" + new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19)); mkdirSync(out, { recursive: true });
const cache = join(homedir(), ".cache", "ms-playwright");
const exe = readdirSync(cache).filter(d => d.startsWith("chromium-")).sort().reverse().map(d => join(cache, d, "chrome-linux64", "chrome")).find(existsSync);
const staged = await stage(base, issuer);
const browser = await chromium.launch({ executablePath: exe });
const results = [];
const check = (name, ok, detail = "") => { results.push({ name, ok }); console.log((ok ? "PASS " : "FAIL ") + name + (detail ? "  " + detail : "")); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

async function newPage(viewport) {
  const desc = { ...devices["Galaxy S24"] }; delete desc.defaultBrowserType;
  if (viewport) desc.viewport = viewport;
  const context = await browser.newContext(desc);
  await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(PEOPLE.viewer));
  const page = await context.newPage();
  page.errors = []; page.on("pageerror", e => page.errors.push(e.message));
  await page.goto(base + "/movies/" + encodeURIComponent(staged.film.id), { waitUntil: "load" });
  await page.waitForSelector(".kc-title .kc-actions .k-btn--accent");
  await page.waitForTimeout(800);
  page.cdp = await context.newCDPSession(page);
  return page;
}
// Events carry their own clock (CDP `timestamp`, seconds), so the speed a drag has is the one asked for
// whatever the rig's own latency; the delivery is paced at the same step.
async function touch(page, x, ys, stepMs) {
  const t0 = Date.now() / 1000;
  const t = (type, y, i) => page.cdp.send("Input.dispatchTouchEvent", { type, touchPoints: type === "touchEnd" ? [] : [{ x, y }], timestamp: t0 + i * stepMs / 1000 });
  await t("touchStart", ys[0], 0);
  let i = 1;
  for (const y of ys.slice(1)) { await sleep(stepMs); await t("touchMove", y, i++); }
  await sleep(stepMs); await t("touchEnd", 0, i);
}
const headY = async (page) => (await page.evaluate(() => document.querySelector(".k-drawer__top").getBoundingClientRect().y)) + 12;
const isOpen = (page) => page.evaluate(() => !!document.querySelector(".k-drawer"));
const ramp = (y0, y1, n) => Array.from({ length: n + 1 }, (_, i) => Math.round(y0 + (y1 - y0) * i / n));
async function openSave(page) {
  await page.locator(".kc-actions .k-btn--accent").first().click();
  await page.waitForSelector(".k-drawer");
  await page.waitForTimeout(400);
}
const geom = (page) => page.evaluate(() => {
  const d = document.querySelector(".k-drawer"), top = d.querySelector(".k-drawer__top"), g = d.querySelector(".k-drawer__grabber");
  const r = (e) => { const b = e.getBoundingClientRect(); return { x: b.x, y: b.y, w: b.width, h: b.height }; };
  const cs = getComputedStyle;
  return { d: r(d), top: r(top), g: r(g), radius: cs(d).borderTopLeftRadius, ta: cs(top).touchAction, ob: cs(d).overscrollBehaviorY,
    gbg: cs(g).backgroundColor, clip: cs(g).clipPath, tr: d.style.transform, open: !!document.querySelector(".k-drawer"), mainTop: document.querySelector(".k-app__main")?.scrollTop };
});
const cx = 206;

// 1. Geometry and style at rest.
let page = await newPage();
await openSave(page);
let g = await geom(page);
check("drawer has square corners", g.radius === "0px", g.radius);
check("tab is 40x6, along the top edge, inside the drawer", Math.abs(g.g.w - 40) < .5 && Math.abs(g.g.h - 6) < .5 && Math.abs(g.g.y - g.d.y - 1) < 1.5 && g.g.y >= g.d.y, JSON.stringify(g.g) + " drawer " + JSON.stringify(g.d));
check("head holds touch-action none; drawer holds overscroll-behavior contain", g.ta === "none" && g.ob === "contain", g.ta + " / " + g.ob);
await page.screenshot({ path: join(out, "drawer-rest.png") });
const strong = await page.evaluate(() => { const p = document.createElement("i"); p.style.color = "var(--border-strong)"; document.body.append(p); const c = getComputedStyle(p).color; p.remove(); const t = document.createElement("i"); t.style.color = "var(--krystal-teal)"; document.body.append(t); const c2 = getComputedStyle(t).color; t.remove(); return [c, c2]; });
check("tab at rest is --border-strong", g.gbg === strong[0], g.gbg + " vs " + strong[0]);
// held: touch down on the head and hold
const topY = g.top.y + 12;
await page.cdp.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x: cx, y: topY }] });
for (const y of ramp(topY, topY + 30, 5)) { await page.cdp.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: [{ x: cx, y }] }); await sleep(16); }
await page.waitForTimeout(300);
const held = await geom(page);
check("tab is teal while held", held.gbg === strong[1] && await page.evaluate(() => !!document.querySelector(".k-drawer__top.k-is-held")), held.gbg);
check("drawer follows the finger (translated down ~30px)", /translateY\(3[01](\.\d+)?px\)/.test(held.tr), held.tr);
await page.screenshot({ path: join(out, "drawer-held.png") });
await page.cdp.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
await page.waitForTimeout(500);
g = await geom(page);
check("short pull (30px, slow) settles back open", g.open && (g.tr === "" || g.tr === "translateY(0px)"), "tr=" + JSON.stringify(g.tr) + " h=" + Math.round(g.d.h));

// 2. Distance: slow pulls (well under the flick speed) at increasing distance; which close.
const h = g.d.h;
const far = Math.min(120, Math.max(40, h * .3));
console.log("  drawer height", Math.round(h), "-> distance threshold", far.toFixed(0), "px");
for (const dy of [far - 8, far + 8]) {
  if (!(await isOpen(page))) await openSave(page);
  const y = await headY(page);
  await touch(page, cx, ramp(y, y + dy, 20), 40);       // about 0.25 px/ms or less
  await page.waitForTimeout(600);
  const o = await isOpen(page);
  check(`slow pull ${Math.round(dy)}px ${dy < far ? "stays open" : "closes"}`, dy < far ? o : !o);
}
// 3. Speed: the same short distance, slow and fast; and the flick floor.
for (const [dy, steps, stepMs, expectClosed, name] of [[60, 4, 20, true, "flick 60px at 0.75 px/ms closes"], [60, 4, 40, false, "60px at 0.375 px/ms stays open"], [10, 2, 8, false, "twitch 10px at 0.6 px/ms stays open"]]) {
  if (!(await isOpen(page))) await openSave(page);
  const y = await headY(page);
  await touch(page, cx, ramp(y, y + dy, steps), stepMs);
  await page.waitForTimeout(600);
  const o = await isOpen(page);
  check(name, expectClosed ? !o : o);
}
// 4. Taps and the other directions.
if (!(await isOpen(page))) await openSave(page);
let y = await headY(page);
await page.touchscreen.tap(cx, y); await page.waitForTimeout(300);
check("tap on the head leaves it open", await isOpen(page));
await touch(page, cx, ramp(y, y - 80, 10), 16);
await page.waitForTimeout(300);
check("pull up on the head moves nothing", await page.evaluate(() => !!document.querySelector(".k-drawer") && document.querySelector(".k-drawer").style.transform === ""));
await touch(page, 60, ramp(y, y + 4, 2), 16); await touch(page, cx - 100, [y, y + 2], 16);
// pointer stream is never cancelled by the browser on the head; the same drag on a list option is (the control)
await page.evaluate(() => { window.__pc = 0; document.addEventListener("pointercancel", () => window.__pc++, true); });
y = await headY(page);
await touch(page, cx, ramp(y, y + 50, 10), 16); await page.waitForTimeout(500);
check("a pull by the head is never taken over by the browser (0 pointercancel)", (await page.evaluate(() => window.__pc)) === 0);
if (!(await isOpen(page))) await openSave(page);
y = await headY(page);
await page.evaluate(() => { window.__pc = 0; });
await touch(page, cx, ramp(y + 60, y + 110, 10), 16); await page.waitForTimeout(300);
check("control: the same drag on the drawer's list IS taken over for scrolling (pointercancel > 0)", (await page.evaluate(() => window.__pc)) > 0);
await page.touchscreen.tap(cx, 40); await page.waitForTimeout(400);
check("tap on the scrim closes it", !(await isOpen(page)));
check("no page errors", page.errors.length === 0, page.errors.join("; "));
await page.context().close();

// 5. A drawer that overflows: its body scrolls under a finger, and a sweep down at its top neither closes it nor moves the page.
page = await newPage({ width: 412, height: 420 });
await openSave(page);
const body = await page.evaluate(() => { const d = document.querySelector(".k-drawer"); return { sh: d.scrollHeight, ch: d.clientHeight }; });
console.log("  Save drawer at 412x420:", JSON.stringify(body));
await page.evaluate(() => { const m = document.querySelector(".k-app__main"); if (m) m.scrollTop = 200; window.__main = m ? m.scrollTop : null; });
const m0 = await page.evaluate(() => window.__main);
if (body.sh > body.ch + 4) {
  const gg = await geom(page);
  await touch(page, cx, ramp(gg.d.y + gg.d.h - 40, gg.d.y + 60, 14), 16);
  const after = await page.evaluate(() => document.querySelector(".k-drawer").scrollTop);
  check("a finger sweeping up the drawer body scrolls it", after > 0, "scrollTop " + after);
  await touch(page, cx, ramp(gg.d.y + 60, gg.d.y + gg.d.h - 20, 14), 16);
  await page.waitForTimeout(400);
  const back = await page.evaluate(() => ({ s: document.querySelector(".k-drawer")?.scrollTop, open: !!document.querySelector(".k-drawer"), main: document.querySelector(".k-app__main")?.scrollTop }));
  check("sweeping down scrolls it back to the top, the drawer stays open", back.open && back.s < after, JSON.stringify(back));
  await touch(page, cx, ramp(gg.d.y + 60, gg.d.y + gg.d.h - 20, 14), 16);
  await page.waitForTimeout(400);
  const more = await page.evaluate(() => ({ open: !!document.querySelector(".k-drawer"), main: document.querySelector(".k-app__main")?.scrollTop, doc: document.scrollingElement.scrollTop }));
  check("a further sweep down at the top neither closes the drawer nor moves the page behind", more.open && more.main === m0 && more.doc === 0, JSON.stringify({ ...more, m0 }));
} else console.log("  SKIP: drawer does not overflow at this size");
await page.context().close();
console.log(`\n${results.filter(r => r.ok).length}/${results.length} ok`);
await browser.close(); await staged.close();
process.exit(results.some(r => !r.ok) ? 1 : 0);
