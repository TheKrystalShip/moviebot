/**
 * How far past the playhead Buffer ahead's Dynamic keeps the film on the device (`ahead.ts`), grown
 * from what the connection does. It starts at nothing, which is Standard: the player's own few minutes
 * in memory. It grows while filling is cheap (the network carries several times what the film needs,
 * or the device is on an unmetered link) and after the film stalls or the server cannot be reached,
 * which is the connection saying it comes and goes. It never shrinks within a title, since what is
 * kept is already paid for, and it does not grow at all with the browser's data saver on.
 */

/** The horizon is looked at again no more often than this. */
export const GrowEveryMs = 30_000;
/** A stall this recent still counts as the connection coming and going. */
export const StallMemoryMs = 10 * 60_000;

/** What the connection is doing, as the stream measures it. */
export interface Link {
  /** The network's measured throughput over what the film plays at, or null before it is measured. */
  ratio: number | null;
  saveData: boolean;
  /** Wi-Fi or a wire, where the browser says which. */
  unmetered: boolean;
}

export class Horizon {
  private seconds = 0;
  private grownAt = Number.NEGATIVE_INFINITY;
  private stalledAt = Number.NEGATIVE_INFINITY;

  /** Keeps everything ahead from the start, which is A lot. */
  readonly whole: boolean;

  constructor(whole: boolean) {
    this.whole = whole;
  }

  /** How far past the playhead the device keeps the film, in seconds. */
  get ahead(): number {
    return this.whole ? Number.POSITIVE_INFINITY : this.seconds;
  }

  stalled(now = performance.now()): void {
    this.stalledAt = now;
  }

  /** Looks at the connection again; true when the horizon grew. */
  grow(link: Link, now = performance.now()): boolean {
    if (this.whole || link.saveData || now - this.grownAt < GrowEveryMs) return false;
    const ratio = link.ratio ?? 0;
    const shaky = now - this.stalledAt < StallMemoryMs;
    const step = link.unmetered || ratio >= 3 ? 600 : shaky ? 300 : ratio >= 1.5 ? 120 : 0;
    const before = this.seconds;
    if (shaky) this.seconds = Math.max(this.seconds, 600);
    this.seconds += step;
    if (this.seconds === before) return false;
    this.grownAt = now;
    return true;
  }
}
