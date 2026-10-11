// Which pushes of a room's state are applied: newer within a run of the room, any in a new run, and a
// resync whatever it carries.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { RevisionGate } from '../src/player/engine/session/revision.ts';

const state = (epoch, revision) => ({ epoch, revision });

test('a push no newer than the last applied is a reordered or duplicated message', () => {
  const gate = new RevisionGate();
  assert.equal(gate.admits(state('a', 3)), true);
  assert.equal(gate.admits(state('a', 3)), false);
  assert.equal(gate.admits(state('a', 2)), false);
  assert.equal(gate.admits(state('a', 4)), true);
});

test('a room built again counts from zero, so a new epoch resets the comparison', () => {
  const gate = new RevisionGate();
  gate.admits(state('a', 40));
  assert.equal(gate.admits(state('b', 0)), true);
  assert.equal(gate.admits(state('b', 1)), true);
  assert.equal(gate.admits(state('a', 41)), true);
});

test('a resync is applied whatever revision it carries, and the revisions after it count from it', () => {
  const gate = new RevisionGate();
  gate.admits(state('a', 10));
  assert.equal(gate.admits(state('a', 4), true), true);
  assert.equal(gate.admits(state('a', 5)), true);
  assert.equal(gate.admits(state('a', 4)), false);
});
