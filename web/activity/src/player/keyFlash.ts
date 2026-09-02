/** Long enough to read, short enough that it is gone before it is in the way. */
const VisibleMs = 460;

/** A name and a verb take longer to read than a number, and nothing is waiting behind them. */
const ActionVisibleMs = 1400;

const Arrows = `<svg class="mb-flash__glyph mb-flash__arrows" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
  <path fill="currentColor" d="M4 5.2 11 12l-7 6.8V5.2zm8.6 0L19.6 12l-7 6.8V5.2z"/>
</svg>`;

const Speaker = `<svg class="mb-flash__glyph mb-flash__speaker" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
  <path fill="currentColor" d="M4 9v6h4l5 4V5L8 9H4zm12.5 3a4.5 4.5 0 0 0-2.5-4v8a4.5 4.5 0 0 0 2.5-4zM14 2.2v2.1a7.7 7.7 0 0 1 0 15.4v2.1a9.8 9.8 0 0 0 0-19.6z"/>
</svg>`;

const Silence = `<svg class="mb-flash__glyph mb-flash__speaker" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
  <path fill="currentColor" d="M4 9v6h4l5 4V5L8 9H4zm15.5 3 2.3-2.3-1.2-1.2-2.3 2.3-2.3-2.3-1.2 1.2 2.3 2.3-2.3 2.3 1.2 1.2 2.3-2.3 2.3 2.3 1.2-1.2z"/>
</svg>`;

type Kind = 'seek' | 'volume' | 'action';

/**
 * What a key press or a wheel turn did, said in the middle of the film.
 *
 * A seek from the keyboard otherwise leaves nothing behind. The scrub bar is at the bottom of a
 * screen nobody is looking at while the film is playing, and five seconds inside a scene can pass
 * without a visible cut, so the press and the shrug that follows it are indistinguishable from a
 * key that never registered. A volume step is the same: the slider is on the same bar, and one
 * step is not something an ear can be sure it heard.
 *
 * A seek answers the press rather than the seek. The film moves when the server says so, and this
 * is gone well before that: it reports what was asked for, which is the thing that was in doubt.
 * Volume is this viewer's own, so what is shown is where it landed, with the step beside it.
 *
 * Presses inside one window are one gesture and are summed, so a key held down reads as a single
 * growing number rather than the same one flashing over and over. A gesture of the other kind, or
 * in the other direction, is a new one.
 *
 * What somebody else did to the room is said here too, in words: who paused, who resumed, who
 * moved the film and to where. The film reacting is the only other sign, and a pause looks like
 * a stall while a seek inside the scene looks like nothing at all.
 */
export class KeyFlash {
  readonly el: HTMLElement;

  private readonly glyph: HTMLElement;
  private readonly amount: HTMLElement;
  private readonly level: HTMLElement;
  private timer: number | null = null;
  private kind: Kind | null = null;
  private total = 0;

  constructor() {
    this.el = document.createElement('div');
    this.el.className = 'mb-flash';
    this.el.hidden = true;
    this.el.setAttribute('role', 'status');
    this.el.innerHTML = `<span class="mb-flash__icon"></span><span class="mb-flash__amount"></span><span class="mb-flash__level"></span>`;

    this.glyph = this.el.querySelector('.mb-flash__icon') as HTMLElement;
    this.amount = this.el.querySelector('.mb-flash__amount') as HTMLElement;
    this.level = this.el.querySelector('.mb-flash__level') as HTMLElement;
  }

  /** Whole seconds, signed. A seek that moved nothing says nothing. */
  seek(seconds: number): void {
    const step = Math.round(seconds);
    if (step === 0) return;

    this.accumulate('seek', step);
    this.glyph.innerHTML = Arrows;
    this.el.dataset.direction = this.total < 0 ? 'back' : 'forward';
    this.amount.textContent = `${this.total < 0 ? '−' : '+'}${Math.abs(this.total)}s`;
    this.level.textContent = '';
    this.reveal();
  }

  /**
   * A step in loudness, as whole percentage points, and where the volume now stands. A step that
   * moved nothing — the top of the range, or the bottom — still shows where it is, because that
   * is the answer to why the key did nothing.
   */
  volume(step: number, position: number, muted: boolean): void {
    const points = Math.round(step * 100);
    const at = Math.round(position * 100);

    this.accumulate('volume', points);
    this.glyph.innerHTML = muted || at === 0 ? Silence : Speaker;
    this.el.dataset.direction = this.total < 0 ? 'down' : 'up';
    this.amount.textContent = muted ? 'Muted' : `${at}%`;
    this.level.textContent = this.total === 0 || muted ? '' : `${this.total < 0 ? '−' : '+'}${Math.abs(this.total)}%`;
    this.reveal();
  }

  /**
   * Reversing is a new gesture: after five forward and five back the film is where it started,
   * and a badge reading zero would be the one thing that is never worth showing.
   */
  private accumulate(kind: Kind, step: number): void {
    if (this.timer === null || kind !== this.kind || Math.sign(step) !== Math.sign(this.total)) this.total = 0;
    this.kind = kind;
    this.total += step;
    this.el.dataset.kind = kind;
  }

  /** Somebody's act on the room, as a sentence. Shown for longer, because it has to be read. */
  action(text: string): void {
    if (this.timer !== null) window.clearTimeout(this.timer);
    this.total = 0;
    this.kind = 'action';
    this.el.dataset.kind = 'action';
    delete this.el.dataset.direction;
    this.glyph.innerHTML = '';
    this.amount.textContent = text;
    this.level.textContent = '';
    this.reveal(ActionVisibleMs);
  }

  private reveal(visibleMs = VisibleMs): void {
    this.el.hidden = false;

    // Taking the class off and reading a layout value puts the animation back at its first frame.
    // Without the read the browser coalesces both writes and the second press changes nothing.
    this.el.classList.remove('is-on');
    void this.el.offsetWidth;
    this.el.classList.add('is-on');

    if (this.timer !== null) window.clearTimeout(this.timer);
    this.timer = window.setTimeout(() => {
      this.el.hidden = true;
      this.el.classList.remove('is-on');
      this.timer = null;
      this.total = 0;
      this.kind = null;
    }, visibleMs);
  }
}
