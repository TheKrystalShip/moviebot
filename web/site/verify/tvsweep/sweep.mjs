// Every key press of a walk on the reference television, timed one at a time, and optionally traced:
// how long focus takes to move, how many frames go over the panel's budget while the screen settles,
// and with --trace where the main thread, the GPU and the image decoders spent that time.
//
//   node verify/tvsweep/sweep.mjs --serial <host:port> [--only walk,walk] [--out dir]
//        [--scratch] [--trace] [--profile] [--invalidations]
//        [--css "<rules>"] [--reduced] [--eager] [--shrink <px>]
//
// It drives the debug build of tv/ over adb, attaching Playwright to its WebView, on the production
// site, or with --scratch on the scratch site verify/mobile/scratch.sh serves, which the television
// reaches through `adb reverse` with a session planted the way the lab plants one. The scratch site
// lets a local build be measured on the television before anything is published or deployed.
//
// Each step's record (results.json, and a printed line):
// - focusMs: the key to the first focusin after it;
// - frames, over25, over50, longestGap: requestAnimationFrame stamps in the window after the key
//   (`window`, 1500 ms unless the walk says), and how many gaps between them exceed 25 and 50 ms. The
//   panel runs at 50 Hz, so a frame is 20 ms; a gap over 25 ms is a frame missed;
// - loafs: long animation frames with the scripts in them;
// - with --trace, `trace` (trace.mjs): the main thread's busy time by kind, the GPU thread's, the
//   compositor's drawn and dropped frames, and the trace itself as <step>.json beside results.json
//   for profile.mjs, callers.mjs, tasks.mjs and forced.mjs.
//
// What a run can switch off or change, to measure what something costs (an ablation):
// - --css injects rules into every page;
// - --reduced emulates prefers-reduced-motion, which makes the navigator's scrolling instant;
// - --eager turns every `loading="lazy"` image eager as it is added;
// - --shrink <px> serves any image asked for with a `w=` query, shrunk to that width with
//   vipsthumbnail, which measures smaller artwork before the API serves it (the page has to ask with
//   `w=`, for example from a patched build on the scratch site).
//
// Tracing roughly doubles what the television's CPU spends, so a trace says where time goes and an
// untraced run says how long it takes. A step with no frames at all was taken with the panel dark
// and is reported INVALID rather than fast.

import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { WALKS } from "./walks.mjs";
import { analyse } from "./trace.mjs";
import { awake } from "../awake.mjs";

const args = process.argv.slice(2);
const flag = (n) => args.includes("--" + n);
const opt = (n) => { const i = args.indexOf("--" + n); return i >= 0 ? args[i + 1] : null; };
const serial = opt("serial") || process.env.ADB_SERIAL;
if (!serial) { console.error("--serial <host:port> (or ADB_SERIAL) names the television."); process.exit(2); }
const only = opt("only") ? opt("only").split(",") : null;
const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
const out = opt("out") || join(dir, "tvsweep", new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19));
mkdirSync(out, { recursive: true });
const adb = await awake(serial, dir);
const Debug = "com.thekrystalship.cinema.debug";
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const KEYS = { Up: "ArrowUp", Down: "ArrowDown", Left: "ArrowLeft", Right: "ArrowRight", Back: "Escape", OK: "Enter" };

let origin = "https://movies.thekrystalship.com";
let staged = null;
let planted = null;
let scratchPort = null;
if (flag("scratch")) {
  const { VIEWER, stage } = await import("../mobile/stage.mjs");
  const { TV_CLIENT, issuerIn } = await import("../issuer.mjs");
  const site = Object.fromEntries(readFileSync(join(dir, "api.env"), "utf8").split("\n").filter(Boolean)
    .map((l) => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1)]));
  scratchPort = site.PORT;
  origin = `http://127.0.0.1:${site.PORT}`;
  const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
  staged = await stage(origin, issuer);
  planted = issuer.planted(VIEWER, { hours: 6, client: TV_CLIENT });
  adb("reverse", "tcp:" + site.PORT, "tcp:" + site.PORT);
  adb("reverse", "tcp:" + site.ISSUER_PORT, "tcp:" + site.ISSUER_PORT);
}

/** Installed before the site's scripts: the keys, where focus goes, every frame, the long frames. */
const recorder = () => {
  if (window.__sweep) return;
  const S = window.__sweep = { keys: [], focus: [], frames: [], loafs: [], on: false };
  const name = (el) => !el || el === document.body ? "body"
    : `${el.tagName.toLowerCase()}${typeof el.className === "string" && el.className ? "." + el.className.trim().split(/\s+/).slice(0, 2).join(".") : ""} "${(el.getAttribute("aria-label") || el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 30)}"`;
  addEventListener("keydown", (e) => { if (S.on) S.keys.push({ key: e.key, t: e.timeStamp }); }, true);
  document.addEventListener("focusin", (e) => { if (S.on) S.focus.push({ t: performance.now(), el: name(e.target) }); });
  const tick = (t) => { if (S.on) S.frames.push(t); requestAnimationFrame(tick); };
  requestAnimationFrame(tick);
  try {
    new PerformanceObserver((l) => {
      if (!S.on) return;
      for (const e of l.getEntries()) {
        S.loafs.push({
          t: e.startTime, d: e.duration, block: e.blockingDuration,
          scripts: (e.scripts || []).filter((s) => s.duration >= 4).map((s) => ({
            d: Math.round(s.duration), forced: Math.round(s.forcedStyleAndLayoutDuration || 0), inv: s.invoker,
            src: `${(s.sourceURL || "").split("/").pop()}:${s.sourceFunctionName || ""}:${s.sourceCharPosition}`,
          })),
        });
      }
    }).observe({ type: "long-animation-frame", buffered: false });
  } catch { /* the engine offers no long animation frames */ }
};

// A fresh process holds one WebView: one left from an earlier start keeps its page, which draws no
// frames behind the new one, and would be the page attached to.
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

if (planted) await context.addInitScript(({ key, value }) => { localStorage.setItem(key, value); }, planted);
if (flag("eager")) {
  await context.addInitScript(() => {
    const eager = (n) => {
      if (n.tagName === "IMG" && n.loading === "lazy") n.loading = "eager";
      if (n.querySelectorAll) for (const i of n.querySelectorAll('img[loading="lazy"]')) i.loading = "eager";
    };
    new MutationObserver((ms) => { for (const m of ms) for (const n of m.addedNodes) if (n.nodeType === 1) eager(n); })
      .observe(document, { childList: true, subtree: true });
  });
}
await context.addInitScript(recorder);
if (opt("css")) {
  await context.addInitScript((css) => {
    const put = () => { const st = document.createElement("style"); st.textContent = css; document.head.appendChild(st); };
    if (document.head) put(); else document.addEventListener("DOMContentLoaded", put);
  }, opt("css"));
}
const cdp = await context.newCDPSession(page);
if (flag("reduced")) await cdp.send("Emulation.setEmulatedMedia", { features: [{ name: "prefers-reduced-motion", value: "reduce" }] });
if (opt("shrink")) {
  const tmp = mkdtempSync(join(dir, "tvsweep-shrink-"));
  const cache = new Map();
  await cdp.send("Fetch.enable", { patterns: [{ urlPattern: "*w=*", requestStage: "Request" }] });
  cdp.on("Fetch.requestPaused", async (e) => {
    try {
      const u = new URL(e.request.url);
      const w = u.searchParams.get("w") || opt("shrink");
      const key = u.pathname + "@" + w;
      if (!cache.has(key)) {
        const original = Buffer.from(await (await fetch(u.origin + u.pathname)).arrayBuffer());
        const name = key.replace(/[^a-z0-9]+/gi, "_");
        writeFileSync(join(tmp, name + ".jpg"), original);
        execFileSync("vipsthumbnail", [join(tmp, name + ".jpg"), "-s", `${w}x`, "-o", join(tmp, name + "-s.jpg") + "[Q=85]"]);
        cache.set(key, readFileSync(join(tmp, name + "-s.jpg")).toString("base64"));
      }
      await cdp.send("Fetch.fulfillRequest", {
        requestId: e.requestId, responseCode: 200, body: cache.get(key),
        responseHeaders: [{ name: "Content-Type", value: "image/jpeg" }, { name: "Cache-Control", value: "max-age=3600" }],
      });
    } catch { await cdp.send("Fetch.continueRequest", { requestId: e.requestId }).catch(() => {}); }
  });
}

const categories = [
  "devtools.timeline", "disabled-by-default-devtools.timeline", "disabled-by-default-devtools.timeline.frame", "toplevel",
  "v8", "disabled-by-default-v8.gc", "blink", "cc", "gpu", "viz", "benchmark", "input", "latencyInfo",
  ...(flag("invalidations") ? ["disabled-by-default-devtools.timeline.invalidationTracking", "disabled-by-default-devtools.timeline.stack"] : []),
  ...(flag("profile") ? ["disabled-by-default-v8.cpu_profiler"] : []),
];
async function traced(press, ms) {
  const events = [];
  const onData = (d) => events.push(...d.value);
  cdp.on("Tracing.dataCollected", onData);
  const done = new Promise((r) => cdp.once("Tracing.tracingComplete", r));
  await cdp.send("Tracing.start", { transferMode: "ReportEvents", traceConfig: { includedCategories: categories } });
  await press();
  await wait(ms);
  await cdp.send("Tracing.end");
  await done;
  cdp.off("Tracing.dataCollected", onData);
  return events;
}

const results = [];
try {
  for (const [name, walk] of Object.entries(WALKS)) {
    if (only ? !only.includes(name) : walk.optional) continue;
    await page.goto(origin + walk.start, { waitUntil: "commit" });
    await wait(walk.settle || (flag("scratch") ? 12000 : 7000));
    for (const k of walk.setup || []) { await page.keyboard.press(KEYS[k] || k); await wait(700); }
    await wait(1200);
    for (let i = 0; i < walk.steps.length; i++) {
      const step = walk.steps[i];
      for (const k of step.pre || []) { await page.keyboard.press(KEYS[k] || k); await wait(700); }
      await wait(step.rest || 900);
      await page.evaluate(() => Object.assign(window.__sweep, { keys: [], focus: [], frames: [], loafs: [], on: true }));
      const windowMs = step.window || 1500;
      const press = () => page.keyboard.press(KEYS[step.key] || step.key);
      let events = null;
      if (flag("trace")) events = await traced(press, windowMs);
      else { await press(); await wait(windowMs); }
      const r = await page.evaluate(() => {
        window.__sweep.on = false;
        const a = document.activeElement;
        return { ...window.__sweep, path: location.pathname, on: a ? (a.getAttribute("aria-label") || a.textContent || "").trim().slice(0, 30) : "" };
      });
      const label = `${name}/${i + 1}:${step.label || step.key}`;
      const k = r.keys[0];
      if (!k || r.frames.length === 0) { console.log(`${label}: INVALID (key ${!!k}, frames ${r.frames.length})`); continue; }
      const after = r.frames.filter((t) => t >= k.t);
      const gaps = after.slice(1).map((t, j) => t - after[j]);
      const focus = r.focus.find((f) => f.t >= k.t);
      const m = {
        step: label, landed: r.path, on: r.on,
        focusMs: focus ? Math.round(focus.t - k.t) : null,
        frames: after.length, over25: gaps.filter((g) => g > 25).length, over50: gaps.filter((g) => g > 50).length,
        longestGap: Math.round(Math.max(0, ...gaps)),
        loafs: r.loafs.filter((l) => l.t + l.d >= k.t)
          .map((l) => ({ at: Math.round(l.t - k.t), d: Math.round(l.d), block: Math.round(l.block), scripts: l.scripts })),
      };
      if (events) {
        m.trace = analyse(events, windowMs);
        writeFileSync(join(out, label.replace(/[^a-z0-9]+/gi, "_") + ".json"), JSON.stringify(events));
      }
      results.push(m);
      const t = m.trace && !m.trace.error
        ? `  main ${m.trace.main.busy} [script ${m.trace.main.script} style ${m.trace.main.style} layout ${m.trace.main.layout} paint ${m.trace.main.paint} gc ${m.trace.main.gc}] gpu ${m.trace.gpu} decode ${m.trace.decode}`
        : "";
      console.log(`${label.padEnd(30)} focus ${String(m.focusMs).padStart(4)}  frames ${String(m.frames).padStart(3)}  >25ms ${String(m.over25).padStart(2)}  >50ms ${String(m.over50).padStart(2)}  longest ${String(m.longestGap).padStart(4)}${t}`);
    }
  }
} finally {
  writeFileSync(join(out, "results.json"), JSON.stringify(results, null, 1));
  await browser.close().catch(() => {});
  try { adb("forward", "--remove", `tcp:${port}`); } catch { /* gone with the app */ }
  adb("shell", "am", "force-stop", Debug);
  adb("shell", "am", "start", "-n", "com.thekrystalship.cinema/.CinemaActivity");
  if (staged) await staged.close();
  if (scratchPort) { try { adb("reverse", "--remove-all"); } catch { /* nothing reversed */ } }
  console.log("Results:", join(out, "results.json"));
}
