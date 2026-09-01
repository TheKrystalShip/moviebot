import { Checks, TITLE_ID, hubTargets, openViewer, playhead, scrubTo, wait, wake } from './harness.mjs';

/** Two viewers on one timeline: what each does to the other, and what neither sends back. */
export async function roomSuite(browser) {
  const checks = new Checks('Two viewers');
  const session = 'verify-room-' + Date.now();
  const alice = await openViewer(browser, 'alice', session, TITLE_ID);
  const bob = await openViewer(browser, 'bob', session);

  await Promise.all([alice, bob].map((page) =>
    page.waitForFunction(() => document.querySelector('video')?.readyState >= 1, null, { timeout: 20000 })));
  checks.add('a title loaded by one viewer loads for the other', true);
  checks.add('the other viewer is told who loaded it',
    (await bob.textContent('#actor')).includes('alice'), await bob.textContent('#actor'));

  await wake(alice);
  await alice.click('.vjs-play-control');
  await bob.waitForFunction(() => !document.querySelector('video').paused, null, { timeout: 10000 });
  checks.add('play by one viewer starts the other', true);
  checks.add('and says who started it',
    (await bob.textContent('#actor')) === 'alice started playback', await bob.textContent('#actor'));

  await wait(4000);
  let [a, b] = await Promise.all([playhead(alice), playhead(bob)]);
  checks.add('the two playheads agree', Math.abs(a.seconds - b.seconds) < 0.5,
    `alice ${a.seconds.toFixed(2)}s, bob ${b.seconds.toFixed(2)}s`);

  await wake(bob);
  await bob.click('.vjs-play-control');
  await alice.waitForFunction(() => document.querySelector('video').paused, null, { timeout: 10000 });
  checks.add('pause by the other viewer stops the first', true);
  checks.add('and says who paused',
    (await alice.textContent('#actor')) === 'bob paused', await alice.textContent('#actor'));

  await scrubTo(bob, 30);
  await alice.waitForFunction(() => Math.abs(document.querySelector('video').currentTime - 30) < 1.5, null, { timeout: 10000 });
  await bob.waitForFunction(() => Math.abs(document.querySelector('video').currentTime - 30) < 1.5, null, { timeout: 10000 });
  checks.add('a seek by one viewer moves the other', true);

  await scrubTo(alice, 5);
  await bob.waitForFunction(() => Math.abs(document.querySelector('video').currentTime - 5) < 1.5, null, { timeout: 10000 });
  checks.add('and a seek back from the other direction', true);

  const followed = await hubTargets(bob);
  checks.add('the follower never echoes an intent back',
    !followed.includes('Play') && !followed.includes('LoadTitle'), `bob sent: ${followed.join(', ')}`);
  const drove = await hubTargets(alice);
  checks.add('the driver sent exactly what it was asked for',
    drove.includes('LoadTitle') && drove.includes('Play') && drove.includes('Seek'), `alice sent: ${drove.join(', ')}`);

  // A viewer whose decoder stops for a moment, as a stall does. Nothing is published: the room
  // must keep going and the viewer must find its own way back.
  await wake(alice);
  await alice.click('.vjs-play-control');
  await bob.waitForFunction(() => !document.querySelector('video').paused, null, { timeout: 10000 });
  await alice.evaluate(() => {
    window.__paused = false;
    document.querySelector('video').addEventListener('pause', () => { window.__paused = true; });
  });
  await bob.evaluate(() => {
    window.__seeks = 0;
    document.querySelector('video').addEventListener('seeked', () => { window.__seeks += 1; });
  });
  await wait(2000);

  // A second lost, then playing normally again: inside the band the client trims the rate.
  await bob.evaluate(() => { window.__seeks = 0; document.querySelector('video').playbackRate = 0; });
  await wait(1000);
  await bob.evaluate(() => { document.querySelector('video').playbackRate = 1; });
  let nudged = true;
  await bob.waitForFunction(() => document.querySelector('video').playbackRate === 1.02, null, { timeout: 5000 })
    .catch(() => { nudged = false; });
  checks.add('a small gap is closed by nudging the rate, not by seeking',
    nudged && (await bob.evaluate(() => window.__seeks)) === 0,
    `rate ${(await playhead(bob)).rate}, ${await bob.evaluate(() => window.__seeks)} seek(s)`);

  await bob.evaluate(() => { window.__seeks = 0; document.querySelector('video').playbackRate = 0; });
  await wait(4500);
  [a, b] = await Promise.all([playhead(alice), playhead(bob)]);
  const seeks = await bob.evaluate(() => window.__seeks);
  checks.add('a large gap is closed by a seek', seeks > 0 && Math.abs(a.seconds - b.seconds) < 1.0,
    `gap ${(a.seconds - b.seconds).toFixed(2)}s after ${seeks} seek(s)`);
  checks.add('the room never paused for the viewer that fell behind',
    (await alice.evaluate(() => window.__paused)) === false);
  checks.add('falling behind published nothing to the room',
    !(await hubTargets(bob)).includes('Play'), `bob sent: ${(await hubTargets(bob)).join(', ')}`);

  await alice.context().close();
  await bob.context().close();
  return checks;
}
