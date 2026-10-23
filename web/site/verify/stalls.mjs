// The player against what keeps a film from starting or going on, in a real Chromium against the
// scratch site: a private file system that never answers, which the film streams past rather than
// waiting on; a film whose playlists are missing for a while, which the player waits out at a widening
// interval rather than asking for hundreds of times a second, and plays once they are back; and a
// transcode begun again from nothing, its playlists gone and then regrown, with one viewer in the room
// before and one opening the film while they are gone, both of whom play on with the room.
//
//   node verify/stalls.mjs
//
// Needs the scratch site verify/mobile/scratch.sh serves, over a library holding a ready film
// (`MOVIEBOT_VERIFY_TITLE`, by default `coherence-2013`, the film scratch.sh plays in full), and a Chromium that decodes H.264 and AAC. Settings
// (environment): MOVIEBOT_VERIFY_DIR as scratch.sh; CHROME_PATH a Chromium. The restarted transcode is
// a copy of the film hard-linked beside it under `<title>-restarted`, its playlists and manifest its
// own, removed at the end. Exits 1 when any check fails, 2 when there is no scratch site.

import { existsSync, linkSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, statSync, unlinkSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";
import { VIEWER } from "./mobile/stage.mjs";
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
const TITLE = process.env.MOVIEBOT_VERIFY_TITLE || "coherence-2013";

function chromePath() {
  if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
  const cache = join(homedir(), ".cache", "ms-playwright");
  for (const entry of readdirSync(cache).filter((d) => d.startsWith("chromium-")).sort().reverse()) {
    const candidate = join(cache, entry, "chrome-linux64", "chrome");
    if (existsSync(candidate)) return candidate;
  }
  throw new Error("No Chromium found. Set CHROME_PATH to a Chrome or Chromium binary.");
}

let failed = 0;
const check = (ok, line) => { if (!ok) failed++; console.log(`${ok ? "✓" : "✗"} ${line}`); };
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const room = (what) => `verify-stalls-${what}-${Date.now().toString(36)}`;
const playlist = (url) => url.includes(`/media/${TITLE}/`) && /\/index\.m3u8/.test(url);
const segment = (url) => url.includes(`/media/${TITLE}/`) && /\.m4s/.test(url);

const browser = await chromium.launch({
  executablePath: chromePath(),
  args: ["--autoplay-policy=no-user-gesture-required", "--mute-audio"],
});

/** A signed-in context keeping the film ahead on the device (Dynamic), with every request recorded. */
async function signedIn(init, person = VIEWER) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 } });
  if (init) await context.addInitScript(init);
  const planted = issuer.planted(person);
  const page = await context.newPage();
  await page.goto(base + "/manifest.webmanifest");
  await page.evaluate(({ key, value }) => {
    localStorage.setItem(key, value);
    localStorage.setItem("cinema.bufferAhead", "dynamic");
  }, planted);
  const requests = [];
  context.on("request", (r) => requests.push({ url: r.url(), at: Date.now() }));
  return { context, page, requests };
}

const metadata = (page, timeout) => page.waitForFunction(() => document.querySelector("video")?.readyState >= 1, null, { timeout })
  .then(() => true, () => false);

// ── A private file system that never answers ─────────────────────────────────────────────────────

{
  const { context, page, requests } = await signedIn(() => {
    navigator.storage.getDirectory = () => new Promise(() => {});
  });
  const started = Date.now();
  await page.goto(`${base}/r/${room("opfs")}?title=${TITLE}`, { waitUntil: "domcontentloaded" });
  const loaded = await metadata(page, 20000);
  check(loaded, `a film starts with a private file system that never answers (${((Date.now() - started) / 1000).toFixed(1)}s)`);
  check(requests.some((r) => segment(r.url)), "and streams its segments from the server");
  await context.close();
}

// ── Playlists missing for a while ────────────────────────────────────────────────────────────────

{
  const { context, page, requests } = await signedIn();
  const missingMs = 15000;
  let missing = true;
  await context.route((url) => playlist(url.href), (route) => (missing
    ? route.fulfill({ status: 404, body: "" })
    : route.continue()));
  const started = Date.now();
  await page.goto(`${base}/r/${room("missing")}?title=${TITLE}`, { waitUntil: "domcontentloaded" });
  await wait(missingMs);
  missing = false;
  const asked = requests.filter((r) => playlist(r.url) && r.at - started < missingMs).length;
  check(asked < 40, `missing playlists for ${missingMs / 1000}s are asked for ${asked} times, not hundreds a second`);
  const loaded = await metadata(page, 45000);
  check(loaded, `and the film starts once they are back (${((Date.now() - started - missingMs) / 1000).toFixed(1)}s after)`);
  await context.close();
}

// ── A transcode begun again from nothing ─────────────────────────────────────────────────────────

{
  const source = [join(dir, "library", "media", TITLE), join(dir, "library", "cold", TITLE)].find((d) => existsSync(d));
  const RESTARTED = `${TITLE}-restarted`;
  const copy = join(dir, "library", "media", RESTARTED);
  const manifest = JSON.parse(readFileSync(join(source, "manifest.json"), "utf8"));
  const renditions = [...manifest.video.renditions, ...manifest.audio].map((r) => r.uri.split("/")[0]);
  const whole = Object.fromEntries(renditions.map((r) => [r, readFileSync(join(source, r, "index.m3u8"), "utf8")]));
  /** A playlist cut to its first `keep` segments, unended, as a transcode part way through writes it. */
  const cut = (text, keep) => {
    const out = [];
    let segments = 0;
    for (const line of text.split("\n")) {
      if (line === "#EXT-X-ENDLIST") continue;
      if (line && !line.startsWith("#") && segments++ >= keep) break;
      out.push(line);
    }
    while (out.length && out[out.length - 1].startsWith("#EXTINF")) out.pop();
    return out.join("\n").replace("#EXT-X-PLAYLIST-TYPE:VOD", "#EXT-X-PLAYLIST-TYPE:EVENT") + "\n";
  };
  const atomic = (path, text) => { writeFileSync(path + ".tmp", text); renameSync(path + ".tmp", path); };
  const link = (from, to) => {
    mkdirSync(to, { recursive: true });
    for (const entry of readdirSync(from)) {
      const path = join(from, entry);
      if (statSync(path).isDirectory()) link(path, join(to, entry));
      else if (!/^(index\.m3u8|manifest\.json)$/.test(entry)) linkSync(path, join(to, entry));
    }
  };
  /** The transcode `keep` segments in, two seconds each, as the hand-off writes it. */
  const written = (keep) => {
    for (const r of renditions) atomic(join(copy, r, "index.m3u8"), cut(whole[r], keep));
    const head = keep * 2;
    atomic(join(copy, "manifest.json"), JSON.stringify({
      ...manifest, id: RESTARTED, film: undefined, status: "transcoding", headSeconds: head, audioHeadSeconds: head,
      video: { ...manifest.video, renditions: manifest.video.renditions.map((r) => ({ ...r, headSeconds: head })) },
    }));
  };

  rmSync(copy, { recursive: true, force: true });
  link(source, copy);
  written(60);
  await wait(11000); // the library's listing is rebuilt every ten seconds

  const name = room("restart");
  const join_ = async (person) => {
    const viewer = await signedIn(undefined, person);
    await viewer.page.goto(`${base}/r/${name}?title=${RESTARTED}`, { waitUntil: "domcontentloaded" });
    return viewer;
  };
  const playhead = (viewer) => viewer.page.evaluate(() => {
    const video = document.querySelector("video");
    return video ? { at: video.currentTime, ready: video.readyState } : { at: 0, ready: 0 };
  });

  const before = await join_({ id: "verify-stalls-before", name: "Ada" });
  await metadata(before.page, 30000);
  await wait(2000);

  // The hand-off starting the film again: every playlist gone, the head at nothing, then the film
  // written afresh at the pace a transcode runs.
  for (const r of renditions) unlinkSync(join(copy, r, "index.m3u8"));
  atomic(join(copy, "manifest.json"), JSON.stringify({ ...JSON.parse(readFileSync(join(copy, "manifest.json"), "utf8")), headSeconds: 0, audioHeadSeconds: 0 }));
  let keep = 0;
  let writing = true;
  const transcode = (async () => {
    await wait(2500);
    while (writing) { keep += 7; written(keep); await wait(2000); }
  })();
  await wait(1000);
  const during = await join_({ id: "verify-stalls-during", name: "Sushi" });
  await wait(15000);
  await before.page.locator(".mp-play").first().click();
  await wait(25000);
  const [first, second] = [await playhead(before), await playhead(during)];
  check(first.ready >= 3 && first.at > 10, `a viewer in the room as the transcode starts again plays on with it (${first.at.toFixed(1)}s)`);
  check(second.ready >= 3 && second.at > 10, `and one who opened the film while its playlists were gone plays too (${second.at.toFixed(1)}s)`);
  check(Math.abs(first.at - second.at) < 2, `together (${Math.abs(first.at - second.at).toFixed(1)}s apart)`);
  writing = false;
  await transcode;
  await before.context.close();
  await during.context.close();
  rmSync(copy, { recursive: true, force: true });
}

await browser.close();
process.exit(failed > 0 ? 1 : 0);
