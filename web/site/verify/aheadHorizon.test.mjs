// Dynamic's horizon: how far past the playhead the device keeps the film, grown from the connection.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { GrowEveryMs, Horizon, StallMemoryMs } from '../src/player/engine/media/aheadHorizon.ts';

const link = (ratio, extra = {}) => ({ ratio, saveData: false, unmetered: false, ...extra });

test('it starts at nothing, which is Standard', () => {
  assert.equal(new Horizon(false).ahead, 0);
  assert.equal(new Horizon(true).ahead, Infinity);
});

test('a connection carrying several times the film grows it ten minutes a step, no faster than a step a while', () => {
  const h = new Horizon(false);
  assert.equal(h.grow(link(4), 0), true);
  assert.equal(h.ahead, 600);
  assert.equal(h.grow(link(4), GrowEveryMs - 1), false);
  assert.equal(h.grow(link(4), GrowEveryMs), true);
  assert.equal(h.ahead, 1200);
});

test('a link the browser calls unmetered counts as fast whatever was measured', () => {
  const h = new Horizon(false);
  assert.equal(h.grow(link(null, { unmetered: true }), 0), true);
  assert.equal(h.ahead, 600);
});

test('a comfortable connection grows it slowly, and a tight one not at all', () => {
  const h = new Horizon(false);
  h.grow(link(2), 0);
  assert.equal(h.ahead, 120);
  assert.equal(h.grow(link(1.2), GrowEveryMs), false);
  assert.equal(h.ahead, 120);
  assert.equal(h.grow(link(null), 2 * GrowEveryMs), false);
});

test('a stall puts it at ten minutes at least and keeps it growing while the stall is recent', () => {
  const h = new Horizon(false);
  h.stalled(0);
  assert.equal(h.grow(link(1.2), 1), true);
  assert.equal(h.ahead, 900);
  assert.equal(h.grow(link(1.2), 1 + GrowEveryMs), true);
  assert.equal(h.ahead, 1200);
  assert.equal(h.grow(link(1.2), StallMemoryMs + 1), false);
});

test('with data saver on it never grows, stall or not', () => {
  const h = new Horizon(false);
  h.stalled(0);
  assert.equal(h.grow(link(10, { saveData: true }), 1), false);
  assert.equal(h.ahead, 0);
});

test('A lot keeps everything and nothing grows it', () => {
  const h = new Horizon(true);
  assert.equal(h.grow(link(10), 0), false);
  assert.equal(h.ahead, Infinity);
});
