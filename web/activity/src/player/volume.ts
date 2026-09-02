/**
 * The volume control, and the curve behind it.
 *
 * `HTMLMediaElement.volume` is a linear amplitude multiplier, and loudness is not linear in
 * amplitude. Halving amplitude is about -6 dB, which people hear as roughly two thirds as loud,
 * so a slider wired straight to that property spends its lower half doing almost nothing and its
 * top quarter doing most of the work.
 *
 * Perceived loudness rises roughly with the 0.6 power of sound pressure, so amplitude has to rise
 * with the 1/0.6 power of the loudness that was asked for. At half the slider that gives
 * 0.5^(5/3) ≈ 0.315, which is about -10 dB — the accepted figure for half as loud.
 *
 * The slider therefore holds the loudness a person asked for, and the media element is given the
 * amplitude that produces it. The two are different numbers on purpose.
 */

const LoudnessExponent = 5 / 3;

/** Slider position (0..1, the loudness asked for) to media amplitude. */
export function amplitudeFor(position: number): number {
  const clamped = Math.min(1, Math.max(0, position));
  return clamped === 0 ? 0 : Math.pow(clamped, LoudnessExponent);
}

/** The inverse, for reading a stored amplitude back onto the slider. */
export function positionFor(amplitude: number): number {
  const clamped = Math.min(1, Math.max(0, amplitude));
  return clamped === 0 ? 0 : Math.pow(clamped, 1 / LoudnessExponent);
}

export interface VolumeHooks {
  /** The loudness the person asked for, and whether they silenced it. */
  onChange(position: number, muted: boolean): void;
}

/**
 * Replaces the player library's own volume panel, which has no way to hold a position that
 * differs from the amplitude it sets.
 */
export class VolumeControl {
  readonly el: HTMLElement;

  private readonly button: HTMLButtonElement;
  private readonly track: HTMLElement;
  private readonly fill: HTMLElement;

  private position = 1;
  private muted = false;
  private dragging = false;

  constructor(private readonly hooks: VolumeHooks) {
    this.el = document.createElement('div');
    this.el.className = 'mb-volume';
    this.el.innerHTML = `
      <button type="button" class="mb-volume__mute" aria-label="Mute">
        <svg class="mb-volume__on" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <path fill="currentColor" d="M4 9v6h4l5 4V5L8 9H4zm12.5 3a4.5 4.5 0 0 0-2.5-4v8a4.5 4.5 0 0 0 2.5-4zM14 2.2v2.1a7.7 7.7 0 0 1 0 15.4v2.1a9.8 9.8 0 0 0 0-19.6z"/>
        </svg>
        <svg class="mb-volume__off" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <path fill="currentColor" d="M4 9v6h4l5 4V5L8 9H4zm15.5 3 2.3-2.3-1.2-1.2-2.3 2.3-2.3-2.3-1.2 1.2 2.3 2.3-2.3 2.3 1.2 1.2 2.3-2.3 2.3 2.3 1.2-1.2z"/>
        </svg>
      </button>
      <div class="mb-volume__track" role="slider" tabindex="0" aria-label="Volume"
           aria-valuemin="0" aria-valuemax="100" aria-valuenow="100">
        <div class="mb-volume__fill"></div>
      </div>
    `;

    this.button = this.el.querySelector('.mb-volume__mute') as HTMLButtonElement;
    this.track = this.el.querySelector('.mb-volume__track') as HTMLElement;
    this.fill = this.el.querySelector('.mb-volume__fill') as HTMLElement;

    this.button.addEventListener('click', () => this.setMuted(!this.muted));

    const positionFromEvent = (event: PointerEvent) => {
      const box = this.track.getBoundingClientRect();
      return Math.min(1, Math.max(0, (event.clientX - box.left) / box.width));
    };

    this.track.addEventListener('pointerdown', (event) => {
      this.dragging = true;
      this.track.setPointerCapture(event.pointerId);
      this.apply(positionFromEvent(event), false);
    });
    this.track.addEventListener('pointermove', (event) => {
      if (this.dragging) this.apply(positionFromEvent(event), false);
    });
    this.track.addEventListener('pointerup', (event) => {
      this.dragging = false;
      this.track.releasePointerCapture(event.pointerId);
    });

    this.track.addEventListener('keydown', (event) => {
      const step = event.key === 'ArrowRight' || event.key === 'ArrowUp' ? 0.05
        : event.key === 'ArrowLeft' || event.key === 'ArrowDown' ? -0.05
        : 0;
      if (step === 0) return;
      event.preventDefault();
      this.apply(this.position + step, false);
    });

    this.render();
  }

  /** Restores a remembered setting without reporting it back as a fresh choice. */
  set(position: number, muted: boolean): void {
    this.position = Math.min(1, Math.max(0, position));
    this.muted = muted;
    this.render();
  }

  /** Reported back as a fresh choice, because it is one — it just came from a key.  */
  toggleMute(): void {
    this.setMuted(!this.muted);
  }

  private setMuted(muted: boolean): void {
    this.muted = muted;
    this.render();
    this.hooks.onChange(this.position, this.muted);
  }

  private apply(position: number, silent: boolean): void {
    this.position = Math.min(1, Math.max(0, position));
    // Touching the slider is how a person unmutes; leaving it silent would look broken.
    if (this.position > 0) this.muted = false;
    this.render();
    if (!silent) this.hooks.onChange(this.position, this.muted);
  }

  private render(): void {
    const shown = this.muted ? 0 : this.position;
    this.fill.style.width = `${shown * 100}%`;
    this.track.setAttribute('aria-valuenow', String(Math.round(shown * 100)));
    this.button.dataset.state = this.muted || shown === 0 ? 'muted' : 'on';
    this.button.setAttribute('aria-label', this.muted ? 'Unmute' : 'Mute');
  }
}
