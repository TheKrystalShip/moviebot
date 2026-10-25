// The stats panel's arithmetic: how far a presented frame is from the sound, the median it is shown
// as, the drift graph's window, a buffer's seconds ahead, and the snapshot cut to what the server takes.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { aheadOf, fitSnapshot, median, pictureOffset, trimOlder } from '../src/player/engine/media/playbackStats.ts';

test('a frame shown when the sound is where it was made is level', () => {
  // The frame for 10.050 s reaches the screen 50 ms from now, when the sound will have reached 10.050 s.
  assert.ok(Math.abs(pictureOffset(10.05, 10.0, 1050, 1000, 1)) < 1e-9);
});

test('a frame for later than the sound will be is the picture ahead', () => {
  assert.ok(Math.abs(pictureOffset(10.25, 10.0, 1050, 1000, 1) - 0.2) < 1e-9);
  assert.ok(Math.abs(pictureOffset(9.85, 10.0, 1050, 1000, 1) + 0.2) < 1e-9);
});

test('the sound moves on at the element\'s rate until the frame is shown', () => {
  // At 1.02x, 100 ms of waiting moves the sound 102 ms.
  assert.ok(Math.abs(pictureOffset(10.102, 10.0, 1100, 1000, 1.02)) < 1e-9);
});

test('the median ignores the odd late frame', () => {
  assert.equal(median([]), null);
  assert.equal(median([3]), 3);
  assert.equal(median([5, 1, 400, 2, 4]), 4);
  assert.equal(median([1, 2, 3, 4]), 2.5);
});

test('the window drops what is older than it and keeps the rest in order', () => {
  const items = [{ at: 0 }, { at: 100 }, { at: 200 }, { at: 300 }];
  trimOlder(items, 350, 200);
  assert.deepEqual(items.map((i) => i.at), [200, 300]);
  trimOlder(items, 10_000, 200);
  assert.deepEqual(items, []);
});

test('ahead is the range holding the playhead, and nothing when none does', () => {
  const ranges = [{ start: 0, end: 30 }, { start: 60, end: 90 }];
  assert.equal(aheadOf(ranges, 10), 20);
  assert.ok(Math.abs(aheadOf(ranges, 59.8) - 30.2) < 1e-9);
  assert.equal(aheadOf(ranges, 45), 0);
  assert.equal(aheadOf([], 5), 0);
});

function snapshotWith(samples, marks = []) {
  return {
    takenAt: '2026-10-25T15:30:00.000Z', build: 'test', frontDoor: 'site', userAgent: 'test', titleId: 'film',
    playback: 'pb', bufferAhead: 'standard',
    sync: { pictureOffsetMs: 4, outputLatencyMs: 180, soundBehindMs: 184 },
    room: { driftSeconds: 0.1, rate: 1, roomRate: 1, clockOffsetMs: 3, roundTripMs: 40, clockSamples: 8, connection: 'connected', reconnects: 0, hidden: 0 },
    network: { estimateBps: 1, measuredBps: null, aheadVideoSeconds: 30, aheadAudioSeconds: 30, lastFetch: null, failed: 0, fatal: 0, lastError: null },
    media: { playingHeight: 804, wantedHeight: 804, width: 1920, height: 804, videoCodec: null, audioCodec: null, audio: null,
      framesDropped: 0, framesShown: 1, stalls: 0, stalledMs: 0, longestStallMs: 0 },
    history: { samples, marks }
  };
}

test('a snapshot that fits is sent whole', () => {
  const snapshot = snapshotWith([[-2, 0.1, 1]], [[-1, 'stall']]);
  assert.deepEqual(JSON.parse(fitSnapshot(snapshot)), snapshot);
});

test('a snapshot past the limit loses its oldest samples first and keeps the newest', () => {
  const samples = Array.from({ length: 2000 }, (_, i) => [-(2000 - i) * 2, 0.123456, 1.02]);
  const json = fitSnapshot(snapshotWith(samples, [[-1, 'seek', 'behind 2.10s']]), 2048);
  assert.ok(json.length <= 2048);
  const kept = JSON.parse(json).history;
  assert.ok(kept.samples.length > 0 && kept.samples.length < 2000);
  assert.deepEqual(kept.samples.at(-1), samples.at(-1));
  assert.equal(kept.marks.length, 1);
});

test('a snapshot that cannot fit even with no history is not sent', () => {
  assert.equal(fitSnapshot(snapshotWith([]), 100), null);
});
