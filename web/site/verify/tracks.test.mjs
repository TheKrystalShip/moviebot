// The tracks a film starts on, and the ladder the phone's Quality drawer lists: pure functions the player
// and the phone's remote both read, so a film nobody chose tracks for reads the same on both.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { startingTracks } from '../src/player/engine/media/tracks.ts';
import { qualityLevelsOf } from '../src/player/engine/media/ladder.ts';

const track = (id, language, extra = {}) => ({ id, kind: 'feature', language, label: id, channels: 2, uri: `${id}.x`, available: true, ...extra });
const film = {
  audio: [track('a0', 'eng', { default: true }), track('a1', 'spa')],
  subtitles: [track('s0', 'eng'), track('s1', 'spa'), track('s2', 'spa', { forced: true }), track('s3', 'jpn', { available: false, uri: undefined })],
  video: { width: 1920, height: 800, renditions: [{ name: '546p', bitrateKbps: 1600.4, width: 1024, height: 546 }, { name: '800p', bitrateKbps: 9000 }] }
};

test('a choice made for the film stands where the film has the track', () => {
  assert.deepEqual(startingTracks(film, { audioTrackId: 'a1', subtitleTrackId: 's1' }, {}), { audioId: 'a1', subtitleId: 's1' });
  assert.deepEqual(startingTracks(film, { subtitleTrackId: null }, { subtitleLanguage: 'es' }), { audioId: 'a0', subtitleId: null });
});

test('a track the film does not list is no choice, so the languages decide', () => {
  assert.deepEqual(startingTracks(film, { audioTrackId: 'a9', subtitleTrackId: 's9' }, { audioLanguage: 'es', subtitleLanguage: 'es' }),
    { audioId: 'a1', subtitleId: 's1' });
});

test('a language picks the first track that can be shown and is not forced, and none leaves subtitles off', () => {
  assert.equal(startingTracks(film, {}, { subtitleLanguage: 'es' }).subtitleId, 's1');
  assert.equal(startingTracks(film, {}, { subtitleLanguage: 'ja' }).subtitleId, null);
  assert.equal(startingTracks(film, {}, {}).subtitleId, null);
  assert.equal(startingTracks(film, {}, {}).audioId, 'a0');
});

test('where the device holds only some tracks, those in the language come first', () => {
  const twoSpanish = { ...film, subtitles: [track('s1', 'spa'), track('s5', 'spa')] };
  const saved = { subtitles: [{ id: 's5' }], audio: [] };
  assert.equal(startingTracks(twoSpanish, {}, { subtitleLanguage: 'es' }, saved).subtitleId, 's5');
});

test('the ladder is the manifest renditions, tallest first, a rung with no size of its own at the source height', () => {
  assert.deepEqual(qualityLevelsOf(film), [{ height: 800, bitrateKbps: 9000 }, { height: 546, bitrateKbps: 1600 }]);
});
