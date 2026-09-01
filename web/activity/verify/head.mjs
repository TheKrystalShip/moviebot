import { readFileSync, writeFileSync } from 'node:fs';
import { Checks, DURATION_SECONDS, openViewer, playhead, scrubTo, wait } from './harness.mjs';

/**
 * A title that is still transcoding: the bar at the film's real length with the written region
 * shaded, and a seek past the head refused by the server and explained to whoever asked.
 *
 * The fixture's manifest is edited in place and put back, so the API sees a transcode in
 * progress without one running.
 */
export async function headSuite(browser, manifestPath) {
  const checks = new Checks('A title still transcoding');
  const original = readFileSync(manifestPath, 'utf8');

  try {
    const manifest = JSON.parse(original);
    writeFileSync(manifestPath, JSON.stringify({ ...manifest, status: 'transcoding', headSeconds: 20.0 }, null, 2));

    const page = await openViewer(browser, 'head', 'verify-head-' + Date.now());

    const meta = await page.textContent('.library__meta');
    checks.add('the library says how much is ready', meta === '0:43 · 0:20 ready', meta);

    await page.click('.library__button');
    await page.waitForFunction(() => document.querySelector('video')?.readyState >= 1, null, { timeout: 20000 });

    const ready = await page.evaluate(() => document.querySelector('.mb-scrub__ready').style.width);
    checks.add('the bar shades only the transcoded region',
      Math.abs(parseFloat(ready) - (20 / DURATION_SECONDS) * 100) < 0.5, ready);
    const total = await page.textContent('.mb-scrub__time--total');
    checks.add('and still spans the whole film', total === '0:43', total);

    // Asked for, not clamped here: the server refuses it and says so to this viewer alone.
    await scrubTo(page, 40);
    await page.waitForSelector('.notice', { timeout: 8000 });
    const notice = await page.textContent('.notice');
    checks.add('the refused seek is explained to the viewer who asked',
      /0:20/.test(notice) && /0:(39|40)/.test(notice) && /0:10/.test(notice), notice);

    await wait(1200);
    const landed = (await playhead(page)).seconds;
    checks.add('and it lands where the server granted', Math.abs(landed - 10) < 1, `${landed.toFixed(2)}s`);

    await scrubTo(page, 15);
    await wait(1200);
    const inside = (await playhead(page)).seconds;
    checks.add('a seek inside the transcoded region is granted', Math.abs(inside - 15) < 1, `${inside.toFixed(2)}s`);

    await page.context().close();
  } finally {
    writeFileSync(manifestPath, original);
  }

  return checks;
}
