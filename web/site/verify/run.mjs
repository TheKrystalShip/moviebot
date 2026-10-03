// The player, checked in a real browser against a real API: rooms, the controls, the menus, the
// layouts each frame size gets, and a film still transcoding.
//
// Needs a running MovieBot.Api with a library (MOVIEBOT_API, its service key in
// MOVIEBOT_SERVICE_KEY), the site's dev server in front of it (MOVIEBOT_WEB), and a Chromium that
// decodes H.264 and AAC. MOVIEBOT_TITLE names the film to use, which should carry more than one
// audio track and a subtitle track; MOVIEBOT_MEDIA is the API's media root, whose copy of that
// film's manifest is edited for the transcoding checks and put back.

import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join, resolve } from 'node:path';
import { chromium } from 'playwright-core';

const BASE = process.env.MOVIEBOT_WEB ?? 'http://localhost:5175';
const API = process.env.MOVIEBOT_API ?? 'http://127.0.0.1:8099';
const SERVICE_KEY = process.env.MOVIEBOT_SERVICE_KEY ?? '';
const TITLE = process.env.MOVIEBOT_TITLE ?? 'baby-driver-2017';
const MEDIA = process.env.MOVIEBOT_MEDIA ?? resolve(import.meta.dirname, '../../../media');

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

/** A browser that plays without a gesture, and one that waits for one as a person's would. */
const launch = (autoplay = true) => chromium.launch({
  executablePath: chromePath(),
  args: [...(autoplay ? ['--autoplay-policy=no-user-gesture-required'] : []), '--mute-audio'],
});

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
let failed = 0;

function suite(name) {
  console.log(`\n${name}`);
  return (check, ok, detail = '') => {
    if (!ok) failed++;
    console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${check}${detail ? '  — ' + detail : ''}`);
  };
}

async function tokenFor(userId, name) {
  const response = await fetch(`${API}/api/auth/service-token`, {
    method: 'POST',
    headers: { 'X-MovieBot-Service': SERVICE_KEY, 'content-type': 'application/json' },
    body: JSON.stringify({ roomId: '', userId, displayName: name }),
  });
  if (!response.ok) throw new Error(`The API would not mint a token (${response.status}). Is MOVIEBOT_SERVICE_KEY right?`);
  return (await response.json()).roomToken;
}

/** A person signed in to the site, in a frame of a given size. Hub frames they send are recorded. */
async function viewer(browser, userId, name, size = { width: 1440, height: 810 }) {
  const context = await browser.newContext({ viewport: size });
  const token = await tokenFor(userId, name);
  await context.addInitScript(([token, userId, name]) => {
    localStorage.setItem('cinema:session', JSON.stringify({
      token, expiresAt: Math.floor(Date.now() / 1000) + 3600, user: { id: userId, name, username: name.toLowerCase() },
    }));
    window.__sent = [];
    const send = WebSocket.prototype.send;
    WebSocket.prototype.send = function (data) {
      try { window.__sent.push(String(data)); } catch { /* binary */ }
      return send.call(this, data);
    };
  }, [token, userId, name]);
  const page = await context.newPage();
  page.on('pageerror', (e) => console.log(`    [${name}] page error: ${e.message}`));
  return Object.assign(page, { token });
}

const holds = (page) => page.waitForFunction(() => document.querySelector('video')?.readyState >= 2, null, { timeout: 30000 });
const playhead = (page) => page.evaluate(() => ({ seconds: document.querySelector('video').currentTime, paused: document.querySelector('video').paused }));
const sent = (page) => page.evaluate(() => window.__sent.join('\n'));

/** A person reaches for the controls before pressing one: the pointer moves, and they show. */
async function press(page, selector) {
  const { width, height } = page.viewportSize();
  await page.mouse.move(width / 2, height / 3);
  await page.mouse.move(width / 2 + 4, height / 3 + 4);
  await wait(200);
  await page.click(selector);
}

const roomName = (what) => `verify-${what}-${Date.now().toString(36)}`;

// ── A room ──────────────────────────────────────────────────────────────────────────────────

async function roomChecks(browser) {
  const check = suite('A room');
  const room = roomName('room');

  const ada = await viewer(browser, 'v1', 'Ada');
  await ada.goto(`${BASE}/r/${room}?title=${TITLE}`);
  await holds(ada);
  check('a link naming a film loads it', (await ada.textContent('.mp-top__name')).length > 0, await ada.textContent('.mp-top__name'));
  check('and the room was told', (await sent(ada)).includes('"LoadTitle"'));

  const bob = await viewer(browser, 'v2', 'Bob', { width: 640, height: 360 });
  await bob.goto(`${BASE}/r/${room}?title=${TITLE}`);
  await holds(bob);
  await wait(800);
  check('a later arrival on the same link does not load it again', !(await sent(bob)).includes('"LoadTitle"'));

  await press(bob, '.mp-play');
  await ada.waitForSelector('.mp-event', { timeout: 5000 }).catch(() => null);
  const said = await ada.textContent('.mp-event').catch(() => '');
  check('a play by one is announced to the other, with who did it', /Bob/.test(said), said);
  await wait(1500);
  check('and both are playing', !(await playhead(ada)).paused && !(await playhead(bob)).paused);

  await ada.keyboard.press('l');
  await wait(1500);
  const [a, b] = [await playhead(ada), await playhead(bob)];
  check('a key seek moves the whole room', Math.abs(a.seconds - b.seconds) < 1 && a.seconds > 9,
    `ada ${a.seconds.toFixed(2)}s, bob ${b.seconds.toFixed(2)}s`);

  const people = await ada.$$eval('.mp-top .mp-avatar', (els) => els.map((e) => e.title));
  check('everybody in the room is on the roster', people.length === 2, people.join(', '));

  // Bob's element waits for data: past the stall grace, the room sees him marked.
  await bob.evaluate(() => document.querySelector('video').dispatchEvent(new Event('waiting')));
  await ada.waitForSelector('.mp-top .mp-avatar.is-buffering', { timeout: 4000 }).catch(() => null);
  check('a viewer waiting for the film is marked for the others', (await ada.$('.mp-top .mp-avatar.is-buffering')) !== null);
  await bob.evaluate(() => document.querySelector('video').dispatchEvent(new Event('playing')));
  await ada.waitForFunction(() => !document.querySelector('.mp-top .mp-avatar.is-buffering'), null, { timeout: 4000 }).catch(() => null);
  check('and unmarked once it plays again', (await ada.$('.mp-top .mp-avatar.is-buffering')) === null);

  // A browser that wants a gesture before it plays sound. Headless Chromium's own policy is not
  // something a check can count on, so the refusal is made explicit: play() is refused, as a browser
  // refuses it, until the page has had a press.
  const strict = await launch(false);
  try {
    const dee = await viewer(strict, 'v4', 'Dee', { width: 1024, height: 576 });
    await dee.addInitScript(() => {
      let pressed = false;
      window.addEventListener('pointerdown', () => { pressed = true; }, true);
      const play = HTMLMediaElement.prototype.play;
      HTMLMediaElement.prototype.play = function () {
        return pressed ? play.call(this) : Promise.reject(new DOMException('play() needs a gesture', 'NotAllowedError'));
      };
    });
    await dee.goto(`${BASE}/r/${room}`);
    await dee.waitForSelector('.mp-gate', { timeout: 20000 }).catch(() => null);
    const gate = await dee.textContent('.mp-gate').catch(() => '');
    check('a viewer whose browser waits for a press is shown who is watching', /Ada/.test(gate) && /Bob/.test(gate),
      gate.slice(0, 80) || JSON.stringify(await playhead(dee)));
    await dee.click('.mp-gate__join').catch(() => null);
    await wait(2000);
    const d = await playhead(dee);
    check('and Join puts them where the room is', !d.paused && Math.abs(d.seconds - (await playhead(ada)).seconds) < 1.5,
      `${d.seconds.toFixed(2)}s`);
  } finally {
    await strict.close();
  }

  await ada.context().close();
  await bob.context().close();

  const cy = await viewer(browser, 'v3', 'Cy');
  await cy.goto(`${BASE}/r/${roomName('empty')}`);
  await cy.waitForSelector('.mp-message', { timeout: 15000 }).catch(() => null);
  check('an empty room says so', /Nothing playing/.test(await cy.textContent('.mp-message').catch(() => '')));
  await cy.context().close();
}

// ── Menus and preferences ───────────────────────────────────────────────────────────────────

async function menuChecks(browser) {
  const check = suite('Menus, and preferences that follow the person');
  const room = roomName('menus');

  const ada = await viewer(browser, 'v5', 'Ada');
  await ada.goto(`${BASE}/r/${room}?title=${TITLE}`);
  await holds(ada);

  await press(ada, '.mp-menu-button[title="Subtitles"]');
  const rows = await ada.$$eval('.mp-popover .mp-option__label', (els) => els.map((e) => e.textContent));
  check('the subtitle menu opens over the controls, Off first', rows[0] === 'Off' && rows.length > 1, rows.join(' | '));
  const english = await ada.$$('.mp-popover .mp-option[role="menuitemradio"]:not([disabled])');
  await english[english.length - 1].click();
  await ada.waitForFunction(() => [...document.querySelector('video').textTracks].some((t) => t.mode === 'hidden'), null, { timeout: 8000 }).catch(() => null);
  check('choosing a track attaches it hidden, for the page to draw',
    await ada.evaluate(() => [...document.querySelector('video').textTracks].some((t) => t.mode === 'hidden')));
  check('and the button says which', (await ada.textContent('.mp-menu-button[title="Subtitles"]')) !== 'Off');
  await ada.keyboard.press('Escape');

  const audio = await ada.$('.mp-menu-button[title="Audio track"]');
  if (audio) {
    await press(ada, '.mp-menu-button[title="Audio track"]');
    const options = await ada.$$('.mp-popover .mp-option');
    const before = await ada.textContent('.mp-menu-button[title="Audio track"]');
    await options[options.length - 1].click();
    await wait(300);
    const after = await ada.textContent('.mp-menu-button[title="Audio track"]');
    check('choosing an audio track changes what the button says', before !== after, `${before} → ${after}`);
  } else {
    check('the film carries more than one audio track (MOVIEBOT_TITLE)', false);
  }

  check('quality is offered only where there is a choice',
    (await ada.$('.mp-menu-button[title="Quality"]') !== null)
      === (await fetch(`${API}/api/titles/${TITLE}`, { headers: { authorization: `Bearer ${ada.token}` } })
        .then((r) => r.json()).then((m) => m.video.renditions.length > 1)));
  await ada.context().close();

  // What the site keeps for a person is what the next player they open starts with.
  const token = await tokenFor('v6', 'Eve');
  await fetch(`${API}/api/me/preferences`, {
    method: 'PUT', headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
    body: JSON.stringify({ subtitleLanguage: 'en' }),
  });
  const eve = await viewer(browser, 'v6', 'Eve');
  await eve.goto(`${BASE}/r/${roomName('prefs')}?title=${TITLE}`);
  await holds(eve);
  await wait(800);
  const value = await eve.getAttribute('.mp-menu-button[title="Subtitles"]', 'aria-label');
  check('a person whose language is English starts on English subtitles', !/: Off$/.test(value ?? ''), value ?? '');
  await eve.context().close();
}

// ── Frame sizes ─────────────────────────────────────────────────────────────────────────────

async function layoutChecks(browser) {
  const check = suite('Each frame size');
  const room = roomName('sizes');
  for (const [name, size, expected] of [
    ['wide', { width: 1440, height: 810 }, 'wide'],
    ['an Activity at 640×360', { width: 640, height: 360 }, 'compact'],
    ['a phone held upright, 390×844', { width: 390, height: 844 }, 'compact'],
    ['a popped-out 400×225', { width: 400, height: 225 }, 'tiny'],
  ]) {
    const page = await viewer(browser, 'v7', 'Fay', size);
    await page.goto(`${BASE}/r/${room}?title=${TITLE}`);
    await holds(page);
    const got = await page.getAttribute('.mp-frame', 'data-size');
    check(`${name} is laid out ${expected}`, got === expected, got);
    if (expected === 'compact') {
      await press(page, '.mp-menu-button[title="Subtitles"]');
      check('  and its menus come up from the bottom edge', (await page.$('.mp-sheet')) !== null);
    }
    if (expected === 'tiny') {
      check('  and its controls are one row', (await page.$('.mp-tiny')) !== null && (await page.$('.mp-bottom')) === null);
      await press(page, '.mp-tiny .mp-icon');
      const folded = await page.$$eval('.mp-sheet .mp-option__label', (els) => els.map((e) => e.textContent));
      check('  with the rest folded behind the overflow', folded.includes('Subtitles'), folded.join(', '));
    }
    await page.context().close();
  }
}

// ── A film still transcoding ────────────────────────────────────────────────────────────────

async function headChecks(browser) {
  const check = suite('A film still transcoding');
  const path = join(MEDIA, TITLE, 'manifest.json');
  if (!existsSync(path)) {
    check(`the film's manifest is reachable at ${path} (MOVIEBOT_MEDIA)`, false);
    return;
  }
  const original = readFileSync(path, 'utf8');
  try {
    const manifest = JSON.parse(original);
    const head = 60;
    writeFileSync(path, JSON.stringify({ ...manifest, status: 'transcoding', headSeconds: head }, null, 2));

    const page = await viewer(browser, 'v8', 'Gus');
    await page.goto(`${BASE}/r/${roomName('head')}?title=${TITLE}`);
    await holds(page);
    const width = parseFloat(await page.evaluate(() => document.querySelector('.mp-scrub__ready')?.style.width ?? '0'));
    check('the bar shades only what is written', Math.abs(width - (head / manifest.durationSeconds) * 100) < 0.5, `${width}%`);

    // Past the head: refused by the server and explained to whoever asked.
    const bar = await page.locator('.mp-scrub').boundingBox();
    await press(page, '.mp-scrub');
    await page.mouse.click(bar.x + bar.width * 0.5, bar.y + bar.height / 2);
    await page.waitForSelector('.mp-notice', { timeout: 8000 }).catch(() => null);
    const notice = await page.textContent('.mp-notice').catch(() => '');
    check('a seek past it is explained to the viewer who asked', /prepared up to/.test(notice), notice);
    await page.context().close();
  } finally {
    writeFileSync(path, original);
  }
}

console.log(`Krystal Cinema player checks against ${BASE} (API ${API}, film ${TITLE})`);
const browser = await launch();
try {
  await roomChecks(browser);
  await menuChecks(browser);
  await layoutChecks(browser);
  await headChecks(browser);
} finally {
  await browser.close();
}
console.log(failed === 0 ? '\nAll checks passed' : `\n${failed} checks failed`);
process.exit(failed === 0 ? 0 : 1);
