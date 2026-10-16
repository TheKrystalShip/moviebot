// The playlists the device reads to keep a film ahead: every segment with where it lies in the film,
// and the initialisation segment, each by its path under the title's media.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { parsePlaylist } from '../src/player/engine/media/aheadPlaylist.ts';

const playlist = [
  '#EXTM3U', '#EXT-X-VERSION:7', '#EXT-X-TARGETDURATION:2', '#EXT-X-MAP:URI="init.mp4"',
  '#EXTINF:2.002000,', 'seg00000.m4s', '#EXTINF:2.002000,', 'seg00001.m4s', '#EXTINF:1.5,', 'q23-seg00002.m4s',
  '#EXT-X-ENDLIST', ''
].join('\n');

test('each segment is named under the rung and placed in the film', () => {
  const { init, segments } = parsePlaylist(playlist, 'v0/index.m3u8');
  assert.equal(init, 'v0/init.mp4');
  assert.deepEqual(segments.map((s) => s.uri), ['v0/seg00000.m4s', 'v0/seg00001.m4s', 'v0/q23-seg00002.m4s']);
  assert.equal(segments[1].start, 2.002);
  assert.ok(Math.abs(segments[2].end - 5.504) < 1e-9);
});

test('a segment addressed anywhere but beside its playlist is not one the device keeps', () => {
  const { segments } = parsePlaylist('#EXTINF:2,\nhttps://elsewhere/x.m4s\n#EXTINF:2,\n/abs.m4s\n#EXTINF:2,\nseg.m4s?x=1\n#EXTINF:2,\nok.m4s\n', 'a0/index.m3u8');
  assert.deepEqual(segments.map((s) => [s.uri, s.start]), [['a0/ok.m4s', 6]]);
});
