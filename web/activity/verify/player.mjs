import { Checks, DURATION_SECONDS, TITLE_ID, loadFirstTitle, menu, hubTargets, openPanel, openViewer, playhead, scrubTo, wait, wake } from './harness.mjs';

/** One viewer: the menus the manifest describes, playback, and what stays local. */
export async function playerSuite(browser) {
  const checks = new Checks('One viewer');
  const media = [];
  const page = await openViewer(browser, 'alice', 'verify-player-' + Date.now(), TITLE_ID);
  page.on('request', (r) => { if (r.url().includes('/media/')) media.push(r.url()); });


  await loadFirstTitle(page);
  checks.add('the chosen title loads into the media element', true);

  const audio = await menu(page, 'Audio');
  checks.add('the audio menu splits feature from commentary',
    audio.groups.join('|') === 'Feature|Commentary', audio.groups.join('|'));
  checks.add('audio labels are the manifest labels',
    audio.labels[0] === 'English' && audio.labels[1].startsWith('Commentary with director Ridley Scott'),
    audio.labels.join(' / '));

  const subtitles = await menu(page, 'Subtitles');
  checks.add('the subtitle menu lists Off plus both groups',
    subtitles.labels[0] === 'Off' && subtitles.groups.join('|') === 'Feature|Commentary');
  checks.add('every manifest subtitle is listed', subtitles.total === 49, `${subtitles.total} rows including Off`);
  checks.add('bitmap tracks are shown disabled with their reason',
    subtitles.disabled === 16 && subtitles.reasons.join() === 'picture-based, needs OCR',
    `${subtitles.disabled} disabled, reason "${subtitles.reasons.join()}"`);

  await page.click('.vjs-play-control');
  await wait(3000);
  const playing = await playhead(page);
  checks.add('the video actually plays',
    playing.seconds > 1 && !playing.paused && playing.readyState >= 3, JSON.stringify(playing));
  checks.add('it decodes at the manifest resolution',
    playing.width === 1920 && playing.height === 800, `${playing.width}x${playing.height}`);
  checks.add('video and audio segments were both fetched',
    media.some((u) => u.includes('/v0/seg')) && media.some((u) => u.includes('/a0/seg')));

  media.length = 0;
  const audioPanel = await openPanel(page, 'Audio');
  await audioPanel.locator('.mb-menu__item', { hasText: 'Commentary with director' }).click();
  await wait(2500);
  checks.add('choosing the commentary loads the a1 rendition', media.some((u) => u.includes('/a1/')));
  checks.add('the audio menu shows the chosen track',
    (await menu(page, 'Audio')).value.startsWith('Commentary with director'));

  const subtitlePanel = await openPanel(page, 'Subtitles');
  await subtitlePanel.locator('.mb-menu__item', { hasText: 'English SDH' }).click();
  await wait(500);
  checks.add('the chosen subtitle file is fetched', media.some((u) => u.endsWith('s4.vtt')));

  const cues = await page.evaluate(() => {
    const tracks = document.querySelector('.video-js').player.textTracks();
    return { count: tracks.length, mode: tracks[0]?.mode, cues: tracks[0]?.cues?.length ?? 0 };
  });
  checks.add('its cues are parsed and showing',
    cues.count === 1 && cues.mode === 'showing' && cues.cues > 0, JSON.stringify(cues));

  await scrubTo(page, 8.8);
  await wait(1500);
  const afterSeek = await playhead(page);
  checks.add('a scrub-bar click seeks the film',
    Math.abs(afterSeek.seconds - 8.8) < 2, `landed at ${afterSeek.seconds.toFixed(2)}s`);

  await wake(page);
  await page.click('.vjs-play-control');
  await page.waitForFunction(() => document.querySelector('video').paused, null, { timeout: 5000 });
  await scrubTo(page, 21);
  await wait(1200);
  const shown = await page.textContent('.vjs-text-track-display');
  checks.add('the subtitle cue renders on screen', /CROWD EXCLAIMS/.test(shown ?? ''), JSON.stringify(shown));

  // How subtitles look, asserted against the cue on screen rather than against the setting that
  // asked for it. The library draws cues itself and writes their appearance onto the elements,
  // so the cue is the only place an answer exists.
  const cue = () => page.evaluate(() => {
    const box = document.querySelector('.vjs-text-track-cue');
    const text = box?.firstElementChild;
    if (!text) return null;
    const style = getComputedStyle(text);
    return {
      color: style.color,
      background: style.backgroundColor,
      size: parseFloat(style.fontSize),
      stroke: parseFloat(style.webkitTextStrokeWidth),
      transform: box.style.transform
    };
  });

  const plain = await cue();
  checks.add('the cue is the library\'s own, which is what a style can be written onto',
    plain !== null && plain.color === 'rgb(255, 255, 255)' && plain.background === 'rgba(0, 0, 0, 0.8)',
    JSON.stringify(plain));

  const style = await openPanel(page, 'Subtitle style');
  await style.locator('.mb-style__swatch[aria-label="Yellow"]').click();
  await style.locator('.mb-style__option', { hasText: 'Outline' }).click();
  await style.locator('.mb-style__row', { hasText: 'Size' }).locator('input').fill('1.5');
  await style.locator('.mb-style__row', { hasText: 'Position' }).locator('input').fill('0.1');
  await wait(400);

  const styled = await cue();
  checks.add('a colour chosen in the panel reaches the cue over the film',
    styled?.color === 'rgb(242, 213, 74)', styled?.color);
  checks.add('an outline is drawn as a stroke under the glyph', styled?.stroke > 0, `${styled?.stroke}px`);
  checks.add('size multiplies the size the frame gave the cue',
    Math.abs(styled.size - plain.size * 1.5) < 0.8, `${styled?.size}px against ${plain?.size}px`);
  checks.add('position raises the cue off the bottom', /translateY\(-\d/.test(styled?.transform ?? ''),
    styled?.transform);

  const keptStyle = await page.evaluate(() =>
    JSON.parse(localStorage.getItem('moviebot.prefs.v1')).subtitleStyle);
  checks.add('how subtitles look is kept once for this browser, not per title',
    keptStyle.textColor === '#f2d54a' && keptStyle.scale === 1.5 && keptStyle.edge === 'outline',
    JSON.stringify(keptStyle));

  const at = await playhead(page);
  const bar = await page.evaluate(() => ({
    total: document.querySelector('.mb-scrub__time--total').textContent,
    ready: document.querySelector('.mb-scrub__ready').style.width,
    played: parseFloat(document.querySelector('.mb-scrub__played').style.width)
  }));
  checks.add('the bar is drawn at the manifest duration',
    bar.total === '0:43' && Math.abs(bar.played - (at.seconds / DURATION_SECONDS) * 100) < 4, JSON.stringify(bar));
  checks.add('a ready title shades the whole bar', bar.ready === '100%', bar.ready);

  const stored = await page.evaluate(() => JSON.parse(localStorage.getItem('moviebot.prefs.v1')));
  checks.add('track choices are kept in localStorage',
    stored.byTitle.clip.audioTrackId === 'a1' && stored.byTitle.clip.subtitleTrackId === 's4',
    JSON.stringify(stored.byTitle.clip));

  const sent = await page.evaluate(() => window.__sent.join('\n'));
  checks.add('only timeline intent goes on the wire',
    (await hubTargets(page)).every((t) => ['Join', 'LoadTitle', 'Play', 'Pause', 'Seek', 'ServerTime'].includes(t)),
    (await hubTargets(page)).join(', '));
  checks.add('no preference appears in any hub frame', !/volume|subtitle|audioTrack/i.test(sent));

  await page.reload();
  await page.waitForFunction(() => document.querySelector('video')?.readyState >= 1, null, { timeout: 20000 });
  await wait(1000);
  const restored = {
    audio: (await menu(page, 'Audio')).value,
    subtitles: (await menu(page, 'Subtitles')).value,
    style: (await menu(page, 'Subtitle style')).value
  };
  checks.add('a reload restores this viewer\'s own tracks',
    restored.audio.startsWith('Commentary') && restored.subtitles === 'English SDH', JSON.stringify(restored));
  checks.add('and the way they are drawn', restored.style === 'Custom', restored.style);

  // A push older than the last applied carries nothing new and must not move the playhead.
  await scrubTo(page, 5);
  await wait(1200);
  const stale = await page.evaluate(() => window.__frames.filter((f) => f.includes('"StateChanged"')).at(-1));
  await scrubTo(page, 35);
  await wait(1200);
  const before = (await playhead(page)).seconds;
  await page.evaluate((frame) => window.__replay(frame), stale);
  await wait(800);
  const after = (await playhead(page)).seconds;
  checks.add('a push that arrives late is discarded',
    Math.abs(after - before) < 0.5 && Math.abs(after - 35) < 2,
    `${before.toFixed(2)}s then ${after.toFixed(2)}s after replaying an older push`);

  await page.context().close();
  return checks;
}
