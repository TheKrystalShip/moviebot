// The player's remote: OK playing and pausing while the controls are away, the scrub bar moving the film
// only while it seeks, presses gathered into one skip, and a held key scrubbing and seeking once on
// release.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { Remote, SkipSeconds, SkipSettleMs, StaleHoldMs } from '../src/player/engine/media/remote.ts';

/** A clock the test moves by hand, with the timers it would fire. */
function fakeClock() {
  let now = 0;
  let next = 1;
  const timers = new Map();
  return {
    now: () => now,
    setTimeout: (run, ms) => { const id = next++; timers.set(id, { run, at: now + ms }); return id; },
    clearTimeout: (id) => timers.delete(id),
    advance(ms) {
      now += ms;
      for (const [id, timer] of [...timers]) {
        if (timer.at <= now) { timers.delete(id); timer.run(); }
      }
    },
  };
}

function player({ shown = false, onBar = false, seeking = false, position = 600, duration = 7200 } = {}) {
  const calls = [];
  const state = { shown, onBar, seeking, position, duration };
  const hooks = {
    shown: () => state.shown,
    reveal: () => { calls.push(['reveal']); state.shown = true; },
    onBar: () => state.onBar,
    seeking: () => state.seeking,
    setSeeking: (on) => {
      calls.push(['setSeeking', on]);
      state.seeking = on;
      if (on) { state.shown = true; state.onBar = true; }
    },
    below: () => { calls.push(['below']); state.onBar = false; },
    position: () => state.position,
    duration: () => state.duration,
    togglePlay: () => calls.push(['togglePlay']),
    play: () => calls.push(['play']),
    pause: () => calls.push(['pause']),
    skipBy: (s) => calls.push(['skipBy', s]),
    gathering: (s) => calls.push(['gathering', s]),
    scrubbing: (at) => calls.push(['scrubbing', at]),
    seek: (s) => calls.push(['seek', s]),
  };
  return { hooks, calls, state };
}

const only = (calls, name) => calls.filter((c) => c[0] === name);

test('OK with the controls away plays or pauses; with them up it is the focused control\'s', () => {
  const clock = fakeClock();
  const p = player();
  const remote = new Remote(p.hooks, clock);
  assert.equal(remote.keydown('Enter'), true);
  assert.deepEqual(p.calls, [['togglePlay']]);
  p.state.shown = true;
  clock.advance(500);
  assert.equal(remote.keydown('Enter'), false);
});

test('Up and Down with the controls away only bring them back', () => {
  const clock = fakeClock();
  const p = player();
  const remote = new Remote(p.hooks, clock);
  assert.equal(remote.keydown('ArrowUp'), true);
  assert.deepEqual(p.calls, [['reveal']]);
  clock.advance(500);
  assert.equal(remote.keydown('ArrowUp'), false, 'with them up, the navigator moves focus');
});

test('presses that follow each other are one skip, sent once they stop', () => {
  const clock = fakeClock();
  const p = player();
  const remote = new Remote(p.hooks, clock);
  for (let i = 0; i < 3; i++) {
    assert.equal(remote.keydown('ArrowRight'), true);
    remote.keyup('ArrowRight');
    clock.advance(300);
  }
  assert.deepEqual(only(p.calls, 'skipBy'), []);
  clock.advance(SkipSettleMs);
  assert.deepEqual(only(p.calls, 'skipBy'), [['skipBy', 3 * SkipSeconds]]);
  assert.deepEqual(only(p.calls, 'gathering').at(-1), ['gathering', null]);
});

test('Left and Right move focus among the controls, and on the scrub bar move nothing until OK', () => {
  const clock = fakeClock();
  const p = player({ shown: true });
  const remote = new Remote(p.hooks, clock);
  assert.equal(remote.keydown('ArrowLeft'), false);
  remote.keyup('ArrowLeft');
  p.state.onBar = true;
  clock.advance(500);
  assert.equal(remote.keydown('ArrowLeft'), true, 'the bar keeps the key');
  remote.keyup('ArrowLeft');
  clock.advance(SkipSettleMs);
  assert.deepEqual(only(p.calls, 'skipBy'), [], 'and moves nothing');
  assert.equal(remote.keydown('Enter'), true);
  assert.deepEqual(only(p.calls, 'setSeeking'), [['setSeeking', true]]);
  clock.advance(500);
  assert.equal(remote.keydown('ArrowLeft'), true);
  remote.keyup('ArrowLeft');
  clock.advance(SkipSettleMs);
  assert.deepEqual(only(p.calls, 'skipBy'), [['skipBy', -SkipSeconds]]);
});

test('Left or Right with the controls away skips and brings the bar up seeking, which goes on skipping', () => {
  const clock = fakeClock();
  const p = player();
  const remote = new Remote(p.hooks, clock);
  assert.equal(remote.keydown('ArrowRight'), true);
  remote.keyup('ArrowRight');
  assert.deepEqual(only(p.calls, 'setSeeking'), [['setSeeking', true]]);
  clock.advance(SkipSettleMs + 300);
  assert.equal(remote.keydown('ArrowRight'), true);
  remote.keyup('ArrowRight');
  clock.advance(SkipSettleMs);
  assert.deepEqual(only(p.calls, 'skipBy'), [['skipBy', SkipSeconds], ['skipBy', SkipSeconds]]);
});

test('OK, Up and Down stop seeking, and a skip being gathered is sent at once', () => {
  for (const key of ['Enter', 'ArrowUp', 'ArrowDown']) {
    const clock = fakeClock();
    const p = player({ shown: true, onBar: true, seeking: true });
    const remote = new Remote(p.hooks, clock);
    remote.keydown('ArrowRight');
    remote.keyup('ArrowRight');
    clock.advance(100);
    remote.keydown(key);
    assert.deepEqual(only(p.calls, 'skipBy'), [['skipBy', SkipSeconds]], `${key} sends the skip`);
    assert.deepEqual(only(p.calls, 'setSeeking').at(-1), ['setSeeking', false], `${key} stops seeking`);
    clock.advance(SkipSettleMs);
    assert.equal(only(p.calls, 'skipBy').length, 1, 'and nothing is sent twice');
  }
  const clock = fakeClock();
  const p = player({ shown: true, onBar: true, seeking: true });
  const remote = new Remote(p.hooks, clock);
  assert.equal(remote.stopSeeking(), true, 'Back stops seeking');
  assert.equal(remote.stopSeeking(), false, 'and is left to the player when nothing seeks');
});

test('Down from the scrub bar goes to the controls the player chooses', () => {
  const clock = fakeClock();
  const p = player({ shown: true, onBar: true });
  const remote = new Remote(p.hooks, clock);
  assert.equal(remote.keydown('ArrowDown'), true);
  assert.deepEqual(only(p.calls, 'below'), [['below']]);
  clock.advance(500);
  assert.equal(remote.keydown('ArrowDown'), false, 'below the bar the navigator moves focus');
});

test('Rewind and Fast-forward skip wherever focus is', () => {
  const clock = fakeClock();
  const p = player({ shown: true });
  const remote = new Remote(p.hooks, clock);
  assert.equal(remote.keydown('MediaFastForward'), true);
  clock.advance(SkipSettleMs);
  assert.deepEqual(only(p.calls, 'skipBy'), [['skipBy', SkipSeconds]]);
});

test('a held key scrubs, and the room is sent one seek when it comes up', () => {
  const clock = fakeClock();
  const p = player({ position: 600, duration: 7200 });
  const remote = new Remote(p.hooks, clock);
  remote.keydown('ArrowRight');
  // The reference Fire TV's cadence: the first repeat after about 400 ms, then every 50 ms.
  clock.advance(400);
  for (let i = 0; i < 20; i++) {
    assert.equal(remote.keydown('ArrowRight'), true);
    clock.advance(50);
  }
  const scrubs = only(p.calls, 'scrubbing').map((c) => c[1]);
  assert.equal(scrubs[0], 600 + SkipSeconds, 'the scrub starts where the first press was taking the room');
  assert.ok(scrubs.at(-1) > scrubs[0], 'and moves forward while the key is held');
  assert.deepEqual(only(p.calls, 'skipBy'), [], 'no skip is sent while holding');
  assert.equal(remote.keyup('ArrowRight'), true);
  assert.deepEqual(only(p.calls, 'seek'), [['seek', scrubs.at(-1)]]);
  assert.deepEqual(only(p.calls, 'scrubbing').at(-1), ['scrubbing', null]);
  clock.advance(SkipSettleMs * 2);
  assert.deepEqual(only(p.calls, 'skipBy'), [], 'and the first press is not sent as well');
});

test('a scrub speeds up the longer it is held, and stops at the ends of the film', () => {
  const clock = fakeClock();
  const p = player({ position: 7000, duration: 7200 });
  const remote = new Remote(p.hooks, clock);
  remote.keydown('ArrowRight');
  clock.advance(400);
  for (let i = 0; i < 200; i++) { remote.keydown('ArrowRight'); clock.advance(50); }
  remote.keyup('ArrowRight');
  assert.deepEqual(only(p.calls, 'seek'), [['seek', 7200]]);

  const early = player({ position: 3600, duration: 7200 });
  const steps = new Remote(early.hooks, clock);
  steps.keydown('ArrowLeft');
  clock.advance(400);
  for (let i = 0; i < 80; i++) { steps.keydown('ArrowLeft'); clock.advance(50); }
  const at = only(early.calls, 'scrubbing').map((c) => c[1]).filter((v) => v !== null);
  const first = at[1] - at[2];
  const later = at.at(-2) - at.at(-1);
  assert.ok(later > first * 2, `a step after four seconds (${later}) is several times the first (${first})`);
});

test('a key that came up in between is pressed again, however quickly', () => {
  const clock = fakeClock();
  const p = player();
  const remote = new Remote(p.hooks, clock);
  remote.keydown('ArrowLeft');
  clock.advance(40);
  remote.keyup('ArrowLeft');
  clock.advance(40);
  remote.keydown('ArrowLeft');
  remote.keyup('ArrowLeft');
  assert.deepEqual(only(p.calls, 'scrubbing'), []);
  clock.advance(SkipSettleMs);
  assert.deepEqual(only(p.calls, 'skipBy'), [['skipBy', -2 * SkipSeconds]]);
});

test('a key whose keyup was lost is pressed again once it has been still for a while', () => {
  const clock = fakeClock();
  const p = player();
  const remote = new Remote(p.hooks, clock);
  remote.keydown('ArrowRight');
  clock.advance(StaleHoldMs + 10);
  remote.keydown('ArrowRight');
  assert.deepEqual(only(p.calls, 'scrubbing'), []);
});

test('the media keys act directly, controls or not', () => {
  const clock = fakeClock();
  const p = player();
  const remote = new Remote(p.hooks, clock);
  assert.equal(remote.keydown('MediaPlayPause'), true);
  clock.advance(200);
  assert.equal(remote.keydown('MediaPause'), true);
  clock.advance(200);
  assert.equal(remote.keydown('MediaPlay'), true);
  assert.deepEqual(p.calls, [['togglePlay'], ['pause'], ['play']]);
});
