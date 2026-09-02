import type { SessionState } from '../types';
import { formatTime } from '../ui/format';

/**
 * What one state says somebody did to the room, in the words every surface uses for it.
 *
 * The line under the player and the bubble in the middle of the screen both say it, and two
 * spellings of "paused" would have the room read as two different events. Null when the state
 * names nobody, which is a room restored or reset rather than one somebody touched.
 */
export function describeChange(state: SessionState, previous: SessionState | null): string | null {
  const who = state.updatedBy?.displayName;
  if (who === undefined) return null;

  const what =
    previous === null || previous.titleId !== state.titleId
      ? 'changed the film'
      : previous.paused !== state.paused
        ? state.paused
          ? 'paused'
          : 'resumed'
        : `moved to ${formatTime(state.positionSeconds)}`;

  return `${who} ${what}`;
}
