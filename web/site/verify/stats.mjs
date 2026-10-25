// The stats panel, checked in a real browser against a real API: hidden until asked for, opened by its
// chord and by Settings, every value a measurement or unknown, the drift graph and its marks, kept open
// across a reload, and Send reaching the API's log once per ten seconds.
//
// Needs what verify/run.mjs needs: a MovieBot.Api with a library (MOVIEBOT_API) serving the site
// (MOVIEBOT_WEB, the API's own address when it serves the build), trusting the checks' identity
// provider (MOVIEBOT_VERIFY_DIR, MOVIEBOT_ISSUER_PORT), and a Chromium that decodes H.264 and AAC.
// MOVIEBOT_API_LOG is the API's log, read for the line a Send writes; without it that check is skipped.
// MOVIEBOT_SHOT_DIR, when set, is where a screenshot of each frame size is put.

import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { chromium } from 'playwright-core';
import { issuerIn } from './issuer.mjs';

const BASE = process.env.MOVIEBOT_WEB ?? 'http://localhost:5175';
const ISSUER = issuerIn(
  process.env.MOVIEBOT_VERIFY_DIR ?? join(homedir(), '.cache', 'moviebot-verify'),
  Number(process.env.MOVIEBOT_ISSUER_PORT ?? 8300));
const TITLE = process.env.MOVIEBOT_TITLE ?? 'coherence-2013';
const API_LOG = process.env.MOVIEBOT_API_LOG ?? null;
const SHOTS = process.env.MOVIEBOT_SHOT_DIR ?? null;
/** How long the panel shows Send's answer before its button reads Send again. */
const SentShownMs = 3000;

function chromePath() {
  if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
  const cache = join(homedir(), '.cache', 'ms-playwright');
  if (existsSync(cache)) {
    for (const entry of readdirSync(cache).filter((d) => d.startsWith('chromium-')).sort().reverse()) {
      const candidate = join(cache, entry, 'chrome-linux64', 'chrome');
      if (existsSync(candidate)) return candidate;
    }
  }
  throw new Error('No Chromium found. Set CHROME_PATH to a Chrome or Chromium binary.');
}

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
let failed = 0;
const check = (name, ok, detail = '') => {
  if (!ok) failed++;
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
};

const browser = await chromium.launch({
  executablePath: chromePath(),
  args: ['--autoplay-policy=no-user-gesture-required', '--mute-audio'],
});

async function viewer(userId, name, size, userAgent = undefined) {
  const context = await browser.newContext({ viewport: size, userAgent });
  await context.addInitScript(([{ key, value }]) => localStorage.setItem(key, value), [ISSUER.planted({ id: userId, name })]);
  const page = await context.newPage();
  page.on('pageerror', (e) => console.log(`    [${name}] page error: ${e.message}`));
  return page;
}

const holds = (page) => page.waitForFunction(() => document.querySelector('video')?.readyState >= 2, null, { timeout: 30000 });
const playing = (page) => page.waitForFunction(() => {
  const video = document.querySelector('video');
  return video && video.readyState >= 3 && !video.paused;
}, null, { timeout: 30000 });

/** A person reaches for the controls before pressing one: the pointer moves, and they show. */
async function press(page, selector) {
  const { width, height } = page.viewportSize();
  await page.mouse.move(width / 2, height / 3);
  await page.mouse.move(width / 2 + 4, height / 3 + 4);
  await wait(200);
  await page.click(selector);
}

/** Every row of the panel, label to value. */
const rows = (page) => page.$$eval('.mp-stats dt', (dts) => Object.fromEntries(dts.map((dt) => [dt.textContent, dt.nextElementSibling?.textContent ?? ''])));

const chord = (page) => page.keyboard.press('Control+Alt+Shift+KeyD');

console.log('\nThe stats panel');
const room = `verify-stats-${Date.now().toString(36)}`;
const ada = await viewer('s1', 'Ada', { width: 1440, height: 810 });
await ada.goto(`${BASE}/r/${room}?title=${TITLE}`);
await holds(ada);
// A room a film was just put into waits paused for somebody to start it.
await press(ada, '.mp-play');
await playing(ada);

check('it is hidden until asked for', (await ada.$('.mp-stats')) === null);

await chord(ada);
await ada.waitForSelector('.mp-stats', { timeout: 3000 }).catch(() => null);
check('Ctrl+Alt+Shift+D opens it', (await ada.$('.mp-stats')) !== null);

// Long enough for a few of the sync controller's checks and a second of frames.
await wait(7000);
let values = await rows(ada);
const empty = Object.entries(values).filter(([, value]) => value.trim() === '');
check('every row says something', empty.length === 0, empty.map(([label]) => label).join(', '));
const nan = Object.entries(values).filter(([, value]) => /NaN|undefined|null|Infinity/.test(value));
check('and nothing says NaN, undefined, null or Infinity', nan.length === 0, nan.map(([l, v]) => `${l}: ${v}`).join('; '));

const offset = values['Picture ahead of sound'];
const offsetMs = Number(/^([+−]?)(\d+) ms$/.exec(offset)?.slice(1).join('').replace('−', '-'));
check('the picture is measured against the sound', Number.isFinite(offsetMs), offset);
check('and a browser playing normally keeps them within a frame or two', Math.abs(offsetMs) < 100, offset);
console.log(`    output latency: ${values['Output latency']}; sound behind picture: ${values['Sound behind picture']}`);

check('the room row says where this player is', /level|behind|ahead/.test(values.Room), values.Room);
check('the server clock is estimated', /round trip \d+ ms, \d+ samples?$/.test(values['Server clock']), values['Server clock']);
check('the picture and the sound are each buffered ahead', /picture \d+\.\d s, sound \d+\.\d s/.test(values.Ahead), values.Ahead);
check('a fetch was timed', /first byte \d+ ms, done \d+ ms/.test(values['Last fetch']), values['Last fetch']);
check('the rung and its codec are named', /^\d+p, wanted \d+p, \d+×\d+, avc1/.test(values.Picture), values.Picture);
check('frames are counted', /^\d[\d,]* of \d[\d,]*$/.test(values['Frames dropped']), values['Frames dropped']);
check('the drift graph draws the checks', (await ada.$('.mp-stats__line')) !== null);

// Pull the playhead well behind the room behind the controller's back: the next check seeks it back,
// and the graph marks the seek.
await ada.evaluate(() => { const v = document.querySelector('video'); v.currentTime = Math.max(0, v.currentTime - 6); });
await ada.waitForSelector('.mp-stats__mark.is-seek', { timeout: 8000 }).catch(() => null);
check('a correcting seek is marked on the graph', (await ada.$('.mp-stats__mark.is-seek')) !== null);

// Kept open across a reload, which is what somebody trying again does.
await ada.reload();
await playing(ada);
check('a reload brings it back open', (await ada.$('.mp-stats')) !== null);
// A few of the controller's checks, for the graph a Send carries.
await wait(5000);

// Send, and the limit.
const logBefore = API_LOG ? readFileSync(API_LOG, 'utf8').length : 0;
await ada.click('.mp-stats__action:has-text("Send")');
await ada.waitForFunction(() => /Sent|Not sent/.test(document.querySelector('.mp-stats__actions')?.textContent ?? ''), null, { timeout: 5000 }).catch(() => null);
check('Send is answered Sent', /Sent/.test(await ada.textContent('.mp-stats__actions')) && !/Not sent/.test(await ada.textContent('.mp-stats__actions')),
  await ada.textContent('.mp-stats__actions'));
if (API_LOG) {
  await wait(500);
  const written = readFileSync(API_LOG, 'utf8').slice(logBefore);
  const line = written.split('\n').find((l) => l.includes('sent its playback stats'));
  check('and the API wrote it, under the viewer\'s name and the film', Boolean(line) && line.includes('Ada') && line.includes(TITLE));
  const json = line ? line.slice(line.indexOf(': {') + 2) : '';
  let parsed = null;
  try { parsed = JSON.parse(json); } catch { /* checked below */ }
  check('as the snapshot, with the drift graph in it', parsed !== null && parsed.history.samples.length > 0, `${json.length} characters`);
}
await wait(SentShownMs);
await ada.click('.mp-stats__action:has-text("Send")');
await ada.waitForFunction(() => /moments ago|Not sent/.test(document.querySelector('.mp-stats__actions')?.textContent ?? ''), null, { timeout: 5000 }).catch(() => null);
check('a second Send within ten seconds is not sent', /moments ago/.test(await ada.textContent('.mp-stats__actions')), await ada.textContent('.mp-stats__actions'));

// Closed with its own button, and opened from Settings.
await ada.click('.mp-stats__close');
check('its close button closes it', (await ada.$('.mp-stats')) === null);
await press(ada, '[aria-label="Settings"]');
await ada.click('[role=menuitem]:has-text("Playback stats")');
check('Settings opens it', (await ada.$('.mp-stats')) !== null);
await chord(ada);
check('and the chord closes it again', (await ada.$('.mp-stats')) === null);

// A compact frame: the panel stays inside it.
const bob = await viewer('s2', 'Bob', { width: 640, height: 360 });
await bob.goto(`${BASE}/r/${room}`);
await playing(bob);
await chord(bob);
await bob.waitForSelector('.mp-stats', { timeout: 3000 }).catch(() => null);
const box = await bob.$eval('.mp-stats', (el) => { const r = el.getBoundingClientRect(); return { left: r.left, top: r.top, right: r.right, bottom: r.bottom }; }).catch(() => null);
check('in a small frame it stays inside the frame', box !== null && box.left >= 0 && box.top >= 0 && box.right <= 640 && box.bottom <= 360,
  JSON.stringify(box));
if (SHOTS) await bob.screenshot({ path: join(SHOTS, 'stats-small.png') });
// A television: reached with the remote's arrows alone, from the play button along the row to Settings.
// The television app's mark in the user agent is what tells the site it is on one.
const tv = await viewer('s3', 'Tv', { width: 960, height: 540 }, 'Mozilla/5.0 (Linux; Android 9) Chrome/120 KrystalCinemaTV');
await tv.goto(`${BASE}/r/${room}`);
// It signs in with the device grant, which the checks' provider approves for the person, by the code
// the screen shows.
const issuerUrl = `http://127.0.0.1:${process.env.MOVIEBOT_ISSUER_PORT ?? 8300}`;
const code = (await tv.locator('.kc-tv-signin__code').textContent({ timeout: 15000 })).trim();
await fetch(`${issuerUrl}/verify/decide?code=${code}&decision=approve&person=s3&name=Tv`, { method: 'POST' });
await holds(tv);
await wait(1500);
// Up brings the controls back with focus on the scrub bar; Down from it goes to the row.
await tv.keyboard.press('ArrowUp');
await wait(300);
await tv.keyboard.press('ArrowDown');
await wait(200);
let reached = false;
for (let i = 0; i < 16 && !reached; i++) {
  reached = await tv.evaluate(() => document.activeElement?.getAttribute('aria-label') === 'Settings');
  if (!reached) { await tv.keyboard.press('ArrowRight'); await wait(120); }
}
check('a remote reaches Settings along the control row', reached);
await tv.keyboard.press('Enter');
await tv.waitForSelector('[role=menuitem]:has-text("Playback stats")', { timeout: 3000 }).catch(() => null);
const row = await tv.evaluate(() => document.activeElement?.textContent ?? '');
check('and Settings opens with Playback stats focused', /Playback stats/.test(row), row);
await tv.keyboard.press('Enter');
await tv.waitForSelector('.mp-stats', { timeout: 3000 }).catch(() => null);
check('which OK opens', (await tv.$('.mp-stats')) !== null);
if (SHOTS) await tv.screenshot({ path: join(SHOTS, 'stats-tv.png') });

if (SHOTS) {
  await chord(ada);
  await ada.waitForSelector('.mp-stats', { timeout: 3000 });
  await wait(1500);
  await ada.screenshot({ path: join(SHOTS, 'stats-wide.png') });
}

await browser.close();
console.log(failed === 0 ? '\nAll checks passed.' : `\n${failed} check(s) failed.`);
process.exit(failed === 0 ? 0 : 1);
