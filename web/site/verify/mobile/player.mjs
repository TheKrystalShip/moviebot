// The player on a phone, as a finger drives it: what each tap and sweep does to the film and to the
// controls, and how the controls are laid out, held to the way the big streaming apps answer a touch.
//
//   node verify/mobile/player.mjs [--device galaxy-s24] [--out dir]
//
// Against the scratch site verify/mobile/scratch.sh serves, staged as the mobile harness stages it, on
// the film the scratch library holds video for (`MOVIEBOT_VERIFY_TV_FILM`, coherence-2013 unless set).
// The page is a phone held sideways, as the player holds the film from the first press; the layout is
// checked held upright as well.
//
// Each step is a touch, with what it should leave: the film paused or playing, the controls up or away,
// and whether the film jumped rather than played on. Then the layout, each orientation: play and pause
// a large button in the middle of the film with the skips either side of it, no volume button, the
// row under the bar holding Audio & Subtitles, Settings and Lock and no menu of its own for quality or
// chapters, the time left at the bar's end, the bar thick enough for a thumb, the controls over a
// shade, and every control in the top bar a thumb wide. Then the lock: touches do nothing while it
// holds, and the one way out is its own button.
//
// A screenshot of each step lands in ~/.cache/moviebot-verify/phone-player/<time>/. Exits 1 on any miss.

import { existsSync, mkdirSync, readFileSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium, devices } from "playwright-core";
import { DEVICES } from "./devices.mjs";
import { VIEWER, stage } from "./stage.mjs";
import { issuerIn } from "../issuer.mjs";

const args = process.argv.slice(2);
const option = (name, fallback) => { const at = args.indexOf("--" + name); return at >= 0 && args[at + 1] ? args[at + 1] : fallback; };
const device = option("device", "galaxy-s24");
const film = process.env.MOVIEBOT_VERIFY_TV_FILM || "coherence-2013";
const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
const out = option("out", join(dir, "phone-player", new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19)));
mkdirSync(out, { recursive: true });
if (!DEVICES[device] || DEVICES[device].engine !== "chromium") { console.error(`--device names a Chromium phone: ${Object.keys(DEVICES).filter((d) => DEVICES[d].engine === "chromium").join(", ")}`); process.exit(2); }

const site = Object.fromEntries(readFileSync(join(dir, "api.env"), "utf8").split("\n").filter(Boolean)
  .map((l) => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1)]));
const origin = `http://127.0.0.1:${site.PORT}`;
const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
const staged = await stage(origin, issuer);

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
const { defaultBrowserType, ...descriptor } = devices[DEVICES[device].descriptor];
const upright = descriptor.viewport;
const sideways = { width: upright.height, height: upright.width };
const context = await browser.newContext({ ...descriptor, viewport: sideways });
await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER, {}));
const page = await context.newPage();
const cdp = await context.newCDPSession(page);
const wait = (ms) => page.waitForTimeout(ms);

let misses = 0;
let shots = 0;
const shot = async (name) => page.screenshot({ path: join(out, `${String(++shots).padStart(2, "0")}-${name}.png`) });
function report(ok, label, detail) {
  if (!ok) misses++;
  console.log(`  ${ok ? "ok  " : "MISS"} ${label}${!ok && detail ? `   ${detail}` : ""}`);
}

/** The film and the controls: paused, where, whether the controls are up, and when it was read. */
const state = () => page.evaluate(() => {
  const v = document.querySelector(".mp-frame video");
  return { paused: v ? v.paused : null, at: v ? v.currentTime : 0, t: performance.now(), shown: !!document.querySelector(".mp-stage.is-shown") };
});
const stageBox = () => page.evaluate(() => { const r = document.querySelector(".mp-stage").getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });

/** A touch and what it should leave: `paused`, `shown`, `moved` (the film jumped), each where given. */
async function step(label, act, expect, settle = 1200) {
  const before = await state();
  await act();
  await wait(settle);
  const after = await state();
  const played = before.paused ? 0 : (after.t - before.t) / 1000;
  const moved = Math.abs(after.at - before.at - played) > 4;
  const got = { paused: after.paused, shown: after.shown, moved };
  const wrong = Object.keys(expect).filter((k) => expect[k] !== got[k]).map((k) => `${k} ${got[k]}, expected ${expect[k]}`);
  await shot(label.replace(/[^a-z0-9]+/gi, "-").toLowerCase());
  report(wrong.length === 0, label, wrong.join("; "));
}

const tapAt = async (fx, fy) => { const b = await stageBox(); await page.touchscreen.tap(b.x + b.w * fx, b.y + b.h * fy); };
const doubleTapAt = async (fx, fy) => { await tapAt(fx, fy); await wait(120); await tapAt(fx, fy); };
/** A finger swept across the film from one point to another, as touch events: headless Chromium scrolls nothing for a synthesized gesture. */
async function sweep(fx0, fy0, fx1, fy1) {
  const b = await stageBox();
  const at = (f) => ({ x: b.x + b.w * (fx0 + (fx1 - fx0) * f), y: b.y + b.h * (fy0 + (fy1 - fy0) * f) });
  await cdp.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [at(0)] });
  for (let i = 1; i <= 10; i++) { await cdp.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: [at(i / 10)] }); await wait(16); }
  await cdp.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
}
const press = async (label) => {
  const el = await page.$(`.mp-frame [aria-label="${label}"]`);
  if (el) await el.tap({ timeout: 3000 }).catch(() => {});
  return Boolean(el);
};
const untilAway = async () => { for (let i = 0; i < 20 && (await state()).shown; i++) await wait(250); };

/** The layout with the controls up. */
const layout = () => page.evaluate(() => {
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return r.width > 0 ? { x: r.left, y: r.top, w: r.width, h: r.height, cx: r.left + r.width / 2, cy: r.top + r.height / 2 } : null; };
  const stage = box(document.querySelector(".mp-stage"));
  const play = [...document.querySelectorAll('.mp-frame button[aria-label="Play"], .mp-frame button[aria-label="Pause"]')].map(box).filter(Boolean);
  const back = box(document.querySelector('.mp-frame button[aria-label^="Back 10"]'));
  const forward = box(document.querySelector('.mp-frame button[aria-label^="Forward 10"]'));
  const row = [...document.querySelectorAll(".mp-controls button")].filter((b) => box(b)).map((b) => (b.getAttribute("aria-label") || "").split(":")[0]);
  const times = [...document.querySelectorAll(".mp-scrub-row__time")].map((t) => t.textContent);
  const track = box(document.querySelector(".mp-scrub__track"));
  const knob = box(document.querySelector(".mp-scrub__knob"));
  const shade = document.querySelector(".mp-shade");
  const shaded = Boolean(shade) && /gradient/.test(getComputedStyle(shade).backgroundImage) && Number(getComputedStyle(shade).opacity) > 0.5;
  const top = [...document.querySelectorAll(".mp-top button")].filter((b) => box(b)).map((b) => ({ name: b.getAttribute("aria-label"), w: box(b).w, h: box(b).h, text: b.textContent.trim() }));
  return { stage, play, back, forward, row, times, track: track && track.h, knob: knob && knob.w, shaded, top };
});

async function checkLayout(where) {
  const l = await layout();
  const centre = l.play.find((p) => Math.abs(p.cx - (l.stage.x + l.stage.w / 2)) < l.stage.w * 0.1 && Math.abs(p.cy - (l.stage.y + l.stage.h / 2)) < l.stage.h * 0.15);
  report(Boolean(centre) && centre.w >= 64, `${where}: play and pause is a large button in the middle of the film`,
    centre ? `${Math.round(centre.w)}px wide` : `play is at ${l.play.map((p) => `${Math.round(p.cx)},${Math.round(p.cy)} ${Math.round(p.w)}px`).join(" ")}, the film's middle ${Math.round(l.stage.x + l.stage.w / 2)},${Math.round(l.stage.y + l.stage.h / 2)}`);
  const flanked = centre && l.back && l.forward && l.back.cx < centre.cx && l.forward.cx > centre.cx && Math.abs(l.back.cy - centre.cy) < 20 && Math.abs(l.forward.cy - centre.cy) < 20;
  report(Boolean(flanked), `${where}: the skips stand either side of it`, l.back ? `back at ${Math.round(l.back.cx)},${Math.round(l.back.cy)}` : "no skip buttons");
  report(!l.row.includes("Mute") && !l.row.includes("Unmute"), `${where}: no volume button`, `the row has ${l.row.join(", ")}`);
  const slim = ["Audio & Subtitles", "Settings", "Lock"].every((n) => l.row.includes(n)) && !["Quality", "Chapters", "Subtitles", "Audio"].some((n) => l.row.includes(n));
  report(slim, `${where}: the row holds Audio & Subtitles, Settings and Lock, and no menu of its own for quality or chapters`, `the row has ${l.row.join(", ")}`);
  report(l.times.length > 0 && /^[−-]/.test(l.times.at(-1)), `${where}: the bar ends with the time left`, `it ends with ${l.times.at(-1)}`);
  report(l.track >= 6 && l.knob >= 18, `${where}: the bar is thick enough for a thumb`, `track ${l.track}px, knob ${l.knob}px`);
  report(l.shaded, `${where}: the controls stand over a shade`);
  const narrow = l.top.filter((b) => b.w < 44 || b.h < 44);
  report(narrow.length === 0, `${where}: every control in the top bar is a thumb wide`, narrow.map((b) => `${b.name} ${Math.round(b.w)}x${Math.round(b.h)}`).join(", "));
  const invite = l.top.find((b) => b.name === "Invite" || b.text === "Invite");
  report(!invite || invite.text === "", `${where}: Invite is an icon`, invite && `it says "${invite.text}"`);
}

try {
  console.log(`player on ${device} (${film})`);
  await page.goto(`${origin}/r/verify-phone-${Date.now().toString(36)}?title=${encodeURIComponent(film)}`);
  await page.waitForFunction(() => { const v = document.querySelector(".mp-frame video"); return v && v.readyState >= 3; }, null, { timeout: 40000 }).catch(() => {});
  if (!(await page.evaluate(() => { const v = document.querySelector(".mp-frame video"); return v && v.readyState >= 3; }))) {
    console.error(`${film} did not load; the scratch library needs its segments (verify/README)`);
    process.exit(2);
  }
  await wait(1000);
  await shot("opened");

  await step("Play leaves the controls up a moment", () => press("Play"), { paused: false, shown: true }, 700);
  await untilAway();
  await step("a tap in the middle brings the controls back, and the film plays on", () => tapAt(0.5, 0.5), { paused: false, shown: true }, 700);
  await step("a tap on the film with the controls up puts them away", () => tapAt(0.5, 0.25), { paused: false, shown: false }, 900);
  await step("a tap at the side brings them back", () => tapAt(0.15, 0.5), { paused: false, shown: true }, 700);
  await untilAway();
  await step("two taps at the right skip forward", () => doubleTapAt(0.85, 0.5), { paused: false, moved: true }, 1600);
  await untilAway();
  await step("a sweep across the film moves nothing", () => sweep(0.3, 0.5, 0.75, 0.5), { paused: false, moved: false }, 1200);

  await tapAt(0.15, 0.5);
  await wait(500);
  await checkLayout("sideways");
  await shot("layout-sideways");
  await page.setViewportSize(upright);
  await wait(900);
  if (!(await state()).shown) { await tapAt(0.15, 0.5); await wait(500); }
  await checkLayout("upright");
  await shot("layout-upright");
  await page.setViewportSize(sideways);
  await wait(900);

  if (!(await state()).shown) { await tapAt(0.15, 0.5); await wait(500); }
  const locked = await press("Lock");
  await wait(700);
  if (!locked) report(false, "Lock locks the screen", "there is no Lock");
  else {
    await step("Lock puts the controls away", async () => {}, { paused: false, shown: false }, 300);
    const unlock = async () => page.evaluate(() => [...document.querySelectorAll('.mp-frame button[aria-label="Unlock"]')].some((b) => b.getBoundingClientRect().width > 0));
    await step("locked, two taps at the right move nothing", () => doubleTapAt(0.85, 0.5), { paused: false, moved: false }, 1600);
    await step("locked, a sweep up at the right changes nothing", () => sweep(0.85, 0.7, 0.85, 0.3), { paused: false, moved: false }, 600);
    await tapAt(0.5, 0.5);
    await wait(400);
    report(await unlock(), "locked, a tap shows the way to unlock and nothing else",
      (await page.evaluate(() => [...document.querySelectorAll(".mp-frame button")].filter((b) => b.getBoundingClientRect().width > 0).map((b) => b.getAttribute("aria-label")))).join(", "));
    await shot("locked-tap");
    await step("Unlock brings the controls back", () => press("Unlock"), { paused: false, shown: true }, 600);
  }
} finally {
  await browser.close();
  await staged.close();
}
console.log(`\n${misses} miss(es). Screenshots in ${out}`);
process.exit(misses > 0 ? 1 : 0);
