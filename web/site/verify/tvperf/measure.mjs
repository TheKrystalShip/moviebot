// What one step's record comes to: the numbers a person feels when a key is pressed on a television.

/** How many of the grid's points must change at once for the frame to count as the page relaid. */
const RelayoutPoints = 4;
/** Changes closer together than this are one movement: a slide or a scroll drawn over several frames. */
const EpisodeGapMs = 120;
/** How many of them hitting nothing is a blank screen. */
const BlankShare = 0.8;

/**
 * The step's measures, from the key's own time (or, for a load, from the navigation's start).
 *
 * - `answerMs`: the key to the next frame drawn after it was handled (Event Timing's duration), or the
 *   first contentful paint for a load: how long a press goes without a visible answer.
 * - `focusMs`: until focus is on another element or its rectangle moves.
 * - `readyMs`: until the last change: the grid relaid, a shift, the focus moving, or an image decoding.
 * - `relayouts`: how many times the page took a new layout: the grid changing at `RelayoutPoints`
 *   points or more, with changes less than `EpisodeGapMs` apart counted as one movement. A page that
 *   arrives once counts one, and so does a row sliding over several frames; skeletons replaced, a
 *   spotlight drawn over half a page and rows that swap each count again.
 * - `blankMs`: time with the screen blank after something had been drawn.
 * - `freezeMs`: the longest gap between two frames: the screen stood still that long.
 * - `blockingMs`: the long animation frames' blocking time, summed.
 * - `cls`: the layout shifts' values, summed, including those right after the key.
 * - `requests` and `kb`: what was fetched.
 */
export function measure(record, { load = false } = {}) {
  const key = record.keys.at(-1);
  const start = load ? 0 : key ? key.t : record.since;
  const before = record.frames.filter((f) => f.t < start).at(-1);
  const frames = [...(before ? [before] : []), ...record.frames.filter((f) => f.t >= start)];
  const rel = (t) => Math.max(0, Math.round(t - start));

  let focusAt = null, last = start, relayouts = 0, lastBig = -Infinity, blank = 0, drawn = false, freeze = 0;
  for (let i = 1; i < frames.length; i++) {
    const a = frames[i - 1], b = frames[i];
    const ga = a.grid.split(" "), gb = b.grid.split(" ");
    const changed = gb.filter((p, n) => p !== ga[n]).length;
    const focusMoved = a.focus !== b.focus || String(a.rect) !== String(b.rect);
    if (focusMoved && focusAt === null) focusAt = b.t;
    if (changed || focusMoved) last = b.t;
    if (changed >= RelayoutPoints) {
      if (b.t - lastBig > EpisodeGapMs) relayouts++;
      lastBig = b.t;
    }
    const empty = gb.filter((p) => p === "0").length / gb.length >= BlankShare;
    if (!empty) drawn = true;
    else if (drawn) blank += b.t - a.t;
    freeze = Math.max(freeze, b.t - a.t);
  }
  for (const s of record.shifts) if (s.t >= start) last = Math.max(last, s.t);

  const events = record.events.filter((e) => e.name === "keydown" && Math.abs(e.t - start) < 5);
  const resources = record.resources.filter((r) => r.t >= start - 5);
  return {
    answerMs: load ? (record.fcp != null ? Math.round(record.fcp) : null) : events.length ? Math.round(events[0].d) : 16,
    focusMs: focusAt === null ? null : rel(focusAt),
    readyMs: rel(last),
    relayouts,
    blankMs: Math.round(blank),
    freezeMs: Math.round(freeze),
    blockingMs: Math.round(record.loafs.filter((l) => l.t + l.d >= start).reduce((s, l) => s + l.block, 0)),
    cls: +record.shifts.filter((s) => s.t >= start).reduce((s, x) => s + x.v, 0).toFixed(3),
    requests: resources.length,
    kb: Math.round(resources.reduce((s, r) => s + (r.size || 0), 0) / 1024),
  };
}

/** The slowest long animation frames of a record, with the scripts that filled them. */
export function slowest(record, count = 3) {
  return [...record.loafs].sort((a, b) => b.block - a.block).slice(0, count)
    .map((l) => ({ ms: Math.round(l.d), blocking: Math.round(l.block), scripts: l.scripts }));
}

export const Metrics = ["answerMs", "focusMs", "readyMs", "relayouts", "blankMs", "freezeMs", "blockingMs", "cls", "requests", "kb"];

/** The median of each measure over repeated runs of one step; a measure absent from a run is skipped. */
export function median(runs) {
  const out = {};
  for (const m of Metrics) {
    const values = runs.map((r) => r[m]).filter((v) => v != null).sort((a, b) => a - b);
    out[m] = values.length ? values[Math.floor((values.length - 1) / 2)] : null;
  }
  return out;
}
