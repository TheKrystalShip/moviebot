// What the page checks share: the browser, and the scratch site's address and identity provider.
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { issuerIn } from "../issuer.mjs";

/** The Chromium Playwright keeps, or `CHROME_PATH`. */
function chromePath() {
  if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
  const cache = join(homedir(), ".cache", "ms-playwright");
  for (const entry of readdirSync(cache).filter((d) => d.startsWith("chromium-")).sort().reverse()) {
    const candidate = join(cache, entry, "chrome-linux64", "chrome");
    if (existsSync(candidate)) return candidate;
  }
  throw new Error("No Chromium found. Set CHROME_PATH to a Chrome or Chromium binary.");
}

export const launch = () => chromium.launch({ executablePath: chromePath() });

/** Where the scratch site keeps everything (`verify/mobile/scratch.sh`). */
export const verifyDir = process.env.MOVIEBOT_VERIFY_DIR || join(homedir(), ".cache", "moviebot-verify");

/** The scratch site that `scratch.sh` serves: its address and its identity provider. */
export function scratchSite() {
  const env = Object.fromEntries(readFileSync(join(verifyDir, "api.env"), "utf8").trim().split("\n").map((l) => l.split("=")));
  return { base: `http://127.0.0.1:${env.PORT}`, issuer: issuerIn(verifyDir, Number(env.ISSUER_PORT)) };
}
