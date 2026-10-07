import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { environment } from '../environment';
import type { Manifest, Participant, SeekClamped, SessionState, SessionStatePush } from '../types';
import { ServerClock } from './clock';

/**
 * Where the room is now, derived from the anchor rather than read as a ticking number. A client
 * that missed ten seconds of pushes still computes the right answer.
 */
export function derivePosition(state: SessionState, serverNowMs: number): number {
  if (state.paused) return state.positionSeconds;
  const elapsed = ((serverNowMs - Date.parse(state.anchorUtc)) / 1000) * state.rate;
  return Math.max(0, state.positionSeconds + elapsed);
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
}

/**
 * What answers for a room's timeline: the hub, or a film's own timeline when it plays alone
 * (`local.ts`). Every act is intent, answered with the state it led to.
 */
export interface Timeline {
  readonly clock: ServerClock;
  start(): Promise<SessionStatePush>;
  resync(): Promise<void>;
  loadTitle(titleId: string, startSeconds?: number | null): Promise<SessionStatePush>;
  play(atSeconds: number): Promise<SessionStatePush>;
  pause(atSeconds: number): Promise<SessionStatePush>;
  seek(toSeconds: number): Promise<SessionStatePush>;
  buffering(waiting: boolean): Promise<void>;
  reportQuality(playingHeight: number | null, wantedHeight: number | null): Promise<void>;
  stop(): Promise<void>;
}

const ProbeIntervalMs = 30_000;

export class SessionHub implements Timeline {
  readonly clock = new ServerClock();

  private connection: HubConnection;
  private probeTimer: number | null = null;
  private lastQuality: [number | null, number | null] | null = null;

  constructor(
    private readonly sessionId: string,
    private readonly handlers: HubHandlers
  ) {
    this.connection = new HubConnectionBuilder()
      // The WebSocket transport cannot set headers, so SignalR puts the token on the query
      // string itself. The server reads it from there for hub routes only, and admits the
      // connection on it when it opens; a reconnect asks for a fresh one.
      .withUrl(environment().hubUrl(), {
        accessTokenFactory: async () => (await environment().freshToken()) ?? ''
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('StateChanged', (push: SessionStatePush) => this.handlers.onState(push));
    this.connection.on('SeekClamped', (clamp: SeekClamped) => this.handlers.onSeekClamped(clamp));
    this.connection.on('ParticipantsChanged', (list: Participant[]) => this.handlers.onParticipants(list));
    this.connection.on('TitleChanged', (manifest: Manifest) => this.handlers.onTitle(manifest));

    this.connection.onreconnecting(() => this.handlers.onStatus('reconnecting'));
    this.connection.onclose(() => this.handlers.onStatus('disconnected'));

    // A reconnect leaves the server holding no membership for this connection, so rejoining is
    // what puts the viewer back in the participant list and hands back the state it missed.
    // What comes back settles the room's position rather than joining the queue of broadcasts:
    // a client that has been away has no standing to judge it against the last thing it heard.
    this.connection.onreconnected(async () => {
      this.handlers.onStatus('connected');
      this.handlers.onState(await this.join(), true);
      if (this.lastQuality !== null) void this.reportQuality(...this.lastQuality);
    });
  }

  /** Asks the room where it is, and takes the answer as the authority. */
  async resync(): Promise<void> {
    if (!this.connected) return;
    this.handlers.onState(await this.join(), true);
  }

  async start(): Promise<SessionStatePush> {
    this.handlers.onStatus('connecting');
    await this.connection.start();
    this.handlers.onStatus('connected');

    await this.probe();
    this.probeTimer = window.setInterval(() => void this.probe(), ProbeIntervalMs);

    return this.join();
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

  /** Tells the room whether this viewer is waiting for the film. Moves nothing, and a lost one costs only the mark. */
  async buffering(waiting: boolean): Promise<void> {
    if (!this.connected) return;
    try {
      await this.connection.invoke('Buffering', waiting);
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

  /** Who joins is whoever the connection's token names; the room is the only thing said. */
  private join(): Promise<SessionStatePush> {
    return this.connection.invoke<SessionStatePush>('Join', this.sessionId);
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
