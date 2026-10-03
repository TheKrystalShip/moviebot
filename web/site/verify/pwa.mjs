// Does a real Chromium accept Krystal Cinema as an installable app, and does its service worker keep
// to what it may cache? It asks the browser rather than the filesystem: CDP's Page.getAppManifest is
// the parse Chrome's "Install app" decision uses, and serviceWorker.ready resolves only on an
// activated worker.
//
//   node verify/pwa.mjs [origin]     default: the scratch site verify/mobile/scratch.sh serves
//
// Nothing here needs a session, so it measures a deployed site as readily as the scratch one, and
// a signed-out visitor is the right one to measure: the browser offers the install to somebody who
// has not signed in yet. Exits 1 when any check fails.
//
// Settings (environment): MOVIEBOT_VERIFY_DIR as scratch.sh; CHROME_PATH a Chromium, when
// Playwright's cached one is not wanted.

import { existsSync, readFileSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";

function scratchOrigin() {
  const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
  const file = join(dir, "api.env");
  if (!existsSync(file)) {
    console.error(`No scratch site in ${dir}. Start verify/mobile/scratch.sh, or name an origin.`);
    process.exit(2);
  }
  const port = readFileSync(file, "utf8").match(/^PORT=(\d+)$/m)?.[1];
  return `http://127.0.0.1:${port}`;
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

const ORIGIN = process.argv[2] ? new URL(process.argv[2]).origin : scratchOrigin();

// Routes the origin answers that the worker must never keep: the API, a film's media, the room hub,
// and the health report. Each is fetched through the controlled page, and the cache read back.
const NEVER = ["/api/config", "/api/titles", "/api/stays", "/health", "/media/probe/poster.jpg", "/media/probe/index.m3u8", "/hub/session/negotiate"];

let failed = 0;
const check = (ok, line) => { if (!ok) failed++; console.log(`${ok ? "✓" : "✗"} ${line}`); };

const browser = await chromium.launch({ executablePath: chromePath() });
const context = await browser.newContext();
const page = await context.newPage();
await page.goto(ORIGIN + "/", { waitUntil: "load" });
console.log(`${ORIGIN}, settled on ${new URL(page.url()).pathname}\n`);

// The manifest, as Chrome parses it.
const cdp = await context.newCDPSession(page);
const { url: manifestUrl, errors, data } = await cdp.send("Page.getAppManifest");
const manifest = data ? JSON.parse(data) : {};
check(!!data, `manifest at ${manifestUrl || "(none)"}`);
check(errors.length === 0, `manifest parses${errors.length ? ": " + errors.map((e) => e.message).join("; ") : ""}`);
check(manifest.display === "standalone", `display ${manifest.display}`);
check(manifest.start_url === "/" && manifest.scope === "/", `start_url ${manifest.start_url}, scope ${manifest.scope}`);
check(!("orientation" in manifest), "names no orientation, so the phone's rotation lock holds");
check(!!manifest.name && !!manifest.short_name, `name "${manifest.name}", short name "${manifest.short_name}"`);
const icons = manifest.icons || [];
const has = (size, purpose) => icons.some((i) => i.sizes === size && (i.purpose || "any").split(" ").includes(purpose));
check(has("192x192", "any") && has("512x512", "any"), "a 192 and a 512 icon, which Chrome needs to offer the install");
check(has("512x512", "maskable"), "a maskable icon");

// Every image the manifest and the page name fetches, at the size it claims. A launch image's size
// is its media query's points times its pixel ratio.
const decode = (src) => page.evaluate((u) => new Promise((res) => {
  const img = new Image();
  img.onload = () => res(`${img.naturalWidth}x${img.naturalHeight}`);
  img.onerror = () => res("nothing");
  img.src = u;
}), src);
for (const i of icons) {
  const got = await decode(new URL(i.src, manifestUrl).href);
  check(got === i.sizes, `${i.src} (${i.purpose || "any"}) is ${got}`);
}
const links = await page.$$eval('link[rel="apple-touch-icon"], link[rel="apple-touch-startup-image"], link[rel="icon"]',
  (els) => els.map((l) => ({ rel: l.rel, href: l.href, sizes: l.getAttribute("sizes"), media: l.media })));
let images = 0;
for (const l of links) {
  const got = await decode(l.href);
  let want = l.sizes;
  if (l.rel === "apple-touch-startup-image") {
    const [, w, h, dpr] = l.media.match(/device-width:\s*(\d+)px.*device-height:\s*(\d+)px.*pixel-ratio:\s*(\d+)/);
    want = `${w * dpr}x${h * dpr}`;
  }
  const ok = got !== "nothing" && (!want || got === want);
  if (!ok || l.rel !== "apple-touch-startup-image") check(ok, `${new URL(l.href).pathname} is ${got}${want ? ", wants " + want : ""}`);
  else images++;
}
check(images > 0, `${images} launch images, each at the size its device asks for`);
const apple = await page.$$eval("meta", (ms) => Object.fromEntries(ms.filter((m) => m.name).map((m) => [m.name, m.content])));
check(apple["apple-mobile-web-app-capable"] === "yes", "Safari opens it standalone");
check(!!apple["apple-mobile-web-app-title"], `home-screen title "${apple["apple-mobile-web-app-title"]}"`);

// The worker: activated, at the root, and in control after a reload, since a worker does not
// intercept the document that registered it.
const sw = await page.evaluate(async () => {
  const reg = await Promise.race([navigator.serviceWorker.ready, new Promise((r) => setTimeout(() => r(null), 15000))]);
  return reg ? { scope: reg.scope, script: reg.active?.scriptURL, state: reg.active?.state } : null;
});
check(sw?.state === "activated" && sw.scope === ORIGIN + "/", sw ? `worker ${sw.state} at ${sw.scope}` : "the worker never activated");
if (sw) {
  await page.reload({ waitUntil: "load" });
  const result = await page.evaluate(async (never) => {
    for (const p of never) { try { await fetch(p); } catch { /* the answer does not matter, only whether it was kept */ } }
    const urls = [];
    for (const n of await caches.keys()) urls.push(...(await (await caches.open(n)).keys()).map((r) => new URL(r.url).pathname));
    return { controlled: !!navigator.serviceWorker.controller, urls: urls.sort() };
  }, NEVER);
  check(result.controlled, "the worker controls the page after a reload");
  check(result.urls.includes("/") && result.urls.some((u) => u.startsWith("/assets/")), "the shell and the build's assets are kept for offline");
  const kept = NEVER.filter((p) => result.urls.includes(p));
  check(kept.length === 0, kept.length ? `kept routes it must not: ${kept.join(", ")}` : "kept none of the API, media, hub or health routes");

  // Offline, the installed app still opens its shell.
  await context.setOffline(true);
  const offline = await page.goto(ORIGIN + "/movies", { waitUntil: "domcontentloaded" }).then((r) => r?.status(), () => null);
  await context.setOffline(false);
  check(offline === 200, `offline, a page opens from the cache (${offline ?? "no answer"})`);
}

// The Activity runs inside Discord and never registers the worker.
const embed = await (await browser.newContext()).newPage();
await embed.goto(ORIGIN + "/embed/", { waitUntil: "load" }).catch(() => {});
await embed.waitForTimeout(2000);
const embedded = await embed.evaluate(async () => (await navigator.serviceWorker.getRegistrations()).length).catch(() => -1);
check(embedded === 0, `the Activity registers no worker (${embedded})`);

await browser.close();
console.log(failed ? `\n${failed} failed` : "\nall passed");
process.exit(failed ? 1 : 0);
