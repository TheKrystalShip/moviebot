import { existsSync, readdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { chromium } from 'playwright-core';

export const BASE = process.env.MOVIEBOT_WEB ?? 'http://localhost:5173';
export const DURATION_SECONDS = 43.501;
/** The fixture film. Named at launch, because there is no library to pick from. */
export const TITLE_ID = process.env.MOVIEBOT_TITLE ?? 'clip';

/**
 * A browser that decodes H.264 and AAC. Playwright's own download is used when it is there;
 * anything else is named through CHROME_PATH, because a browser without the proprietary
 * decoders reports every one of these checks as a playback failure.
 */
export function chromePath() {
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

export function launch() {
  return chromium.launch({
    executablePath: chromePath(),
    args: ['--autoplay-policy=no-user-gesture-required', '--mute-audio']
  });
}

export class Checks {
  constructor(suite) {
    this.suite = suite;
    this.results = [];
    console.log(`\n${suite}`);
  }

  add(name, ok, detail = '') {
    this.results.push({ name, ok });
    console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  }

  get failed() {
    return this.results.filter((r) => !r.ok).length;
  }
}

export const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * A viewer. Outbound hub frames are recorded so a check can assert what did and did not go on
 * the wire, and inbound frames are kept so one can be handed back to prove it gets discarded.
 */
export async function openViewer(browser, name, session, title = null) {
  const context = await browser.newContext();

  // The launch link carries no identity, so a viewer is seeded the way a person who has already
  // given their name is: the stored entry is what the player reads.
  await context.addInitScript((viewer) => {
    window.localStorage.setItem('moviebot.identity.v1', JSON.stringify(viewer));
  }, { userId: name, displayName: name });

  await context.addInitScript(() => {
    window.__sent = [];
    const send = WebSocket.prototype.send;
    WebSocket.prototype.send = function (data) {
      try { window.__sent.push(String(data)); } catch { /* binary frame */ }
      return send.call(this, data);
    };

    window.__frames = [];
    const onmessage = Object.getOwnPropertyDescriptor(WebSocket.prototype, 'onmessage');
    Object.defineProperty(WebSocket.prototype, 'onmessage', {
      get: onmessage.get,
      set(handler) {
        window.__replay = (data) => handler.call(this, { data });
        onmessage.set.call(this, (event) => {
          window.__frames.push(event.data);
          handler.call(this, event);
        });
      }
    });
  });

  const page = await context.newPage();
  page.on('pageerror', (error) => console.log(`    [${name}] ${error.message}`));

  // MOVIEBOT_WEB may carry query parameters of its own, such as an api override.
  const url = new URL(BASE);
  if (session !== null) url.searchParams.set('session', session);
  if (title !== null) url.searchParams.set('title', title);
  await page.goto(url.toString());
  await page.waitForSelector('#connection[data-status="connected"]', { timeout: 15000 });
  return page;
}

/** The control bar fades while playing, exactly as it does for a person who stops moving. */
export async function wake(page) {
  const box = await page.locator('.video-js').boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.move(box.x + box.width / 2 + 6, box.y + box.height / 2 + 4);
  await page.waitForTimeout(200);
}

/**
 * Waits for the film the launch named to be in the media element. There is no library to click:
 * a film is chosen with the slash command, and the room is told before anyone opens it.
 */
export async function loadFirstTitle(page) {
  await page.waitForFunction(() => document.querySelector('video')?.readyState >= 1, null, { timeout: 20000 });
}

export async function scrubTo(page, seconds) {
  await wake(page);
  const box = await page.locator('.mb-scrub__track').boundingBox();
  await page.mouse.move(box.x + box.width * (seconds / DURATION_SECONDS), box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.up();
}

export const playhead = (page) => page.evaluate(() => {
  const video = document.querySelector('video');
  return {
    seconds: video.currentTime,
    paused: video.paused,
    rate: video.playbackRate,
    readyState: video.readyState,
    width: video.videoWidth,
    height: video.videoHeight
  };
});

/**
 * The cog, open on its root list. Everything a viewer sets for themselves is a row behind it.
 *
 * A menu already open is closed and opened again rather than navigated out of: it always reopens
 * at the top, so that is one step where finding the way back is several and depends on where an
 * earlier check left it.
 */
export async function openSettings(page) {
  await wake(page);
  const popup = page.locator('.mb-settings .mb-menu__popup');
  const button = page.locator('.mb-settings__button');
  if (await popup.isVisible()) await button.click();
  await button.click();
  await popup.waitFor({ state: 'visible' });
}

/** The row in the root list that opens one panel, found by the name it carries. */
export const settingsEntry = (page, name) =>
  page.locator('.mb-settings__entry').filter({ has: page.getByText(name, { exact: true }) });

/**
 * What one panel behind the cog holds, and the value shown beside its name in the root list.
 *
 * The menu is closed again on the way out: a panel left open covers the film the next check is
 * about to look at, and the menu always reopens at the top anyway.
 */
export async function menu(page, name) {
  await openSettings(page);
  const entry = settingsEntry(page, name);
  const value = (await entry.locator('.mb-settings__value').textContent()) ?? '';
  await entry.click();

  const contents = await page.evaluate(() => {
    const popup = document.querySelector('.mb-settings .mb-menu__popup');
    const items = [...popup.querySelectorAll('.mb-menu__item')];
    // An unavailable row carries its reason on the mark rather than in it: the glyph is what is
    // seen and the words are what it means, which is what a pointer and a screen reader both read.
    const detail = (item) => {
      const mark = item.querySelector('.mb-menu__item-detail');
      return mark === null ? undefined : (mark.getAttribute('aria-label') ?? mark.textContent);
    };
    return {
      groups: [...popup.querySelectorAll('.mb-menu__heading')].map((e) => e.textContent),
      labels: items.map((e) => e.querySelector('.mb-menu__item-label').textContent),
      disabled: items.filter((e) => e.disabled).length,
      reasons: [...new Set(items.filter((e) => e.disabled).map(detail))],
      total: items.length
    };
  });

  await page.locator('.mb-settings__button').click();
  return { value, ...contents };
}

/** Opens one panel and leaves it open, for a check that drives the controls inside it. */
export async function openPanel(page, name) {
  await openSettings(page);
  await settingsEntry(page, name).click();
  return page.locator('.mb-settings .mb-menu__popup');
}

export const hubTargets = (page) => page.evaluate(() =>
  [...new Set(window.__sent.join('\n').match(/"target":"(\w+)"/g) ?? [])].map((s) => s.slice(10, -1)));
