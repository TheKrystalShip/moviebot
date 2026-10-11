import { api } from './api';
import { environment, type Identity } from './environment';
import { formatTime } from './format';
import { prefs } from './prefs';
import { playableHead, playableHeadOf } from './media/heads';
import type { DeviceCopy } from './media/deviceLoader';
import { FilmStream, type QualityLevel } from './media/stream';
import { startingTracks } from './media/tracks';
import { describeChange } from './session/changes';
import { SessionHub, type ConnectionStatus, type Timeline } from './session/hub';
import { LocalTimeline } from './session/local';
import { Ordered } from './session/ordered';
import { SyncController } from './session/sync';
import type {
  Actor, FilmTracks, GuestRefusal, Manifest, Participant, RoomDoor, SeekClamped, SessionState, SessionStatePush, SubtitleSearch,
  SubtitleStyle, SubtitleTrack, ThumbnailStrip, TitleSummary, ViewerPreferences
} from './types';

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
  /**
   * Where this viewer stands: in the room, or at its door because it is private to them — until they
   * ask (`private`), while they wait for an answer (`asking`), and once somebody in it said no
   * (`refused`). Null until the room has answered.
   */
  entry: 'in' | 'private' | 'asking' | 'refused' | null;
  /** Who may come into the room, for a viewer in it. */
  door: RoomDoor | null;
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
  /** Another device of this person's is changing how subtitles look: a sample line stands in while the film is silent. */
  styleSample: boolean;
  /** The film plays alone from this device: there is no room, nobody else and nothing to invite to. */
  solo: boolean;
  /** The film this viewer holds is saved whole on this device, so it can go on with no connection. */
  saved: boolean;
  /** The poster saved on the device, as an address the page can draw, when the film plays alone. */
  artwork: string | null;
  /**
   * A guest on the room's link: they watch the room and change nothing about it. Every act that would
   * move the room is a no-op, and the page draws none of them.
   */
  viewOnly: boolean;
  /**
   * An episode was asked for and the room has not moved since: Next, Previous and the episodes
   * offer no second press until it does, since every press of one fetches what is not here.
   */
  stepping: boolean;
  /** Why this guest is not watching: never let in, or sent away. Null while they are. */
  guestEnded: GuestRefusal | null;
}

/**
 * How long a sample line stays over the film after the last change to how subtitles look arrived from
 * another device, so a style being chosen elsewhere is seen here while it is chosen.
 */
const StyleSampleLapseMs = 4000;

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
   * The film the launch named, which is what an empty room is given, until the room holds anything.
   * From then on the room is the authority on what it holds.
   */
  private launched: string | null = null;
  /** The room's hub, for the acts only a room has: moving through a show. Null for a film played alone. */
  private roomHub: SessionHub | null = null;
  private previous: SessionState | null = null;
  private noticeIds = 0;
  private readonly subscribers = new Set<() => void>();
  private readonly announcers = new Set<(announcement: Announcement) => void>();
  private disposed = false;
  private styleSave: ReturnType<typeof setTimeout> | null = null;
  private styleSampleLapse: ReturnType<typeof setTimeout> | null = null;
  /** The API's pushes, applied in the order its writes were kept: one stream for preferences, one per film for tracks. */
  private readonly preferencesOrder = new Ordered<ViewerPreferences>((preferences) => this.applyPreferences(preferences));
  private readonly tracksOrder = new Map<string, Ordered<FilmTracks>>();
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
      entry: null,
      door: null,
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
      styleSample: false,
      playingHeight: null,
      solo: environment().solo,
      saved: false,
      artwork: null,
      viewOnly: environment().guest !== null,
      stepping: false,
      guestEnded: null
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
        void this.hub?.buffering(waiting, video.currentTime);
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
    // them the film still plays, on the film's own tracks and the connection's choice of quality. A
    // guest's are this browser's.
    const guest = env.guest;
    if (guest !== null) {
      const preferences = guest.preferences();
      this.set({ preferences, quality: preferences.qualityHeight ?? null });
    } else {
      try {
        const preferences = await api.preferences();
        this.set({ preferences, quality: preferences.qualityHeight ?? null });
      } catch {
        this.notice('Your playback settings could not be read, so this film starts on its own defaults.', 'error');
      }
    }

    this.launched = env.titleId();
    const sessionId = env.sessionId();

    const hub = new SessionHub(sessionId, {
      onState: (push, resync) => this.controller?.applyPush(push, resync),
      onSeekClamped: (clamp) => this.clampNotice(clamp),
      onParticipants: (list) => {
        this.set({ participants: list });
        void this.showPresence();
      },
      onTitle: (fresh) => this.followTitle(fresh),
      onDoor: (door) => this.set({ door }),
      onKnockAnswered: (admitted) => {
        if (admitted) void this.comeIn();
        else this.set({ entry: 'refused' });
      },
      // The room this page was in was forgotten and made again by somebody else, private to them.
      onShutOut: () => {
        this.video.pause();
        this.set({ entry: 'private', door: null });
      },
      onGuestEnded: (reason) => this.endGuest(reason),
      onPreferences: (preferences) => this.preferencesOrder.heard(preferences),
      onTracks: (tracks) => this.tracksOrderOf(tracks.titleId).heard(tracks),
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
    this.roomHub = hub;
    this.controller = this.controllerFor(hub, identity);

    // A guest has the room's film and no library.
    if (guest === null) {
      try {
        this.set({ titles: await api.titles() });
      } catch {
        this.notice('The library could not be read.', 'error');
      }
    }

    let joined;
    try {
      joined = await hub.start();
    } catch {
      this.set({ fatal: 'The room could not be reached. Try again in a moment.', connection: 'disconnected' });
      return;
    }
    if (this.disposed) return;

    if (joined.you) this.set({ identity: { userId: joined.you.userId, displayName: joined.you.displayName } });
    if (joined.push) await this.entered(hub, joined.push);
    else if (joined.refused) this.endGuest(joined.refused);
    else this.set({ entry: 'private' });
  }

  /** This guest is not watching: never let in, or sent away. Nothing more is applied to the element. */
  private endGuest(reason: GuestRefusal): void {
    this.controller?.setPlayerReady(false);
    this.controller?.setLive(false);
    this.video.pause();
    this.set({ guestEnded: reason });
  }

  /** Everything that follows from being in the room, whether this viewer walked in or was let in. */
  private async entered(hub: SessionHub, joined: SessionStatePush): Promise<void> {
    if (this.controller === null) return;
    this.set({ entry: 'in' });
    // The launch is read before the room's state is applied: a state holding anything spends it
    // (`onState`), and the front door forgets it from the address, `at` with it.
    const launch = this.launched === null ? null : { titleId: this.launched, at: environment().startAt() };
    this.controller.applyPush(joined, true);
    // A guest is told who is in the room by the hub alone, which says so as they arrive.
    if (!this.view.viewOnly) {
      try {
        this.set({ participants: await api.participants(this.view.sessionId) });
      } catch {
        /* The hub's next list carries them. */
      }
    }
    void this.showPresence();

    // Coming back to a tab that has been in the background is the moment its picture of the room
    // is least likely to be right, and the cheapest moment to ask.
    document.addEventListener('visibilitychange', this.onVisible);

    // A launch link names the film. The room is told once, by whoever arrives while it is showing
    // something else; a viewer joining a room that already holds the title says nothing, because
    // loading it again would send everyone back to the beginning. Once the room holds anything the
    // front door forgets the launch (`Environment.forgetLaunch`), so a reload tells the room nothing.
    const held = joined.state.titleId ?? null;
    if (launch !== null && (held !== null || joined.state.preparing != null) && held !== launch.titleId) {
      await hub.loadTitle(launch.titleId, launch.at);
    }
  }

  // ── The room's door ──────────────────────────────────────────────────────────────────────

  /** Asks the people in a room private to this viewer to let them in. */
  async askToJoin(): Promise<void> {
    const hub = this.roomHub;
    if (hub === null || this.view.viewOnly) return;
    this.set({ entry: 'asking' });
    try {
      if (await hub.knock()) await this.comeIn();
    } catch {
      this.set({ entry: 'private' });
      this.notice('Asking to join did not reach the room. Try again in a moment.', 'error');
    }
  }

  /** Opens the room to anybody who may watch, or closes it to anybody not already let in. */
  setOpen(open: boolean): void {
    if (this.view.viewOnly) return;
    void this.roomHub?.setOpen(open).catch(() => this.notice('The room could not be changed.', 'error'));
  }

  /** Lets somebody asking into the room, or tells them they are not let in. */
  answerKnock(userId: string, admit: boolean): void {
    if (this.view.viewOnly) return;
    void this.roomHub?.answer(userId, admit).catch(() => this.notice('The answer did not reach the room.', 'error'));
  }

  /** Enters the room once this page was let in. */
  private async comeIn(): Promise<void> {
    const hub = this.roomHub;
    if (hub === null || this.disposed) return;
    const reply = await hub.enter().catch(() => null);
    if (this.disposed) return;
    if (reply?.push) await this.entered(hub, reply.push);
    else this.set({ entry: 'private' });
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
    }, this.view.viewOnly);
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
      void this.savePreferences(this.view.preferences).catch(() => {});
    }
    if (this.styleSampleLapse !== null) clearTimeout(this.styleSampleLapse);
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
    const moved = previous === null || state.epoch !== previous.epoch || state.revision > previous.revision;
    this.set({ state, actor: describeChange(state, previous), ...(moved ? { stepping: false } : {}) });
    void this.showPresence();

    // A room held for its players while this one is playing has not heard it say so, and would
    // stay held for as long as it went unsaid.
    if (state.waiting === true && this.view.ready && !this.view.stalled) {
      void this.hub?.buffering(false, this.video.currentTime);
    }

    // Normalised first: a room holding no film leaves the field out of the state altogether, so
    // what arrives is undefined and never the null it is compared against.
    const titleId = state.titleId ?? null;
    // The room holds one thing: a title, or something arriving, which is no title to this player.
    // A room holding nothing at all is given the film its launch named, from where it said.
    const holds = titleId !== null || state.preparing != null;
    if (!holds && this.launched !== null) {
      void this.hub?.loadTitle(this.launched, environment().startAt());
      this.launched = null;
    } else if (holds && !this.view.solo) {
      this.launched = null;
      environment().forgetLaunch();
    }

    if (titleId !== this.loadedTitleId) {
      void this.loadTitle(titleId);
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
    const held = await this.chosenTracks(manifest.id);
    if (this.loadedTitleId !== titleId) return;
    const { audioId, subtitleId } = startingTracks(manifest, held, this.view.preferences, copy?.manifest);

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
      // A room held for its players has a clock that does not run, and the bar is that clock.
      paused: state.paused || state.waiting === true,
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
    if (this.view.viewOnly) return;
    void this.hub?.loadTitle(titleId);
  }

  /**
   * Moves the room through the show it holds: the episode after, the one before, or one by its place.
   * The server decides what that is and how it gets there; a refusal is said over the film.
   */
  nextEpisode(): void {
    this.step(() => this.roomHub?.next());
  }

  previousEpisode(): void {
    this.step(() => this.roomHub?.previous());
  }

  playEpisode(season: number, number: number): void {
    this.step(() => this.roomHub?.playEpisode(season, number));
  }

  /** One episode asked for at a time: the next waits for the room to move, or for the refusal. */
  private step(ask: () => Promise<void> | undefined): void {
    if (this.view.viewOnly || this.view.stepping) return;
    const asked = ask();
    if (asked === undefined) return;
    this.set({ stepping: true });
    asked.catch((error: unknown) => {
      this.set({ stepping: false });
      this.notice(hubMessage(error) ?? 'That episode cannot be put on.', 'error');
    });
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
    if (!this.stream.holdsFilm || this.view.catchingUp || this.view.viewOnly) return;
    if (this.video.paused) void this.video.play().catch(() => {});
    else this.video.pause();
  }

  /** A seek is intent: the playhead moves when the room answers. */
  seek(seconds: number): void {
    if (this.view.viewOnly) return;
    void this.hub?.seek(Math.max(0, seconds));
  }

  /**
   * A relative seek, measured from this viewer's playhead while it holds the film and from the
   * room otherwise: an element that never loaded, or one a recovery has just emptied, sits at
   * zero, and five seconds on from there is the opening titles for everyone. Answers how far it
   * actually asked to move, so the page can say so.
   */
  seekBy(seconds: number): number {
    if (this.view.viewOnly) return 0;
    const duration = this.view.manifest?.durationSeconds ?? 0;
    const from = this.stream.holdsFilm ? this.video.currentTime : this.roomPosition();
    const to = Math.min(duration, Math.max(0, from + seconds));
    this.seek(to);
    return to - from;
  }

  /**
   * The audio track, chosen by its id. An id the film does not list is refused: it would be kept as
   * this film's choice and found nowhere.
   */
  selectAudio(trackId: string): void {
    if (!this.applyAudio(trackId)) return;
    this.keepTracks({ audioId: trackId });
  }

  /**
   * The subtitle track, chosen by its id, or null for none. An id the film does not list as a track
   * that can be shown is refused: the stream would clear the subtitles it was showing, and the id
   * would be kept as this film's choice.
   */
  selectSubtitle(trackId: string | null): void {
    if (!this.applySubtitle(trackId)) return;
    this.keepTracks(trackId === null ? { subtitleOff: true } : { subtitleId: trackId });
  }

  private applyAudio(trackId: string): boolean {
    const manifest = this.view.manifest;
    if (manifest === null || !manifest.audio.some((track) => track.id === trackId)) return false;
    this.stream.selectAudio(trackId);
    this.set({ audioId: trackId });
    return true;
  }

  private applySubtitle(trackId: string | null): boolean {
    const manifest = this.view.manifest;
    if (manifest === null) return false;
    if (trackId !== null
      && !manifest.subtitles.some((track) => track.id === trackId && track.available && track.uri !== undefined)) return false;
    void this.stream.selectSubtitle(trackId);
    this.set({ subtitleId: trackId });
    return true;
  }

  /** A height to hold the film at, or null to let the connection decide. */
  selectQuality(height: number | null): void {
    if (!this.applyQuality(height)) return;
    const preferences = { ...this.view.preferences, qualityHeight: height };
    this.set({ preferences });
    void this.savePreferences(preferences).catch(() => this.notice('Your quality choice could not be saved.', 'error'));
  }

  /** Auto, or a height; anything else is not a quality. */
  private applyQuality(height: number | null): boolean {
    if (height !== null && !(Number.isFinite(height) && height > 0)) return false;
    this.stream.selectQuality(height);
    this.set({ quality: height });
    return true;
  }

  /**
   * Where this viewer's track choices for a film are kept: the API for a person, so every device they
   * watch it on agrees, and this browser for a guest, who has no account, and for a saved film played
   * alone with no connection.
   */
  private tracksKeptHere(): boolean {
    const env = environment();
    return env.guest !== null || (this.view.solo && !env.online());
  }

  /** The tracks chosen for a film: from the API or from this browser (`tracksKeptHere`), none when nothing was chosen. */
  private async chosenTracks(titleId: string): Promise<{ audioTrackId?: string; subtitleTrackId?: string | null }> {
    if (this.tracksKeptHere()) return prefs.forTitle(titleId);
    try {
      const tracks = await api.tracks(titleId);
      return {
        audioTrackId: tracks.audioId ?? undefined,
        subtitleTrackId: tracks.subtitleOff === true ? null : tracks.subtitleId ?? undefined
      };
    } catch {
      this.notice('Your track choices could not be read, so this film starts on your languages.', 'error');
      return {};
    }
  }

  private keepTracks(choice: { audioId?: string; subtitleId?: string; subtitleOff?: boolean }): void {
    const manifest = this.view.manifest;
    if (manifest === null) return;
    if (this.tracksKeptHere()) {
      if (choice.audioId !== undefined) prefs.setAudioTrack(manifest.id, choice.audioId);
      if (choice.subtitleId !== undefined) prefs.setSubtitleTrack(manifest.id, choice.subtitleId);
      if (choice.subtitleOff === true) prefs.setSubtitleTrack(manifest.id, null);
      return;
    }
    const done = this.tracksOrderOf(manifest.id).writing();
    api.setTracks(manifest.id, choice).then(done, () => {
      done(null);
      this.notice('Your track choice could not be saved.', 'error');
    });
  }

  private tracksOrderOf(titleId: string): Ordered<FilmTracks> {
    let order = this.tracksOrder.get(titleId);
    if (order === undefined) {
      order = new Ordered<FilmTracks>((tracks) => this.applyTracks(tracks));
      this.tracksOrder.set(titleId, order);
    }
    return order;
  }

  /**
   * The tracks this person watches a film with changed on another device (or this one, echoed): the
   * film this viewer holds follows, and any other film reads them when it starts.
   */
  private applyTracks(tracks: FilmTracks): void {
    if (this.view.manifest?.id !== tracks.titleId) return;
    if (tracks.subtitleOff === true) {
      if (this.view.subtitleId !== null) this.applySubtitle(null);
    } else if (tracks.subtitleId && tracks.subtitleId !== this.view.subtitleId) {
      const wanted = tracks.subtitleId;
      // A track another device just fetched may not have reached this copy of the film's list yet.
      if (!this.applySubtitle(wanted)) {
        void this.refreshManifest().then(() => {
          if (this.view.subtitleId !== wanted) this.applySubtitle(wanted);
        }).catch(() => {});
      }
    }
    if (tracks.audioId && tracks.audioId !== this.view.audioId) this.applyAudio(tracks.audioId);
  }

  /**
   * How this person watches changed on another device (or this one, echoed). The record replaces
   * the one held, so what a save writes back is current; a style being chosen here is left alone
   * until it is saved, and a style that arrives from elsewhere shows its sample line for a few seconds.
   */
  private applyPreferences(preferences: ViewerPreferences): void {
    const before = this.view.preferences;
    const choosingStyle = this.styleSave !== null;
    const next = choosingStyle ? { ...preferences, subtitleStyle: before.subtitleStyle } : preferences;
    const styled = JSON.stringify(before.subtitleStyle ?? null) !== JSON.stringify(next.subtitleStyle ?? null);
    this.set({ preferences: next });
    if (styled) this.sampleStyle();
    const quality = next.qualityHeight ?? null;
    if (quality !== this.view.quality) this.applyQuality(quality);
  }

  /** Puts the sample line over the film, and takes it away a few seconds after the last time this is asked. */
  private sampleStyle(): void {
    this.set({ styleSample: true });
    if (this.styleSampleLapse !== null) clearTimeout(this.styleSampleLapse);
    this.styleSampleLapse = setTimeout(() => {
      this.styleSampleLapse = null;
      this.set({ styleSample: false });
    }, StyleSampleLapseMs);
  }

  /** Keeps how this viewer watches: with the API for a person, in this browser for a guest. */
  private async savePreferences(preferences: ViewerPreferences): Promise<void> {
    const guest = environment().guest;
    if (guest !== null) guest.keepPreferences(preferences);
    else {
      const done = this.preferencesOrder.writing();
      try {
        done(await api.setPreferences(preferences));
      } catch (error) {
        done(null);
        throw error;
      }
    }
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
      void this.savePreferences(this.view.preferences).catch(() => this.notice('Your subtitle style could not be saved.', 'error'));
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

/** What the server said when it refused a hub call, without the client library's wrapping. */
function hubMessage(error: unknown): string | null {
  const said = error instanceof Error ? error.message : '';
  const at = said.indexOf('HubException: ');
  return at >= 0 ? said.slice(at + 'HubException: '.length) : null;
}
