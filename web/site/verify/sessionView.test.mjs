// The gate: a session that has ended is the one state a page draws in place of itself; one that is
// being restored or recovered is somebody signed in, drawn as they were.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { isEnded, viewOf } from '../src/sessionView.js';

const CLAIM = 'cinema_actions';
const account = { id: 'sub-1', name: 'Ana', username: 'ana' };

const snapshot = (status, extra = {}) => ({
  status,
  reason: null,
  account,
  claims: { [CLAIM]: ['cinema:watch', 'cinema:download'] },
  expiresAt: Date.now() + 300000,
  ...extra,
});

test('only ended is drawn', () => {
  for (const status of ['none', 'restoring', 'live', 'stale']) assert.equal(isEnded(snapshot(status)), false, status);
  assert.equal(isEnded(snapshot('ended', { reason: 'refused', account: null })), true);
});

test('a live session is the person with the actions the token lists', () => {
  const view = viewOf(snapshot('live'), CLAIM);
  assert.deepEqual(view.user, { id: 'sub-1', name: 'Ana', username: 'ana', device: null });
  assert.deepEqual(view.actions, ['cinema:watch', 'cinema:download']);
  assert.equal(view.stale, false);
});

test('a stale session is still somebody signed in, with everything unchanged but the mark', () => {
  const live = viewOf(snapshot('live'), CLAIM);
  const stale = viewOf(snapshot('stale'), CLAIM);
  assert.equal(stale.stale, true);
  assert.deepEqual({ ...stale, stale: false }, live);
});

test('restoring, ended and nobody draw no session', () => {
  assert.equal(viewOf(snapshot('restoring', { account: null }), CLAIM), null);
  assert.equal(viewOf(snapshot('ended', { account: null, reason: 'revoked' }), CLAIM), null);
  assert.equal(viewOf(snapshot('none', { account: null }), CLAIM), null);
});

test('a television carries its name from the token', () => {
  const view = viewOf(snapshot('live', { claims: { tks_device_name: 'Living room' } }), CLAIM);
  assert.equal(view.user.device, 'Living room');
  assert.deepEqual(view.actions, []);
});

test('one action listed as a string is a list of one', () => {
  assert.deepEqual(viewOf(snapshot('live', { claims: { [CLAIM]: 'cinema:watch' } }), CLAIM).actions, ['cinema:watch']);
});

test('a renewal that changes nothing a page draws answers the same view', () => {
  const first = viewOf(snapshot('live'), CLAIM);
  const renewed = viewOf(snapshot('live', { claims: { [CLAIM]: ['cinema:watch', 'cinema:download'], jti: 'next' }, expiresAt: Date.now() + 600000 }), CLAIM, first);
  assert.equal(renewed, first);
  const changed = viewOf(snapshot('live', { claims: { [CLAIM]: ['cinema:watch'] } }), CLAIM, first);
  assert.notEqual(changed, first);
});
