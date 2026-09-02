import type { SubtitleCandidate, SubtitleSearch, SubtitleTrack } from '../types';

export interface SubtitleMenuHooks {
  /** Null turns subtitles off. */
  select(id: string | null): void;
  /** Held open, the player must not hide its controls. */
  toggle(open: boolean): void;
  /** Costs no allowance, so it runs whenever the menu opens without one. */
  search(): Promise<SubtitleSearch>;
  /** Spends one of the day's downloads and adds the track for the whole room. */
  fetch(fileId: number): Promise<SubtitleTrack>;
  pin(trackId: string): Promise<void>;
  unpin(trackId: string): Promise<void>;
}

/** A short note beside a row, and how loudly to say it. */
interface Note {
  text: string;
  tone: 'good' | 'warn' | 'bad';
}

/**
 * The subtitle picker.
 *
 * Its own component rather than the shared track menu, because what it does is a different thing:
 * it searches, waits, fails, spends a limited allowance and records a judgement. The audio menu
 * needs none of that and would carry all of it.
 *
 * Two sections. What the room already has is rendered at once and never waits on the network.
 * What the index offers is fetched underneath it, and only when nobody has confirmed a track for
 * this film yet — once somebody has, there is nothing to go looking for.
 */
export class SubtitleMenu {
  readonly el: HTMLElement;

  private readonly button: HTMLButtonElement;
  private readonly popup: HTMLElement;

  private tracks: SubtitleTrack[] = [];
  private selected: string | null = null;
  private found: SubtitleSearch | null = null;
  private searching = false;
  private failure: string | null = null;
  private busy: number | null = null;

  constructor(private readonly hooks: SubtitleMenuHooks) {
    this.el = document.createElement('div');
    this.el.className = 'mb-menu mb-subs';
    this.el.innerHTML = `
      <button type="button" class="mb-menu__button" aria-haspopup="true" aria-expanded="false">
        <span class="mb-menu__title">Subtitles</span>
        <span class="mb-menu__value"></span>
      </button>
      <div class="mb-menu__popup" hidden></div>`;

    this.button = this.el.querySelector('.mb-menu__button') as HTMLButtonElement;
    this.popup = this.el.querySelector('.mb-menu__popup') as HTMLElement;

    this.button.addEventListener('click', (event) => {
      event.stopPropagation();
      this.open(this.popup.hidden);
    });

    document.addEventListener('click', (event) => {
      if (!this.el.contains(event.target as Node)) this.open(false);
    });

    this.el.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        this.open(false);
        this.button.focus();
      }
    });
  }

  setTracks(tracks: SubtitleTrack[], selected: string | null): void {
    this.tracks = tracks;
    this.selected = selected;
    this.render();
  }

  private get pinnedExists(): boolean {
    return this.tracks.some((track) => track.pin !== undefined);
  }

  private open(open: boolean): void {
    if (this.popup.hidden === !open) return;

    this.popup.hidden = !open;
    this.button.setAttribute('aria-expanded', String(open));
    this.el.classList.toggle('mb-menu--open', open);
    this.hooks.toggle(open);

    // Nothing to look for when somebody has already confirmed a track for this film, and nothing
    // to look for twice: the answer is held for as long as the menu exists.
    if (open && this.found === null && !this.searching && !this.pinnedExists) void this.look();
  }

  private async look(): Promise<void> {
    this.searching = true;
    this.failure = null;
    this.render();

    try {
      this.found = await this.hooks.search();
    } catch (error) {
      this.failure = error instanceof Error ? error.message : 'The subtitle index did not answer.';
    } finally {
      this.searching = false;
      this.render();
    }
  }

  private async take(candidate: SubtitleCandidate): Promise<void> {
    this.busy = candidate.fileId;
    this.failure = null;
    this.render();

    try {
      // One click does both: the room gains the subtitle and whoever asked is switched to it.
      const track = await this.hooks.fetch(candidate.fileId);
      this.hooks.select(track.id);
      this.open(false);
    } catch (error) {
      this.failure = error instanceof Error ? error.message : 'That subtitle could not be fetched.';
    } finally {
      this.busy = null;
      this.render();
    }
  }

  private render(): void {
    this.popup.replaceChildren();

    this.popup.appendChild(this.row({
      id: null,
      label: 'Off',
      selected: this.selected === null,
      enabled: true
    }));

    this.popup.appendChild(this.available());
    this.popup.appendChild(this.fromIndex());

    const value = this.el.querySelector('.mb-menu__value') as HTMLElement;
    const chosen = this.tracks.find((track) => track.id === this.selected);
    value.textContent = chosen?.label ?? 'Off';
    this.button.setAttribute('aria-label', `Subtitles: ${value.textContent}`);
  }

  private available(): HTMLElement {
    const section = this.section('Available');

    if (this.tracks.length === 0) {
      section.appendChild(this.message('This film carries no subtitles.'));
      return section;
    }

    // Confirmed tracks first: somebody watched the film with one and said it was right, which
    // outranks everything else that could be said about the rest.
    const ordered = [...this.tracks].sort((a, b) => rank(a) - rank(b));

    for (const track of ordered) {
      const row = this.row({
        id: track.id,
        label: track.label,
        note: noteFor(track),
        selected: this.selected === track.id,
        enabled: track.available,
        unavailable: track.available ? undefined : (track.reason ?? 'unavailable')
      });

      if (track.available) row.appendChild(this.pinControl(track));
      section.appendChild(row);
    }

    return section;
  }

  private pinControl(track: SubtitleTrack): HTMLElement {
    const pinned = track.pin !== undefined;

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'mb-subs__pin';
    button.classList.toggle('is-pinned', pinned);
    button.textContent = pinned ? 'Confirmed' : 'Confirm';
    button.title = pinned
      ? 'Remove the confirmation on this track'
      : 'Say this track fits the film, so the next person sees it first';

    button.addEventListener('click', (event) => {
      event.stopPropagation();
      void (pinned ? this.hooks.unpin(track.id) : this.hooks.pin(track.id));
    });

    return button;
  }

  private fromIndex(): HTMLElement {
    const section = this.section('From OpenSubtitles');

    if (this.failure !== null) {
      section.appendChild(this.message(this.failure));
      return section;
    }

    if (this.searching) {
      section.appendChild(this.message('Looking...'));
      return section;
    }

    // A confirmed track means there is nothing worth interrupting anyone for, so the search waits
    // to be asked for rather than running every time somebody opens the menu.
    if (this.found === null) {
      const ask = document.createElement('button');
      ask.type = 'button';
      ask.className = 'mb-subs__ask';
      ask.textContent = this.pinnedExists ? 'Look for others' : 'Search';
      ask.addEventListener('click', (event) => {
        event.stopPropagation();
        void this.look();
      });
      section.appendChild(ask);
      return section;
    }

    if (this.found.remainingDownloads !== undefined) {
      const heading = section.querySelector('.mb-menu__heading') as HTMLElement | null;
      if (heading !== null) {
        const left = document.createElement('span');
        left.className = 'mb-subs__quota';
        left.textContent = `${this.found.remainingDownloads} left today`;
        heading.appendChild(left);
      }
    }

    // Anything already fetched is in the section above, and offering it twice invites somebody to
    // spend a download on a subtitle the room already has.
    const held = new Set(this.tracks.map((track) => track.id));
    const offered = this.found.candidates.filter((c) => !held.has(`os${c.fileId}`));

    if (offered.length === 0) {
      section.appendChild(this.message(this.found.explanation ?? 'Nothing here the room does not already have.'));
      return section;
    }

    for (const candidate of offered) {
      const row = this.row({
        id: `os${candidate.fileId}`,
        label: candidate.release === '' ? 'Untitled release' : candidate.release,
        note: noteForCandidate(candidate),
        selected: false,
        enabled: this.busy === null,
        onPick: () => void this.take(candidate)
      });

      if (this.busy === candidate.fileId) row.classList.add('is-busy');
      section.appendChild(row);
    }

    return section;
  }

  private section(title: string): HTMLElement {
    const section = document.createElement('div');
    section.className = 'mb-menu__group';

    const heading = document.createElement('div');
    heading.className = 'mb-menu__heading';
    heading.textContent = title;
    section.appendChild(heading);

    return section;
  }

  private message(text: string): HTMLElement {
    const line = document.createElement('div');
    line.className = 'mb-subs__message';
    line.textContent = text;
    return line;
  }

  /**
   * A row is a container rather than a button, because a confirm control lives inside it and a
   * button inside a button is not something a browser or a screen reader can make sense of.
   */
  private row(options: {
    id: string | null;
    label: string;
    note?: Note;
    selected: boolean;
    enabled: boolean;
    unavailable?: string;
    onPick?: () => void;
  }): HTMLElement {
    const row = document.createElement('div');
    row.className = 'mb-subs__row';
    row.classList.toggle('is-selected', options.selected);

    const pick = document.createElement('button');
    pick.type = 'button';
    pick.className = 'mb-menu__item mb-subs__pick';
    pick.setAttribute('role', 'menuitemradio');
    pick.setAttribute('aria-checked', String(options.selected));
    pick.classList.toggle('is-selected', options.selected);

    const label = document.createElement('span');
    label.className = 'mb-menu__item-label';
    label.textContent = options.label;
    pick.appendChild(label);

    if (options.unavailable !== undefined) {
      pick.disabled = true;
      pick.classList.add('is-unavailable');
      pick.appendChild(this.detail({ text: options.unavailable, tone: 'warn' }));
      pick.title = `${options.label} — ${options.unavailable}`;
    } else {
      if (options.note !== undefined) pick.appendChild(this.detail(options.note));

      pick.disabled = !options.enabled;
      pick.addEventListener('click', (event) => {
        event.stopPropagation();

        if (options.onPick !== undefined) {
          options.onPick();
          return;
        }

        this.open(false);
        this.hooks.select(options.id);
      });
    }

    row.appendChild(pick);
    return row;
  }

  private detail(note: Note): HTMLElement {
    const detail = document.createElement('span');
    detail.className = `mb-menu__item-detail mb-subs__note is-${note.tone}`;

    // The words carry it and the colour agrees with them. A marker that only differs by hue is
    // one some of the room cannot read.
    detail.textContent = note.text;
    return detail;
  }
}

/** Confirmed first, then everything else in the order the film offers it. */
function rank(track: SubtitleTrack): number {
  return track.pin === undefined ? 1 : 0;
}

/**
 * What to say about a track the room already has.
 *
 * A track that came out of the film says nothing, because nothing about it has been measured:
 * there is no frame rate to disagree with and no release to mismatch, and a green tick on an
 * unmeasured track would be inventing the one answer that matters.
 */
function noteFor(track: SubtitleTrack): Note | undefined {
  if (track.pin !== undefined) {
    const watched = track.pin.watchedFraction;
    const how = watched === undefined
      ? ''
      : watched >= 0.8 ? ', watched to the end' : `, ${Math.round(watched * 100)}% in`;

    return { text: `confirmed by ${track.pin.pinnedBy}${how}`, tone: 'good' };
  }

  if (track.source !== 'sidecar') return undefined;

  if (track.alignedFraction === undefined) {
    return { text: 'not checked against the film', tone: 'warn' };
  }

  const shift = track.appliedShiftSeconds ?? 0;
  return shift === 0
    ? { text: 'matched to your copy', tone: 'good' }
    : { text: `shifted ${shift > 0 ? '+' : ''}${shift.toFixed(1)}s to fit`, tone: 'good' };
}

/**
 * What to say about a subtitle on offer.
 *
 * Silence is the ordinary answer. Most uploads declare nothing useful, so marking every one of
 * them would leave the whole list flagged and the flags meaning nothing. Only a strong claim or a
 * real problem earns a word.
 */
function noteForCandidate(candidate: SubtitleCandidate): Note | undefined {
  const exact = candidate.checks.find((c) => c.name === 'This file' && c.result === 'match');
  if (exact !== undefined) return { text: 'timed for your exact copy', tone: 'good' };

  const wrong = candidate.checks.find((c) => c.result === 'mismatch');
  if (wrong !== undefined) {
    return {
      text: wrong.detail,
      tone: wrong.name === 'Frame rate' ? 'bad' : 'warn'
    };
  }

  return undefined;
}
