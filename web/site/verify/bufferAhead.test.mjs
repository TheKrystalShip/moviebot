// How far ahead a device fetches: kept per browser, read back safely, and turned into hls.js settings
// measured in seconds alone.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BufferAheadLevels, bufferAhead, bufferConfig, setBufferAhead } from '../src/player/engine/media/bufferAhead.ts';

/** A browser whose pointer is a mouse (`desktop`) or a finger, and whose user agent says `agent`. */
function browser(local, { desktop = false, agent = 'Mozilla/5.0' } = {}) {
  return {
    localStorage: local,
    navigator: { userAgent: agent },
    matchMedia: (query) => ({ matches: desktop ? /fine|hover: hover/.test(query) : /coarse|hover: none/.test(query) }),
  };
}

function storage(initial = {}) {
  const kept = { ...initial };
  return { getItem: (k) => (k in kept ? kept[k] : null), setItem: (k, v) => { kept[k] = String(v); }, kept };
}

test('a desktop that chose nothing streams as it plays, and anything else keeps the film ahead', () => {
  globalThis.window = browser(storage(), { desktop: true });
  assert.equal(bufferAhead(), 'standard');
  globalThis.window = browser(storage());
  assert.equal(bufferAhead(), 'dynamic');
  globalThis.window = browser(storage(), { desktop: true, agent: 'Mozilla/5.0 KrystalCinemaTV/1.2.0' });
  assert.equal(bufferAhead(), 'dynamic');
});

test('a choice stands over the default on every device', () => {
  globalThis.window = browser(storage({ 'cinema.bufferAhead': 'dynamic' }), { desktop: true });
  assert.equal(bufferAhead(), 'dynamic');
  globalThis.window = browser(storage({ 'cinema.bufferAhead': 'standard' }));
  assert.equal(bufferAhead(), 'standard');
});

test('a choice is kept and read back, and a stray value reads as the default', () => {
  globalThis.window = browser(storage());
  setBufferAhead('light');
  assert.equal(bufferAhead(), 'light');
  globalThis.window = browser(storage({ 'cinema.bufferAhead': 'everything' }));
  assert.equal(bufferAhead(), 'dynamic');
});

test('storage that throws leaves the default standing', () => {
  globalThis.window = browser({ getItem() { throw new Error('denied'); }, setItem() { throw new Error('denied'); } }, { desktop: true });
  assert.equal(bufferAhead(), 'standard');
  assert.doesNotThrow(() => setBufferAhead('light'));
});

test('each level is its seconds and nothing else', () => {
  for (const [level, { seconds }] of Object.entries(BufferAheadLevels)) {
    assert.deepEqual(bufferConfig(level), { maxBufferLength: seconds, maxMaxBufferLength: seconds, maxBufferSize: 0 });
  }
});

test('a phone that chose nothing is on Dynamic, which keeps the film on the device as it grows', () => {
  globalThis.window = browser(storage());
  assert.equal(bufferAhead(), 'dynamic');
  assert.equal(BufferAheadLevels.dynamic.device, true);
  assert.equal(BufferAheadLevels.dynamic.dynamic, true);
});
