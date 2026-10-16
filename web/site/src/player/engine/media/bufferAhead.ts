import type { HlsConfig } from 'hls.js';

/**
 * How far ahead of the playhead this device fetches a film. It is the device's, not the person's: it
 * is about the connection in front of it, a phone on mobile data wanting what a television on a fixed
 * line does not, so it is kept in this browser like volume and brightness are.
 */
export type BufferAhead = 'light' | 'standard' | 'ahead';

/**
 * Each level: what it is called, how far ahead the player holds the film in memory, and whether the
 * rest of the episode and the start of the next are kept on the device as well (`ahead.ts`), which
 * only a browser with a private file system can do.
 */
export const BufferAheadLevels: Record<BufferAhead, { label: string; seconds: number; device: boolean }> = {
  light: { label: 'Light', seconds: 30, device: false },
  standard: { label: 'Standard', seconds: 180, device: false },
  ahead: { label: 'A lot', seconds: 180, device: true }
};

export const DefaultBufferAhead: BufferAhead = 'standard';

const Key = 'cinema.bufferAhead';

/** This device's choice, or the default where it made none or its storage cannot be read. */
export function bufferAhead(): BufferAhead {
  try {
    const kept = window.localStorage.getItem(Key);
    return kept !== null && kept in BufferAheadLevels ? (kept as BufferAhead) : DefaultBufferAhead;
  } catch {
    return DefaultBufferAhead;
  }
}

export function setBufferAhead(level: BufferAhead): void {
  try {
    window.localStorage.setItem(Key, level);
  } catch {
    /* Storage refused: the default stands. */
  }
}

/**
 * The hls.js settings that hold the buffer to the level's seconds. hls.js otherwise sizes it by a byte
 * budget over the rung's stated peak rate, which makes how far ahead it reaches depend on the rung; a
 * budget of nothing leaves the seconds as the only measure.
 */
export function bufferConfig(level: BufferAhead): Partial<HlsConfig> {
  const { seconds } = BufferAheadLevels[level];
  return { maxBufferLength: seconds, maxMaxBufferLength: seconds, maxBufferSize: 0 };
}
