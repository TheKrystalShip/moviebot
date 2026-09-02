import type { Chapter, ThumbnailStrip } from '../types';
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

/** How long a pointer has to rest on the bar before the film is worth magnifying. */
const DwellMs = 380;
/** A hand never holds a mouse perfectly still, and it has not moved on until it moves this far. */
const DwellSlopPx = 4;

/**
 * What the lane spans.
 *
 * Wide enough that a point made at the coarse scale lands inside it — three seconds of pointing
 * error on a two-hour bar is a couple of minutes — and narrow enough that one second is a visible
 * distance rather than a rounding error.
 */
const LaneWindowSeconds = 180;
const LaneTickSeconds = 10;
const LaneLabelSeconds = 30;
/** Narrower than this and a frame is too small to recognise a scene in, whatever the sheet holds. */
const LaneNarrowestFramePx = 90;
/** Below this the bar is already precise enough that a second bar would only be in the way. */
const LaneShortestFilmSeconds = 240;

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
  private readonly previewFrame: HTMLElement;
  private readonly previewChapter: HTMLElement;
  private readonly marks: HTMLElement;
  private readonly lane: HTMLElement;
  private readonly laneTrack: HTMLElement;
  private readonly laneStrip: HTMLElement;
  private readonly laneUnready: HTMLElement;
  private readonly lanePlayed: HTMLElement;
  private readonly laneTicks: HTMLElement;
  private readonly laneScale: HTMLElement;
  private readonly laneHandle: HTMLElement;
  private readonly laneCaret: HTMLElement;

  private durationSeconds = 0;
  private readySeconds: number | null = null;
  private positionSeconds = 0;
  private dragSeconds: number | null = null;
  private chapters: Chapter[] = [];
  private strip: ThumbnailStrip | null = null;
  private stripUrl: string | null = null;
  private showRemaining = false;
  private laneFrom = 0;
  private laneTo = 0;
  private laneDragging = false;
  private dwellTimer: number | null = null;
  private dwellAtX = Number.NaN;
  private watchOutside: ((event: PointerEvent) => void) | null = null;

  /**
   * The lane is a thing to stop and read, and a control bar that fades when nobody is moving would
   * take it away mid-read, so whoever owns the bar is told to hold it up.
   */
  constructor(
    private readonly onSeek: (seconds: number) => void,
    private readonly onMagnify: (open: boolean) => void = () => {}
  ) {
    this.el = document.createElement('div');
    this.el.className = 'mb-scrub';
    this.el.innerHTML = `
      <span class="mb-scrub__time mb-scrub__time--current">0:00</span>
      <div class="mb-scrub__track" role="slider" tabindex="0"
           aria-label="Position" aria-valuemin="0" aria-valuenow="0" aria-valuetext="0:00">
        <div class="mb-scrub__ready"></div>
        <div class="mb-scrub__played"></div>
        <div class="mb-scrub__marks"></div>
        <div class="mb-scrub__handle"></div>
        <div class="mb-scrub__preview" aria-hidden="true" hidden>
          <div class="mb-scrub__preview-frame" hidden></div>
          <span class="mb-scrub__preview-chapter" hidden></span>
          <span class="mb-scrub__preview-time"></span>
        </div>
        <div class="mb-lane" aria-hidden="true" hidden>
          <div class="mb-lane__track">
            <div class="mb-lane__strip"></div>
            <div class="mb-lane__played"></div>
            <div class="mb-lane__unready" hidden></div>
            <div class="mb-lane__ticks"></div>
            <div class="mb-lane__handle" hidden></div>
            <div class="mb-lane__caret" hidden></div>
          </div>
          <div class="mb-lane__scale"></div>
        </div>
      </div>
      <button type="button" class="mb-scrub__time mb-scrub__time--total"
              title="Show time remaining">0:00</button>`;

    this.track = this.el.querySelector('.mb-scrub__track') as HTMLElement;
    this.ready = this.el.querySelector('.mb-scrub__ready') as HTMLElement;
    this.played = this.el.querySelector('.mb-scrub__played') as HTMLElement;
    this.handle = this.el.querySelector('.mb-scrub__handle') as HTMLElement;
    this.current = this.el.querySelector('.mb-scrub__time--current') as HTMLElement;
    this.total = this.el.querySelector('.mb-scrub__time--total') as HTMLElement;
    this.preview = this.el.querySelector('.mb-scrub__preview') as HTMLElement;
    this.previewTime = this.el.querySelector('.mb-scrub__preview-time') as HTMLElement;
    this.previewFrame = this.el.querySelector('.mb-scrub__preview-frame') as HTMLElement;
    this.previewChapter = this.el.querySelector('.mb-scrub__preview-chapter') as HTMLElement;
    this.marks = this.el.querySelector('.mb-scrub__marks') as HTMLElement;
    this.lane = this.el.querySelector('.mb-lane') as HTMLElement;
    this.laneTrack = this.el.querySelector('.mb-lane__track') as HTMLElement;
    this.laneStrip = this.el.querySelector('.mb-lane__strip') as HTMLElement;
    this.laneUnready = this.el.querySelector('.mb-lane__unready') as HTMLElement;
    this.lanePlayed = this.el.querySelector('.mb-lane__played') as HTMLElement;
    this.laneTicks = this.el.querySelector('.mb-lane__ticks') as HTMLElement;
    this.laneScale = this.el.querySelector('.mb-lane__scale') as HTMLElement;
    this.laneHandle = this.el.querySelector('.mb-lane__handle') as HTMLElement;
    this.laneCaret = this.el.querySelector('.mb-lane__caret') as HTMLElement;

    this.total.addEventListener('click', () => {
      this.showRemaining = !this.showRemaining;
      this.total.title = this.showRemaining ? 'Show the full length' : 'Show time remaining';
      this.render();
    });

    this.track.addEventListener('pointerdown', (event) => this.beginDrag(event));
    this.track.addEventListener('keydown', (event) => this.onKey(event));

    // Where a seek would land, before it is made. It matters more here than in a player somebody
    // watches alone: a seek moves the whole room, so being able to read the time under the pointer
    // is the difference between choosing a moment and discovering one.
    this.track.addEventListener('pointermove', (event) => this.overBar(event.clientX));
    this.track.addEventListener('pointerleave', () => {
      this.cancelDwell();
      this.hidePreview();
    });

    // The lane is its own control over its own span, which is the whole point of it: the same
    // pointer movement is worth a fraction of the time it is worth on the bar below.
    this.laneTrack.addEventListener('pointermove', (event) => {
      event.stopPropagation();
      this.overLane(event.clientX);
    });
    this.laneTrack.addEventListener('pointerdown', (event) => this.beginLaneDrag(event));
    this.laneTrack.addEventListener('pointerleave', () => {
      if (!this.laneDragging) this.hidePreview();
    });
  }

  setDuration(seconds: number): void {
    this.closeLane();
    this.durationSeconds = seconds;
    this.track.setAttribute('aria-valuemax', seconds.toFixed(0));
    this.renderMarks();
    this.render();
  }

  /** Where the film's parts begin. Empty for a release that carries none, which is common. */
  setChapters(chapters: Chapter[]): void {
    this.chapters = [...chapters].sort((a, b) => a.startSeconds - b.startSeconds);
    this.renderMarks();
  }

  /** The sheet of preview frames, and where it is served from. */
  setThumbnails(strip: ThumbnailStrip | null, url: string | null): void {
    this.strip = strip;
    this.stripUrl = url;

    if (strip === null || url === null) {
      this.previewFrame.hidden = true;
      return;
    }

    this.previewFrame.style.width = `${strip.width}px`;
    this.previewFrame.style.height = `${strip.height}px`;
    this.previewFrame.style.backgroundImage = `url("${url}")`;
    this.previewFrame.style.backgroundSize =
      `${strip.columns * strip.width}px ${strip.rows * strip.height}px`;
  }

  /**
   * Ticks where the chapters begin.
   *
   * The one at the start is left off: every film begins at zero and a mark there only draws the
   * eye to the one place on the bar nobody needs help finding.
   */
  private renderMarks(): void {
    this.marks.replaceChildren();
    if (this.durationSeconds <= 0) return;

    for (const chapter of this.chapters) {
      if (chapter.startSeconds <= 0 || chapter.startSeconds >= this.durationSeconds) continue;

      const mark = document.createElement('div');
      mark.className = 'mb-scrub__mark';
      mark.style.left = `${(chapter.startSeconds / this.durationSeconds) * 100}%`;
      this.marks.appendChild(mark);
    }
  }

  /** The chapter a moment falls in, which is the last one to have begun by then. */
  private chapterAt(seconds: number): Chapter | undefined {
    let found: Chapter | undefined;
    for (const chapter of this.chapters) {
      if (chapter.startSeconds > seconds) break;
      found = chapter;
    }
    return found;
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
    this.total.textContent = this.showRemaining
      ? `-${formatTime(Math.max(0, this.durationSeconds - this.shown))}`
      : formatTime(this.durationSeconds);
    this.track.setAttribute('aria-valuenow', this.shown.toFixed(0));
    this.track.setAttribute('aria-valuetext', formatTime(this.shown));
    this.renderLane();
  }

  /**
   * Draws the time under the pointer.
   *
   * Past the transcode head it is drawn as unreachable, because a seek there is refused by the
   * server and the refusal reaches only the person who tried. Saying so before the click is a
   * better answer than explaining it after.
   */
  private showPreview(clientX: number, over: HTMLElement = this.track, from = 0,
                      to = this.durationSeconds, withFrame = true): void {
    if (this.durationSeconds <= 0) return;

    const box = over.getBoundingClientRect();
    const at = this.secondsIn(over, clientX, from, to);

    this.previewTime.textContent = formatTime(at);
    this.preview.classList.toggle(
      'mb-scrub__preview--unreachable', this.readySeconds !== null && at > this.readySeconds);

    const chapter = this.chapterAt(at);
    this.previewChapter.textContent = chapter?.title ?? '';
    this.previewChapter.hidden = chapter?.title === undefined;

    // The lane draws its own frames, and a second copy of the nearest one laid over them hides the
    // very thing somebody opened the lane to look at.
    this.previewFrame.hidden = true;
    if (this.strip !== null && withFrame) {
      // The window over the sheet, rather than a frame fetched for the moment under the pointer.
      const index = Math.min(this.strip.count - 1, Math.max(0, Math.floor(at / this.strip.intervalSeconds)));
      const column = index % this.strip.columns;
      const row = Math.floor(index / this.strip.columns);

      this.previewFrame.style.backgroundPosition =
        `-${column * this.strip.width}px -${row * this.strip.height}px`;
      this.previewFrame.hidden = false;
    }

    // Clamped to whichever bar it is being drawn against, so it never hangs off either end, and
    // offset from that bar's own left edge rather than the element the bubble hangs in.
    const host = (this.preview.offsetParent as HTMLElement | null) ?? this.track;
    const half = this.preview.offsetWidth / 2;
    const inside = Math.min(box.width - half, Math.max(half, clientX - box.left));
    const offset = inside + box.left - host.getBoundingClientRect().left;

    this.preview.style.left = `${offset}px`;
    this.preview.hidden = false;
  }

  private hidePreview(): void {
    // A drag holds the pointer, so it keeps the preview even when it leaves the bar.
    if (this.dragSeconds === null) this.preview.hidden = true;
  }

  /** Where a horizontal position falls in the span some bar is drawn over. */
  private secondsIn(over: HTMLElement, clientX: number, from: number, to: number): number {
    const box = over.getBoundingClientRect();
    const ratio = box.width === 0 ? 0 : (clientX - box.left) / box.width;
    return Math.min(to, Math.max(from, from + ratio * (to - from)));
  }

  private secondsAt(clientX: number): number {
    return this.secondsIn(this.track, clientX, 0, this.durationSeconds);
  }

  /**
   * The pointer over the coarse bar, which is where the lane is asked for.
   *
   * While the lane is open the bar's job is to say which part of those three minutes it is
   * pointing at, and to move the window when it is pointing outside them. The window holds still
   * inside itself deliberately: a lane that slid under every pixel of travel would be a thing to
   * chase rather than a thing to read.
   */
  private overBar(clientX: number): void {
    if (this.laneOpen) {
      const at = this.secondsAt(clientX);

      // Out of the neighbourhood it was opened over, and magnifying somewhere nobody is looking.
      if (at < this.laneFrom || at > this.laneTo) this.centreLane(at);

      this.hidePreview();
      this.markCaret(at);
      return;
    }

    this.armDwell(clientX);
    this.showPreview(clientX);
  }

  private get laneOpen(): boolean {
    return !this.lane.hidden;
  }

  /**
   * Arms the rest that opens the lane.
   *
   * A pointer crossing the bar on its way somewhere else never rests, and one being aimed always
   * does, which is what makes resting the thing to watch for rather than a button to press.
   */
  private armDwell(clientX: number): void {
    if (this.dragSeconds !== null) return;
    if (this.durationSeconds < LaneShortestFilmSeconds) return;

    // Still resting: leave the timer that is already running alone, or it never finishes.
    if (this.dwellTimer !== null && Math.abs(clientX - this.dwellAtX) <= DwellSlopPx) return;

    this.cancelDwell();
    this.dwellAtX = clientX;
    this.dwellTimer = window.setTimeout(() => {
      this.dwellTimer = null;
      this.openLane(this.secondsAt(this.dwellAtX));
    }, DwellMs);
  }

  private cancelDwell(): void {
    if (this.dwellTimer === null) return;

    window.clearTimeout(this.dwellTimer);
    this.dwellTimer = null;
    this.dwellAtX = Number.NaN;
  }

  /**
   * Opens the lane over the minutes around a moment.
   *
   * It is a second bar rather than the first one rescaled. The bar below never changes what a
   * pixel is worth, so nothing anybody already knows how to do behaves differently while this is
   * on screen, and there is no state to be caught in: leave the neighbourhood and it is gone.
   */
  private openLane(centre: number): void {
    if (this.durationSeconds < LaneShortestFilmSeconds) return;

    this.lane.hidden = false;
    this.el.classList.add('mb-scrub--magnified');
    this.onMagnify(true);
    this.hidePreview();
    this.centreLane(centre);
    this.markCaret(centre);

    // Leaving upward crosses out of the bar and into nothing the bar hears about, so where the
    // pointer actually is decides this rather than which element it left.
    this.watchOutside = (event) => {
      if (this.laneDragging) return;
      if (this.within(this.track, event) || this.within(this.lane, event)) return;
      this.closeLane();
    };
    document.addEventListener('pointermove', this.watchOutside);
  }

  /** Puts the window around a moment, clamped so it always describes film that exists. */
  private centreLane(centre: number): void {
    const span = Math.min(LaneWindowSeconds, this.durationSeconds);

    this.laneFrom = Math.min(this.durationSeconds - span, Math.max(0, centre - span / 2));
    this.laneTo = this.laneFrom + span;
    this.place();
    this.renderLaneStrip();
    this.renderLaneScale();
    this.renderLane();
  }

  /**
   * Puts the lane over the part of the bar it is magnifying. It is narrower than the bar — a strip
   * of frames across a whole screen is a wall rather than a thing to read — so where it sits says
   * which minutes of the film are in it.
   */
  private place(): void {
    const trackWidth = this.track.clientWidth;
    const laneWidth = this.lane.offsetWidth;
    const centre = ((this.laneFrom + this.laneTo) / 2 / this.durationSeconds) * trackWidth;

    this.lane.style.left =
      `${Math.min(trackWidth - laneWidth, Math.max(0, centre - laneWidth / 2))}px`;
  }

  private closeLane(): void {
    if (!this.laneOpen) return;

    this.lane.hidden = true;
    this.laneCaret.hidden = true;
    this.el.classList.remove('mb-scrub--magnified');
    this.onMagnify(false);
    this.movePreview(this.track);
    this.hidePreview();

    if (this.watchOutside !== null) {
      document.removeEventListener('pointermove', this.watchOutside);
      this.watchOutside = null;
    }
  }

  private within(el: HTMLElement, event: PointerEvent): boolean {
    const box = el.getBoundingClientRect();
    return event.clientX >= box.left && event.clientX <= box.right
      && event.clientY >= box.top && event.clientY <= box.bottom;
  }

  /** The bubble hangs in whichever bar the pointer is over, so it is always the nearer one. */
  private movePreview(host: HTMLElement): void {
    if (this.preview.parentElement === host) return;

    this.preview.hidden = true;
    host.appendChild(this.preview);
  }

  /** Which part of the magnified minute the coarse bar is pointing at. */
  private markCaret(at: number): void {
    this.laneCaret.hidden = false;
    this.laneCaret.style.left = `${((at - this.laneFrom) / (this.laneTo - this.laneFrom)) * 100}%`;
  }

  private overLane(clientX: number): void {
    this.laneCaret.hidden = true;
    this.movePreview(this.lane);
    this.showPreview(clientX, this.laneTrack, this.laneFrom, this.laneTo, false);
  }

  /** The lane's fills and its playhead, which are the bar's own drawn over a minute of it. */
  private renderLane(): void {
    if (!this.laneOpen) return;

    const span = this.laneTo - this.laneFrom;
    const place = (at: number) =>
      `${Math.min(100, Math.max(0, ((at - this.laneFrom) / span) * 100))}%`;

    this.lanePlayed.style.width = place(this.shown);

    // The part of the window the transcode has not written, which a seek into is refused.
    this.laneUnready.hidden = this.readySeconds === null || this.readySeconds >= this.laneTo;
    if (!this.laneUnready.hidden) this.laneUnready.style.left = place(this.readySeconds ?? 0);

    const inside = this.shown >= this.laneFrom && this.shown <= this.laneTo;
    this.laneHandle.hidden = !inside;
    if (inside) this.laneHandle.style.left = place(this.shown);
  }

  /**
   * The frames of the window, side by side.
   *
   * The sheet holds one frame every twenty or thirty seconds, so this is a sample of the window
   * rather than every moment in it — which is what a filmstrip is. It is the reason the lane is
   * worth opening: a ruler says which second is under the pointer, and only a picture says whether
   * that second is the one somebody is looking for.
   */
  private renderLaneStrip(): void {
    this.laneStrip.replaceChildren();

    const strip = this.strip;
    const width = this.laneTrack.clientWidth;
    if (strip === null || this.stripUrl === null || width <= 0) {
      this.laneTrack.style.removeProperty('height');
      return;
    }

    const span = this.laneTo - this.laneFrom;
    // As many as the sheet actually has in the window. Drawing more only repeats one, and drawing
    // fewer throws away the only frames there are.
    const count = Math.max(3, Math.min(8,
      Math.min(Math.round(span / strip.intervalSeconds), Math.floor(width / LaneNarrowestFramePx))));
    const cell = width / count;
    const cellHeight = Math.round((cell * strip.height) / strip.width);

    // The track is as tall as the frames in it: the sheet's shape decides this, not a number here
    // that would letterbox one aspect ratio to suit another.
    this.laneTrack.style.height = `${cellHeight}px`;

    for (let i = 0; i < count; i += 1) {
      const at = this.laneFrom + ((i + 0.5) / count) * span;
      const index = Math.min(strip.count - 1,
        Math.max(0, Math.floor(at / strip.intervalSeconds)));

      const frame = document.createElement('div');
      frame.className = 'mb-lane__frame';
      frame.style.left = `${i * cell}px`;
      frame.style.width = `${cell}px`;
      frame.style.height = `${cellHeight}px`;
      frame.style.backgroundImage = `url("${this.stripUrl}")`;
      frame.style.backgroundSize = `${strip.columns * cell}px ${strip.rows * cellHeight}px`;
      frame.style.backgroundPosition =
        `-${(index % strip.columns) * cell}px -${Math.floor(index / strip.columns) * cellHeight}px`;

      this.laneStrip.appendChild(frame);
    }
  }

  /**
   * The ruler: a tick every ten seconds, a time every thirty, and the chapters that begin inside
   * the window. It is what makes the lane a magnification rather than a wider bar.
   */
  private renderLaneScale(): void {
    this.laneTicks.replaceChildren();
    this.laneScale.replaceChildren();

    const span = this.laneTo - this.laneFrom;
    if (span <= 0) return;

    for (let at = Math.ceil(this.laneFrom / LaneTickSeconds) * LaneTickSeconds;
         at <= this.laneTo; at += LaneTickSeconds) {
      const left = ((at - this.laneFrom) / span) * 100;
      const major = at % LaneLabelSeconds === 0;

      const tick = document.createElement('div');
      tick.className = major ? 'mb-lane__tick is-major' : 'mb-lane__tick';
      tick.style.left = `${left}%`;
      this.laneTicks.appendChild(tick);

      // A time centred on the very edge is drawn half outside the lane, and the two beside it say
      // where that end is anyway.
      if (!major || left < 4 || left > 96) continue;

      const label = document.createElement('span');
      label.className = 'mb-lane__label';
      label.style.left = `${left}%`;
      label.textContent = formatTime(at);
      this.laneScale.appendChild(label);
    }

    for (const chapter of this.chapters) {
      if (chapter.startSeconds <= this.laneFrom || chapter.startSeconds >= this.laneTo) continue;

      const mark = document.createElement('div');
      mark.className = 'mb-lane__chapter';
      mark.style.left = `${((chapter.startSeconds - this.laneFrom) / span) * 100}%`;
      this.laneTicks.appendChild(mark);
    }
  }

  /**
   * A drag inside the lane. It publishes the same intent the bar does and the server answers it
   * the same way; the only difference is how much of the film a pixel of it was worth.
   */
  private beginLaneDrag(event: PointerEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.cancelDwell();
    this.laneDragging = true;
    this.laneTrack.setPointerCapture(event.pointerId);
    this.dragSeconds = this.secondsIn(this.laneTrack, event.clientX, this.laneFrom, this.laneTo);
    this.el.classList.add('mb-scrub--dragging');
    this.overLane(event.clientX);
    this.render();

    const move = (moved: PointerEvent) => {
      this.dragSeconds = this.secondsIn(this.laneTrack, moved.clientX, this.laneFrom, this.laneTo);
      this.overLane(moved.clientX);
      this.render();
    };

    const finish = (ended: PointerEvent) => {
      this.laneTrack.removeEventListener('pointermove', move);
      this.laneTrack.removeEventListener('pointerup', finish);
      this.laneTrack.removeEventListener('pointercancel', finish);
      this.laneTrack.releasePointerCapture(ended.pointerId);

      const target = this.secondsIn(this.laneTrack, ended.clientX, this.laneFrom, this.laneTo);
      this.dragSeconds = null;
      this.laneDragging = false;
      this.el.classList.remove('mb-scrub--dragging');
      this.closeLane();
      this.render();
      this.onSeek(target);
    };

    this.laneTrack.addEventListener('pointermove', move);
    this.laneTrack.addEventListener('pointerup', finish);
    this.laneTrack.addEventListener('pointercancel', finish);
  }

  private beginDrag(event: PointerEvent): void {
    if (this.durationSeconds <= 0) return;
    event.preventDefault();
    this.cancelDwell();
    this.closeLane();
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
