import { formatTime } from '../ui/format';

/**
 * The film's own scrub bar.
 *
 * The player library only knows what the playlist advertises, and an EVENT playlist advertises
 * what has been written so far, so its bar shrinks a three-hour film to whatever the transcode
 * has reached. This one is drawn at the true duration from the manifest with the transcoded
 * region shaded behind the played region, which makes "still preparing" legible without a
 * sentence explaining it.
 *
 * Dragging never moves the playhead. It publishes a seek and waits for the server's answer,
 * because clamping here would split the room's timeline against a head that is always a little
 * stale.
 */
const KeyboardStepSeconds = 5;

export class ScrubBar {
  readonly el: HTMLElement;

  private readonly track: HTMLElement;
  private readonly ready: HTMLElement;
  private readonly played: HTMLElement;
  private readonly handle: HTMLElement;
  private readonly current: HTMLElement;
  private readonly total: HTMLElement;
  private readonly preview: HTMLElement;
  private readonly previewTime: HTMLElement;

  private durationSeconds = 0;
  private readySeconds: number | null = null;
  private positionSeconds = 0;
  private dragSeconds: number | null = null;

  constructor(private readonly onSeek: (seconds: number) => void) {
    this.el = document.createElement('div');
    this.el.className = 'mb-scrub';
    this.el.innerHTML = `
      <span class="mb-scrub__time mb-scrub__time--current">0:00</span>
      <div class="mb-scrub__track" role="slider" tabindex="0"
           aria-label="Position" aria-valuemin="0" aria-valuenow="0" aria-valuetext="0:00">
        <div class="mb-scrub__ready"></div>
        <div class="mb-scrub__played"></div>
        <div class="mb-scrub__handle"></div>
        <div class="mb-scrub__preview" aria-hidden="true" hidden>
          <span class="mb-scrub__preview-time"></span>
        </div>
      </div>
      <span class="mb-scrub__time mb-scrub__time--total">0:00</span>`;

    this.track = this.el.querySelector('.mb-scrub__track') as HTMLElement;
    this.ready = this.el.querySelector('.mb-scrub__ready') as HTMLElement;
    this.played = this.el.querySelector('.mb-scrub__played') as HTMLElement;
    this.handle = this.el.querySelector('.mb-scrub__handle') as HTMLElement;
    this.current = this.el.querySelector('.mb-scrub__time--current') as HTMLElement;
    this.total = this.el.querySelector('.mb-scrub__time--total') as HTMLElement;
    this.preview = this.el.querySelector('.mb-scrub__preview') as HTMLElement;
    this.previewTime = this.el.querySelector('.mb-scrub__preview-time') as HTMLElement;

    this.track.addEventListener('pointerdown', (event) => this.beginDrag(event));
    this.track.addEventListener('keydown', (event) => this.onKey(event));

    // Where a seek would land, before it is made. It matters more here than in a player somebody
    // watches alone: a seek moves the whole room, so being able to read the time under the pointer
    // is the difference between choosing a moment and discovering one.
    this.track.addEventListener('pointermove', (event) => this.showPreview(event.clientX));
    this.track.addEventListener('pointerleave', () => this.hidePreview());
  }

  setDuration(seconds: number): void {
    this.durationSeconds = seconds;
    this.total.textContent = formatTime(seconds);
    this.track.setAttribute('aria-valuemax', seconds.toFixed(0));
    this.render();
  }

  /** How far the transcode has reached, or null once the whole film is written. */
  setReady(seconds: number | null): void {
    this.readySeconds = seconds;
    this.render();
  }

  setPosition(seconds: number): void {
    this.positionSeconds = seconds;
    this.render();
  }

  private get shown(): number {
    return this.dragSeconds ?? this.positionSeconds;
  }

  private render(): void {
    const duration = this.durationSeconds > 0 ? this.durationSeconds : 1;
    const fraction = (value: number) => `${Math.min(100, Math.max(0, (value / duration) * 100))}%`;

    this.ready.style.width = this.readySeconds === null ? '100%' : fraction(this.readySeconds);
    this.el.classList.toggle('mb-scrub--transcoding', this.readySeconds !== null);
    this.played.style.width = fraction(this.shown);
    this.handle.style.left = fraction(this.shown);
    this.current.textContent = formatTime(this.shown);
    this.track.setAttribute('aria-valuenow', this.shown.toFixed(0));
    this.track.setAttribute('aria-valuetext', formatTime(this.shown));
  }

  /**
   * Draws the time under the pointer.
   *
   * Past the transcode head it is drawn as unreachable, because a seek there is refused by the
   * server and the refusal reaches only the person who tried. Saying so before the click is a
   * better answer than explaining it after.
   */
  private showPreview(clientX: number): void {
    if (this.durationSeconds <= 0) return;

    const box = this.track.getBoundingClientRect();
    const at = this.secondsAt(clientX);

    this.previewTime.textContent = formatTime(at);
    this.preview.classList.toggle(
      'mb-scrub__preview--unreachable', this.readySeconds !== null && at > this.readySeconds);

    // Clamped to the bar so the bubble never hangs off either end of it.
    const half = this.preview.offsetWidth / 2;
    const offset = Math.min(box.width - half, Math.max(half, clientX - box.left));

    this.preview.style.left = `${offset}px`;
    this.preview.hidden = false;
  }

  private hidePreview(): void {
    // A drag holds the pointer, so it keeps the preview even when it leaves the bar.
    if (this.dragSeconds === null) this.preview.hidden = true;
  }

  private secondsAt(clientX: number): number {
    const box = this.track.getBoundingClientRect();
    const ratio = box.width === 0 ? 0 : (clientX - box.left) / box.width;
    return Math.min(this.durationSeconds, Math.max(0, ratio * this.durationSeconds));
  }

  private beginDrag(event: PointerEvent): void {
    if (this.durationSeconds <= 0) return;
    event.preventDefault();
    this.track.setPointerCapture(event.pointerId);
    this.dragSeconds = this.secondsAt(event.clientX);
    this.el.classList.add('mb-scrub--dragging');
    this.showPreview(event.clientX);
    this.render();

    const move = (moved: PointerEvent) => {
      this.dragSeconds = this.secondsAt(moved.clientX);
      this.showPreview(moved.clientX);
      this.render();
    };

    const finish = (ended: PointerEvent) => {
      this.track.removeEventListener('pointermove', move);
      this.track.removeEventListener('pointerup', finish);
      this.track.removeEventListener('pointercancel', finish);
      this.track.releasePointerCapture(ended.pointerId);
      const target = this.secondsAt(ended.clientX);
      this.dragSeconds = null;
      this.el.classList.remove('mb-scrub--dragging');
      this.hidePreview();
      this.render();
      this.onSeek(target);
    };

    this.track.addEventListener('pointermove', move);
    this.track.addEventListener('pointerup', finish);
    this.track.addEventListener('pointercancel', finish);
  }

  private onKey(event: KeyboardEvent): void {
    const step = (delta: number) => {
      event.preventDefault();
      this.onSeek(Math.min(this.durationSeconds, Math.max(0, this.positionSeconds + delta)));
    };

    if (event.key === 'ArrowRight') step(KeyboardStepSeconds);
    else if (event.key === 'ArrowLeft') step(-KeyboardStepSeconds);
    else if (event.key === 'Home') step(-this.positionSeconds);
    else if (event.key === 'End') step(this.durationSeconds - this.positionSeconds);
  }
}
