// The pure rung logic of the player's engine: what a viewer would be on, and which rungs cover them.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { covers, wantedHeight } from '../src/player/engine/media/heads.ts';

const ladder = [
  { height: 480, bitrate: 1_000_000 },
  { height: 720, bitrate: 3_000_000 },
  { height: 1080, bitrate: 8_000_000 }
];
const Factor = 0.95;
const UpFactor = 0.7;

test('in Auto the wanted rung is the connection\'s pick', () => {
  assert.equal(wantedHeight(ladder, 20_000_000, 0, Factor, UpFactor), 1080);
  assert.equal(wantedHeight(ladder, 5_000_000, 0, Factor, UpFactor), 720);
  assert.equal(wantedHeight(ladder, 500_000, 0, Factor, UpFactor), 480);
});

test('nothing measured and nothing pinned says nothing', () => {
  assert.equal(wantedHeight(ladder, NaN, -1, Factor, UpFactor), null);
});

test('a pinned rung caps what the connection could carry', () => {
  assert.equal(wantedHeight(ladder, 20_000_000, 1, Factor, UpFactor, 720), 720);
});

test('a connection weaker than the pinned rung wants the lower rung', () => {
  assert.equal(wantedHeight(ladder, 1_500_000, 2, Factor, UpFactor, 1080), 480);
});

test('a pinned rung is the wanted one while nothing has been measured', () => {
  assert.equal(wantedHeight(ladder, NaN, -1, Factor, UpFactor, 720), 720);
});

test('a rung covers the playhead only with the margin written past it', () => {
  const reach = [{ height: 1080, seconds: 100 }, { height: 720, seconds: 900 }];
  assert.equal(covers(reach, 100, 1080, 60), true);
  assert.equal(covers(reach, 100, 1080, 80), false);
  assert.equal(covers(reach, 100, 720, 800), true);
  assert.equal(covers(reach, 100, 480, 60), true);
  assert.equal(covers(reach, 100, 480, 80), false);
});
