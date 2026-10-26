// The person's phone tab bar: what is kept, and the order the API's records and this device's writes land in.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { STORAGE_KEY, completed, createTabBar, readSlots } from '../src/tabBar.js';

const DEFAULTS = ['movies', 'discover', 'assistant', 'profile'];

const storage = (initial) => {
  const held = new Map(initial === undefined ? [] : [[STORAGE_KEY, initial]]);
  return {
    getItem: (key) => (held.has(key) ? held.get(key) : null),
    setItem: (key, value) => held.set(key, value),
    removeItem: (key) => held.delete(key),
  };
};

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

/** A bar over a fake API holding one record; `puts` lists every body written. */
function rig({ local, record = { qualityHeight: 720, revision: 3, tabBar: null } } = {}) {
  const disk = storage(local);
  const api = { record, puts: [], loads: 0, gate: null };
  const bar = createTabBar({
    storage: disk,
    defaults: DEFAULTS,
    load: async () => { api.loads += 1; return api.record; },
    save: async (body) => {
      api.puts.push(body);
      if (api.gate) await api.gate;
      api.record = { ...body, revision: api.record.revision + 1 };
      return api.record;
    },
  });
  return { bar, api, disk };
}

test('a bar the device holds is read at start, and anything else is the defaults', () => {
  assert.deepEqual(readSlots(storage(JSON.stringify(['tv', 'anime', 'wants', 'plans']))), ['tv', 'anime', 'wants', 'plans']);
  assert.equal(readSlots(storage()), null);
  assert.equal(readSlots(storage('not json')), null);
  assert.equal(readSlots(storage(JSON.stringify(['tv', 'tv', 'wants', 'plans']))), null);
  assert.equal(readSlots(storage(JSON.stringify(['tv', '', 'wants', 'plans']))), null);
});

test('an empty slot takes its position default, and a choice equal to the defaults is null', () => {
  assert.deepEqual(completed(['tv', '', 'wants', 'plans'], DEFAULTS), ['tv', 'discover', 'wants', 'plans']);
  assert.deepEqual(completed(['discover', '', 'wants', 'plans'], DEFAULTS), ['discover', 'movies', 'wants', 'plans']);
  assert.equal(completed([...DEFAULTS], DEFAULTS), null);
  assert.equal(completed(['', '', '', ''], DEFAULTS), null);
});

test('a change is shown and kept on the device at once, and written with the rest of the record', async () => {
  const { bar, api, disk } = rig();
  await bar.attach();
  await bar.change(['tv', 'anime', 'wants', 'plans']);
  assert.deepEqual(bar.slots(), ['tv', 'anime', 'wants', 'plans']);
  assert.deepEqual(readSlots(disk), ['tv', 'anime', 'wants', 'plans']);
  assert.deepEqual(api.puts, [{ qualityHeight: 720, revision: 3, tabBar: ['tv', 'anime', 'wants', 'plans'] }]);
});

test('nothing is written from a record that was not read: the first change reads it', async () => {
  const { bar, api } = rig();
  bar.attach();
  await bar.change(['tv', 'anime', 'wants', 'plans']);
  assert.equal(api.puts.length, 1);
  assert.equal(api.puts[0].qualityHeight, 720);
});

test('a signed-out bar keeps the device copy and writes nothing', async () => {
  const { bar, api, disk } = rig();
  await bar.change(['tv', 'anime', 'wants', 'plans']);
  assert.deepEqual(readSlots(disk), ['tv', 'anime', 'wants', 'plans']);
  assert.equal(api.puts.length, 0);
});

test('returning to the defaults writes null and forgets the device copy', async () => {
  const { bar, api, disk } = rig({ local: JSON.stringify(['tv', 'anime', 'wants', 'plans']) });
  await bar.attach();
  await bar.change([...DEFAULTS]);
  assert.equal(api.puts[0].tabBar, null);
  assert.equal(readSlots(disk), null);
});

test('the API\'s record replaces the device copy when it is read', async () => {
  const { bar, disk } = rig({
    local: JSON.stringify(['tv', 'anime', 'wants', 'plans']),
    record: { revision: 5, tabBar: ['plans', 'wants', 'anime', 'tv'] },
  });
  await bar.attach();
  assert.deepEqual(bar.slots(), ['plans', 'wants', 'anime', 'tv']);
  assert.deepEqual(readSlots(disk), ['plans', 'wants', 'anime', 'tv']);
});

test('a read carries no revision and still replaces the device copy', async () => {
  const { bar } = rig({ record: { tabBar: ['plans', 'wants', 'anime', 'tv'] } });
  await bar.attach();
  assert.deepEqual(bar.slots(), ['plans', 'wants', 'anime', 'tv']);
  await bar.refresh();
  assert.deepEqual(bar.slots(), ['plans', 'wants', 'anime', 'tv']);
});

test('a read that a push or a change overtook while it was out is dropped', async () => {
  const { bar, api } = rig({ record: { tabBar: null } });
  const reading = bar.attach();
  bar.heard({ revision: 9, tabBar: ['tv', 'anime', 'wants', 'plans'] });
  await reading;
  assert.deepEqual(bar.slots(), ['tv', 'anime', 'wants', 'plans']);
  api.record = { tabBar: null };
  const again = bar.refresh();
  const changing = bar.change(['plans', 'wants', 'anime', 'tv']);
  await Promise.all([again, changing]);
  assert.deepEqual(bar.slots(), ['plans', 'wants', 'anime', 'tv']);
});

test('a push is adopted, and a push older than what is held is dropped', async () => {
  const { bar } = rig({ record: { revision: 5, tabBar: null } });
  await bar.attach();
  bar.heard({ revision: 7, tabBar: ['tv', 'anime', 'wants', 'plans'] });
  assert.deepEqual(bar.slots(), ['tv', 'anime', 'wants', 'plans']);
  bar.heard({ revision: 6, tabBar: ['plans', 'wants', 'anime', 'tv'] });
  assert.deepEqual(bar.slots(), ['tv', 'anime', 'wants', 'plans']);
  bar.heard({ revision: 8, tabBar: null });
  assert.equal(bar.slots(), null);
});

test('a push that lands while this device writes waits for the write\'s answer, and an echo of an older write is dropped', async () => {
  const { bar, api } = rig({ record: { revision: 3, tabBar: null } });
  await bar.attach();
  let release;
  api.gate = new Promise((resolve) => { release = resolve; });
  const writing = bar.change(['tv', 'anime', 'wants', 'plans']);
  await settle();
  bar.heard({ revision: 4, tabBar: ['tv', 'anime', 'wants', 'plans'] });
  assert.deepEqual(bar.slots(), ['tv', 'anime', 'wants', 'plans']);
  release();
  await writing;
  assert.deepEqual(bar.slots(), ['tv', 'anime', 'wants', 'plans']);
});

test('writes go one at a time, each with the bar as it stands then, from the record the last answered', async () => {
  const { bar, api } = rig();
  await bar.attach();
  const first = bar.change(['tv', 'anime', 'wants', 'plans']);
  const second = bar.change(['tv', 'anime', 'plans', 'wants']);
  await Promise.all([first, second]);
  assert.deepEqual(api.puts.map((body) => body.tabBar), [['tv', 'anime', 'plans', 'wants'], ['tv', 'anime', 'plans', 'wants']]);
  assert.deepEqual(api.puts.map((body) => body.revision), [3, 4]);
});

test('a write another device\'s preferences overtook carries their record, not an older one', async () => {
  const { bar, api } = rig({ record: { qualityHeight: 720, revision: 3, tabBar: null } });
  await bar.attach();
  bar.heard({ qualityHeight: 1080, revision: 4, tabBar: null });
  await bar.change(['tv', 'anime', 'wants', 'plans']);
  assert.equal(api.puts[0].qualityHeight, 1080);
});

test('a refused write is reported and leaves the device copy', async () => {
  const failures = [];
  const disk = storage();
  const bar = createTabBar({
    storage: disk, defaults: DEFAULTS, failed: (error) => failures.push(error),
    load: async () => ({ revision: 1 }),
    save: async () => { throw new Error('refused'); },
  });
  await bar.attach();
  await bar.change(['tv', 'anime', 'wants', 'plans']);
  assert.equal(failures.length, 1);
  assert.deepEqual(readSlots(disk), ['tv', 'anime', 'wants', 'plans']);
});
