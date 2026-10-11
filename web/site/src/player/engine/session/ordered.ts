/**
 * Applies a record the API pushes in the order its writes were kept, whatever order the pushes and this
 * device's own writes arrive in. Every record the API sends carries a `revision` that only rises with each
 * write, so a push that is not newer than what was last applied is dropped.
 *
 * While this device has a write in flight the push is held instead of applied, because the write's own
 * answer says where that write stands: an echo of a write this device made before its latest one is older
 * than the answer and is dropped when the answer arrives, while a push from another device that is newer
 * than the answer is applied then. Without it, choosing 720 then 1080 in a moment shows 720 again when the
 * first echo lands.
 */
export class Ordered<T extends { revision?: number | null }> {
  private applied = 0;
  private writes = 0;
  private held: T | null = null;

  private readonly apply: (value: T) => void;

  constructor(apply: (value: T) => void) {
    this.apply = apply;
  }

  /** The API pushed a record. */
  heard(value: T): void {
    const revision = value.revision ?? 0;
    if (revision <= this.applied) return;
    if (this.writes > 0) {
      if (this.held === null || revision > (this.held.revision ?? 0)) this.held = value;
      return;
    }
    this.applied = revision;
    this.apply(value);
  }

  /** A write to the API starts; the returned function is called with its answer, or null when it failed. */
  writing(): (answer: T | null) => void {
    this.writes += 1;
    return (answer) => {
      this.writes -= 1;
      const revision = answer?.revision ?? 0;
      if (revision > this.applied) this.applied = revision;
      if (this.writes > 0 || this.held === null) return;
      const held = this.held;
      this.held = null;
      if ((held.revision ?? 0) > this.applied) {
        this.applied = held.revision ?? 0;
        this.apply(held);
      }
    };
  }
}
