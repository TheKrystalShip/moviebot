import { describeChange } from '../session/changes';
import type { ConnectionStatus } from '../session/hub';
import type { Participant, SeekClamped, SessionState, TitleSummary } from '../types';
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
  private readonly empty = document.getElementById('empty') as HTMLElement;

  private titles: TitleSummary[] = [];
  private currentTitleId: string | null = null;

  constructor(private readonly handlers: ShellHandlers) {
    (document.getElementById('gate-button') as HTMLButtonElement).addEventListener('click', () => {
      this.showGate(false);
      this.handlers.onJoinPlayback();
    });

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
    // The only thing on an otherwise black page: say what to do about it.
    this.empty.hidden = name !== null;
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
    this.actor.textContent = describeChange(state, previous) ?? '';
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
