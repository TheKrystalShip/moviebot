// The site's addresses, looked at in a real browser: every address cold-loaded in a fresh signed-in
// context, where it lands and what it draws, then the flows that move between them (a season, a profile
// tab, a search and Back, a signed-out deep link, signing out, a forward followed by Back).
//
//   node verify/routes.mjs
//
// Needs the scratch site verify/mobile/scratch.sh serves, whose library holds films and no show. A show
// the library holds an episode of therefore cannot be reached here: the forward from Discover's address
// to the shelf's is the one direction not looked at. Settings (environment): MOVIEBOT_VERIFY_DIR as
// scratch.sh; CHROME_PATH a Chromium, when Playwright's cached one is not wanted. Exits 1 when any
// check fails. A failed request is not a fault (the scratch site has no tracker, so a show's releases
// are a 502); the page raising an error is.

import { existsSync, readFileSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { VIEWER, memberToken } from "./mobile/stage.mjs";
import { issuerIn } from "./issuer.mjs";

const dir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");
if (!existsSync(join(dir, "api.env"))) {
  console.error(`No scratch site in ${dir}. Start verify/mobile/scratch.sh first.`);
  process.exit(2);
}
const readEnv = (file) => Object.fromEntries(
  readFileSync(join(dir, file), "utf8").split("\n").filter(Boolean).map((line) => [line.slice(0, line.indexOf("=")), line.slice(line.indexOf("=") + 1)]));
const site = readEnv("api.env");
const base = `http://127.0.0.1:${site.PORT}`;
const issuer = issuerIn(dir, Number(site.ISSUER_PORT));
const token = memberToken(issuer, VIEWER);

function chromePath() {
  if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
  const cache = join(homedir(), ".cache", "ms-playwright");
  for (const entry of readdirSync(cache).filter((d) => d.startsWith("chromium-")).sort().reverse()) {
    const candidate = join(cache, entry, "chrome-linux64", "chrome");
    if (existsSync(candidate)) return candidate;
  }
  throw new Error("No Chromium found. Set CHROME_PATH to a Chrome or Chromium binary.");
}

const titles = await (await fetch(base + "/api/titles", { headers: { Authorization: `Bearer ${token}` } })).json();
const MATRIX = { id: "the-matrix-1999", imdbId: "tt0133093" };
const ABSENT_FILM = "tt0234215";
const SHOW = "tt0903747";
if (!titles.some((t) => t.id === MATRIX.id && t.film && t.film.imdbId === MATRIX.imdbId)) {
  console.error(`The scratch library does not hold ${MATRIX.id}.`);
  process.exit(2);
}
if (titles.some((t) => t.film && t.film.imdbId === ABSENT_FILM)) {
  console.error(`The scratch library holds ${ABSENT_FILM}, which the checks need it not to.`);
  process.exit(2);
}

const browser = await chromium.launch({ executablePath: chromePath() });
let failures = 0;
let checked = 0;

function report(name, problems) {
  checked += 1;
  if (problems.length === 0) {
    console.log(`  ok    ${name}`);
    return;
  }
  failures += 1;
  console.log(`  FAIL  ${name}`);
  for (const p of problems) console.log(`          ${p}`);
}

async function open({ signedIn = true, width = 1280 } = {}) {
  const context = await browser.newContext({ viewport: { width, height: 900 } });
  if (signedIn) await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER));
  const page = await context.newPage();
  page.errors = [];
  page.on("pageerror", (e) => page.errors.push(e.message));
  page.on("console", (m) => { if (m.type() === "error" && !m.text().startsWith("Failed to load resource")) page.errors.push(m.text()); });
  return { context, page };
}

const where = (page) => page.url().replace(base, "");

/** Waits for the address to be `want`, then a moment longer, so an address that moves on again is caught. */
async function settled(page, want) {
  const until = Date.now() + 10000;
  while (where(page) !== want && Date.now() < until) await page.waitForTimeout(100);
  await page.waitForTimeout(600);
  return where(page);
}

/** What the page drew: its heading, what the sidebar and the phone's tab bar light, and what is selected. */
const drawn = (page) => page.evaluate(() => {
  const text = (selector) => [...document.querySelectorAll(selector)].map((e) => e.textContent.trim());
  return {
    h1: text("h1")[0] || null,
    nav: text(".nav-item--active"),
    tab: text(".tabbar__tab--active"),
    chipText: text(".kc-season-chip.is-on"),
    subtab: text(".subtab--active"),
    empty: text(".kc-empty__title"),
    trail: [...document.querySelectorAll(".crumbs__item")].map((li) => [li.textContent.trim(), li.querySelector("a")?.getAttribute("href") ?? null]),
  };
});

/** The trail each address ends up showing once its page has loaded: `[label, href]`, the last without an href. */
const HOME = ["Home", "/"];
const TRAILS = {
  "/": [],
  "/movies": [HOME, ["Movies", null]],
  [`/movies/${MATRIX.id}`]: [HOME, ["Movies", "/movies"], ["The Matrix", null]],
  "/movies/nothing-here": [HOME, ["Movies", "/movies"], ["Not found", null]],
  "/tv": [HOME, ["TV Shows", null]],
  "/tv/tt9999999999": [HOME, ["TV Shows", "/tv"], ["No such show", null]],
  [`/discover/show/${SHOW}`]: [HOME, ["Discover", "/discover"], ["Breaking Bad", null]],
  [`/discover/show/${SHOW}/season/3`]: [HOME, ["Discover", "/discover"], ["Breaking Bad", `/discover/show/${SHOW}`], ["Season 3", null]],
  [`/discover/show/${SHOW}/season/4`]: [HOME, ["Discover", "/discover"], ["Breaking Bad", `/discover/show/${SHOW}`], ["Season 4", null]],
  "/discover": [HOME, ["Discover", null]],
  "/discover?q=matrix": [HOME, ["Discover", null]],
  [`/discover/film/${ABSENT_FILM}`]: [HOME, ["Discover", "/discover"], ["The Matrix Reloaded", null]],
  "/saved": [HOME, ["Saved", null]],
  "/saved/later": [HOME, ["Saved", "/saved"], [/./, null]],
  "/profile": [HOME, ["Profile", null]],
  "/profile/requests": [HOME, ["Profile", "/profile"], ["Requests", null]],
  "/profile/playback": [HOME, ["Profile", "/profile"], ["Playback", null]],
  "/notify": [HOME, ["Notify me", null]],
  "/about": [HOME, ["About", null]],
  "/nonsense": [HOME, ["Not found", null]],
  "/nonsense/": [HOME, ["Not found", null]],
  "/nonsense/deeper/still": [HOME, ["Not found", null]],
};

const sameTrail = (got, want) => got.length === want.length
  && want.every(([label, href], i) => (label instanceof RegExp ? label.test(got[i][0]) : label === got[i][0]) && href === got[i][1]);
const show = (trail) => trail.map(([label, href]) => (href ? `${label} (${href})` : label)).join(" / ") || "none";

const sameList = (got, want) => JSON.stringify(got) === JSON.stringify(want);

/**
 * Cold-loads `path` signed in and checks where it lands and what it draws. `expect`: `lands` the
 * address; `h1` a pattern for the heading; `nav` and `tab` the sidebar item and tab-bar tab lit, or
 * null for none; `empty` the empty state's title; `chip` the season chip lit; `subtab` the profile tab.
 */
async function cold(path, expect) {
  const { context, page } = await open();
  const problems = [];
  try {
    await page.goto(base + path, { waitUntil: "load" });
    const landed = await settled(page, expect.lands);
    await page.waitForLoadState("networkidle", { timeout: 10000 }).catch(() => {});
    await page.waitForTimeout(500);
    const d = await drawn(page);
    if (landed !== expect.lands) problems.push(`landed at ${landed}, wanted ${expect.lands}`);
    if (expect.h1 && !(d.h1 && expect.h1.test(d.h1))) problems.push(`heading ${JSON.stringify(d.h1)}, wanted ${expect.h1}`);
    if ("nav" in expect && !sameList(d.nav, expect.nav ? [expect.nav] : [])) problems.push(`sidebar lights ${JSON.stringify(d.nav)}, wanted ${expect.nav}`);
    if ("tab" in expect && !sameList(d.tab, expect.tab ? [expect.tab] : [])) problems.push(`tab bar lights ${JSON.stringify(d.tab)}, wanted ${expect.tab}`);
    if (expect.empty && !d.empty.includes(expect.empty)) problems.push(`empty state ${JSON.stringify(d.empty)}, wanted ${expect.empty}`);
    if (expect.chip && !d.chipText.some((t) => t.startsWith(expect.chip))) problems.push(`season chip lit ${JSON.stringify(d.chipText)}, wanted ${expect.chip}`);
    const trail = TRAILS[expect.lands];
    if (!trail) problems.push(`no trail is expected for ${expect.lands}`);
    else if (!sameTrail(d.trail, trail)) problems.push(`trail ${show(d.trail)}, wanted ${show(trail.map(([l, h]) => [String(l), h]))}`);
    if (expect.subtab && !d.subtab.includes(expect.subtab)) problems.push(`profile tab ${JSON.stringify(d.subtab)}, wanted ${expect.subtab}`);
    problems.push(...page.errors.map((e) => `the page raised: ${e}`));
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report(`${path}  ->  ${expect.lands}`, problems);
}

const M = `/movies/${MATRIX.id}`;
const here = (item, tab) => ({ nav: item, tab });

console.log(`Cold loads at ${base}`);
await cold("/", { lands: "/", ...here("Home", "Home") });
await cold("/movies", { lands: "/movies", h1: /Movies/, ...here("Movies", "Library") });
await cold(M, { lands: M, h1: /Matrix/, ...here("Movies", "Library") });
await cold("/movies/nothing-here", { lands: "/movies/nothing-here", empty: "Not found", ...here("Movies", "Library") });
await cold("/tv", { lands: "/tv", h1: /TV/, ...here("TV Shows", "Library") });
await cold(`/tv/${SHOW}`, { lands: `/discover/show/${SHOW}`, h1: /Breaking Bad/, ...here("Discover", "Discover") });
await cold(`/tv/${SHOW}/season/3`, { lands: `/discover/show/${SHOW}/season/3`, h1: /Breaking Bad/, chip: "Season 3", ...here("Discover", "Discover") });
await cold(`/discover/show/${SHOW}`, { lands: `/discover/show/${SHOW}`, h1: /Breaking Bad/, ...here("Discover", "Discover") });
await cold(`/discover/show/${SHOW}/season/4`, { lands: `/discover/show/${SHOW}/season/4`, chip: "Season 4", ...here("Discover", "Discover") });
await cold(`/discover/show/${SHOW}/season/99`, { lands: `/discover/show/${SHOW}`, h1: /Breaking Bad/ });
await cold(`/tv/${SHOW}/season/99`, { lands: `/discover/show/${SHOW}`, h1: /Breaking Bad/ });
await cold(`/discover/show/${SHOW}/season/abc`, { lands: `/discover/show/${SHOW}` });
await cold(`/discover/show/${SHOW}/season`, { lands: `/discover/show/${SHOW}` });
await cold(`/discover/show/${SHOW}/season/3/extra`, { lands: `/discover/show/${SHOW}/season/3` });
await cold(`/discover/show/${SHOW}/extra`, { lands: `/discover/show/${SHOW}` });
await cold(`/discover/show/${SHOW}/`, { lands: `/discover/show/${SHOW}` });
await cold("/tv/tt9999999999", { lands: "/tv/tt9999999999", empty: "No such show" });
await cold("/discover", { lands: "/discover", h1: /Discover/, ...here("Discover", "Discover") });
await cold("/discover?q=matrix", { lands: "/discover?q=matrix", ...here("Discover", "Discover") });
await cold("/discover/", { lands: "/discover" });
await cold("/discover/other", { lands: "/discover" });
await cold("/discover/film", { lands: "/discover" });
await cold("/discover/show", { lands: "/discover" });
await cold(`/discover/film/${ABSENT_FILM}`, { lands: `/discover/film/${ABSENT_FILM}`, h1: /Matrix Reloaded/, ...here("Discover", "Discover") });
await cold(`/discover/film/${ABSENT_FILM}/extra`, { lands: `/discover/film/${ABSENT_FILM}` });
await cold(`/discover/film/${MATRIX.imdbId}`, { lands: M, h1: /Matrix/, ...here("Movies", "Library") });
await cold(`/discover/film/${MATRIX.imdbId}/extra`, { lands: M });
await cold("/saved", { lands: "/saved", ...here("Saved", "Me") });
await cold("/saved/", { lands: "/saved" });
await cold("/saved/later", { lands: "/saved/later", ...here("Saved", "Me") });
await cold("/saved/later/extra", { lands: "/saved/later" });
await cold("/profile", { lands: "/profile", subtab: "History", nav: null, tab: "Me" });
await cold("/profile/", { lands: "/profile" });
await cold("/profile/history", { lands: "/profile", subtab: "History" });
await cold("/profile/requests", { lands: "/profile/requests", subtab: "Requests", tab: "Me" });
await cold("/profile/playback", { lands: "/profile/playback", subtab: "Playback" });
await cold("/profile/offline", { lands: "/profile", subtab: "History" });
await cold("/profile/nope", { lands: "/profile", subtab: "History" });
await cold("/notify", { lands: "/notify", ...here("Notify me", "Me") });
await cold("/notify/", { lands: "/notify" });
await cold("/about", { lands: "/about", h1: /About/ });
await cold("/about/x", { lands: "/about" });
await cold("/r", { lands: "/" });

console.log("\nAddresses the API sent and people hold");
await cold(`/title/${MATRIX.id}`, { lands: M, h1: /Matrix/, ...here("Movies", "Library") });
await cold(`/title/${MATRIX.id}/extra`, { lands: M });
await cold("/title", { lands: "/movies" });
await cold("/title/", { lands: "/movies" });
await cold(`/show/${SHOW}`, { lands: `/discover/show/${SHOW}`, h1: /Breaking Bad/ });
await cold(`/show/${SHOW}?season=3`, { lands: `/discover/show/${SHOW}/season/3`, chip: "Season 3" });
await cold(`/show/${SHOW}?season=99`, { lands: `/discover/show/${SHOW}` });
await cold("/show", { lands: "/tv" });
await cold(`/film/${MATRIX.imdbId}`, { lands: M });
await cold(`/film/${ABSENT_FILM}`, { lands: `/discover/film/${ABSENT_FILM}` });
await cold("/film", { lands: "/discover" });
await cold("/profile?tab=requests", { lands: "/profile/requests", subtab: "Requests" });
await cold("/profile?tab=history", { lands: "/profile", subtab: "History" });
await cold("/profile?tab=nope", { lands: "/profile" });

console.log("\nCanonical or not found");
await cold("/movies/", { lands: "/movies" });
await cold(`${M}/extra`, { lands: M });
await cold("/nonsense", { lands: "/nonsense", empty: "Not found", nav: null, tab: null });
await cold("/nonsense/", { lands: "/nonsense/", empty: "Not found" });
await cold("/nonsense/deeper/still", { lands: "/nonsense/deeper/still", empty: "Not found" });

console.log("\nFlows");

{
  const { context, page } = await open();
  const problems = [];
  try {
    await page.goto(base + `/discover/show/${SHOW}`, { waitUntil: "load" });
    await page.waitForSelector("button.kc-season-chip", { timeout: 15000 });
    const before = await page.evaluate(() => { document.querySelector("h1").dataset.mark = "kept"; return history.length; });
    await page.locator("button.kc-season-chip", { hasText: "Season 4" }).first().click();
    const landed = await settled(page, `/discover/show/${SHOW}/season/4`);
    if (landed !== `/discover/show/${SHOW}/season/4`) problems.push(`a season chip left the address at ${landed}`);
    const after = await page.evaluate(() => ({ length: history.length, mark: document.querySelector("h1")?.dataset.mark }));
    if (after.length !== before) problems.push("a season chip added a history entry");
    if (after.mark !== "kept") problems.push("a season chip remounted the page");
    if (!(await drawn(page)).chipText.some((t) => t.startsWith("Season 4"))) problems.push("Season 4 is not the lit chip after pressing it");
    await page.locator("button.kc-season-chip", { hasText: "Season 2" }).first().click();
    await settled(page, `/discover/show/${SHOW}/season/2`);
    if (!(await drawn(page)).chipText.some((t) => t.startsWith("Season 2"))) problems.push("Season 2 is not the lit chip after pressing it");
    await page.reload({ waitUntil: "load" });
    await page.waitForSelector("button.kc-season-chip", { timeout: 15000 });
    await page.waitForTimeout(600);
    if (where(page) !== `/discover/show/${SHOW}/season/2`) problems.push(`a reload moved the address to ${where(page)}`);
    if (!(await drawn(page)).chipText.some((t) => t.startsWith("Season 2"))) problems.push("a reload did not keep Season 2 lit");
    problems.push(...page.errors.map((e) => `the page raised: ${e}`));
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report("a season chip writes the address in place and survives a reload", problems);
}

{
  const { context, page } = await open();
  const problems = [];
  try {
    await page.goto(base + "/profile", { waitUntil: "load" });
    await page.waitForSelector(".kc-profile", { timeout: 15000 });
    const before = await page.evaluate(() => { document.querySelector(".kc-profile").dataset.mark = "kept"; return history.length; });
    await page.locator(".subtab", { hasText: "Playback" }).click();
    if ((await settled(page, "/profile/playback")) !== "/profile/playback") problems.push(`a tab left the address at ${where(page)}`);
    await page.locator(".subtab", { hasText: "History" }).click();
    if ((await settled(page, "/profile")) !== "/profile") problems.push(`the first tab left the address at ${where(page)}`);
    const after = await page.evaluate(() => ({ length: history.length, mark: document.querySelector(".kc-profile")?.dataset.mark }));
    if (after.length !== before) problems.push("a tab added a history entry");
    if (after.mark !== "kept") problems.push("a tab remounted the page");
    problems.push(...page.errors.map((e) => `the page raised: ${e}`));
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report("a profile tab writes the address in place; the first has no segment", problems);
}

{
  const { context, page } = await open();
  const problems = [];
  try {
    await page.goto(base + "/discover", { waitUntil: "load" });
    await page.waitForSelector(".kc-discover input", { timeout: 15000 });
    await page.fill(".kc-discover input", "matrix");
    await page.keyboard.press("Enter");
    if ((await settled(page, "/discover?q=matrix")) !== "/discover?q=matrix") problems.push(`a search left the address at ${where(page)}`);
    const result = page.locator(".kc-hit h2 a").first();
    await result.waitFor({ timeout: 15000 });
    const to = await result.getAttribute("href");
    await result.click();
    await page.waitForTimeout(1500);
    if (where(page) !== to) problems.push(`a result led to ${where(page)}, not ${to}`);
    await page.goBack();
    await page.waitForTimeout(1200);
    if (where(page) !== "/discover?q=matrix") problems.push(`Back landed at ${where(page)}`);
    const field = await page.inputValue(".kc-discover input");
    if (field !== "matrix") problems.push(`Back left ${JSON.stringify(field)} in the field`);
    problems.push(...page.errors.map((e) => `the page raised: ${e}`));
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report("Discover search, a result, and Back keeps the query", problems);
}

{
  const { context, page } = await open();
  const problems = [];
  try {
    await page.route(new RegExp(`/api/titles/${MATRIX.id}$`), async (route) => { await new Promise((r) => setTimeout(r, 2500)); await route.continue(); });
    await page.goto(base + `/movies/${MATRIX.id}`, { waitUntil: "commit" });
    await page.waitForSelector(".crumbs", { timeout: 15000 });
    const early = (await drawn(page)).trail;
    if (!sameTrail(early, [HOME, ["Movies", "/movies"], [MATRIX.id, null]])) problems.push(`before the name loads: ${show(early)}`);
    await page.waitForSelector("h1", { timeout: 15000 });
    await page.waitForTimeout(500);
    const late = (await drawn(page)).trail;
    if (!sameTrail(late, TRAILS[`/movies/${MATRIX.id}`])) problems.push(`after the name loads: ${show(late)}`);
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report("a film's crumb is its id until the page has its name", problems);
}

{
  const { context, page } = await open();
  const problems = [];
  try {
    await page.route(new RegExp(`/api/shows/${SHOW}$`), async (route) => { await new Promise((r) => setTimeout(r, 2500)); await route.continue(); });
    await page.goto(base + `/discover/show/${SHOW}/season/3`, { waitUntil: "commit" });
    await page.waitForSelector(".crumbs", { timeout: 15000 });
    const early = (await drawn(page)).trail;
    if (!sameTrail(early, [HOME, ["Discover", "/discover"], [SHOW, `/discover/show/${SHOW}`], ["Season 3", null]])) problems.push(`before the name loads: ${show(early)}`);
    await page.waitForSelector("h1", { timeout: 15000 });
    await page.waitForTimeout(500);
    const late = (await drawn(page)).trail;
    if (!sameTrail(late, TRAILS[`/discover/show/${SHOW}/season/3`])) problems.push(`after the name loads: ${show(late)}`);
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report("a show's crumb is its id until the page has its name", problems);
}

for (const [from, crumb, lands] of [
  [`/movies/${MATRIX.id}`, "Movies", "/movies"],
  [`/movies/${MATRIX.id}`, "Home", "/"],
  [`/discover/show/${SHOW}/season/3`, "Breaking Bad", `/discover/show/${SHOW}`],
  [`/discover/show/${SHOW}/season/3`, "Discover", "/discover"],
  ["/profile/playback", "Profile", "/profile"],
  ["/saved/later", "Saved", "/saved"],
]) {
  const { context, page } = await open();
  const problems = [];
  try {
    await page.goto(base + from, { waitUntil: "load" });
    await page.waitForSelector(".crumbs", { timeout: 15000 });
    await page.waitForTimeout(800);
    await page.evaluate(() => { window.__stayed = true; });
    const before = await page.evaluate(() => history.length);
    await page.locator(".crumbs__link", { hasText: new RegExp(`^${crumb}$`) }).click();
    if ((await settled(page, lands)) !== lands) problems.push(`landed at ${where(page)}, wanted ${lands}`);
    if (!(await page.evaluate(() => window.__stayed === true))) problems.push("the page was reloaded");
    if ((await page.evaluate(() => history.length)) !== before + 1) problems.push("the crumb did not push one entry");
    await page.goBack();
    if ((await settled(page, from)) !== from) problems.push(`Back landed at ${where(page)}, not ${from}`);
    problems.push(...page.errors.map((e) => `the page raised: ${e}`));
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report(`${from}: the ${crumb} crumb goes to ${lands} in place`, problems);
}

for (const [from, forwards] of [[`/discover/film/${MATRIX.imdbId}`, M], [`/tv/${SHOW}`, `/discover/show/${SHOW}`], [`/film/${ABSENT_FILM}`, `/discover/film/${ABSENT_FILM}`]]) {
  const { context, page } = await open();
  const problems = [];
  try {
    await page.goto(base + "/about", { waitUntil: "load" });
    await page.waitForSelector(".kc-about", { timeout: 15000 });
    await page.goto(base + from, { waitUntil: "load" });
    if ((await settled(page, forwards)) !== forwards) problems.push(`landed at ${where(page)}, wanted ${forwards}`);
    await page.goBack();
    await page.waitForTimeout(1200);
    if (where(page) !== "/about") problems.push(`Back landed at ${where(page)}, not the page before`);
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report(`${from} forwards to ${forwards} and Back does not bounce`, problems);
}

/**
 * Waits for the browser to reach the provider's authorize endpoint, and answers what is wrong with the
 * request it made: the site's client, PKCE, and /signin to come back to.
 */
async function leftForProvider(page) {
  const authorize = issuer.url + "/authorize";
  const until = Date.now() + 10000;
  while (!page.url().startsWith(authorize) && Date.now() < until) await page.waitForTimeout(100);
  const reached = new URL(page.url());
  if (reached.origin + reached.pathname !== authorize) return [`landed at ${page.url()}, wanted the provider's ${authorize}`];
  const problems = [];
  if (reached.searchParams.get("client_id") !== "cinema-site") problems.push(`signed in as ${reached.searchParams.get("client_id")}`);
  if (reached.searchParams.get("code_challenge_method") !== "S256") problems.push("no PKCE challenge");
  if (reached.searchParams.get("redirect_uri") !== base + "/signin") problems.push(`returns to ${reached.searchParams.get("redirect_uri")}`);
  return problems;
}

// Signed out, a page leaves for the provider at once, remembering itself to come back to.
for (const path of ["/tv/tt0903747/season/3", "/discover?q=matrix", "/movies/the-matrix-1999", "/profile/playback", "/nonsense", "/signin"]) {
  const { context, page } = await open({ signedIn: false });
  const problems = [];
  try {
    await page.goto(base + path, { waitUntil: "load" });
    problems.push(...await leftForProvider(page));
    if (path !== "/signin") {
      await page.goto(base + "/manifest.webmanifest").catch(() => {});
      const remembered = await page.evaluate(() => sessionStorage.getItem("cinema:signin-return"));
      if (remembered !== path) problems.push(`remembers ${remembered}, wanted ${path}`);
    }
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report(`signed out, ${path} goes to the provider${path === "/signin" ? "" : " and remembers it"}`, problems);
}

// Signing out is the provider's: the browser goes to its end-session endpoint, naming the session and
// asking to come back to the site. The session is planted once, so what is held afterwards is the site's.
{
  const { context, page } = await open({ signedIn: false });
  const problems = [];
  try {
    await page.goto(base + "/manifest.webmanifest");
    await page.evaluate(({ key, value }) => localStorage.setItem(key, value), issuer.planted(VIEWER));
    await page.goto(base + "/profile", { waitUntil: "load" });
    await page.getByRole("button", { name: "Sign out" }).click();
    const signOut = issuer.url + "/sign-out";
    const until = Date.now() + 10000;
    while (!page.url().startsWith(signOut) && Date.now() < until) await page.waitForTimeout(100);
    const reached = new URL(page.url());
    if (reached.origin + reached.pathname !== signOut) problems.push(`landed at ${page.url()}, wanted the provider's ${signOut}`);
    else {
      if (!reached.searchParams.get("id_token_hint")) problems.push("names no session");
      if (reached.searchParams.get("post_logout_redirect_uri") !== base + "/") problems.push(`returns to ${reached.searchParams.get("post_logout_redirect_uri")}`);
    }
    const left = await context.newPage();
    await left.goto(base + "/manifest.webmanifest");
    if (await left.evaluate(() => Object.keys(localStorage).some((k) => k.startsWith("cinema.oidc.user")))) problems.push("the session is still held");
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report("signing out goes to the provider's end-session endpoint and forgets the session", problems);
}

// A session that ends while a page is open is shown, with the way back, and never followed.
{
  const { context, page } = await open();
  const problems = [];
  try {
    await page.goto(base + "/profile", { waitUntil: "load" });
    await page.getByRole("button", { name: "Sign out" }).waitFor({ timeout: 10000 });
    // The API refuses the session from here on, and the planted one has no refresh token to renew it.
    await page.route("**/api/**", (route) => route.fulfill({ status: 401, contentType: "application/json", body: "{}" }));
    await page.locator(".nav-item", { hasText: "Discover" }).first().click();
    const card = page.locator(".kc-signin__card");
    await card.waitFor({ timeout: 10000 });
    await page.waitForTimeout(1500);
    if (!(await card.textContent()).includes("Your session ended.")) problems.push(`says ${await card.textContent()}`);
    if (page.url().startsWith(issuer.url)) problems.push("left for the provider by itself");
    await page.unroute("**/api/**");
    await card.locator("button").click();
    problems.push(...await leftForProvider(page));
  } catch (error) {
    problems.push(error.message.split("\n")[0]);
  }
  await context.close();
  report("a session ending while a page is open is shown, and its button goes to the provider", problems);
}

await browser.close();
console.log(`\n${checked} checks, ${failures} failed.`);
process.exit(failures ? 1 : 0);
