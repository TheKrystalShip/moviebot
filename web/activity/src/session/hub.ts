import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { environment, type Identity } from '../environment';
import type { Participant, SeekClamped, SessionState, SessionStatePush } from '../types';
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
  onState(push: SessionStatePush): void;
  onSeekClamped(clamp: SeekClamped): void;
  onParticipants(participants: Participant[]): void;
  onStatus(status: ConnectionStatus): void;
}

const ProbeIntervalMs = 30_000;

export class SessionHub {
  readonly clock = new ServerClock();

  private connection: HubConnection;
  private probeTimer: number | null = null;

  constructor(
    private readonly identity: Identity,
    private readonly sessionId: string,
    private readonly handlers: HubHandlers
  ) {
    this.connection = new HubConnectionBuilder()
      // The WebSocket transport cannot set headers, so SignalR puts the token on the query
      // string itself. The server reads it from there for hub routes only.
      .withUrl(environment().hubUrl(), {
        accessTokenFactory: () => environment().authToken() ?? ''
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('StateChanged', (push: SessionStatePush) => this.handlers.onState(push));
    this.connection.on('SeekClamped', (clamp: SeekClamped) => this.handlers.onSeekClamped(clamp));
    this.connection.on('ParticipantsChanged', (list: Participant[]) => this.handlers.onParticipants(list));

    this.connection.onreconnecting(() => this.handlers.onStatus('reconnecting'));
    this.connection.onclose(() => this.handlers.onStatus('disconnected'));

    // A reconnect leaves the server holding no membership for this connection, so rejoining is
    // what puts the viewer back in the participant list and hands back the state it missed.
    this.connection.onreconnected(async () => {
      this.handlers.onStatus('connected');
      this.handlers.onState(await this.join());
    });
  }

  async start(): Promise<SessionStatePush> {
    this.handlers.onStatus('connecting');
    await this.connection.start();
    this.handlers.onStatus('connected');

    await this.probe();
    this.probeTimer = window.setInterval(() => void this.probe(), ProbeIntervalMs);

    return this.join();
  }

  loadTitle(titleId: string): Promise<SessionStatePush> {
    return this.connection.invoke<SessionStatePush>('LoadTitle', titleId);
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

  get connected(): boolean {
    return this.connection.state === HubConnectionState.Connected;
  }

  async stop(): Promise<void> {
    if (this.probeTimer !== null) window.clearInterval(this.probeTimer);
    await this.connection.stop();
  }

  private join(): Promise<SessionStatePush> {
    return this.connection.invoke<SessionStatePush>(
      'Join',
      this.sessionId,
      this.identity.userId,
      this.identity.displayName
    );
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
