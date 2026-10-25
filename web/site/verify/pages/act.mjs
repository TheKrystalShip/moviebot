// Acts on the scratch site as a person would and checks each step by the requests it makes and what the
// page then shows: a list paging on as its end nears, a sort and a genre picked in the drawer, wanting a
// film, narrowing a shelf of the library, a person's, a keyword's and a franchise's links, a question,
// a show's seasons and making a plan. Needs `verify/mobile/scratch.sh` running with a catalogue.
//
//   node verify/pages/act.mjs
import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { launch, scratchSite, verifyDir } from "./browser.mjs";

const { base, issuer } = scratchSite();
const out = join(verifyDir, "pages", "act");
const viewer = { id: "verify-actor", name: "Act Check" };
mkdirSync(out, { recursive: true });

const browser = await launch();
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), issuer.planted(viewer));
const page = await context.newPage();
const calls = [];
const errors = [];
page.on("response", (r) => { const u = r.url().replace(base, ""); if (u.startsWith("/api")) calls.push(`${r.status()} ${r.request().method()} ${u}`); if (r.status() >= 400 && !/screens|releases/.test(u)) errors.push(`${r.status()} ${r.request().method()} ${u}`); });
page.on("pageerror", (e) => errors.push(`pageerror: ${e.message}`));
const results = [];
const check = (name, ok, detail = "") => { results.push(`${ok ? "✓" : "✗"} ${name}${detail ? " · " + detail : ""}`); };
const since = (n) => calls.slice(n);
const cards = () => page.locator(".kc-grid > *").count();

async function step(name, fn) {
  const before = calls.length;
  try { await fn(before); } catch (error) { check(name, false, error.message.split("\n")[0]); }
  await page.screenshot({ path: join(out, name.replace(/\W+/g, "-") + ".png") });
}

await step("a list pages on as its end nears", async (n) => {
  await page.goto(base + "/discover?list=Horror&kind=film&genre=Horror", { waitUntil: "networkidle" });
  const first = await cards();
  await page.mouse.move(800, 600);
  for (let i = 0; i < 8; i++) { await page.mouse.wheel(0, 4000); await page.waitForTimeout(700); }
  await page.waitForTimeout(1500);
  const after = await cards();
  check("a list pages on as its end nears", after > first && since(n).some((c) => /page=2/.test(c)), `${first} then ${after} cards`);
});

await step("a sort is picked in the drawer", async (n) => {
  await page.goto(base + "/discover?list=Horror&kind=film&genre=Horror", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: "Filters" }).first().click();
  await page.waitForTimeout(500);
  const option = page.getByText(/Highest rated/i).first();
  await option.click();
  await page.waitForTimeout(1500);
  check("a sort is picked in the drawer", /sort=rating/.test(page.url()) && since(n).some((c) => /sort=rating/.test(c)), page.url().replace(base, ""));
  await page.keyboard.press("Escape");
});

await step("a genre picked on the front opens the filter", async (n) => {
  await page.goto(base + "/discover", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: "Filters" }).first().click();
  await page.waitForTimeout(500);
  await page.getByText("Comedy", { exact: true }).first().click();
  await page.waitForTimeout(1500);
  check("a genre picked on the front opens the filter", /filter=1/.test(page.url()) && /g=Comedy/.test(page.url())
    && since(n).some((c) => /\/api\/catalogue\/titles\?.*anyGenre=Comedy/.test(c)), page.url().replace(base, ""));
  await page.keyboard.press("Escape");
});

await step("a film is wanted from its page and listed on Want to watch", async (n) => {
  // Not wanted when the step starts, so it is the same step every run.
  await fetch(base + "/api/me/wants/tt0816692", { method: "DELETE", headers: { Authorization: "Bearer " + issuer.mint(viewer) } });
  await page.goto(base + "/discover/film/tt0816692", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: /^Want$/ }).first().click();
  await page.waitForTimeout(1500);
  const put = since(n).find((c) => /PUT \/api\/me\/wants\/tt0816692/.test(c));
  await page.goto(base + "/wants", { waitUntil: "networkidle" });
  const listed = await page.getByText("Interstellar", { exact: true }).count();
  check("a film is wanted from its page and listed on Want to watch", !!put && put.startsWith("200") && listed > 0, put || "no PUT");
});

await step("a shelf of the library sorts and narrows", async (n) => {
  await page.goto(base + "/movies", { waitUntil: "networkidle" });
  const first = await cards();
  await page.getByRole("button", { name: "Filters" }).first().click();
  await page.waitForTimeout(500);
  await page.getByText("Action", { exact: true }).first().click();
  await page.waitForTimeout(1500);
  const narrowed = await cards();
  check("a shelf of the library sorts and narrows", narrowed > 0 && narrowed < first && since(n).some((c) => /\/api\/library\/films\?.*anyGenre=Action/.test(c)), `${first} then ${narrowed}`);
  await page.keyboard.press("Escape");
});

await step("a person's name on a film leads to their work", async () => {
  await page.goto(base + "/discover/film/tt0232500", { waitUntil: "networkidle" });
  await page.getByRole("link", { name: "Vin Diesel" }).first().click();
  await page.waitForTimeout(1500);
  const heading = await page.locator("h2").first().innerText();
  check("a person's name on a film leads to their work", /person=/.test(page.url()) && /Vin Diesel/.test(heading) && (await cards()) > 5, heading);
});

await step("a keyword leads to its list", async () => {
  await page.goto(base + "/discover/film/tt0232500", { waitUntil: "networkidle" });
  const chip = page.locator(".kc-keyword").first();
  const name = (await chip.innerText()).trim();
  await chip.click();
  await page.waitForTimeout(1500);
  check("a keyword leads to its list", /keyword=k/.test(page.url()) && (await cards()) > 0, `${name} → ${page.url().replace(base, "")}`);
});

await step("a franchise tile opens its films in order", async () => {
  await page.goto(base + "/discover?browse=franchises", { waitUntil: "networkidle" });
  await page.locator(".kc-browse-grid a").first().click();
  await page.waitForTimeout(1500);
  check("a franchise tile opens its films in order", /franchise=/.test(page.url()) && (await cards()) > 3, page.url().replace(base, ""));
});

await step("a question reads into parts and answers", async (n) => {
  await page.goto(base + "/discover?q=" + encodeURIComponent("best sci-fi of the 80s"), { waitUntil: "networkidle" });
  const pills = await page.locator(".kc-keywords .k-pill, .kc-keywords > *").count();
  check("a question reads into parts and answers", pills >= 2 && (await cards()) > 5, `${pills} parts`);
});

await step("a show's season chip changes the season", async () => {
  await page.goto(base + "/discover/show/tt0903747", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: "Season 4" }).click();
  await page.waitForTimeout(1500);
  const title = await page.locator(".kc-season__title").innerText();
  check("a show's season chip changes the season", /season\/4/.test(page.url()) && /Season 4/.test(title), title);
});

await step("a plan is made", async (n) => {
  await page.goto(base + "/plans/new?title=tt0232500", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: "Make the plan" }).click();
  await page.waitForTimeout(1500);
  const post = since(n).find((c) => /POST \/api\/plans$/.test(c));
  check("a plan is made", !!post && post.startsWith("200") && /\/plans\/[^n]/.test(page.url()), `${post} → ${page.url().replace(base, "")}`);
});

await browser.close();
console.log(results.join("\n"));
if (errors.length) console.log("errors:\n  " + [...new Set(errors)].join("\n  "));
