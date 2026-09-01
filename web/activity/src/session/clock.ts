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
  private samples: Sample[] = [];
  private offsetMs = 0;

  observe(sentAtMs: number, receivedAtMs: number, serverTimeIso: string): void {
    const rttMs = receivedAtMs - sentAtMs;
    const offsetMs = Date.parse(serverTimeIso) - (sentAtMs + rttMs / 2);
    if (!Number.isFinite(offsetMs)) return;

    this.samples.push({ rttMs, offsetMs });
    if (this.samples.length > Window) this.samples.shift();

    this.offsetMs = this.samples.reduce((best, s) => (s.rttMs < best.rttMs ? s : best)).offsetMs;
  }

  /** Server time, in epoch milliseconds. */
  now(): number {
    return Date.now() + this.offsetMs;
  }

  get offset(): number {
    return this.offsetMs;
  }

  get roundTrip(): number {
    return this.samples.length === 0
      ? 0
      : this.samples.reduce((best, s) => (s.rttMs < best.rttMs ? s : best)).rttMs;
  }
}
