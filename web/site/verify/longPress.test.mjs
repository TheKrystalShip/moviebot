// What begins a long press and when a finger has drifted too far to be holding.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { SLOP, beginsPress, drifted } from '../src/longPress.js';

test('only a finger on its main contact begins a long press', () => {
  assert.equal(beginsPress('touch', 0), true);
  assert.equal(beginsPress('mouse', 0), false);
  assert.equal(beginsPress('pen', 0), false);
  assert.equal(beginsPress('touch', 2), false);
});

test('a finger drifts once it is more than the slop away, in any direction', () => {
  const from = { x: 100, y: 100 };
  assert.equal(drifted(from, { x: 100, y: 100 }), false);
  assert.equal(drifted(from, { x: 100 + SLOP, y: 100 }), false);
  assert.equal(drifted(from, { x: 100 + SLOP + 1, y: 100 }), true);
  assert.equal(drifted(from, { x: 100, y: 100 - SLOP - 1 }), true);
  assert.equal(drifted(from, { x: 107, y: 107 }), false);
  assert.equal(drifted(from, { x: 108, y: 108 }), true);
});

test('a wider slop lets a finger wander further', () => {
  assert.equal(drifted({ x: 0, y: 0 }, { x: 20, y: 0 }, 24), false);
});
