// How far ahead a device fetches: kept per browser, read back safely, and turned into hls.js settings
// measured in seconds alone.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BufferAheadLevels, DefaultBufferAhead, bufferAhead, bufferConfig, setBufferAhead } from '../src/player/engine/media/bufferAhead.ts';

function storage(initial = {}) {
  const kept = { ...initial };
  return { getItem: (k) => (k in kept ? kept[k] : null), setItem: (k, v) => { kept[k] = String(v); }, kept };
}

test('a device that chose nothing fetches the default', () => {
  globalThis.window = { localStorage: storage() };
  assert.equal(bufferAhead(), DefaultBufferAhead);
});

test('a choice is kept and read back, and a stray value reads as the default', () => {
  globalThis.window = { localStorage: storage() };
  setBufferAhead('light');
  assert.equal(bufferAhead(), 'light');
  globalThis.window = { localStorage: storage({ 'cinema.bufferAhead': 'everything' }) };
  assert.equal(bufferAhead(), DefaultBufferAhead);
});

test('storage that throws leaves the default standing', () => {
  globalThis.window = { localStorage: { getItem() { throw new Error('denied'); }, setItem() { throw new Error('denied'); } } };
  assert.equal(bufferAhead(), DefaultBufferAhead);
  assert.doesNotThrow(() => setBufferAhead('light'));
});

test('each level is its seconds and nothing else', () => {
  for (const [level, { seconds }] of Object.entries(BufferAheadLevels)) {
    assert.deepEqual(bufferConfig(level), { maxBufferLength: seconds, maxMaxBufferLength: seconds, maxBufferSize: 0 });
  }
});
