import type { SessionState, SessionStatePush } from '../types';
import { derivePosition } from './hub';
import type { ServerClock } from './clock';

/** Position difference at which applying remote state seeks rather than leaves the playhead. */
const SnapSeconds = 0.3;
/** Above this, correction is a seek: nudging the rate would take minutes to close the gap. */
const HardSeekSeconds = 2;
/** Below this, drift is left alone. Between here and a hard seek, the rate is nudged. */
const NudgeSeconds = 0.5;
/** Once inside this, the nudge is done and the rate goes back to the room's. */
const SettleSeconds = 0.25;
const NudgeRate = 0.02;
const CheckIntervalMs = 2000;

export interface SyncIntents {
  play(atSeconds: number): void;
  pause(atSeconds: number): void;
  seek(toSeconds: number): void;
}

export interface SyncHooks {
  /** Every push that survived revision gating, before it reaches the player. */
  onState(state: SessionState): void;
  /** The browser refused to start playback without a gesture. */
  onPlaybackBlocked(): void;
}

/**
 * The room's timeline, applied to one video element.
 *
 * The player library never owns the timeline: its events publish intent, and hub state drives
 * it back. Everything that makes that survive contact with two connected people is here —
 * revision gating, echo suppression, and correction of the drift that accumulates regardless.
 */
export class SyncController {
  private lastRevision = -1;
  /** Where this client's own write put the playhead, so the resulting seek is not republished. */
  private appliedSeek: number | null = null;
  /** Whether the room is believed to be paused, including an intent sent but not yet answered. */
  private assumedPaused: boolean | null = null;
  private latest: SessionState | null = null;
  private playerReady = false;
  private correcting = false;
  /** Set when the room wants playback but a seek has to land before it can start. */
  private startAfterSeek = false;
  private timer: number | null = null;

  constructor(
    private readonly video: HTMLVideoElement,
    private readonly clock: ServerClock,
    private readonly intents: SyncIntents,
    private readonly hooks: SyncHooks
  ) {
    // Applying remote state moves the playhead, which raises exactly the events below. An event
    // is an echo when it tells the room what the room just told this client, and is dropped:
    // without that, two connected clients feed each other forever. The test is a comparison
    // against what was applied rather than a count of the events an apply should raise, because
    // a seek interrupted by a second seek raises no `seeked` at all and a count left waiting for
    // one goes on to swallow the next thing the person actually does.
    this.video.addEventListener('play', () => {
      if (this.assumedPaused === false) return;
      this.assumedPaused = false;
      this.intents.play(this.video.currentTime);
    });

    // The end of the film raises a pause on every screen at once. That is the media ending, not
    // a person acting, and publishing it would attribute the room's pause to whoever buffered least.
    this.video.addEventListener('pause', () => {
      if (this.assumedPaused !== false) return;
      if (this.video.ended) return;

      // A seek in flight pauses the element as machinery, not as anybody's decision. Publishing
      // it stops the room every time one person's playhead moves.
      if (this.video.seeking) return;

      this.assumedPaused = true;
      this.intents.pause(this.video.currentTime);
    });

    // On `seeked` and never `seeking`: a scrub otherwise publishes a storm of intents.
    this.video.addEventListener('seeked', () => {
      // Playback the room wanted, held until the playhead had arrived. Starting it while the seek
      // was still running is what aborted it.
      if (this.startAfterSeek) {
        this.startAfterSeek = false;
        this.start();
      }

      if (this.appliedSeek !== null && Math.abs(this.video.currentTime - this.appliedSeek) < SnapSeconds) {
        this.appliedSeek = null;
        return;
      }
      this.intents.seek(this.video.currentTime);
    });

    this.timer = window.setInterval(() => this.correctDrift(), CheckIntervalMs);
  }

  get state(): SessionState | null {
    return this.latest;
  }

  /** Where the room is, as opposed to where this viewer's playhead has got to. */
  roomPosition(): number {
    return this.latest === null ? 0 : derivePosition(this.latest, this.clock.now());
  }

  /**
   * Applies a push, or discards it. A revision no greater than the last applied is a reordered
   * or duplicated message and carries nothing new.
   */
  applyPush(push: SessionStatePush): void {
    const state = push.state;
    if (state.revision <= this.lastRevision) return;

    this.lastRevision = state.revision;
    this.latest = state;
    this.assumedPaused = state.paused;
    this.hooks.onState(state);

    if (this.playerReady) this.applyToPlayer(state);
  }

  /**
   * Called when the player holds the title the state names. State that arrived while a title was
   * loading is applied now rather than dropped.
   */
  setPlayerReady(ready: boolean): void {
    this.playerReady = ready;
    if (ready && this.latest !== null) this.applyToPlayer(this.latest);
  }

  /**
   * Starts playback after the gesture the browser was waiting for, at the room's position
   * rather than wherever the paused playhead sat. It goes through the gate like any other
   * apply: the person pressed a button to catch up, not to move the room.
   */
  resumeFromGesture(): void {
    if (this.latest !== null && this.playerReady) this.applyToPlayer(this.latest);
  }

  dispose(): void {
    if (this.timer !== null) window.clearInterval(this.timer);
    this.timer = null;
  }

  private applyToPlayer(state: SessionState): void {
    const target = derivePosition(state, this.clock.now());
    const seeking = Math.abs(target - this.video.currentTime) > SnapSeconds;
    const pausing = state.paused && !this.video.paused;
    const starting = !state.paused && this.video.paused;

    if (pausing) this.video.pause();

    // Not while one is already running. A second seek abandons the first, and abandoning one
    // during the opening buffer is what makes the film restart loading over and over.
    if (seeking && !this.video.seeking) {
      this.appliedSeek = target;
      this.video.currentTime = target;
    }

    // Never in the same breath as a seek. A play interrupted by one is rejected, the element goes
    // back to paused, and the pause it then reports is indistinguishable from somebody deciding
    // to stop the film — which is how pressing play stopped the room a moment later.
    if (starting) {
      if (this.video.seeking) this.startAfterSeek = true;
      else this.start();
    }

    if (this.correcting) this.restoreRate(state.rate);
  }

  /**
   * Starts playback, and tells apart the two reasons it might not.
   *
   * A play interrupted by a seek rejects, and the seek is usually one this class issued a moment
   * earlier to put the playhead where the room is. That is not the browser refusing to play
   * without a gesture, and asking somebody to press a button for it leaves them pressing one
   * repeatedly while the film loads, each press starting the same race again.
   */
  private start(): void {
    void this.video.play().catch((error: unknown) => {
      // The element is back to paused and is about to say so. That pause is this play failing,
      // not a person stopping the film, so the room is not told about it: setting the assumption
      // back is what the pause handler reads to know the difference.
      this.assumedPaused = true;

      // Only the browser wanting a gesture is worth asking somebody for one. A play this class
      // interrupted itself is not, and asking leaves them pressing a button that restarts the
      // same race.
      if (error instanceof DOMException && error.name === 'AbortError') return;

      this.hooks.onPlaybackBlocked();
    });
  }

  /**
   * Graduated correction. A hard seek for a gap big enough that nudging would take minutes;
   * a rate nudge for anything smaller, because a seek of half a second is jarring and audible
   * while playing two per cent fast is not.
   */
  private correctDrift(): void {
    const state = this.latest;
    if (state === null || !this.playerReady) return;

    // Nothing is corrected while the film cannot play forward. A playhead that is not moving
    // because it is waiting for data drifts from the room by definition, and seeking it to catch
    // up throws away the buffer it was waiting for — which is the same stall again, further
    // behind. Waiting is the correction.
    if (state.paused || this.video.paused || this.video.seeking
        || this.video.readyState < HTMLMediaElement.HAVE_FUTURE_DATA) {
      if (this.correcting) this.restoreRate(state.rate);
      return;
    }

    const expected = derivePosition(state, this.clock.now());
    const drift = expected - this.video.currentTime;
    const magnitude = Math.abs(drift);

    if (magnitude > HardSeekSeconds) {
      this.appliedSeek = expected;
      this.video.currentTime = expected;
      this.restoreRate(state.rate);
      return;
    }

    if (magnitude > NudgeSeconds) {
      this.video.playbackRate = state.rate * (drift > 0 ? 1 + NudgeRate : 1 - NudgeRate);
      this.correcting = true;
      return;
    }

    if (this.correcting && magnitude < SettleSeconds) this.restoreRate(state.rate);
  }

  private restoreRate(rate: number): void {
    this.video.playbackRate = rate;
    this.correcting = false;
  }
}
