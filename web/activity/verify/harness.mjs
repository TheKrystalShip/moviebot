import { existsSync, readdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { chromium } from 'playwright-core';

export const BASE = process.env.MOVIEBOT_WEB ?? 'http://localhost:5173';
export const DURATION_SECONDS = 43.501;

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
export async function openViewer(browser, name, session) {
  const context = await browser.newContext();
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
  url.searchParams.set('session', session);
  url.searchParams.set('user', name);
  url.searchParams.set('name', name);
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

export async function loadFirstTitle(page) {
  await page.waitForSelector('.library__item');
  await page.click('.library__button');
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

export const menu = (page, index) => page.evaluate((i) => {
  const element = document.querySelectorAll('.mb-menu')[i];
  const items = [...element.querySelectorAll('.mb-menu__item')];
  return {
    value: element.querySelector('.mb-menu__value').textContent,
    groups: [...element.querySelectorAll('.mb-menu__heading')].map((e) => e.textContent),
    labels: items.map((e) => e.querySelector('.mb-menu__item-label').textContent),
    disabled: items.filter((e) => e.disabled).length,
    reasons: [...new Set(items.filter((e) => e.disabled).map((e) => e.querySelector('.mb-menu__item-detail')?.textContent))],
    total: items.length
  };
}, index);

export const hubTargets = (page) => page.evaluate(() =>
  [...new Set(window.__sent.join('\n').match(/"target":"(\w+)"/g) ?? [])].map((s) => s.slice(10, -1)));
