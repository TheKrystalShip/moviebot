import type { ConnectionStatus } from '../session/hub';
import type { Participant, SeekClamped, SessionState, TitleSummary } from '../types';
import { prefs } from '../prefs';
import { formatTime } from './format';

export interface ShellHandlers {
  onPickTitle(titleId: string): void;
  onJoinPlayback(): void;
}

const NoticeLifetimeMs = 8000;

/** The page around the player: the library, who is watching, and what just happened. */
export class Shell {
  readonly playerHost = document.getElementById('player') as HTMLElement;

  private readonly library = document.getElementById('library') as HTMLElement;
  private readonly participants = document.getElementById('participants') as HTMLElement;
  private readonly notices = document.getElementById('notices') as HTMLElement;
  private readonly connection = document.getElementById('connection') as HTMLElement;
  private readonly sessionLabel = document.getElementById('session-label') as HTMLElement;
  private readonly filmTitle = document.getElementById('film-title') as HTMLElement;
  private readonly actor = document.getElementById('actor') as HTMLElement;
  private readonly gate = document.getElementById('gate') as HTMLElement;
  private readonly app = document.getElementById('app') as HTMLElement;
  private readonly side = document.getElementById('side') as HTMLElement;
  private readonly sideToggle = document.getElementById('side-toggle') as HTMLButtonElement;

  private titles: TitleSummary[] = [];
  private currentTitleId: string | null = null;

  constructor(private readonly handlers: ShellHandlers) {
    (document.getElementById('gate-button') as HTMLButtonElement).addEventListener('click', () => {
      this.showGate(false);
      this.handlers.onJoinPlayback();
    });

    this.setSideOpen(prefs.sideOpen());
    this.sideToggle.addEventListener('click', () => this.setSideOpen(this.side.hidden));

    // Escape leaves the maximised player. The Fullscreen API is unavailable inside Discord's
    // iframe, so nothing else is listening for it and the habit is worth honouring.
    window.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && this.isMaximised()) this.setMaximised(false);
    });
  }

  /**
   * The library and who is watching, folded away.
   *
   * A film is the point of the page and the panel beside it is not, so it collapses and stays
   * collapsed: the choice is this viewer's and is remembered like their volume.
   */
  setSideOpen(open: boolean): void {
    this.side.hidden = !open;
    this.app.classList.toggle('app--side-open', open);
    this.sideToggle.setAttribute('aria-expanded', String(open));
    prefs.setSideOpen(open);
  }

  isMaximised(): boolean {
    return this.app.classList.contains('app--maximised');
  }

  /**
   * Fills the frame with the film.
   *
   * Discord's iframe does not grant the Fullscreen API, so a real fullscreen button cannot work
   * there. This gives up the surrounding page instead, which is the part worth reclaiming.
   */
  setMaximised(maximised: boolean): void {
    this.app.classList.toggle('app--maximised', maximised);
  }

  setSession(sessionId: string, displayName: string): void {
    this.sessionLabel.textContent = `${sessionId} · ${displayName}`;
  }

  setConnection(status: ConnectionStatus): void {
    this.connection.textContent = status;
    this.connection.dataset.status = status;
  }

  setTitles(titles: TitleSummary[]): void {
    this.titles = titles;
    this.renderLibrary();
  }

  setCurrentTitle(titleId: string | null, name: string | null): void {
    this.currentTitleId = titleId;
    this.filmTitle.textContent = name ?? 'No film loaded';
    this.renderLibrary();
  }

  setParticipants(list: Participant[]): void {
    this.participants.replaceChildren(
      ...list.map((person) => {
        const item = document.createElement('li');
        item.className = 'participants__item';
        item.textContent = person.displayName;
        return item;
      })
    );
  }

  /** Who moved the room last, and how. The state records it so nobody has to ask. */
  setActor(state: SessionState, previous: SessionState | null): void {
    const who = state.updatedBy?.displayName;
    if (who === undefined) {
      this.actor.textContent = '';
      return;
    }

    const what =
      previous === null || previous.titleId !== state.titleId
        ? 'loaded the film'
        : previous.paused !== state.paused
          ? state.paused
            ? 'paused'
            : 'started playback'
          : `jumped to ${formatTime(state.positionSeconds)}`;

    this.actor.textContent = `${who} ${what}`;
  }

  showGate(show: boolean): void {
    this.gate.hidden = !show;
  }

  notice(message: string, kind: 'info' | 'error' = 'info'): void {
    const item = document.createElement('div');
    item.className = `notice notice--${kind}`;
    item.textContent = message;
    this.notices.appendChild(item);
    window.setTimeout(() => item.remove(), NoticeLifetimeMs);
  }

  /** Only the viewer whose seek was refused sees this, so it says what happened to them. */
  clampNotice(clamp: SeekClamped): void {
    this.notice(
      `The transcode has reached ${formatTime(clamp.headSeconds)}, so ` +
        `${formatTime(clamp.requestedSeconds)} is not written yet. Playing from ` +
        `${formatTime(clamp.grantedSeconds)}.`
    );
  }

  private renderLibrary(): void {
    this.library.replaceChildren(
      ...this.titles.map((title) => {
        const item = document.createElement('li');
        item.className = 'library__item';
        item.classList.toggle('is-current', title.id === this.currentTitleId);

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'library__button';
        button.addEventListener('click', () => this.handlers.onPickTitle(title.id));

        const name = document.createElement('span');
        name.className = 'library__name';
        name.textContent = title.title;

        const meta = document.createElement('span');
        meta.className = 'library__meta';
        meta.textContent =
          title.status === 'transcoding'
            ? `${formatTime(title.durationSeconds)} · ${formatTime(title.headSeconds ?? 0)} ready`
            : title.status === 'failed'
              ? 'failed'
              : formatTime(title.durationSeconds);

        button.append(name, meta);
        item.appendChild(button);
        return item;
      })
    );
  }
}
