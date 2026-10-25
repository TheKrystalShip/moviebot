// Where the page column stands after each kind of move, on a desktop and on a phone, with this checkout's
// `dist/` drawn over the live site and its live data, signed in. The session is a Playwright storage
// state from `tks-login browser -o <file>`, which signs the account `claude` in at the live anchor.
//
//   npm run build
//   tks-login browser -o /tmp/live-state.json
//   node verify/pages/scroll.mjs /tmp/live-state.json [https://movies.thekrystalship.com]
//
// Each page is scrolled far down, a list far enough to have read further pages, then a card is opened: a
// page of its own (a search's film, Home's title) and a page drawn in place (a franchise from the
// franchises). The page opened must open at its top; Back must come back to the place left, a list as
// long as it was without reading it again; a reload must come back to the same place.
//
// Exits 1 on any move that ends elsewhere.
import { existsSync, readFileSync } from "node:fs";
import { extname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { launch } from "./browser.mjs";

const state = process.argv[2];
const base = process.argv[3] || "https://movies.thekrystalship.com";
if (!state) { console.error("node verify/pages/scroll.mjs <storage state> [base]"); process.exit(2); }
const dist = fileURLToPath(new URL("../../dist/", import.meta.url));
if (!existsSync(join(dist, "index.html"))) { console.error("No dist/: run npm run build first."); process.exit(2); }

const LIVE = /^\/(api|films|shows|hub|media|assistant)(\/|$)/;
const TYPES = { ".js": "text/javascript", ".css": "text/css", ".html": "text/html", ".json": "application/json", ".webmanifest": "application/manifest+json", ".svg": "image/svg+xml", ".png": "image/png", ".woff2": "font/woff2" };
const LISTS = [
  { name: "search to a film", from: "/discover?q=horror%20films%20from%20the%2090s", card: ".k-poster", paged: true },
  { name: "franchises to a franchise", from: "/discover?browse=franchises", card: "a[href*='franchise=']", paged: true },
  { name: "Home to a title", from: "/", card: ".k-poster", paged: false },
];
const SCREENS = [
  ["desktop", { viewport: { width: 1440, height: 900 } }],
  ["phone", { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true }],
];
/** How far down a list is scrolled before a card is opened, in screens. */
const DEPTH = 8;
/** How near the place left counts as the same place, in pixels. */
const NEAR = 4;

const results = [];
const check = (name, ok, detail) => results.push(`${ok ? "✓" : "✗"} ${name} · ${detail}`);

const browser = await launch();
for (const [size, options] of SCREENS) {
  const context = await browser.newContext({ ...options, storageState: state, serviceWorkers: "block" });
  await context.route(base + "/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    if (LIVE.test(path)) return route.continue();
    const file = join(dist, path);
    if (path !== "/" && existsSync(file) && extname(file)) return route.fulfill({ body: readFileSync(file), contentType: TYPES[extname(file)] || "application/octet-stream" });
    if (route.request().resourceType() === "document") return route.fulfill({ body: readFileSync(join(dist, "index.html")), contentType: "text/html" });
    return route.continue();
  });
  for (const list of LISTS) {
    const page = await context.newPage();
    const reads = [];
    page.on("request", (r) => { if (new URL(r.url()).pathname.startsWith("/api/catalogue")) reads.push(r.url()); });
    // The first load signs in through the anchor and returns by its own redirect; the second is this build's.
    await page.goto(base + list.from, { waitUntil: "networkidle" });
    await page.goto(base + list.from, { waitUntil: "networkidle" });
    const column = () => page.evaluate(() => {
      const m = document.querySelector(".k-app__main");
      return { top: Math.round(m.scrollTop), height: m.scrollHeight, address: location.pathname + location.search };
    });
    for (let i = 1; i <= DEPTH; i++) {
      await page.evaluate((i) => { const m = document.querySelector(".k-app__main"); m.scrollTop = m.clientHeight * i; }, i);
      await page.waitForTimeout(700);
    }
    await page.waitForLoadState("networkidle");
    const label = `${size} ${list.name}`;
    // The end of a page can be its footer alone: up a screen at a time until a card is in view.
    const inView = (selector) => page.evaluate((selector) => {
      const m = document.querySelector(".k-app__main");
      const r = m.getBoundingClientRect();
      const c = [...m.querySelectorAll(selector)].find((e) => { const b = e.getBoundingClientRect(); return b.width > 40 && b.top > r.top + 40 && b.bottom < r.bottom - 40; });
      if (!c) return null;
      const b = c.getBoundingClientRect();
      return { x: b.left + b.width / 2, y: b.top + b.height / 3 };
    }, selector);
    let card = await inView(list.card);
    for (let i = 0; !card && i < 3; i++) {
      await page.evaluate(() => { const m = document.querySelector(".k-app__main"); m.scrollTop -= m.clientHeight; });
      await page.waitForTimeout(700);
      card = await inView(list.card);
    }
    const left = await column();
    if (!card || left.top < 2000) {
      check(label, false, `no card to open, or the list did not scroll (${left.top}px)`);
      await page.close();
      continue;
    }
    await page.mouse.click(card.x, card.y);
    await page.waitForURL((u) => u.pathname + u.search !== left.address);
    await page.waitForTimeout(1200);
    const opened = await column();
    check(`${label}: opened at the top`, opened.top === 0, `${opened.address} at ${opened.top}px`);

    const readsBefore = reads.length;
    await page.goBack();
    await page.waitForTimeout(3000);
    const back = await column();
    check(`${label}: Back at the place left`, back.address === left.address && Math.abs(back.top - left.top) <= NEAR,
      `${back.top}px of ${left.top}px, ${back.height}px tall of ${left.height}px`);
    if (list.paged) {
      const reread = reads.slice(readsBefore).filter((u) => /\/(titles|franchises)\b/.test(u));
      check(`${label}: Back read the list nothing`, reread.length === 0, `${reread.length} list reads`);
    }

    await page.reload({ waitUntil: "networkidle" });
    await page.waitForTimeout(3000);
    const reloaded = await column();
    check(`${label}: reload at the place left`, Math.abs(reloaded.top - left.top) <= NEAR, `${reloaded.top}px of ${left.top}px`);
    await page.close();
  }
  await context.close();
}
await browser.close();
console.log(results.join("\n"));
process.exit(results.some((r) => r.startsWith("✗")) ? 1 : 0);
