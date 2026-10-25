// Walks every page of the scratch site at a desktop, a phone and a television size, signed in, and
// reports each failed or slow request, console error and page error, with a screenshot of each page and
// how many API requests it made. Needs `verify/mobile/scratch.sh` running with a catalogue
// (`MOVIEBOT_VERIFY_CATALOGUE`). Known answers that are not faults: `/api/me/screens` is a 400 for a
// session without a device, `/releases` a 502 with no tracker, and the television has no plans pages.
//
//   node verify/pages/walk.mjs [only]     only: walk the pages whose name holds it
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { TV_CLIENT } from "../issuer.mjs";
import { launch, scratchSite, verifyDir } from "./browser.mjs";

const only = process.argv[2] || null;
const { base, issuer } = scratchSite();
const out = join(verifyDir, "pages", "walk");
mkdirSync(out, { recursive: true });
const viewer = { id: "verify-viewer", name: "Phone Check" };
const token = issuer.mint(viewer);
const api = async (path) => {
  const r = await fetch(base + path, { headers: { Authorization: "Bearer " + token } });
  if (!r.ok) throw new Error(`${path}: ${r.status} ${await r.text()}`);
  return r.json();
};

// Every address names something real, read from the API.
const titles = await api("/api/titles");
const film = titles.find((t) => t.kind === "film" && t.status === "ready");
const shows = await api("/api/shows");
const index = await api("/api/catalogue/discover");
const nolan = (await api("/api/catalogue/terms?q=christopher%20nolan")).find((t) => t.filter === "person");
const franchise = new URLSearchParams((await api("/api/catalogue/franchises?size=1")).franchises[0].query).get("franchise");

const pages = [
  ["home", "/"], ["movies", "/movies"], ["tv", "/tv"], ["anime", "/anime"],
  ["film-held", `/movies/${film.id}`],
  ...(shows.length ? [["show-held", `/tv/${shows[0].imdbId}`]] : []),
  ["discover", "/discover"],
  ["search", "/discover?q=fast%20and%20furious"],
  ["question", "/discover?q=horror%20films%20from%20the%2090s"],
  ...(nolan ? [["person", `/discover?person=${nolan.value}`]] : []),
  ["franchise", `/discover?franchise=${franchise}`],
  ...index.sections.map((s) => [`browse-${s.id}`, `/discover?browse=${s.id}`]),
  ["list-genre", "/discover?list=Horror&kind=film&genre=Horror"],
  ["list-keyword-sort", "/discover?list=Heist&keyword=k10051&sort=rating"],
  ["filter", "/discover?filter=1&g=Comedy&decade=1990"],
  ["catalogue-film", "/discover/film/tt0232500"],
  ["catalogue-show", "/discover/show/tt0903747/season/3"],
  ["anime-film", "/discover/film/tt0245429"],
  ["wants", "/wants"], ["plans", "/plans"], ["plan-new", "/plans/new?title=tt0232500"],
  ["profile", "/profile"], ["following", "/profile/following"], ["about", "/about"],
].filter(([name]) => !only || name.includes(only));

const sizes = [
  ["desktop", { viewport: { width: 1440, height: 900 } }, {}],
  ["phone", { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, deviceScaleFactor: 2 }, {}],
  ["tv", { viewport: { width: 1920, height: 1080 } }, { hours: 6, client: TV_CLIENT }],
];

const browser = await launch();
const report = [];
for (const [size, options, session] of sizes) {
  const context = await browser.newContext(options);
  await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(viewer, session));
  for (const [name, path] of pages) {
    const page = await context.newPage();
    const problems = [];
    const started = new Map();
    let apiCalls = 0;
    page.on("request", (r) => started.set(r, Date.now()));
    page.on("requestfailed", (r) => {
      const failure = r.failure() && r.failure().errorText;
      if (failure !== "net::ERR_ABORTED") problems.push(`failed ${r.method()} ${r.url().replace(base, "")}: ${failure}`);
    });
    page.on("response", (r) => {
      const url = r.url().replace(base, "");
      const ms = Date.now() - (started.get(r.request()) || Date.now());
      if (url.startsWith("/api")) apiCalls++;
      if (r.status() >= 400) problems.push(`${r.status()} ${r.request().method()} ${url}`);
      if (ms > 1500 && url.startsWith("/api")) problems.push(`slow ${ms}ms ${url}`);
    });
    page.on("console", (m) => { if (m.type() === "error" && !m.text().startsWith("Failed to load resource")) problems.push(`console: ${m.text().slice(0, 300)}`); });
    page.on("pageerror", (e) => problems.push(`pageerror: ${e.message.slice(0, 300)}`));
    const tv = size === "tv" ? (path.includes("?") ? "&" : "?") + "tv=1" : "";
    await page.goto(base + path + tv, { waitUntil: "networkidle", timeout: 30000 }).catch((e) => problems.push(`navigation: ${e.message.split("\n")[0]}`));
    // Scrolled through, so lazy shelves ask for themselves.
    await page.mouse.move(700, 500);
    for (let i = 0; i < 6; i++) { await page.mouse.wheel(0, 2000); await page.waitForTimeout(400); }
    await page.waitForTimeout(1500);
    await page.screenshot({ path: join(out, `${size}-${name}.png`) }).catch(() => {});
    report.push({ size, name, path, apiCalls, problems });
    console.log(`${size} ${name}: ${apiCalls} api${problems.length ? "\n  " + problems.slice(0, 10).join("\n  ") : ""}`);
    await page.close();
  }
  await context.close();
}
await browser.close();
writeFileSync(join(out, "report.json"), JSON.stringify(report, null, 2));
console.log(`Screenshots and report.json in ${out}`);
