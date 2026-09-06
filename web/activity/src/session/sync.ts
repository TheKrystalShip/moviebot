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
/** A seek still running after this never took. Later seeks stop waiting for it rather than queue
 *  behind it forever. */
const AbandonedSeekMs = 5000;

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
  /** The initial seek to the room's position started or finished. */
  onInitialSeekChanged(seeking: boolean): void;
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
  /** Which run of the room the revision above belongs to. */
  private epoch: string | null = null;
  /** Whether the room can be heard from. Nothing is extrapolated from a state nothing confirms. */
  private live = false;
  /** Where this client's own write put the playhead, so the resulting seek is not republished. */
  private appliedSeek: number | null = null;
  /** Whether the room is believed to be paused, including an intent sent but not yet answered. */
  private assumedPaused: boolean | null = null;
  private latest: SessionState | null = null;
  private playerReady = false;
  private correcting = false;
  /** True while the player is seeking to the room's position after becoming ready. Blocks user
   *  controls until the seek lands so a play intent cannot fire with a stale position. */
  private _initialSeekInProgress = false;
  /** Set when the room wants playback but a seek has to land before it can start. */
  private startAfterSeek = false;
  private seekStartedMs = 0;
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

    // A seek is published by whatever a person used to make it — the scrub bar, the arrow keys —
    // and never from here. The element seeks for reasons of its own as well: the start position
    // hls.js picks for a playlist still being written is the end of it, and publishing that drags
    // the whole room to the transcode head the moment somebody opens the film.
    this.video.addEventListener('seeked', () => {
      if (this.appliedSeek !== null
          && Math.abs(this.video.currentTime - this.appliedSeek) < SnapSeconds) {
        this.appliedSeek = null;
      }

      // The initial seek landed: controls may be unblocked.
      if (this._initialSeekInProgress) {
        this._initialSeekInProgress = false;
        this.hooks.onInitialSeekChanged(false);
      }

      // Playback the room wanted, held until the playhead had arrived. Starting it while the seek
      // was still running is what aborted it.
      if (this.startAfterSeek) {
        this.startAfterSeek = false;
        this.start();
      }
    });

    this.timer = window.setInterval(() => this.correctDrift(), CheckIntervalMs);
  }

  get state(): SessionState | null {
    return this.latest;
  }

  /**
   * Whether the room can currently be heard from.
   *
   * While it cannot, the last state stands but nothing is derived forward from it. Its anchor
   * keeps running whether or not the film does, and correcting to a position nothing has
   * confirmed is how a paused film comes back minutes ahead of where it stopped.
   */
  setLive(live: boolean): void {
    this.live = live;
  }

  /** Where the room is, as opposed to where this viewer's playhead has got to. */
  roomPosition(): number {
    return this.latest === null ? 0 : derivePosition(this.latest, this.clock.now());
  }

  /** True while the player is seeking to the room's position after joining. */
  get initialSeekInProgress(): boolean {
    return this._initialSeekInProgress;
  }

  /**
   * Applies a push, or discards it. A revision no greater than the last applied is a reordered
   * or duplicated message and carries nothing new.
   *
   * A resync is not a broadcast. It is the server answering where the room is, so it is applied
   * whatever number it carries: it is the thing that settles a disagreement rather than one more
   * message that might have overtaken another.
   */
  applyPush(push: SessionStatePush, resync = false): void {
    const state = push.state;

    // Revisions only count within one run of a room. A room the server has forgotten and built
    // again starts from zero, and measuring it against the run before discards everything it
    // says for as long as this page stays open.
    if (state.epoch !== this.epoch) {
      this.epoch = state.epoch;
      this.lastRevision = -1;
    }

    if (!resync && state.revision <= this.lastRevision) return;

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
    if (ready && this.latest !== null) {
      // If the player is far from the room's position, the first apply will seek. Mark it so
      // controls stay blocked until the playhead arrives: a play before then would fire with
      // whatever position the video element holds, which is usually zero.
      const target = this.targetFor(this.latest);
      if (Math.abs(target - this.video.currentTime) > SnapSeconds) {
        this._initialSeekInProgress = true;
        this.hooks.onInitialSeekChanged(true);
      }
      this.applyToPlayer(this.latest);
    }
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
    const target = this.targetFor(state);
    const seeking = Math.abs(target - this.video.currentTime) > SnapSeconds;
    const pausing = state.paused && !this.video.paused;
    const starting = !state.paused && this.video.paused;

    if (pausing) this.video.pause();

    // Not while one is already running. A second seek abandons the first, and abandoning one
    // during the opening buffer is what makes the film restart loading over and over. A seek
    // that never lands is a different thing, and waiting on it forever is what leaves a film
    // that cannot be moved in either direction.
    if (seeking && (!this.video.seeking || this.seekAbandoned)) this.applySeek(target);

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
    if (!this.live || state.paused || this.video.paused || this.video.seeking
        || this.video.readyState < HTMLMediaElement.HAVE_FUTURE_DATA) {
      if (this.correcting) this.restoreRate(state.rate);
      return;
    }

    const expected = this.targetFor(state);
    const drift = expected - this.video.currentTime;
    const magnitude = Math.abs(drift);

    if (magnitude > HardSeekSeconds) {
      this.applySeek(expected);
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

  /**
   * Where the room is, as far as this film goes.
   *
   * A room left playing derives a position that keeps growing; the film does not. Seeking a media
   * element past the end of what it holds is a seek that never completes, and one of those blocks
   * every seek after it.
   */
  private targetFor(state: SessionState): number {
    const at = derivePosition(state, this.clock.now());
    return Number.isFinite(this.video.duration) ? Math.min(at, this.video.duration) : at;
  }

  private applySeek(to: number): void {
    this.appliedSeek = to;
    this.seekStartedMs = performance.now();
    this.video.currentTime = to;
  }

  private get seekAbandoned(): boolean {
    return this.video.seeking && performance.now() - this.seekStartedMs > AbandonedSeekMs;
  }
}
