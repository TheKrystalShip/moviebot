// Cinema on phones: every view in views.mjs, on every phone in devices.mjs, against the scratch
// site scratch.sh serves. Each view is opened in a fresh browser context signed in as the staged
// viewer, measured (inspect.mjs), and photographed — the screen as a phone shows it and, for a page,
// the whole page in one picture. A contact sheet per view puts every phone's screen side by side.
//
//   node verify/mobile/run.mjs [--devices a,b|all] [--views a,b] [--out dir] [--no-stage] [--no-full]
//   node verify/mobile/run.mjs --out <a run's dir> --sheets-only   (draw that run's sheets again)
//
// Needs scratch.sh running, and webkit.sh for the iPhones; an iPhone is skipped, and says so, when
// WebKit cannot be reached. Exits 1 when any view has a fault.
//
// Settings (environment): MOVIEBOT_VERIFY_DIR as scratch.sh; MOVIEBOT_VERIFY_WEBKIT the WebKit socket
// (ws://127.0.0.1:3123/); CHROME_PATH a Chromium, when Playwright's cached one is not wanted.

import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { join, relative } from "node:path";
import { chromium, devices as descriptors, webkit } from "playwright-core";
import { DEFAULT_DEVICES, DEVICES } from "./devices.mjs";
import { VIEWS } from "./views.mjs";
import { inspect, unroll } from "./inspect.mjs";
import { PEOPLE, VIEWER, memberToken, stage, televisions } from "./stage.mjs";
import { issuerIn } from "../issuer.mjs";

const args = process.argv.slice(2);
const flag = (name) => args.includes("--" + name);
const option = (name, fallback) => {
  const at = args.indexOf("--" + name);
  return at >= 0 && args[at + 1] ? args[at + 1] : fallback;
};

const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
const readEnv = (file) => Object.fromEntries(
  readFileSync(join(dir, file), "utf8").split("\n").filter(Boolean).map((line) => [line.slice(0, line.indexOf("=")), line.slice(line.indexOf("=") + 1)]));
if (!existsSync(join(dir, "keys.env"))) {
  console.error(`No scratch site in ${dir}. Start verify/mobile/scratch.sh first.`);
  process.exit(2);
}
const site = readEnv("api.env");
const base = `http://127.0.0.1:${site.PORT}`;
const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
const webkitSocket = process.env.MOVIEBOT_VERIFY_WEBKIT || "ws://127.0.0.1:3123/";

const deviceNames = option("devices", "") === "all" ? Object.keys(DEVICES) : option("devices", DEFAULT_DEVICES.join(",")).split(",");
const viewNames = option("views", "") ? option("views").split(",") : VIEWS.map((v) => v.name);
const views = VIEWS.filter((v) => viewNames.includes(v.name));
const stamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
const out = option("out", join(dir, "runs", stamp));

for (const name of deviceNames) if (!DEVICES[name]) { console.error(`No device "${name}". Known: ${Object.keys(DEVICES).join(", ")}`); process.exit(2); }

function chromePath() {
  if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
  const cache = join(homedir(), ".cache", "ms-playwright");
  for (const entry of readdirSync(cache).filter((d) => d.startsWith("chromium-")).sort().reverse()) {
    const candidate = join(cache, entry, "chrome-linux64", "chrome");
    if (existsSync(candidate)) return candidate;
  }
  throw new Error("No Chromium found. Set CHROME_PATH to a Chrome or Chromium binary.");
}

async function waitForSite() {
  for (let i = 0; i < 60; i++) {
    try { if ((await fetch(base + "/health")).ok) return; } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error(`The scratch site at ${base} is not answering.`);
}

const browsers = {};
async function browserFor(engine) {
  if (browsers[engine] !== undefined) return browsers[engine];
  try {
    browsers[engine] = engine === "webkit"
      ? await webkit.connect(webkitSocket, { timeout: 15000 })
      : await chromium.launch({ executablePath: chromePath() });
  } catch (error) {
    console.log(`  ${engine} is not available (${error.message.split("\n")[0]}); its phones are skipped.`);
    browsers[engine] = null;
  }
  return browsers[engine];
}

async function look(browser, device, view, staged) {
  const { defaultBrowserType, ...descriptor } = descriptors[DEVICES[device].descriptor];
  const context = await browser.newContext(descriptor);
  // A view that drives a television is a phone paired with some of them, remote mode on.
  const phoneSid = view.remote ? `verify-phone-${device}-${view.name}` : undefined;
  if (view.remote) await staged.screens.pair(phoneSid, view.remote);
  if (!view.signedOut) {
    const person = PEOPLE[view.person || "viewer"];
    await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(person, phoneSid ? { sid: phoneSid } : {}));
    if (view.remote) await context.addInitScript(() => localStorage.setItem("cinema:remote", "1"));
  }
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  const result = { device, view: view.name, faults: [], warnings: [], notes: [] };
  try {
    await page.goto(base + view.path(staged), { waitUntil: "load" });
    await page.waitForSelector(view.ready, { timeout: 20000 });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(1200);
    if (view.act) await view.act(page, staged);
    const shot = join(out, device, view.name + ".png");
    await page.screenshot({ path: shot });
    result.screen = relative(out, shot);
    Object.assign(result, await page.evaluate(inspect));
    if (view.full && !flag("no-full")) {
      await page.evaluate(unroll);
      await page.waitForTimeout(300);
      const tall = join(out, device, view.name + "-full.png");
      await page.screenshot({ path: tall, fullPage: true });
      result.page = relative(out, tall);
    }
  } catch (error) {
    result.faults.push(`the view did not open: ${error.message.split("\n")[0]}`);
  }
  result.faults.push(...errors.map((e) => `the page raised: ${e}`));
  await context.close();
  if (view.remote) await staged.screens.unpair(phoneSid, view.remote);
  return result;
}

/** One picture per view: every phone's screen side by side, at its own proportions. */
async function sheets(results) {
  const browser = await browserFor("chromium");
  if (!browser) return;
  const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
  mkdirSync(join(out, "sheets"), { recursive: true });
  for (const view of views) {
    const row = results.filter((r) => r.view === view.name && r.screen);
    if (row.length === 0) continue;
    const cells = row.map((r) => {
      const tone = r.faults.length ? "#e5484d" : r.warnings.length ? "#f5a524" : "#30a46c";
      return `<figure><img src="../${r.screen}"><figcaption style="color:${tone}">${r.device} · ${r.faults.length} faults · ${r.warnings.length} warnings</figcaption></figure>`;
    }).join("");
    // A page opened from a file may load the screenshots beside it; one set as content may not.
    const sheet = join(out, "sheets", view.name + ".html");
    writeFileSync(sheet, `<!doctype html><style>body{margin:0;padding:16px;background:#0b0f14;font:14px system-ui;color:#ddd;display:flex;gap:16px;align-items:flex-start;width:max-content}
      h1{writing-mode:vertical-rl;transform:rotate(180deg);margin:0;font-size:18px}figure{margin:0}img{height:640px;display:block;border:1px solid #333}figcaption{margin-top:6px}</style>
      <h1>${view.name}</h1>${cells}`);
    await page.goto("file://" + sheet, { waitUntil: "load" });
    await page.screenshot({ path: join(out, "sheets", view.name + ".png"), fullPage: true });
  }
  await page.close();
}

if (flag("sheets-only")) {
  await sheets(JSON.parse(readFileSync(join(out, "report.json"), "utf8")));
  for (const browser of Object.values(browsers)) if (browser) await browser.close();
  console.log(`Sheets: ${join(out, "sheets")}`);
  process.exit(0);
}

await waitForSite();
mkdirSync(out, { recursive: true });
console.log(`Staging the scratch library at ${base}…`);
const staged = flag("no-stage")
  ? { token: memberToken(issuer, VIEWER), close: async () => {} }
  : await stage(base, issuer);
if (flag("no-stage")) {
  const titles = await (await fetch(base + "/api/titles", { headers: { Authorization: `Bearer ${staged.token}` } })).json();
  const ready = titles.filter((t) => t.status === "ready");
  staged.film = ready[0] || titles[0];
  staged.onTv = ready[5];
  staged.other = ready[7];
  staged.episode = ready.find((t) => t.episode);
  staged.screens = await televisions(base, issuer, staged.onTv, staged.episode);
  const closed = staged.close;
  staged.close = async () => { await staged.screens.close(); await closed(); };
}

const results = [];
for (const device of deviceNames) {
  const browser = await browserFor(DEVICES[device].engine);
  if (!browser) continue;
  mkdirSync(join(out, device), { recursive: true });
  console.log(`\n${device} (${DEVICES[device].descriptor}, ${DEVICES[device].engine})`);
  for (const view of views) {
    const result = await look(browser, device, view, staged);
    results.push(result);
    const mark = result.faults.length ? "FAIL" : result.warnings.length ? "warn" : "ok  ";
    console.log(`  ${mark}  ${view.name}${result.faults.length ? "  — " + result.faults.length + " fault(s)" : ""}${result.warnings.length ? "  — " + result.warnings.length + " warning(s)" : ""}`);
    for (const fault of result.faults.slice(0, 4)) console.log(`          ${fault}`);
  }
}

await staged.close();
await sheets(results);
for (const browser of Object.values(browsers)) if (browser) await browser.close();
writeFileSync(join(out, "report.json"), JSON.stringify(results, null, 2));

const faulty = results.filter((r) => r.faults.length);
console.log(`\n${results.length} views looked at, ${faulty.length} with faults, ${results.filter((r) => r.warnings.length).length} with warnings.`);
console.log(`Screens, pages and sheets: ${out}`);
process.exit(faulty.length ? 1 : 0);
