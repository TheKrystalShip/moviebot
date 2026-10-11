// Pushes from the API applied in the order its writes were kept: a stale echo never shows after a newer choice.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { Ordered } from '../src/player/engine/session/ordered.ts';

const record = (height, revision) => ({ height, revision });

function watching() {
  const shown = [];
  return { shown, order: new Ordered((value) => shown.push(value.height)) };
}

test('a push newer than what was applied is applied, and an older or repeated one is dropped', () => {
  const { shown, order } = watching();
  order.heard(record(720, 10));
  order.heard(record(480, 9));
  order.heard(record(720, 10));
  order.heard(record(1080, 11));
  assert.deepEqual(shown, [720, 1080]);
});

test('two quick choices never show the first after the second, whichever order echoes and answers arrive in', () => {
  for (const arrival of [
    ['echo1', 'echo2', 'answer1', 'answer2'],
    ['echo1', 'answer1', 'echo2', 'answer2'],
    ['answer1', 'answer2', 'echo1', 'echo2'],
    ['echo2', 'answer2', 'echo1', 'answer1'],
    ['echo2', 'echo1', 'answer2', 'answer1'],
  ]) {
    const { shown, order } = watching();
    const first = order.writing();
    const second = order.writing();
    const step = {
      echo1: () => order.heard(record(720, 20)), echo2: () => order.heard(record(1080, 21)),
      answer1: () => first(record(720, 20)), answer2: () => second(record(1080, 21)),
    };
    for (const name of arrival) step[name]();
    assert.ok(!shown.includes(720), `${arrival}: the first choice came back (${shown})`);
    order.heard(record(720, 20));
    assert.ok(!shown.includes(720), `${arrival}: a late echo of the first choice came back (${shown})`);
  }
});

test("another device's newer choice made while this one writes is applied once the write has answered", () => {
  const { shown, order } = watching();
  const write = order.writing();
  order.heard(record(480, 31));
  assert.deepEqual(shown, []);
  write(record(720, 30));
  assert.deepEqual(shown, [480]);
});

test("another device's older choice made while this one writes is dropped", () => {
  const { shown, order } = watching();
  const write = order.writing();
  order.heard(record(480, 29));
  write(record(720, 30));
  assert.deepEqual(shown, []);
});

test('a write that failed lets a held push through', () => {
  const { shown, order } = watching();
  const write = order.writing();
  order.heard(record(480, 41));
  write(null);
  assert.deepEqual(shown, [480]);
});
