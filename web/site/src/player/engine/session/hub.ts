import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { environment } from '../environment';
import type {
  FilmTracks, GuestRefusal, JoinReply, Manifest, Participant, RoomDoor, SeekClamped, SessionState, SessionStatePush,
  ViewerPreferences
} from '../types';
import { ServerClock } from './clock';

/**
 * Where the room is now, derived from the anchor rather than read as a ticking number. A client
 * that missed ten seconds of pushes still computes the right answer. A room held for its players
 * stays where it was held.
 */
export function derivePosition(state: SessionState, serverNowMs: number): number {
  if (state.paused || state.waiting === true) return state.positionSeconds;
  const elapsed = ((serverNowMs - Date.parse(state.anchorUtc)) / 1000) * state.rate;
  return Math.max(0, state.positionSeconds + elapsed);
}

/**
 * A connection to the hub, not yet started. The WebSocket transport cannot set headers, so SignalR
 * puts the token on the query string itself; the server reads it from there for hub routes only and
 * admits the connection on it when it opens. A reconnect asks for a fresh token. Every connection to
 * the hub is built here, a room's and a television's own alike.
 */
export function buildHubConnection(hubUrl: string, freshToken: () => Promise<string | null>): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(hubUrl, { accessTokenFactory: async () => (await freshToken()) ?? '' })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export interface HubHandlers {
  /** A resync is the server answering where the room is, rather than announcing that it moved. */
  onState(push: SessionStatePush, resync?: boolean): void;
  onSeekClamped(clamp: SeekClamped): void;
  onParticipants(participants: Participant[]): void;
  /** The manifest of a title this room holds changed on the server: head, sheet, subtitles, status. */
  onTitle(manifest: Manifest): void;
  onStatus(status: ConnectionStatus): void;
  /** Who may come into the room, as it stands: on joining, and whenever it changes. */
  onDoor(door: RoomDoor): void;
  /** This page asked to be let in, and somebody in the room answered. */
  onKnockAnswered(admitted: boolean): void;
  /** Coming back into the room was refused: it is a different room now, private to this viewer. */
  onShutOut(): void;
  /** This guest was sent away, or not let back in, and why. The connection is closed by then. */
  onGuestEnded(reason: GuestRefusal): void;
  /** How this person watches changed, on this device or another: the whole record as the API keeps it. */
  onPreferences(preferences: ViewerPreferences): void;
  /** The tracks this person watches a film with changed, on this device or another. */
  onTracks(tracks: FilmTracks): void;
}

/**
 * What answers for a room's timeline: the hub, or a film's own timeline when it plays alone
 * (`local.ts`). Every act is intent, answered with the state it led to.
 */
export interface Timeline {
  readonly clock: ServerClock;
  resync(): Promise<void>;
  loadTitle(titleId: string, startSeconds?: number | null): Promise<SessionStatePush>;
  play(atSeconds: number): Promise<SessionStatePush>;
  pause(atSeconds: number): Promise<SessionStatePush>;
  seek(toSeconds: number): Promise<SessionStatePush>;
  buffering(waiting: boolean, atSeconds: number): Promise<void>;
  reportQuality(playingHeight: number | null, wantedHeight: number | null): Promise<void>;
  stop(): Promise<void>;
}

const ProbeIntervalMs = 30_000;

export class SessionHub implements Timeline {
  readonly clock = new ServerClock();

  private connection: HubConnection;
  private probeTimer: number | null = null;
  private lastQuality: [number | null, number | null] | null = null;
  /** The room let this page in. Until it does, the page is at the door, asking or not. */
  private inside = false;
  /** This page is asking to be let in, which a reconnect has to say again: the server forgot it. */
  private asking = false;

  constructor(
    private readonly sessionId: string,
    private readonly handlers: HubHandlers
  ) {
    this.connection = buildHubConnection(environment().hubUrl(), () => environment().freshToken());

    this.connection.on('StateChanged', (push: SessionStatePush) => this.handlers.onState(push));
    this.connection.on('SeekClamped', (clamp: SeekClamped) => this.handlers.onSeekClamped(clamp));
    this.connection.on('ParticipantsChanged', (list: Participant[]) => this.handlers.onParticipants(list));
    this.connection.on('TitleChanged', (manifest: Manifest) => this.handlers.onTitle(manifest));
    this.connection.on('DoorChanged', (door: RoomDoor) => this.handlers.onDoor(door));
    // The person's own settings, pushed to every connection of theirs that has an account: never the room's.
    this.connection.on('PreferencesChanged', (preferences: ViewerPreferences) => this.handlers.onPreferences(preferences));
    this.connection.on('TracksChanged', (tracks: FilmTracks) => this.handlers.onTracks(tracks));
    this.connection.on('KnockAnswered', (admitted: boolean) => {
      this.asking = false;
      this.handlers.onKnockAnswered(admitted);
    });
    // The server closes a guest's connection right after saying this; stopping it here first keeps the
    // client from reconnecting to a room that has just sent it away.
    this.connection.on('GuestAccessEnded', (reason: GuestRefusal) => this.endGuest(reason));

    this.connection.onreconnecting(() => this.handlers.onStatus('reconnecting'));
    this.connection.onclose(() => this.handlers.onStatus('disconnected'));

    // A reconnect leaves the server holding no membership for this connection, so rejoining is
    // what puts the viewer back in the participant list and hands back the state it missed.
    // What comes back settles the room's position rather than joining the queue of broadcasts:
    // a client that has been away has no standing to judge it against the last thing it heard.
    // A page still at the door asks again, since its asking went with the connection.
    this.connection.onreconnected(async () => {
      this.handlers.onStatus('connected');
      if (this.inside) await this.rejoin();
      else if (this.asking && await this.knock()) this.handlers.onKnockAnswered(true);
      if (this.lastQuality !== null) void this.reportQuality(...this.lastQuality);
    });
  }

  /** Asks the room where it is, and takes the answer as the authority. */
  async resync(): Promise<void> {
    if (!this.connected || !this.inside) return;
    await this.rejoin();
  }

  /** Connects and enters the room, which answers with where it is, or that it is private to this viewer. */
  async start(): Promise<JoinReply> {
    this.handlers.onStatus('connecting');
    await this.connection.start();
    this.handlers.onStatus('connected');

    await this.probe();
    this.probeTimer = window.setInterval(() => void this.probe(), ProbeIntervalMs);

    return this.enter();
  }

  /** Enters the room on a connection already open: the first time, or once this page was let in. */
  async enter(): Promise<JoinReply> {
    const reply = await this.join();
    this.inside = reply.admitted;
    if (reply.door) this.handlers.onDoor(reply.door);
    return reply;
  }

  /**
   * Asks to be let into a room private to this viewer. True when it need not ask and can enter now;
   * otherwise the room's answer arrives as `onKnockAnswered`.
   */
  async knock(): Promise<boolean> {
    this.asking = true;
    const now = await this.connection.invoke<boolean>('Knock', this.sessionId);
    if (now) this.asking = false;
    return now;
  }

  /** Opens the room to anybody who may watch, or closes it to anybody not already let in. */
  setOpen(open: boolean): Promise<void> {
    return this.connection.invoke('SetOpen', open);
  }

  /** Lets somebody asking into the room, or tells them they are not let in. */
  answer(userId: string, admit: boolean): Promise<void> {
    return this.connection.invoke('Answer', userId, admit);
  }

  /** Puts a film on for the room, from the beginning or from `startSeconds`. */
  loadTitle(titleId: string, startSeconds: number | null = null): Promise<SessionStatePush> {
    return this.connection.invoke<SessionStatePush>('LoadTitle', titleId, startSeconds);
  }

  /** Asks the room for the episode after what it holds; the server decides how it gets there. */
  next(): Promise<void> {
    return this.connection.invoke('Next');
  }

  /** Asks the room for the episode before what it holds. */
  previous(): Promise<void> {
    return this.connection.invoke('Previous');
  }

  /** Asks the room for one episode of the show it holds. */
  playEpisode(season: number, number: number): Promise<void> {
    return this.connection.invoke('PlayEpisode', season, number);
  }

  play(atSeconds: number): Promise<SessionStatePush> {
    return this.connection.invoke<SessionStatePush>('Play', atSeconds);
  }

  pause(atSeconds: number): Promise<SessionStatePush> {
    return this.connection.invoke<SessionStatePush>('Pause', atSeconds);
  }

  seek(toSeconds: number): Promise<SessionStatePush> {
    return this.connection.invoke<SessionStatePush>('Seek', toSeconds);
  }

  /**
   * Tells the room whether this viewer is waiting for the film, and where its playhead stopped. A
   * room whose every player waits is held there until one plays again.
   */
  async buffering(waiting: boolean, atSeconds: number): Promise<void> {
    // A guest's player holds nothing: the room is not told about it.
    if (!this.connected || environment().guest !== null) return;
    try {
      await this.connection.invoke('Buffering', waiting, atSeconds);
    } catch {
      /* The next change says it again. */
    }
  }

  /**
   * Tells the server which rung this viewer plays and which their connection would pick. It goes up
   * only: the server keeps it for its own planning and sends it to nobody, so quality stays this
   * viewer's. Kept and sent again after a reconnect, which leaves the server holding nothing for
   * the new connection.
   */
  async reportQuality(playingHeight: number | null, wantedHeight: number | null): Promise<void> {
    this.lastQuality = [playingHeight, wantedHeight];
    if (!this.connected) return;
    try {
      await this.connection.invoke('ReportQuality', playingHeight, wantedHeight);
    } catch {
      /* The heartbeat says it again. */
    }
  }

  get connected(): boolean {
    return this.connection.state === HubConnectionState.Connected;
  }

  async stop(): Promise<void> {
    if (this.probeTimer !== null) window.clearInterval(this.probeTimer);
    await this.connection.stop();
  }

  /**
   * Who joins is whoever the connection's token names; the room is the only thing said. A guest's link
   * names the room, and the guest says what to call them.
   */
  private join(): Promise<JoinReply> {
    const guest = environment().guest;
    return guest === null
      ? this.connection.invoke<JoinReply>('Join', this.sessionId)
      : this.connection.invoke<JoinReply>('JoinAsGuest', guest.name);
  }

  /** Comes back into the room this page was in, taking where it is as the authority. */
  private async rejoin(): Promise<void> {
    const reply = await this.enter();
    if (reply.push) this.handlers.onState(reply.push, true);
    else if (reply.refused) this.endGuest(reply.refused);
    else this.handlers.onShutOut();
  }

  private endGuest(reason: GuestRefusal): void {
    this.inside = false;
    void this.stop();
    this.handlers.onGuestEnded(reason);
  }

  private async probe(): Promise<void> {
    if (!this.connected) return;
    const sentAt = Date.now();
    try {
      const serverTime = await this.connection.invoke<string>('ServerTime');
      this.clock.observe(sentAt, Date.now(), serverTime);
    } catch {
      // A missed probe costs accuracy, never correctness: the previous offset stands.
    }
  }
}
