import type { SubtitleCandidate, SubtitleSearch, SubtitleTrack } from '../types';

export interface SubtitleMenuHooks {
  /** Null turns subtitles off. */
  select(id: string | null): void;
  /** Closes the menu around this panel, once a choice has been made in it. */
  close(): void;
  /** Costs no allowance, so it runs whenever the panel is shown without a confirmed track. */
  search(): Promise<SubtitleSearch>;
  /** Re-reads the film's own tracks, so a panel being looked at shows what the film has now. */
  refresh(): Promise<void>;
  /** Spends one of the day's downloads and adds the track for the whole room. */
  fetch(fileId: number): Promise<SubtitleTrack>;
  pin(trackId: string): Promise<void>;
  unpin(trackId: string): Promise<void>;
}

/**
 * A mark beside a row, and what it means.
 *
 * A mark rather than a sentence: rows carry release names that are already long, and a phrase
 * beside each one pushes the list past the height of the menu. The glyphs are typography rather
 * than pictures, so they inherit the row's colour and size, and the words live on the mark itself
 * where both a pointer and a screen reader can reach them.
 */
interface Note {
  glyph: string;
  text: string;
  tone: 'good' | 'warn' | 'bad';
}

/**
 * The subtitle list, as one panel inside the settings menu.
 *
 * Its own component rather than the audio list with more branches, because what it does is a
 * different thing: it searches, waits, fails, spends a limited allowance and records a judgement.
 * The audio list needs none of that and would carry all of it.
 *
 * Two sections. What the room already has is rendered at once and never waits on the network.
 * What the index offers is fetched underneath it, and only when nobody has confirmed a track for
 * this film yet — once somebody has, there is nothing to go looking for.
 */
export class SubtitleMenu {
  readonly el: HTMLElement;

  private tracks: SubtitleTrack[] = [];
  private others: string[] = [];
  private selected: string | null = null;
  private found: SubtitleSearch | null = null;
  private searching = false;
  private failure: string | null = null;
  private busy: number | null = null;
  /** Which film the search belongs to. A search that lands after the film changed is dropped. */
  private film = 0;

  constructor(private readonly hooks: SubtitleMenuHooks) {
    this.el = document.createElement('div');
    this.el.className = 'mb-panel mb-subs';
  }

  /** What the settings menu shows beside the word Subtitles. */
  get value(): string {
    return this.tracks.find((track) => track.id === this.selected)?.label ?? 'Off';
  }

  setTracks(tracks: SubtitleTrack[], selected: string | null, otherLanguages: string[] = []): void {
    this.tracks = tracks;
    this.selected = selected;
    this.others = otherLanguages;
    this.render();
  }

  /**
   * Forgets what the index offered, because the film changed. The search is about one film, and
   * a list found for the last one shown under the next is a list of subtitles that do not fit.
   */
  reset(): void {
    this.film += 1;
    this.found = null;
    this.searching = false;
    this.failure = null;
    this.busy = null;
  }

  /**
   * Called when the panel comes into view.
   *
   * The film's own tracks are read again first, so what is looked at is current. Nothing to look
   * for in the index when somebody has already confirmed a track for this film, and nothing to
   * look for twice: the answer is held for as long as the film is.
   */
  shown(): void {
    this.render();
    void this.hooks.refresh().catch(() => {
      /* The list already on screen stands. */
    });
    if (this.found === null && !this.searching && !this.pinnedExists) void this.look();
  }

  private get pinnedExists(): boolean {
    return this.tracks.some((track) => track.pin !== undefined);
  }

  private async look(): Promise<void> {
    const film = this.film;
    this.searching = true;
    this.failure = null;
    this.render();

    try {
      const found = await this.hooks.search();
      if (film !== this.film) return;
      this.found = found;
    } catch (error) {
      if (film !== this.film) return;
      this.failure = error instanceof Error ? error.message : 'The subtitle index did not answer.';
    } finally {
      if (film === this.film) {
        this.searching = false;
        this.render();
      }
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
      this.hooks.close();
    } catch (error) {
      this.failure = error instanceof Error ? error.message : 'That subtitle could not be fetched.';
    } finally {
      this.busy = null;
      this.render();
    }
  }

  private render(): void {
    this.el.replaceChildren();

    this.el.appendChild(this.row({
      id: null,
      label: 'Off',
      selected: this.selected === null,
      enabled: true
    }));

    this.el.appendChild(this.available());
    this.el.appendChild(this.fromIndex());
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
        unavailable: track.available ? undefined : (track.reason ?? 'unavailable'),
        describe: track.available ? describe(track) : undefined
      });

      if (track.available) row.appendChild(this.pinControl(track));
      section.appendChild(row);
    }

    // A film that plainly carries thirty languages and offers three would otherwise read as
    // broken. One line answers it, where thirty rows would be the thing being avoided.
    if (this.others.length > 0) {
      const line = this.message(`+${this.others.length} more languages in the source`);
      line.title = `Not extracted: ${this.others.join(', ')}`;
      section.appendChild(line);
    }

    return section;
  }

  private pinControl(track: SubtitleTrack): HTMLElement {
    const pinned = track.pin !== undefined;

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'mb-subs__pin';
    button.classList.toggle('is-pinned', pinned);
    button.textContent = '\u2713';

    // The control is the state as well as the action, so there is no second mark saying the same
    // thing. What it means in full is on the control rather than in the row.
    const said = pinned ? confirmation(track.pin!) : 'Confirm this track fits the film';
    button.title = pinned ? `${said} — click to undo` : said;
    button.setAttribute('aria-label', said);
    button.setAttribute('aria-pressed', String(pinned));

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
        left.textContent = `${this.found.remainingDownloads} left`;
        left.title = `${this.found.remainingDownloads} subtitle downloads left today`;
        heading.appendChild(left);
      }
    }

    // Anything already fetched is in the section above, and offering it twice invites somebody to
    // spend a download on a subtitle the room already has.
    const held = new Set(this.tracks.map((track) => track.id));
    const offered = this.found.candidates.filter((c) => !held.has(`os${c.fileId}`));

    if (offered.length === 0) {
      section.appendChild(this.message(this.found.explanation ?? 'Nothing new here.'));
      return section;
    }

    for (const candidate of offered) {
      const row = this.row({
        id: `os${candidate.fileId}`,
        label: candidate.release === '' ? 'Untitled release' : candidate.release,
        note: noteForCandidate(candidate),
        selected: false,
        enabled: this.busy === null,
        describe: candidate.release === '' ? 'Release not stated' : candidate.release,
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
    describe?: string;
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
      pick.appendChild(this.mark({ glyph: '!', text: options.unavailable, tone: 'warn' }));
      pick.title = `${options.label} — ${options.unavailable}`;
    } else {
      if (options.note !== undefined) pick.appendChild(this.mark(options.note));
      if (options.describe !== undefined) pick.title = options.describe;

      pick.disabled = !options.enabled;
      pick.addEventListener('click', (event) => {
        event.stopPropagation();

        if (options.onPick !== undefined) {
          options.onPick();
          return;
        }

        this.hooks.select(options.id);
        this.hooks.close();
      });
    }

    row.appendChild(pick);
    return row;
  }

  private mark(note: Note): HTMLElement {
    const mark = document.createElement('span');
    mark.className = `mb-menu__item-detail mb-subs__mark is-${note.tone}`;
    mark.textContent = note.glyph;

    // The glyph is what is seen and the words are what it means, so both a pointer and a screen
    // reader reach the same thing. Colour agrees with the glyph rather than carrying it alone:
    // a marker that differs only by hue is one part of the room cannot read.
    mark.title = note.text;
    mark.setAttribute('role', 'img');
    mark.setAttribute('aria-label', note.text);
    return mark;
  }
}

/** Confirmed first, then everything else in the order the film offers it. */
function rank(track: SubtitleTrack): number {
  return track.pin === undefined ? 1 : 0;
}

/** What a confirmation amounts to, in the words the control carries. */
function confirmation(pin: NonNullable<SubtitleTrack['pin']>): string {
  const watched = pin.watchedFraction;
  const how = watched === undefined
    ? ''
    : watched >= 0.8 ? ', watched to the end' : `, ${Math.round(watched * 100)}% in`;

  return `Confirmed by ${pin.pinnedBy}${how}`;
}

/**
 * What to mark a track the room already has.
 *
 * A confirmation is not marked here: the confirm control is both the state and the action, so a
 * second mark beside it would say the same thing twice. A track that came out of the film is not
 * marked either, because nothing about it has been measured — there is no frame rate for it to
 * disagree with and no release for it to mismatch, and a mark would be inventing the one answer
 * that matters.
 */
function noteFor(track: SubtitleTrack): Note | undefined {
  if (track.pin !== undefined || track.source !== 'sidecar') return undefined;

  return track.alignedFraction === undefined
    ? { glyph: '?', text: 'Not checked against the film', tone: 'warn' }
    : undefined;
}

/** The full state of a row, for a pointer resting on it. */
function describe(track: SubtitleTrack): string {
  if (track.pin !== undefined) return `${track.label} — ${confirmation(track.pin)}`;
  if (track.source !== 'sidecar') return `${track.label} — came with the film`;

  if (track.alignedFraction === undefined) return `${track.label} — not checked against the film`;

  const shift = track.appliedShiftSeconds ?? 0;
  return shift === 0
    ? `${track.label} — matched to your copy`
    : `${track.label} — shifted ${shift > 0 ? '+' : ''}${shift.toFixed(1)}s to fit your copy`;
}

/**
 * What to mark a subtitle on offer.
 *
 * Nothing is the ordinary answer. Most uploads declare nothing useful, so marking every one of
 * them would leave the whole list marked and the marks meaning nothing.
 */
function noteForCandidate(candidate: SubtitleCandidate): Note | undefined {
  const exact = candidate.checks.find((c) => c.name === 'This file' && c.result === 'match');
  if (exact !== undefined) {
    return { glyph: '\u2713', text: 'Timed for your exact copy', tone: 'good' };
  }

  const wrong = candidate.checks.find((c) => c.result === 'mismatch');
  if (wrong === undefined) return undefined;

  return wrong.name === 'Frame rate'
    ? { glyph: '\u2717', text: wrong.detail, tone: 'bad' }
    : { glyph: '!', text: wrong.detail, tone: 'warn' };
}
