// How Krystal Cinema feels on a television: the walks in walks.mjs, each from a page loaded with an
// empty cache, every key timed by the recorder in probe.js and measured by measure.mjs.
//
//   node verify/tvperf/run.mjs [--target lab|tv] [--walks home,bar] [--runs n] [--video] [--out dir]
//                              [--cpu n] [--save-baseline] [--strict]
//
// Two targets run the same walks.
//
// - `lab` (the default) is Chromium against the scratch site verify/mobile/scratch.sh serves, staged
//   as verify:tv stages it, drawn the way the reference television draws: its 960 by 540 page at a
//   pixel ratio of 4, its user agent, motion on, the CPU slowed and the network shaped by `Lab`. Each
//   step is the median of `--runs` runs (3), compared with baseline.json: a measure past its
//   baseline by more than `Slack` allows is a regression and the run exits 1. `--save-baseline`
//   writes the run as the baseline instead.
// - `tv` is the reference television itself, over adb (`--serial`, or ADB_SERIAL): the debug build of
//   tv/ opened on `--origin` (https://movies.thekrystalship.com), its WebView driven over the DevTools
//   protocol. The debug build keeps a session of its own, signed in once with the device grant. A run
//   is one pass (`--runs` 1) and is reported, never gated: it is the measurement the lab is calibrated
//   against. The television is left on the release build's app.
//
// `--cpu` slows the lab's CPU by another factor than `Lab.cpuSlowdown`, which is how the factor is
// calibrated against a television's run (README.md).
//
// Every step is also held against `Budget`, what a television should feel like; a measure over it is
// reported, and fails the run under `--strict`.
//
// `--video` records the screen: Playwright's video in the lab, the television's own screen recording
// on a television. report.json holds every run's measures, the slowest frames with the scripts in them,
// and each step's offset into its video. Output goes to ~/.cache/moviebot-verify/tvperf/<target>-<time>/.
// Exits 1 on a regression, a step that lands somewhere other than its `to`, or an over-budget step
// under `--strict`; 2 when the target cannot be reached.

import { execFileSync, spawn } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { WALKS, Keys } from "./walks.mjs";
import { Metrics, measure, median, slowest } from "./measure.mjs";
import { VIEWER, stage } from "../mobile/stage.mjs";
import { TV_CLIENT, issuerIn } from "../issuer.mjs";

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const at = args.indexOf("--" + name);
  return at >= 0 && args[at + 1] && !args[at + 1].startsWith("--") ? args[at + 1] : fallback;
};
const flag = (name) => args.includes("--" + name);

/**
 * The lab's television: what the reference television (tv/README.md) is measured to be, and the
 * slowing that makes this machine's Chromium answer as its WebView does (README.md, "Calibrating").
 */
export const Lab = {
  viewport: { width: 960, height: 540 },
  pixelRatio: 4,
  userAgent: "Mozilla/5.0 (Linux; Android 11; AFTBR92D74 Build/RS8182.3811N; wv) AppleWebKit/537.36 (KHTML, like Gecko) "
    + "Version/4.0 Chrome/148.0.7778.258 Mobile Safari/537.36 KrystalCinemaTV/1.1.0",
  cpuSlowdown: 20,
  network: { latencyMs: 20, downMbps: 20, upMbps: 10 },
};

/** What a press on a television should come to. */
export const Budget = {
  answerMs: 100,
  freezeMs: 100,
  blankMs: 0,
  cls: 0.1,
  /** A page arriving is one change and its slower parts filling in a second; anything past is churn. */
  relayouts: 2,
};

/**
 * How far past its baseline a measure may land before it is a regression: `base × factor + add`.
 * `answerMs` has a wide floor because a key handled inside a frame has no Event Timing entry and reads
 * as 16. `focusMs` is not held: whether focus moves on the key or once the page's content arrives
 * depends on which comes first, so it lands on either of two values from one run to the next.
 */
export const Slack = {
  answerMs: [1.5, 100], readyMs: [1.3, 300], relayouts: [1, 1], blankMs: [1, 50],
  freezeMs: [1.5, 100], blockingMs: [1.5, 150], cls: [1, 0.05], requests: [1.2, 5], kb: [1.25, 100],
};

/** How long nothing may change before a step is over, and how long a step or a load may take at most. */
const QuietMs = 1200;
const StepMaxMs = 8000;
const LoadMaxMs = 12000;
/** The pause between the untimed keys before a timed one, long enough for each to land. */
const KeyGapMs = 350;

const target = option("target", "lab");
const runs = Number(option("runs", target === "lab" ? 3 : 1));
const video = flag("video");
const cpuSlowdown = Number(option("cpu", Lab.cpuSlowdown));
const walkNames = option("walks", "") ? option("walks").split(",") : Object.keys(WALKS);
const unknown = walkNames.filter((w) => !WALKS[w]);
if (unknown.length) { console.error(`No walk named ${unknown.join(", ")}; the walks are ${Object.keys(WALKS).join(", ")}.`); process.exit(2); }
const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
const out = option("out", join(dir, "tvperf", `${target}-${new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19)}`));
mkdirSync(out, { recursive: true });
const baselinePath = new URL("./baseline.json", import.meta.url);
const probe = readFileSync(new URL("./probe.js", import.meta.url), "utf8");

const surface = target === "tv" ? await television() : target === "lab" ? await lab() : null;
if (!surface) { console.error(`--target is lab or tv, not ${target}.`); process.exit(2); }

const report = { target, runs, started: new Date().toISOString(), lab: target === "lab" ? { ...Lab, cpuSlowdown } : undefined, steps: {} };
const failures = [];
try {
  for (let run = 0; run < runs; run++) {
    for (const name of walkNames) await walk(name, run);
  }
} finally {
  await surface.close();
}

// The medians, the budget and the baseline.
const baseline = target === "lab" && existsSync(baselinePath) ? JSON.parse(readFileSync(baselinePath, "utf8")) : null;
const rows = [];
for (const [key, step] of Object.entries(report.steps)) {
  step.median = median(step.runs);
  step.over = Object.entries(Budget).filter(([m, limit]) => step.median[m] != null && step.median[m] > limit).map(([m]) => m);
  const base = baseline?.steps[key];
  step.regressed = base ? Metrics.filter((m) => {
    if (!Slack[m] || step.median[m] == null || base[m] == null) return false;
    const [factor, add] = Slack[m];
    return step.median[m] > base[m] * factor + add;
  }) : [];
  for (const m of step.regressed) failures.push(`${key}: ${m} ${step.median[m]} against a baseline of ${base[m]}`);
  if (flag("strict")) for (const m of step.over) failures.push(`${key}: ${m} ${step.median[m]} over the budget of ${Budget[m]}`);
  rows.push([key, step.median, step.over, step.regressed]);
}

const columns = [["answer", "answerMs"], ["focus", "focusMs"], ["ready", "readyMs"], ["relay", "relayouts"], ["blank", "blankMs"],
  ["freeze", "freezeMs"], ["block", "blockingMs"], ["cls", "cls"], ["req", "requests"], ["kb", "kb"]];
const width = Math.max(...rows.map(([key]) => key.length));
console.log(`\n${"step".padEnd(width)}  ${columns.map(([h]) => h.padStart(6)).join(" ")}`);
for (const [key, m, over, regressed] of rows) {
  const cell = (metric) => {
    const v = m[metric] == null ? "-" : String(m[metric]);
    return (regressed.includes(metric) ? "!" : over.includes(metric) ? "*" : "") + v;
  };
  console.log(`${key.padEnd(width)}  ${columns.map(([, metric]) => cell(metric).padStart(6)).join(" ")}`);
}
console.log(`\n* over the budget   ! a regression against the baseline${baseline ? "" : " (none kept for this target)"}`);

writeFileSync(join(out, "report.json"), JSON.stringify(report, null, 1));
if (flag("save-baseline")) {
  if (target !== "lab") { console.error("Only the lab keeps a baseline; a television's run is the calibration."); process.exit(2); }
  const steps = Object.fromEntries(Object.entries(report.steps).map(([key, step]) => [key, step.median]));
  writeFileSync(baselinePath, JSON.stringify({ lab: { ...Lab, cpuSlowdown }, runs, steps }, null, 1) + "\n");
  console.log(`Baseline written: ${baselinePath.pathname}`);
}
console.log(`Report: ${join(out, "report.json")}`);
if (failures.length) {
  console.log(`\n${failures.length} failure${failures.length === 1 ? "" : "s"}:`);
  for (const f of failures) console.log(`  ✗ ${f}`);
  process.exit(1);
}

/** One walk: its page loaded with an empty cache, then each step timed until the screen is still. */
async function walk(name, run) {
  const w = WALKS[name];
  const page = surface.page;
  const recording = video ? await surface.record(`${name}-${run + 1}`) : null;
  await surface.clearCache();
  const at = recording ? (Date.now() - recording.started) / 1000 : null;
  await page.goto(surface.origin + w.start, { waitUntil: "commit" });
  await settle(page, LoadMaxMs);
  if (await surface.signedOut()) {
    await recording?.stop();
    console.error("The television's debug app is not signed in: sign it in with the code it shows, then run again.");
    process.exit(2);
  }
  note(`${name}/0:load`, await page.evaluate(() => window.__tvPerf.take()), { load: true, at });

  for (let i = 0; i < w.steps.length; i++) {
    const step = w.steps[i];
    for (const k of step.keys.slice(0, -1)) { await page.keyboard.press(Keys[k] || k); await page.waitForTimeout(KeyGapMs); }
    await page.evaluate(() => window.__tvPerf.begin());
    // Two frames, so the record holds the screen as it was before the key.
    await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))));
    const stepAt = recording ? (Date.now() - recording.started) / 1000 : null;
    await page.keyboard.press(Keys[step.keys.at(-1)] || step.keys.at(-1));
    await settle(page, StepMaxMs);
    const record = await page.evaluate(() => window.__tvPerf.take());
    const where = await page.evaluate(() => window.__tvPerf.where());
    const key = `${name}/${i + 1}:${step.name || step.keys.at(-1).toLowerCase()}`;
    if (step.to && !(step.to.endsWith("/") ? where.path.startsWith(step.to) : where.path.split("?")[0] === step.to)) {
      failures.push(`${key}: landed on ${where.path}, not ${step.to}`);
      console.log(`  ✗ ${key} landed on ${where.path}, not ${step.to}; the rest of the walk is skipped`);
      break;
    }
    note(key, record, { at: stepAt, where });
  }
  if (recording) report.videos = { ...report.videos, [`${name}-${run + 1}`]: await recording.stop() };
}

function note(key, record, { load = false, at = null, where } = {}) {
  const m = measure(record, { load });
  const entry = (report.steps[key] ??= { runs: [], detail: [] });
  entry.runs.push(m);
  entry.detail.push({ video: at, where, slowest: slowest(record) });
  console.log(`  ${key.padEnd(24)} ${Metrics.map((n) => `${n.replace("Ms", "")} ${m[n] ?? "-"}`).join("  ")}`);
}

/** Waits until nothing has changed for `QuietMs`, or `max` has passed. */
async function settle(page, max) {
  const until = Date.now() + max;
  await page.waitForTimeout(300);
  while (Date.now() < until) {
    const quiet = await page.evaluate(() => window.__tvPerf ? window.__tvPerf.quiet() : 0).catch(() => 0);
    if (quiet >= QuietMs) return;
    await page.waitForTimeout(Math.min(250, QuietMs - quiet + 20));
  }
}

/** The lab: Chromium against the staged scratch site, drawn as the reference television draws. */
async function lab() {
  if (!existsSync(join(dir, "api.env"))) {
    console.error(`No scratch site in ${dir}. Start verify/mobile/scratch.sh first.`);
    process.exit(2);
  }
  const site = Object.fromEntries(readFileSync(join(dir, "api.env"), "utf8").split("\n").filter(Boolean)
    .map((line) => [line.slice(0, line.indexOf("=")), line.slice(line.indexOf("=") + 1)]));
  const origin = `http://127.0.0.1:${site.PORT}`;
  const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
  const staged = await stage(origin, issuer);

  const browser = await chromium.launch({ executablePath: chromePath() });
  const context = await browser.newContext({
    viewport: Lab.viewport, deviceScaleFactor: Lab.pixelRatio, userAgent: Lab.userAgent, isMobile: false,
    ...(video ? { recordVideo: { dir: join(out, "video"), size: Lab.viewport } } : {}),
  });
  await context.addInitScript(({ key, value }) => { if (!localStorage.getItem(key)) localStorage.setItem(key, value); },
    issuer.planted(VIEWER, { hours: 6, client: TV_CLIENT }));
  await context.addInitScript(probe);
  const page = await context.newPage();
  const cdp = await context.newCDPSession(page);
  await cdp.send("Emulation.setCPUThrottlingRate", { rate: cpuSlowdown });
  await cdp.send("Network.enable");
  await cdp.send("Network.emulateNetworkConditions", {
    offline: false, latency: Lab.network.latencyMs,
    downloadThroughput: Lab.network.downMbps * 125000, uploadThroughput: Lab.network.upMbps * 125000,
  });
  const started = Date.now();
  return {
    origin, page,
    clearCache: () => cdp.send("Network.clearBrowserCache"),
    signedOut: () => page.evaluate(() => !!document.querySelector(".kc-tv-signin")),
    record: async () => ({ started, stop: async () => null }),
    async close() {
      const path = video ? await page.video()?.path() : null;
      await context.close();
      await browser.close();
      await staged.close();
      if (path) report.videos = { run: path };
    },
  };
}

/** The reference television over adb: its debug app opened on the origin, driven over DevTools. */
async function television() {
  const serial = option("serial", process.env.ADB_SERIAL);
  if (!serial) { console.error("A television run needs --serial <host:port> or ADB_SERIAL."); process.exit(2); }
  const origin = option("origin", "https://movies.thekrystalship.com").replace(/\/$/, "");
  const Debug = "com.thekrystalship.cinema.debug";
  const adb = (...a) => execFileSync("adb", ["-s", serial, ...a], { encoding: "utf8" }).trim();
  try { execFileSync("adb", ["connect", serial], { encoding: "utf8" }); } catch { /* answered below */ }
  if (adb("get-state") !== "device") { console.error(`${serial} is not an authorized adb device.`); process.exit(2); }
  if (!adb("shell", "pm", "list", "packages", Debug).includes(Debug)) {
    console.error(`${serial} has no debug build: tv/build.sh --debug --install.`);
    process.exit(2);
  }
  adb("shell", "am", "start", "-W", "-n", `${Debug}/com.thekrystalship.cinema.CinemaActivity`, "--es", "url", origin);
  let socket = null;
  for (let i = 0; i < 40 && !socket; i++) {
    const pid = adb("shell", "pidof", Debug).split(/\s+/)[0];
    if (pid && adb("shell", "cat", "/proc/net/unix").includes(`webview_devtools_remote_${pid}`)) socket = `webview_devtools_remote_${pid}`;
    else await new Promise((r) => setTimeout(r, 250));
  }
  if (!socket) { console.error("The debug app's WebView offered no DevTools socket."); process.exit(2); }
  const port = adb("forward", "tcp:0", `localabstract:${socket}`);
  const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
  const context = browser.contexts()[0];
  let page = context.pages().find((p) => p.url().startsWith("http"));
  for (let i = 0; i < 40 && !page; i++) { await new Promise((r) => setTimeout(r, 250)); page = context.pages().find((p) => p.url().startsWith("http")); }
  await context.addInitScript(probe);
  const cdp = await context.newCDPSession(page);
  await cdp.send("Network.enable");
  return {
    origin, page,
    clearCache: () => cdp.send("Network.clearBrowserCache"),
    signedOut: () => page.evaluate(() => !!document.querySelector(".kc-tv-signin")),
    async record(name) {
      const file = `/sdcard/tvperf-${name}.mp4`;
      const child = spawn("adb", ["-s", serial, "shell", "screenrecord", "--time-limit", "180", "--size", "960x540", "--bit-rate", "6000000", file]);
      await new Promise((r) => setTimeout(r, 800));
      const started = Date.now();
      return {
        started,
        async stop() {
          try { adb("shell", "pkill", "-INT", "screenrecord"); } catch { /* it ended on its own */ }
          await new Promise((r) => child.exitCode !== null ? r() : child.on("exit", r));
          await new Promise((r) => setTimeout(r, 800));
          const local = join(out, `${name}.mp4`);
          adb("pull", file, local);
          adb("shell", "rm", file);
          return local;
        },
      };
    },
    async close() {
      await browser.close().catch(() => {});
      try { adb("forward", "--remove", `tcp:${port}`); } catch { /* gone with the app */ }
      adb("shell", "am", "force-stop", Debug);
      adb("shell", "am", "start", "-n", "com.thekrystalship.cinema/.CinemaActivity");
    },
  };
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
