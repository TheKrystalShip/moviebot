// Walks the live site signed in, at a desktop and a phone size, reporting each failed request and page
// error with how long each page took to settle. The session is a Playwright storage state from
// `tks-login browser -o <file>`, which signs the account `claude` in at the live anchor.
//
//   tks-login browser -o /tmp/live-state.json
//   node verify/pages/live.mjs /tmp/live-state.json [https://movies.thekrystalship.com]
import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { launch, verifyDir } from "./browser.mjs";

const state = process.argv[2];
const base = process.argv[3] || "https://movies.thekrystalship.com";
if (!state) { console.error("node verify/pages/live.mjs <storage state> [base]"); process.exit(2); }
const out = join(verifyDir, "pages", "live");
mkdirSync(out, { recursive: true });

const pages = ["/", "/movies", "/tv", "/discover", "/discover?q=fast%20and%20furious", "/discover?q=horror%20films%20from%20the%2090s",
  "/discover?browse=franchises", "/discover?browse=moods", "/discover?browse=studios", "/discover/film/tt0232500",
  "/discover/show/tt0903747", "/wants", "/plans", "/profile", "/profile/following", "/about"];

const browser = await launch();
for (const [size, options] of [["desktop", { viewport: { width: 1440, height: 900 } }], ["phone", { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true }]]) {
  const context = await browser.newContext({ ...options, storageState: state });
  for (const path of pages) {
    const page = await context.newPage();
    const problems = [];
    let apiCalls = 0;
    page.on("response", (r) => {
      const url = r.url().replace(base, "");
      if (url.startsWith("/api")) apiCalls++;
      if (r.status() >= 400 && !url.startsWith("/api/me/screens")) problems.push(`${r.status()} ${url}`);
    });
    page.on("pageerror", (e) => problems.push(`pageerror ${e.message}`));
    const started = Date.now();
    await page.goto(base + path, { waitUntil: "networkidle", timeout: 45000 }).catch((e) => problems.push(e.message.split("\n")[0]));
    const ms = Date.now() - started;
    await page.mouse.move(700, 500);
    for (let i = 0; i < 4; i++) { await page.mouse.wheel(0, 2500); await page.waitForTimeout(500); }
    await page.waitForTimeout(800);
    const signedIn = (await page.getByText(/^Sign in$/).count()) === 0;
    await page.screenshot({ path: join(out, `${size}${path.replace(/\W+/g, "-")}.png`) });
    console.log(`${size} ${path} ${ms}ms, ${apiCalls} api${signedIn ? "" : " · NOT SIGNED IN"}${problems.length ? "\n  " + problems.slice(0, 6).join("\n  ") : ""}`);
    await page.close();
  }
  await context.close();
}
await browser.close();
console.log(`Screenshots in ${out}`);
