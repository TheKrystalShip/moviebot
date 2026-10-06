// The playlists a saved film plays from: each segment's place in its track's file, and the byte-range
// playlist hls.js is handed for it.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  isIndex, mediaPlaylist, renditionDirectory, segmentRanges, trackBytes, trackFileUri
} from '../src/player/engine/media/devicePlaylist.ts';

const index = { initBytes: 1033, segmentSeconds: [2.002, 2.002, 0.959292], segmentBytes: [500, 700, 300] };

test('a segment starts after the init segment and every segment before it', () => {
  assert.deepEqual(segmentRanges(index), [
    { start: 1033, end: 1533 },
    { start: 1533, end: 2233 },
    { start: 2233, end: 2533 }
  ]);
});

test('the ranges cover the file exactly, with nothing between them', () => {
  const ranges = segmentRanges(index);
  assert.equal(ranges[0].start, index.initBytes);
  for (let i = 1; i < ranges.length; i++) assert.equal(ranges[i].start, ranges[i - 1].end);
  assert.equal(ranges.at(-1).end, trackBytes(index));
  assert.equal(trackBytes(index), 1033 + 500 + 700 + 300);
});

test('a track of thousands of segments keeps exact offsets past four gigabytes', () => {
  const count = 7000;
  const big = { initBytes: 1200, segmentSeconds: Array(count).fill(2.002), segmentBytes: Array(count).fill(700_001) };
  const ranges = segmentRanges(big);
  assert.equal(ranges.at(-1).end, 1200 + count * 700_001);
  assert.ok(ranges.at(-1).end > 2 ** 32);
  assert.equal(ranges[5000].start, 1200 + 5000 * 700_001);
});

test('the playlist maps the init segment and lists every segment as a range of one file', () => {
  const lines = mediaPlaylist(index).trim().split('\n');
  assert.equal(lines[0], '#EXTM3U');
  assert.ok(lines.includes('#EXT-X-TARGETDURATION:3'));
  assert.ok(lines.includes('#EXT-X-PLAYLIST-TYPE:VOD'));
  assert.ok(lines.includes('#EXT-X-MAP:URI="track.mp4",BYTERANGE="1033@0"'));
  assert.deepEqual(lines.filter((l) => l.startsWith('#EXT-X-BYTERANGE:')),
    ['#EXT-X-BYTERANGE:500@1033', '#EXT-X-BYTERANGE:700@1533', '#EXT-X-BYTERANGE:300@2233']);
  assert.deepEqual(lines.filter((l) => l.startsWith('#EXTINF:')),
    ['#EXTINF:2.002000,', '#EXTINF:2.002000,', '#EXTINF:0.959292,']);
  assert.equal(lines.filter((l) => l === 'track.mp4').length, 3);
  assert.equal(lines.at(-1), '#EXT-X-ENDLIST');
});

test('the playlist lasts as long as the segments it lists', () => {
  const total = mediaPlaylist(index).split('\n')
    .filter((l) => l.startsWith('#EXTINF:'))
    .reduce((sum, l) => sum + parseFloat(l.slice(8)), 0);
  assert.ok(Math.abs(total - (2.002 + 2.002 + 0.959292)) < 1e-6);
});

test('an index that cannot be played is told apart', () => {
  assert.ok(isIndex(index));
  assert.ok(!isIndex(null));
  assert.ok(!isIndex({ ...index, segmentBytes: [500, 700] }));
  assert.ok(!isIndex({ ...index, initBytes: 0 }));
  assert.ok(!isIndex({ ...index, segmentSeconds: [] , segmentBytes: [] }));
  assert.ok(!isIndex({ ...index, segmentBytes: [500, -1, 300] }));
});

test('a saved track is listed beside the playlist it stands for', () => {
  assert.equal(renditionDirectory('v1/index.m3u8'), 'v1');
  assert.equal(trackFileUri('v1/index.m3u8'), 'v1/track.mp4');
  assert.equal(trackFileUri('a0/index.m3u8'), 'a0/track.mp4');
  assert.equal(trackFileUri('index.m3u8'), 'track.mp4');
});
