/** Long enough to read, short enough that it is gone before it is in the way. */
const VisibleMs = 460;

const Arrows = `<svg class="mb-seek__arrows" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
  <path fill="currentColor" d="M4 5.2 11 12l-7 6.8V5.2zm8.6 0L19.6 12l-7 6.8V5.2z"/>
</svg>`;

/**
 * What a key press did to the playhead, said in the middle of the film.
 *
 * A seek from the keyboard otherwise leaves nothing behind. The scrub bar is at the bottom of a
 * screen nobody is looking at while the film is playing, and five seconds inside a scene can pass
 * without a visible cut, so the press and the shrug that follows it are indistinguishable from a
 * key that never registered.
 *
 * It answers the press rather than the seek. The film moves when the server says so, and this is
 * gone well before that: it reports what was asked for, which is the thing that was in doubt.
 *
 * Presses inside one window are one gesture and are summed, so a key held down reads as a single
 * growing number rather than the same one flashing over and over.
 */
export class SeekFlash {
  readonly el: HTMLElement;

  private readonly amount: HTMLElement;
  private timer: number | null = null;
  private total = 0;

  constructor() {
    this.el = document.createElement('div');
    this.el.className = 'mb-seek';
    this.el.hidden = true;
    this.el.setAttribute('role', 'status');
    this.el.innerHTML = `${Arrows}<span class="mb-seek__amount"></span>`;

    this.amount = this.el.querySelector('.mb-seek__amount') as HTMLElement;
  }

  /** Whole seconds, signed. A seek that moved nothing says nothing. */
  show(seconds: number): void {
    const step = Math.round(seconds);
    if (step === 0) return;

    // Reversing is a new gesture: after five forward and five back the film is where it started,
    // and a badge reading zero would be the one thing that is never worth showing.
    if (this.timer === null || Math.sign(step) !== Math.sign(this.total)) this.total = 0;
    this.total += step;

    this.el.dataset.direction = this.total < 0 ? 'back' : 'forward';
    this.amount.textContent = `${this.total < 0 ? '−' : '+'}${Math.abs(this.total)}s`;

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
    }, VisibleMs);
  }
}
