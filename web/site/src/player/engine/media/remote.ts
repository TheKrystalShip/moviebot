/**
 * A television remote in the player.
 *
 * A remote has arrows, OK, Back, Menu and the media keys, and every one of them reaches somebody
 * sitting across a room from the controls, so the first press of OK or an arrow while the controls
 * are away only brings them back: waking the screen never moves the room.
 *
 * Left and Right skip while the controls are away, and while the scrub bar holds focus, which a skip
 * gives it; with the controls up anywhere else they move focus among them, which the page's
 * navigator does. Rewind and
 * Fast-forward skip wherever focus is. A press skips by `SkipSeconds`, and presses that follow each
 * other are gathered into one skip, sent once when they stop, for the reason the gestures gather
 * taps: a second seek asked before the first is answered is measured from a playhead that has not
 * moved yet.
 *
 * Holding the key scrubs instead. The bar previews where letting go lands, moving faster the longer
 * the key is held, and the room is sent one seek when the key comes up, as a drag on the bar sends
 * one when it is let go. A held key is a keydown for a key that has not come up since its last one:
 * the reference Fire TV repeats a held key from about 400 ms after the press, every 50 ms, and marks
 * none of the repeats as one, so neither the flag nor the timing tells a hold from a second press.
 */

export interface RemoteHooks {
  /** Whether the controls are on screen. */
  shown(): boolean;
  /** Brings the controls back and puts focus on them. */
  reveal(): void;
  /** Whether the scrub bar holds focus, where Left and Right skip with the controls up. */
  scrubFocused(): boolean;
  /** Where the film is now, which a scrub starts from. */
  position(): number;
  duration(): number;
  togglePlay(): void;
  play(): void;
  pause(): void;
  /** A gathered skip, sent to the room once. */
  skipBy(seconds: number): void;
  /** What the skip being gathered adds up to, or null once it is sent. */
  gathering(seconds: number | null): void;
  /** Where letting go of a scrub lands, or null when there is no scrub. */
  scrubbing(at: number | null): void;
  /** The room is sent here when a scrub is let go. */
  seek(seconds: number): void;
}

export const SkipSeconds = 10;
/** Presses keep adding to a skip while they come this close together; then the skip is sent. */
export const SkipSettleMs = 650;
/**
 * A key still down after this long without a repeat lost its keyup somewhere, and its next keydown
 * is a press. A held key repeats far faster.
 */
export const StaleHoldMs = 1000;
/** How fast a scrub starts, as a share of the film per second, and never slower than the floor. */
const ScrubShare = 1 / 120;
const ScrubFloorSeconds = 20;
/** A scrub speeds up by this much for each second the key is held, up to `ScrubMaxSpeedUp`. */
const ScrubSpeedUpPerSecond = 1;
const ScrubMaxSpeedUp = 6;

type Direction = -1 | 1;

const SkipKeys: Record<string, { direction: Direction; always: boolean }> = {
  ArrowLeft: { direction: -1, always: false },
  ArrowRight: { direction: 1, always: false },
  MediaRewind: { direction: -1, always: true },
  MediaFastForward: { direction: 1, always: true },
};

export interface RemoteClock {
  now(): number;
  setTimeout(run: () => void, ms: number): number;
  clearTimeout(id: number): void;
}

const browserClock: RemoteClock = {
  now: () => performance.now(),
  setTimeout: (run, ms) => window.setTimeout(run, ms),
  clearTimeout: (id) => window.clearTimeout(id),
};

export class Remote {
  private gathered = 0;
  private settleTimer: number | null = null;
  /** The key down and not yet up, and when it last went down. */
  private down: string | null = null;
  private lastDown = -Infinity;
  private scrub: { key: string; direction: Direction; at: number; since: number; tick: number } | null = null;
  private readonly hooks: RemoteHooks;
  private readonly clock: RemoteClock;

  constructor(hooks: RemoteHooks, clock: RemoteClock = browserClock) {
    this.hooks = hooks;
    this.clock = clock;
  }

  /** A key went down. Answers whether the player took it. */
  keydown(key: string, repeat = false): boolean {
    const now = this.clock.now();
    const held = repeat || (key === this.down && now - this.lastDown < StaleHoldMs);
    this.down = key;
    this.lastDown = now;

    switch (key) {
      case 'MediaPlayPause': this.hooks.togglePlay(); return true;
      case 'MediaPlay': this.hooks.play(); return true;
      case 'MediaPause': this.hooks.pause(); return true;
      case 'Enter':
      case 'ArrowUp':
      case 'ArrowDown':
        if (this.hooks.shown()) return false;
        if (!held) this.hooks.reveal();
        return true;
    }

    const skip = SkipKeys[key];
    if (!skip) return false;
    if (!skip.always && this.hooks.shown() && !this.hooks.scrubFocused() && !this.scrub) return false;

    if (this.scrub) {
      if (key === this.scrub.key) this.advance(now);
      return true;
    }
    if (held) {
      this.beginScrub(key, skip.direction, now);
      return true;
    }
    this.gather(skip.direction);
    return true;
  }

  /** A key came up. Answers whether the player took it. */
  keyup(key: string): boolean {
    if (key === this.down) this.down = null;
    if (!this.scrub || key !== this.scrub.key) return false;
    const to = this.scrub.at;
    this.scrub = null;
    this.hooks.scrubbing(null);
    this.hooks.seek(to);
    return true;
  }

  /** Drops whatever is being gathered or scrubbed, sending nothing. */
  dispose(): void {
    if (this.settleTimer !== null) this.clock.clearTimeout(this.settleTimer);
    this.settleTimer = null;
    this.gathered = 0;
    if (this.scrub) this.hooks.scrubbing(null);
    this.scrub = null;
  }

  private gather(direction: Direction): void {
    this.gathered += direction * SkipSeconds;
    this.hooks.gathering(this.gathered);
    if (this.settleTimer !== null) this.clock.clearTimeout(this.settleTimer);
    this.settleTimer = this.clock.setTimeout(() => this.send(), SkipSettleMs);
  }

  private send(): void {
    this.settleTimer = null;
    const total = this.gathered;
    this.gathered = 0;
    this.hooks.gathering(null);
    if (total !== 0) this.hooks.skipBy(total);
  }

  /** A held key: whatever its first press began gathering becomes where the scrub starts. */
  private beginScrub(key: string, direction: Direction, now: number): void {
    if (this.settleTimer !== null) this.clock.clearTimeout(this.settleTimer);
    this.settleTimer = null;
    const start = this.clamp(this.hooks.position() + this.gathered);
    this.gathered = 0;
    this.hooks.gathering(null);
    this.scrub = { key, direction, at: start, since: now, tick: now };
    this.hooks.scrubbing(start);
  }

  private advance(now: number): void {
    const scrub = this.scrub!;
    const heldSeconds = (now - scrub.since) / 1000;
    const base = Math.max(ScrubFloorSeconds, this.hooks.duration() * ScrubShare);
    const speed = base * Math.min(ScrubMaxSpeedUp, 1 + heldSeconds * ScrubSpeedUpPerSecond);
    scrub.at = this.clamp(scrub.at + scrub.direction * speed * ((now - scrub.tick) / 1000));
    scrub.tick = now;
    this.hooks.scrubbing(scrub.at);
  }

  private clamp(seconds: number): number {
    return Math.min(this.hooks.duration(), Math.max(0, seconds));
  }
}
