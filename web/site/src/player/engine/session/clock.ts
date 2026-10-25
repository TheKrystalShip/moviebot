/**
 * An estimate of the server's clock.
 *
 * Position is derived against server time, so a viewer whose machine is a minute fast must not
 * be able to drag the room. The offset comes from round trips to `ServerTime`, and the sample
 * with the shortest round trip wins: it is the one whose one-way delay is closest to half the
 * measurement.
 */
interface Sample {
  rttMs: number;
  offsetMs: number;
}

const Window = 8;

export class ServerClock {
  private samplesHeld: Sample[] = [];
  private offsetMs = 0;

  observe(sentAtMs: number, receivedAtMs: number, serverTimeIso: string): void {
    const rttMs = receivedAtMs - sentAtMs;
    const offsetMs = Date.parse(serverTimeIso) - (sentAtMs + rttMs / 2);
    if (!Number.isFinite(offsetMs)) return;

    this.samplesHeld.push({ rttMs, offsetMs });
    if (this.samplesHeld.length > Window) this.samplesHeld.shift();

    this.offsetMs = this.samplesHeld.reduce((best, s) => (s.rttMs < best.rttMs ? s : best)).offsetMs;
  }

  /** Server time, in epoch milliseconds. */
  now(): number {
    return Date.now() + this.offsetMs;
  }

  get offset(): number {
    return this.offsetMs;
  }

  /** How many round trips the estimate is drawn from: none means it is no estimate yet. */
  get samples(): number {
    return this.samplesHeld.length;
  }

  get roundTrip(): number {
    return this.samplesHeld.length === 0
      ? 0
      : this.samplesHeld.reduce((best, s) => (s.rttMs < best.rttMs ? s : best)).rttMs;
  }
}
