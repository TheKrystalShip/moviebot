import type { Manifest, SessionState } from '../types';

/**
 * How far past the playhead a rung must reach before the player may be on it. A rung switched to
 * while it is behind the playhead stalls the viewer until it catches up, so the player stays on
 * rungs with this much written ahead of where it is.
 */
export const CapMarginSeconds = 30;

/** One rung's reach, by the height the master playlist advertises it at. */
export interface RungReach {
  height: number;
  seconds: number;
}

/**
 * The rungs of a title that is still transcoding and tracks them separately, or null: a title that
 * is ready is written to its end on every rung, and a manifest that records a single head says
 * every rung is at it, so neither has anything to cap.
 */
export function rungReach(manifest: Manifest): RungReach[] | null {
  if (manifest.status !== 'transcoding') return null;
  const renditions = manifest.video.renditions;
  if (!renditions.some((rendition) => rendition.headSeconds !== undefined)) return null;

  const overall = manifest.headSeconds ?? 0;
  return renditions.map((rendition) => ({
    height: rendition.height ?? manifest.video.height,
    seconds: rendition.headSeconds ?? overall
  }));
}

/**
 * Whether a rung is written far enough past `playhead` to be switched to. A height the record
 * does not name is at the overall head.
 */
export function covers(reach: RungReach[], overall: number | undefined, height: number, playhead: number): boolean {
  const seconds = reach.find((rung) => rung.height === height)?.seconds ?? overall ?? Infinity;
  return seconds >= playhead + CapMarginSeconds;
}

/**
 * How far the film can be played to: the best rung's reach, held back to the audio. Null once the
 * title is ready. The server enforces the same limit on every seek.
 */
export function playableHead(manifest: Manifest): number | null {
  if (manifest.status !== 'transcoding') return null;
  return limit(manifest.headSeconds ?? 0, rungReach(manifest)?.map((rung) => rung.seconds), manifest.audioHeadSeconds);
}

/** The same limit, from what a room's state carries. Null once the title is ready. */
export function playableHeadOf(state: SessionState): number | null {
  if (state.transcodeHead === undefined) return null;
  return limit(state.transcodeHead, state.rungHeads?.map((rung) => rung.seconds), state.audioHead);
}

function limit(overall: number, rungs: number[] | undefined, audio: number | undefined): number {
  const best = rungs !== undefined && rungs.length > 0 ? Math.max(...rungs) : overall;
  return audio === undefined ? best : Math.min(best, audio);
}

/** A rung as hls.js lists it. */
export interface LadderLevel {
  height: number;
  /** The advertised bitrate, in bits per second. */
  bitrate: number;
}

/**
 * The height hls.js's own adaptive choice would pick with nothing holding it back: the tallest
 * level whose advertised bitrate fits within the bandwidth it has measured. Switching up asks for
 * more headroom than staying where it is, which is the same asymmetry hls.js applies, so a viewer
 * hovering at a boundary is not reported flipping between two rungs. Levels are in ascending
 * bitrate. Null while nothing has been measured and nothing is pinned.
 *
 * A viewer who holds a rung (`pinned`, its height) wants no more than it: the lower of that rung and
 * the connection's pick, and the pinned rung itself while nothing has been measured.
 */
export function wantedHeight(
  levels: LadderLevel[], bandwidth: number, current: number, factor: number, upFactor: number,
  pinned: number | null = null
): number | null {
  if (levels.length === 0) return null;
  if (!Number.isFinite(bandwidth) || bandwidth <= 0) return pinned;

  let pick = 0;
  levels.forEach((level, index) => {
    if (level.bitrate <= bandwidth * (index <= current ? factor : upFactor)) pick = index;
  });
  const connection = levels[pick].height;
  return pinned === null ? connection : Math.min(pinned, connection);
}
