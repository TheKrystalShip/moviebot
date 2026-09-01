import { BASE, Checks, hubTargets, openViewer, playhead, scrubTo, wait } from './harness.mjs';

/** The launch link: the room it names, the title it names, and the identity it does not carry. */
export async function launchSuite(browser) {
  const checks = new Checks('The launch link');
  const session = 'verify-launch-' + Date.now();

  // session and title together: the film is playing without anybody touching the library.
  const invited = await openViewer(browser, 'alice', session, 'clip');
  await invited.waitForFunction(() => document.querySelector('video')?.readyState >= 1, null, { timeout: 20000 });
  checks.add('a link naming a title loads it without a trip through the library',
    (await invited.textContent('#film-title')).includes('Gladiator'));
  checks.add('and the room was told, once', (await hubTargets(invited)).includes('LoadTitle'));

  await scrubTo(invited, 20);
  await wait(1200);

  // A second person on the same link joins a room that already holds the title and says nothing.
  const joining = await openViewer(browser, 'bob', session, 'clip');
  await joining.waitForFunction(() => document.querySelector('video')?.readyState >= 1, null, { timeout: 20000 });
  await wait(1500);
  checks.add('a later arrival on the same link does not reload the title',
    !(await hubTargets(joining)).includes('LoadTitle'), `bob sent: ${(await hubTargets(joining)).join(', ')}`);
  const [a, b] = await Promise.all([playhead(invited), playhead(joining)]);
  checks.add('so the room keeps its position', Math.abs(a.seconds - 20) < 1.5 && Math.abs(b.seconds - 20) < 1.5,
    `alice ${a.seconds.toFixed(2)}s, bob ${b.seconds.toFixed(2)}s`);

  await invited.context().close();
  await joining.context().close();

  // No session at all: the page names its own and puts it in the address bar to be shared.
  const opened = await openViewer(browser, 'cris', null);
  const generated = await opened.evaluate(() => new URL(location.href).searchParams.get('session'));
  checks.add('a page opened with no room names one and writes it into the address bar',
    typeof generated === 'string' && generated.length > 4, String(generated));
  checks.add('and shows the library rather than a film',
    (await opened.textContent('#film-title')) === 'No film loaded');

  const shared = await openViewer(browser, 'dana', generated);
  await shared.waitForFunction(() => document.querySelectorAll('.participants__item').length === 2, null, { timeout: 10000 });
  checks.add('the written link lands somebody else in the same room', true);
  await opened.context().close();
  await shared.context().close();

  // A first visit carries no stored identity, so the person is asked what to call them.
  const context = await browser.newContext();
  const page = await context.newPage();
  const url = new URL(BASE);
  url.searchParams.set('session', session + '-named');
  await page.goto(url.toString());
  await page.waitForSelector('.name-gate__input', { timeout: 10000 });
  checks.add('a first-time viewer is asked for a name', true);
  await page.fill('.name-gate__input', 'Someone New');
  await page.click('.name-gate__button');
  await page.waitForSelector('#connection[data-status="connected"]', { timeout: 15000 });
  await page.waitForFunction(() => document.querySelectorAll('.participants__item').length === 1, null, { timeout: 10000 });
  checks.add('the name they gave is who the room sees',
    (await page.textContent('.participants__item')) === 'Someone New');

  await page.reload();
  await page.waitForSelector('#connection[data-status="connected"]', { timeout: 15000 });
  checks.add('and they are not asked again', (await page.locator('.name-gate__input').count()) === 0);

  await context.close();
  return checks;
}
