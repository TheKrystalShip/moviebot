import type { Actor, SessionState, SessionStatePush } from '../types';
import { ServerClock } from './clock';
import type { Timeline } from './hub';

/**
 * A film's own timeline, for a film played alone from the device: the room's timeline with this page
 * as its authority.
 *
 * The sync controller drives the element from it exactly as it does from the hub, so a film played
 * alone starts, stops and seeks by the same rules a room does, and none of them is written twice.
 * What a room needs and a lone viewer does not is simply absent: nothing is sent anywhere, a seek is
 * granted whole because the film is whole, and there is nobody to tell about waiting or quality. The
 * clock is this device's own, since there is no other to agree with.
 */
export class LocalTimeline implements Timeline {
  readonly clock = new ServerClock();
  private state: SessionState;

  constructor(
    sessionId: string,
    titleId: string,
    startSeconds: number,
    private readonly actor: Actor,
    private readonly onState: (push: SessionStatePush) => void
  ) {
    this.state = {
      sessionId,
      titleId,
      paused: false,
      positionSeconds: Math.max(0, startSeconds),
      anchorUtc: this.now(),
      rate: 1,
      revision: 0,
      epoch: 'device'
    };
  }

  start(): Promise<SessionStatePush> {
    return Promise.resolve(this.push());
  }

  /** Nothing can disagree with this timeline, so there is nothing to ask again. */
  resync(): Promise<void> {
    return Promise.resolve();
  }

  loadTitle(titleId: string, startSeconds: number | null = null): Promise<SessionStatePush> {
    return this.change({ titleId, positionSeconds: Math.max(0, startSeconds ?? 0) });
  }

  play(atSeconds: number): Promise<SessionStatePush> {
    return this.change({ paused: false, positionSeconds: atSeconds });
  }

  pause(atSeconds: number): Promise<SessionStatePush> {
    return this.change({ paused: true, positionSeconds: atSeconds });
  }

  seek(toSeconds: number): Promise<SessionStatePush> {
    return this.change({ positionSeconds: Math.max(0, toSeconds) });
  }

  buffering(): Promise<void> {
    return Promise.resolve();
  }

  reportQuality(): Promise<void> {
    return Promise.resolve();
  }

  reportTrouble(): Promise<void> {
    return Promise.resolve();
  }

  stop(): Promise<void> {
    return Promise.resolve();
  }

  /**
   * Applies an act and answers the state it led to. The state also arrives the way the hub's
   * broadcast does, after the act has returned, so the controller meets it as it meets the room's:
   * once the element has finished raising the event that asked for it.
   */
  private change(patch: Partial<SessionState>): Promise<SessionStatePush> {
    this.state = {
      ...this.state,
      ...patch,
      anchorUtc: this.now(),
      revision: this.state.revision + 1,
      updatedBy: this.actor
    };
    const push = this.push();
    queueMicrotask(() => this.onState(push));
    return Promise.resolve(push);
  }

  private push(): SessionStatePush {
    return { state: this.state, serverTime: this.now() };
  }

  private now(): string {
    return new Date(this.clock.now()).toISOString();
  }
}
