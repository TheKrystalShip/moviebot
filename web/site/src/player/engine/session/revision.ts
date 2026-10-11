import type { SessionState } from '../types';

/**
 * Which pushes of a room's state to apply: those newer than the last applied within one run of the room.
 * Revisions are monotonic and server-assigned, and count only within an epoch; a room the server built
 * again counts from zero, so a push carrying an epoch not seen before resets the comparison. A state
 * handed back as the answer to "where is the room" settles a disagreement rather than racing the
 * broadcasts, and is applied whatever revision it carries.
 */
export class RevisionGate {
  private epoch: string | null = null;
  private last = -1;

  admits(state: SessionState, resync = false): boolean {
    if (state.epoch !== this.epoch) {
      this.epoch = state.epoch;
      this.last = -1;
    }
    if (!resync && state.revision <= this.last) return false;
    this.last = state.revision;
    return true;
  }
}
