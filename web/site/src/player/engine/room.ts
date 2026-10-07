import { api } from './api';
import { environment, type Identity } from './environment';
import { formatTime } from './format';
import { prefs } from './prefs';
import { playableHead, playableHeadOf } from './media/heads';
import type { DeviceCopy } from './media/deviceLoader';
import { FilmStream, type QualityLevel } from './media/stream';
import { defaultAudioId } from './media/tracks';
import { describeChange } from './session/changes';
import { SessionHub, type ConnectionStatus, type Timeline } from './session/hub';
import { LocalTimeline } from './session/local';
import { SyncController } from './session/sync';
import type {
  Actor, Manifest, Participant, SeekClamped, SessionState, SubtitleSearch, SubtitleStyle, SubtitleTrack, ThumbnailStrip,
  TitleSummary, ViewerPreferences
} from './types';

/**
 * A language's primary code in its canonical spelling. A film's tracks carry the three-letter codes
 * its container wrote (`eng`, `ger`) and a person's preference the two-letter ones, and the browser
 * already knows which is which.
 */
function primaryLanguage(code: string): string {
  try {
    return (Intl.getCanonicalLocales(code)[0] ?? code).split('-')[0].toLowerCase();
  } catch {
    return code.split('-')[0].toLowerCase();
  }
}

/** Whether a track's language is the one asked for: `eng` is `en`, and `es` matches `es-419`. */
function sameLanguage(track: string, wanted: string): boolean {
  return primaryLanguage(track) === primaryLanguage(wanted);
}

export interface Notice {
  id: number;
  text: string;
  kind: 'info' | 'error';
}

/** Everything the page draws about the room, as one value that changes as a whole. */
export interface RoomView {
  identity: Identity | null;
  sessionId: string;
  connection: ConnectionStatus;
  titles: TitleSummary[];
  /** The film this viewer is holding, or null for a room showing nothing. */
  manifest: Manifest | null;
  state: SessionState | null;
  /** Who moved the room last, and how, in the words every surface uses. */
  actor: string | null;
  participants: Participant[];
  /** The browser refused to start playback without a gesture. */
  gate: boolean;
  /** The playhead is being moved to where the room is, after joining. */
  catchingUp: boolean;
  /** The element has been waiting for data for longer than a stutter. */
  stalled: boolean;
  /** Why this viewer cannot play the film, for as long as it holds. */
  unplayable: string | null;
  notices: Notice[];
  thumbnails: { strip: ThumbnailStrip; url: string } | null;
  subtitleTrack: TextTrack | null;
  audioId: string | null;
  subtitleId: string | null;
  /** How far the film can be played to, on the best rung that has audio under it, or null once the whole film is written. */
  transcodeHead: number | null;
  /** The room could not be joined at all. */
  fatal: string | null;
  /** The element holds the film and can be driven. */
  ready: boolean;
  /** The rungs this viewer can hold the film at, tallest first; empty where the browser chooses. */
  qualityLevels: QualityLevel[];
  /** The height chosen, or null for the connection to decide. */
  quality: number | null;
  /** The height being played, as far as the stream says. */
  playingHeight: number | null;
  /** How this person watches, as the API keeps it for them. */
  preferences: ViewerPreferences;
  /** The film plays alone from this device: there is no room, nobody else and nothing to invite to. */
  solo: boolean;
  /** The film this viewer holds is saved whole on this device, so it can go on with no connection. */
  saved: boolean;
  /** The poster saved on the device, as an address the page can draw, when the film plays alone. */
  artwork: string | null;
}

/** How often a film played alone writes down where it has got to, while it plays. */
const ProgressEveryMs = 10_000;

/** What somebody in the room did to it, this viewer included, and who did it. */
export interface Announcement {
  text: string;
  actor: Actor | null;
}

const NoticeLifetimeMs = 8000;

/**
 * One viewer in one room: the hub that carries the room's timeline, the controller that applies it
 * to the media element, and the stream that puts the film in the element.
 *
 * The page draws `view` and calls the methods below; it never touches the hub or the element's
 * timeline itself. Every control moves the whole room: there is one playhead and no host.
 *
 * A film played alone from the device (`Environment.solo`) is the same, with a timeline of its own in
 * place of the hub (`session/local.ts`) and the device in place of the server: the controller and the
 * stream do not tell the two apart.
 */
export class PlayerRoom {
  private hub: Timeline | null = null;
  private controller: SyncController | null = null;
  private readonly stream: FilmStream;
  /** Undefined until the room has said, so the first state is always acted on. Starting at null
   *  makes an empty room indistinguishable from one nothing has been heard about yet. */
  private loadedTitleId: string | null | undefined;
  /**
   * The film the launch named, which is the only thing that knows what an empty room should hold,
   * until the room holds a film. From then on the room is the authority on what it shows, and the
   * film this viewer last held is what an emptied room is given back.
   */
  private launched: string | null = null;
  private previous: SessionState | null = null;
  private noticeIds = 0;
  private readonly subscribers = new Set<() => void>();
  private readonly announcers = new Set<(announcement: Announcement) => void>();
  private disposed = false;
  private styleSave: ReturnType<typeof setTimeout> | null = null;
  /** The film as the device holds it, for the title this viewer holds, or null. */
  private copy: DeviceCopy | null = null;
  private progressTimer: number | null = null;
  private readonly recordProgress = () => {
    const titleId = this.loadedTitleId;
    if (!this.view.solo || !titleId || !this.stream.holdsFilm) return;
    environment().recordProgress(titleId, this.video.currentTime);
  };
  private readonly onVisible = () => {
    if (document.visibilityState === 'visible') void this.hub?.resync();
  };

  view: RoomView;

  constructor(private readonly video: HTMLVideoElement) {
    this.view = {
      identity: null,
      sessionId: environment().sessionId(),
      connection: 'connecting',
      titles: [],
      manifest: null,
      state: null,
      actor: null,
      participants: [],
      gate: false,
      catchingUp: false,
      stalled: false,
      unplayable: null,
      notices: [],
      thumbnails: null,
      subtitleTrack: null,
      audioId: null,
      subtitleId: null,
      transcodeHead: null,
      fatal: null,
      ready: false,
      qualityLevels: [],
      quality: null,
      preferences: {},
      playingHeight: null,
      solo: environment().solo,
      saved: false,
      artwork: null
    };

    this.stream = new FilmStream(video, {
      onReady: () => {
        this.set({ ready: true });
        this.controller?.setPlayerReady(true);
      },
      onError: (message) => this.notice(message, 'error'),
      // Nothing is applied to a film this viewer cannot hold: every push would seek an empty
      // element and ask for a press to start playback that cannot start.
      onUnplayable: (message) => {
        this.controller?.setPlayerReady(false);
        this.set({ unplayable: message, stalled: false });
      },
      onStall: (waiting) => {
        this.set({ stalled: waiting });
        void this.hub?.buffering(waiting);
      },
      onThumbnails: (strip, url) => this.set({ thumbnails: strip !== null && url !== null ? { strip, url } : null }),
      onSubtitleTrack: (track) => this.set({ subtitleTrack: track }),
      onQuality: (levels, playingHeight) => this.set({ qualityLevels: levels, playingHeight }),
      onViewerQuality: (playing, wanted) => void this.hub?.reportQuality(playing, wanted)
    });
  }

  // ── The store React reads ────────────────────────────────────────────────────────────────

  subscribe = (listener: () => void): (() => void) => {
    this.subscribers.add(listener);
    return () => this.subscribers.delete(listener);
  };

  snapshot = (): RoomView => this.view;

  /** What somebody else did to the room, said over the film for a moment. */
  onAnnounce(listener: (announcement: Announcement) => void): () => void {
    this.announcers.add(listener);
    return () => this.announcers.delete(listener);
  }

  private set(patch: Partial<RoomView>): void {
    if (this.disposed) return;
    this.view = { ...this.view, ...patch };
    for (const listener of this.subscribers) listener();
  }

  // ── Joining ──────────────────────────────────────────────────────────────────────────────

  async start(): Promise<void> {
    const env = environment();
    if (env.solo) {
      await this.startAlone();
      return;
    }
    if (env.authToken() === null) {
      this.set({ fatal: 'Sign in to watch.' });
      return;
    }

    let identity: Identity;
    try {
      identity = await env.identity();
    } catch (error) {
      this.set({ fatal: error instanceof Error ? error.message : 'Nobody is signed in.' });
      return;
    }
    this.set({ identity });

    // Before the room is joined, so the first film loads with this person's own defaults. Without
    // them the film still plays, on the film's own tracks and the connection's choice of quality.
    try {
      const preferences = await api.preferences();
      this.set({ preferences, quality: preferences.qualityHeight ?? null });
    } catch {
      this.notice('Your playback settings could not be read, so this film starts on its own defaults.', 'error');
    }

    const launched = env.titleId();
    this.launched = launched;
    const sessionId = env.sessionId();

    const hub = new SessionHub(sessionId, {
      onState: (push, resync) => this.controller?.applyPush(push, resync),
      onSeekClamped: (clamp) => this.clampNotice(clamp),
      onParticipants: (list) => {
        this.set({ participants: list });
        void this.showPresence();
      },
      onTitle: (fresh) => this.followTitle(fresh),
      onStatus: (status) => {
        this.set({ connection: status });
        // Nothing is derived forward from a room that cannot be heard from. Its anchor runs
        // whether or not the film does, so a state nothing has confirmed is a guess about where a
        // film that may have been stopped has got to.
        this.controller?.setLive(status === 'connected');

        // What the title gained while this page could not be heard from was pushed to nobody
        // here. A film still transcoding is the one that changes, so it is asked for again on the
        // way back.
        const manifest = this.view.manifest;
        if (status === 'connected' && manifest?.status === 'transcoding') {
          void api.manifest(manifest.id).then((fresh) => this.followTitle(fresh)).catch(() => {
            /* The next push carries it. */
          });
        }
      }
    });
    this.hub = hub;
    this.controller = this.controllerFor(hub, identity);

    try {
      this.set({ titles: await api.titles() });
    } catch {
      this.notice('The library could not be read.', 'error');
    }

    let joined;
    try {
      joined = await hub.start();
    } catch {
      this.set({ fatal: 'The room could not be reached. Try again in a moment.', connection: 'disconnected' });
      return;
    }
    if (this.disposed) return;

    this.controller.applyPush(joined, true);
    try {
      this.set({ participants: await api.participants(sessionId) });
    } catch {
      /* The hub's next list carries them. */
    }
    void this.showPresence();

    // Coming back to a tab that has been in the background is the moment its picture of the room
    // is least likely to be right, and the cheapest moment to ask.
    document.addEventListener('visibilitychange', this.onVisible);

    // A launch link names the film. The room is told once, by whoever arrives while it is showing
    // something else; a viewer joining a room that already holds the title says nothing, because
    // loading it again would send everyone back to the beginning. The front door keeps what the
    // room shows where the launch is read from (`Environment.showing`), so reloading the page names
    // the room's own film and tells it nothing.
    if (launched !== null && (joined.state.titleId ?? null) !== null && joined.state.titleId !== launched) {
      await hub.loadTitle(launched);
    }
  }

  /**
   * A film saved on this device, played alone: the film and its manifest from the device, how the
   * person watches from the API while it can be asked and else as the device last heard it, and a
   * timeline of its own, starting where the launch said or where the person last got to.
   */
  private async startAlone(): Promise<void> {
    const env = environment();
    let identity: Identity;
    try {
      identity = await env.identity();
    } catch (error) {
      this.set({ fatal: error instanceof Error ? error.message : 'Nobody is signed in.' });
      return;
    }
    this.set({ identity });

    const titleId = env.titleId();
    const copy = titleId === null ? null : await env.deviceCopy(titleId).catch(() => null);
    if (this.disposed) return;
    if (titleId === null || copy === null) {
      this.set({ fatal: 'This film is not saved on this device.' });
      return;
    }
    this.copy = copy;
    this.set({ saved: true, artwork: copy.poster === null ? null : URL.createObjectURL(copy.poster) });

    let preferences = env.keptPreferences();
    if (env.online()) {
      try {
        preferences = await api.preferences();
      } catch {
        /* What the device last heard stands. */
      }
    }
    this.set({ preferences: preferences ?? {} });

    const start = env.startAt()
      ?? await env.resumeAt(titleId, copy.manifest.durationSeconds).catch(() => null)
      ?? 0;
    if (this.disposed) return;

    const timeline = new LocalTimeline(env.sessionId(), titleId, start,
      { userId: identity.userId, displayName: identity.displayName },
      (push) => this.controller?.applyPush(push));
    this.hub = timeline;
    this.controller = this.controllerFor(timeline, identity);

    // Where the person has got to is written down as they watch, and whenever they stop, skip or
    // reach the end, so it is there for the API whenever a connection comes back.
    for (const event of ['pause', 'seeked', 'ended']) this.video.addEventListener(event, this.recordProgress);
    this.progressTimer = window.setInterval(() => {
      if (!this.video.paused) this.recordProgress();
    }, ProgressEveryMs);

    this.controller.applyPush(await timeline.start(), true);
  }

  /** The controller that applies a timeline to the element, whichever timeline it is. */
  private controllerFor(timeline: Timeline, identity: Identity): SyncController {
    return new SyncController(this.video, timeline.clock, {
      play: (at) => void timeline.play(at),
      pause: (at) => void timeline.pause(at),
      seek: (to) => void timeline.seek(to)
    }, {
      onState: (state) => this.onState(state, identity),
      onPlaybackBlocked: () => this.set({ gate: true }),
      onInitialSeekChanged: (seeking) => this.set({ catchingUp: seeking }),
      holdsFilm: () => this.stream.holdsFilm
    });
  }

  dispose(): void {
    this.recordProgress();
    if (this.progressTimer !== null) window.clearInterval(this.progressTimer);
    for (const event of ['pause', 'seeked', 'ended']) this.video.removeEventListener(event, this.recordProgress);
    if (this.view.artwork !== null) URL.revokeObjectURL(this.view.artwork);
    this.disposed = true;
    // A style chosen just before leaving is still saved.
    if (this.styleSave !== null) {
      clearTimeout(this.styleSave);
      this.styleSave = null;
      void api.setPreferences(this.view.preferences).catch(() => {});
    }
    document.removeEventListener('visibilitychange', this.onVisible);
    this.controller?.dispose();
    this.stream.dispose();
    void this.hub?.stop();
    void environment().setPresence(null);
  }

  // ── What the room says ───────────────────────────────────────────────────────────────────

  private onState(state: SessionState, identity: Identity): void {
    // Said on screen to everyone in the room, the person who did it included, so what one person
    // sees happen is what everybody sees. Only a state that moved the room on from the last one
    // counts; the first state is the room being joined, and a resync hands back a state already
    // seen, and neither is anybody acting on it.
    // A film played alone has nobody else to say it to, and the person sees what they pressed.
    const previous = this.previous;
    const advanced = previous !== null
      && !this.view.solo
      && state.epoch === previous.epoch
      && state.revision > previous.revision;
    const change = advanced ? describeChange(state, previous, identity.userId) : null;
    if (change !== null) {
      for (const announce of this.announcers) announce({ text: change, actor: state.updatedBy ?? null });
    }

    this.previous = state;
    this.set({ state, actor: describeChange(state, previous) });
    void this.showPresence();

    // Normalised first: a room holding no film leaves the field out of the state altogether, so
    // what arrives is undefined and never the null it is compared against.
    const titleId = state.titleId ?? null;
    if (titleId !== this.loadedTitleId) {
      // What this viewer was watching until this state arrived, which is the only record of it
      // left once the room says it is showing nothing.
      const held = this.loadedTitleId ?? null;
      void this.loadTitle(titleId);

      // A room holding nothing, while this page is holding a film, is a room nobody has told yet:
      // a fresh link, or one the server built again after forgetting it. The link names the film
      // where there is one; inside the Activity there is only an invite, which names nothing, so
      // what this viewer already has open is the answer. Only the empty case, because a room
      // showing something else was told by somebody. A start rides only with the launched film.
      const launched = this.launched;
      const restore = launched ?? held;
      if (restore !== null && titleId === null) {
        void this.hub?.loadTitle(restore, restore === launched ? environment().startAt() : null);
      }
      if (titleId !== null) {
        this.launched = null;
        if (!this.view.solo) environment().showing(titleId);
      }
      return;
    }

    if (this.view.manifest?.status === 'transcoding') {
      this.set({ transcodeHead: playableHeadOf(state) ?? playableHead(this.view.manifest) ?? 0 });
    }
  }

  private async loadTitle(titleId: string | null): Promise<void> {
    this.loadedTitleId = titleId;
    this.controller?.setPlayerReady(false);
    this.set({ unplayable: null, gate: false, ready: false });

    if (titleId === null) {
      this.copy = null;
      this.set({ manifest: null, thumbnails: null, subtitleTrack: null, transcodeHead: null, saved: false });
      void this.showPresence();
      return;
    }

    // Alone, the film is the device's copy and nothing else. In a room it is the room's, and a copy
    // saved whole on this device plays from it while the hub keeps the timeline.
    let manifest: Manifest;
    let copy: DeviceCopy | null;
    if (this.view.solo) {
      copy = this.copy;
      if (copy === null || copy.manifest.id !== titleId) {
        this.set({ unplayable: 'This film is not saved on this device.' });
        return;
      }
      manifest = copy.manifest;
    } else {
      try {
        manifest = await api.manifest(titleId);
      } catch {
        this.notice(`The film ${titleId} could not be read.`, 'error');
        return;
      }
      copy = manifest.status === 'ready' ? await environment().deviceCopy(titleId).catch(() => null) : null;
    }
    if (this.loadedTitleId !== titleId) return;
    this.copy = copy;
    this.set({ saved: copy !== null });

    // A choice made for this film stands, where the film as this viewer holds it has the track: a
    // copy saved on the device carries only the tracks picked when it was saved. Otherwise the
    // person's languages decide, where the film carries them, and the film's own default after that.
    const held = prefs.forTitle(manifest.id);
    const stored = {
      audioTrackId: manifest.audio.some((t) => t.id === held.audioTrackId) ? held.audioTrackId : undefined,
      subtitleTrackId: held.subtitleTrackId === null || manifest.subtitles.some((t) => t.id === held.subtitleTrackId)
        ? held.subtitleTrackId
        : undefined
    };
    // Among tracks in the person's language, the ones saved on the device come first: they were picked
    // when the film was saved, and they cost no connection.
    const { audioLanguage, subtitleLanguage } = this.view.preferences;
    const onDevice = (ids: { id: string }[] | undefined) => (track: { id: string }) => ids?.some((t) => t.id === track.id) ?? false;
    const first = <T extends { id: string }>(tracks: T[], held: (t: T) => boolean, wanted: (t: T) => boolean) =>
      tracks.find((t) => wanted(t) && held(t)) ?? tracks.find(wanted);
    const audioId = stored.audioTrackId
      ?? (audioLanguage
        ? first(manifest.audio, onDevice(copy?.manifest.audio), (t) => t.kind === 'feature' && sameLanguage(t.language, audioLanguage))?.id
        : undefined)
      ?? defaultAudioId(manifest) ?? null;
    const subtitleId = stored.subtitleTrackId !== undefined
      ? stored.subtitleTrackId
      : (subtitleLanguage
        ? first(manifest.subtitles, onDevice(copy?.manifest.subtitles),
          (t) => t.available && t.uri !== undefined && !t.forced && sameLanguage(t.language, subtitleLanguage))?.id ?? null
        : null);

    this.set({
      manifest,
      audioId,
      subtitleId,
      transcodeHead: playableHead(manifest)
    });
    void this.showPresence();

    if (manifest.status === 'failed') {
      this.set({ unplayable: manifest.error ?? 'This film could not be prepared, so it cannot be played.' });
      return;
    }

    await this.stream.load(manifest, this.controller?.roomPosition(), audioId ?? undefined, subtitleId, this.view.quality, copy);
  }

  /**
   * The film as the server now has it, pushed whenever its manifest changes. The shaded region
   * grows as the transcode does, and the manifest that marks the film ready is the one that names
   * the preview sheet, and the subtitles when the source was still arriving.
   */
  private followTitle(fresh: Manifest): void {
    if (this.loadedTitleId !== fresh.id || this.view.manifest === null) return;
    this.stream.follow(fresh);
    this.set({
      manifest: fresh,
      transcodeHead: playableHead(fresh)
    });
  }

  /**
   * What this viewer is watching, beside their name. Derived from what the page already holds
   * whenever any of it changes, so the front door is told the whole of it each time and keeps
   * nothing.
   */
  private async showPresence(): Promise<void> {
    const { manifest, identity, participants } = this.view;
    const state = this.previous;
    const env = environment();
    if (manifest === null || state === null || identity === null || (state.titleId ?? null) !== manifest.id) {
      await env.setPresence(null);
      return;
    }

    await env.setPresence({
      titleId: manifest.id,
      filmName: manifest.film?.name ?? manifest.title,
      durationSeconds: manifest.durationSeconds,
      paused: state.paused,
      positionSeconds: this.roomPosition(),
      others: Math.max(0, participants.filter((p) => p.userId !== identity.userId).length),
      poster: manifest.poster ?? null
    });
  }

  // ── Telling the person ───────────────────────────────────────────────────────────────────

  notice(text: string, kind: 'info' | 'error' = 'info'): void {
    const id = ++this.noticeIds;
    this.set({ notices: [...this.view.notices, { id, text, kind }] });
    window.setTimeout(() => this.set({ notices: this.view.notices.filter((n) => n.id !== id) }), NoticeLifetimeMs);
  }

  /** Only the viewer whose seek was refused sees this, so it says what happened to them. */
  private clampNotice(clamp: SeekClamped): void {
    this.notice(`The film is prepared up to ${formatTime(clamp.headSeconds)}, so `
      + `${formatTime(clamp.requestedSeconds)} is not ready yet. Playing from ${formatTime(clamp.grantedSeconds)}.`);
  }

  // ── What the person does ─────────────────────────────────────────────────────────────────

  get holdsFilm(): boolean {
    return this.stream.holdsFilm;
  }

  /** Where the room is, as opposed to where this viewer's playhead has got to. */
  roomPosition(): number {
    return this.controller?.roomPosition() ?? 0;
  }

  /** Puts a film on, for everybody in the room. */
  pickTitle(titleId: string): void {
    void this.hub?.loadTitle(titleId);
  }

  /** The press the browser was waiting for, which catches this viewer up to the room. */
  joinPlayback(): void {
    this.set({ gate: false });
    this.controller?.resumeFromGesture();
  }

  /**
   * Play and pause are published as intent like everything else. The controller watches the media
   * element and tells the room, so driving the element is what drives the film.
   */
  togglePlay(): void {
    if (!this.stream.holdsFilm || this.view.catchingUp) return;
    if (this.video.paused) void this.video.play().catch(() => {});
    else this.video.pause();
  }

  /** A seek is intent: the playhead moves when the room answers. */
  seek(seconds: number): void {
    void this.hub?.seek(Math.max(0, seconds));
  }

  /**
   * A relative seek, measured from this viewer's playhead while it holds the film and from the
   * room otherwise: an element that never loaded, or one a recovery has just emptied, sits at
   * zero, and five seconds on from there is the opening titles for everyone. Answers how far it
   * actually asked to move, so the page can say so.
   */
  seekBy(seconds: number): number {
    const duration = this.view.manifest?.durationSeconds ?? 0;
    const from = this.stream.holdsFilm ? this.video.currentTime : this.roomPosition();
    const to = Math.min(duration, Math.max(0, from + seconds));
    this.seek(to);
    return to - from;
  }

  selectAudio(trackId: string): void {
    this.stream.selectAudio(trackId);
    if (this.view.manifest) prefs.setAudioTrack(this.view.manifest.id, trackId);
    this.set({ audioId: trackId });
  }

  selectSubtitle(trackId: string | null): void {
    void this.stream.selectSubtitle(trackId);
    if (this.view.manifest) prefs.setSubtitleTrack(this.view.manifest.id, trackId);
    this.set({ subtitleId: trackId });
  }

  /** A height to hold the film at, or null to let the connection decide. */
  selectQuality(height: number | null): void {
    this.stream.selectQuality(height);
    const preferences = { ...this.view.preferences, qualityHeight: height };
    this.set({ quality: height, preferences });
    void api.setPreferences(preferences).catch(() => this.notice('Your quality choice could not be saved.', 'error'));
  }

  /**
   * How this viewer's subtitles look. Drawn from the next frame and saved once the choosing pauses,
   * since a slider dragged across its range is one decision and not thirty.
   */
  setSubtitleStyle(subtitleStyle: SubtitleStyle | null): void {
    const preferences = { ...this.view.preferences, subtitleStyle };
    this.set({ preferences });
    if (this.styleSave !== null) clearTimeout(this.styleSave);
    this.styleSave = setTimeout(() => {
      this.styleSave = null;
      void api.setPreferences(this.view.preferences).catch(() => this.notice('Your subtitle style could not be saved.', 'error'));
    }, 600);
  }

  /** Back to whatever was last chosen, or the first track the film offers. */
  toggleSubtitles(): void {
    if (this.view.subtitleId !== null) {
      this.selectSubtitle(null);
      return;
    }
    const first = this.view.manifest?.subtitles.find((track) => track.available && track.uri !== undefined);
    if (first !== undefined) this.selectSubtitle(first.id);
  }

  // Fetching and confirming both act on the film rather than on the viewer: one person doing either
  // does it for the whole room. Which track each person then watches with stays theirs.

  /** Costs no allowance, so the picker may call it whenever it is opened. */
  searchSubtitles(): Promise<SubtitleSearch> {
    // This person's language first, and English beside it: most uploads are English, and a list
    // that offers nothing in the wanted language is better with something in it than empty.
    const wanted = this.view.preferences.subtitleLanguage;
    return api.searchSubtitles(this.requireTitle(), wanted ? [wanted, 'en'] : ['en']);
  }

  /** Spends one of the day's downloads and adds the track for everyone in the room. */
  async fetchSubtitle(fileId: number): Promise<SubtitleTrack> {
    const reply = await api.addSubtitle(this.requireTitle(), fileId);
    await this.refreshManifest();
    return reply.track;
  }

  /**
   * The moment matters: drift only shows up late, so what is recorded is how much of the film this
   * person had actually watched before saying the track fits.
   */
  async pinSubtitle(trackId: string): Promise<void> {
    await api.pinSubtitle(this.requireTitle(), trackId, this.video.currentTime);
    await this.refreshManifest();
  }

  async unpinSubtitle(trackId: string): Promise<void> {
    await api.unpinSubtitle(this.requireTitle(), trackId);
    await this.refreshManifest();
  }

  /**
   * Re-reads the film's subtitles. Only the subtitle list is taken: everything else about the
   * manifest is the transcode's to report, and adopting a fresh copy wholesale would quietly move
   * the head a seek is judged against as a side effect of somebody picking a subtitle.
   */
  async refreshManifest(): Promise<void> {
    const current = this.view.manifest;
    // A film played alone has the subtitles that were saved with it, and nothing else to read.
    if (current === null || this.view.solo) return;
    const fresh = await api.manifest(current.id);
    if (this.view.manifest === null || this.view.manifest.id !== fresh.id) return;
    this.stream.follow({ ...this.view.manifest, subtitles: fresh.subtitles, otherLanguages: fresh.otherLanguages });
    this.set({ manifest: { ...this.view.manifest, subtitles: fresh.subtitles, otherLanguages: fresh.otherLanguages } });
  }

  private requireTitle(): string {
    const manifest = this.view.manifest;
    if (manifest === null) throw new Error('No film is loaded.');
    return manifest.id;
  }
}
