import Hls from 'hls.js';
import { environment } from '../environment';
import type { Manifest } from '../types';
import { carriesTicket, ensureTicket, ticketedMediaUrl } from './mediaTicket';
import { parsePlaylist, type ParsedPlaylist, type PlaylistSegment } from './aheadPlaylist';

/**
 * Keeping a film ahead of the playhead on the device, for a connection that comes and goes.
 *
 * While the film plays, the rest of it is fetched into the device's storage a segment at a time: the
 * lowest rung and the audio first, so something watchable is there as far ahead as possible, then the
 * rung the viewer watches at. The start of the next episode follows each. Every segment the player
 * itself fetches is kept too, so nothing is fetched twice. The player reads a segment from the device
 * before asking the server; when the server cannot be reached it is held to the rungs the device has
 * at the point it is fetching (`aheadLevel`). What has been watched is deleted as the playhead passes
 * it, and only the title playing and the one after it are kept at all.
 */

/**
 * What the device keeps ahead of the playhead, by title and by the path a file has under the title's
 * media (`v0/seg00012.m4s`, `v0/index.m3u8`). The site keeps it in the Origin Private File System.
 */
export interface AheadStore {
  list(titleId: string): Promise<string[]>;
  read(titleId: string, relative: string): Promise<ArrayBuffer | null>;
  write(titleId: string, relative: string, bytes: ArrayBuffer): Promise<void>;
  remove(titleId: string, relatives: string[]): Promise<void>;
  /** Forgets every title the device keeps but these. */
  keepOnly(titleIds: string[]): Promise<void>;
  /** How much more the browser lets this origin keep, or null when it does not say. */
  spareBytes(): Promise<number | null>;
}

/** The lowest rung and the audio are fetched this far into the next episode, and then the rung watched. */
export const NextEpisodeSeconds = 300;
/** What the playhead has passed by more than this is deleted. A step back stays on the device. */
const BehindSeconds = 120;
/** How often the fetcher looks again once it has nothing to fetch, and how often it deletes what was watched. */
const IdleMs = 30_000;
/** A playlist still being written by a transcode is read again this often. */
const GrowingPlaylistMs = 60_000;
/** Fetching stops while the browser would let the origin keep less than this. */
const SpareBytes = 512 * 1024 * 1024;
const MaxBackoffMs = 30_000;

/** Which files of each title the device holds, kept in memory so a request is answered without asking the disk. */
export class AheadHeld {
  private readonly titles = new Map<string, Set<string>>();

  async load(store: AheadStore, titleId: string): Promise<void> {
    this.titles.set(titleId, new Set(await store.list(titleId).catch(() => [])));
  }

  has(titleId: string, relative: string): boolean {
    return this.titles.get(titleId)?.has(relative) ?? false;
  }

  add(titleId: string, relative: string): void {
    let held = this.titles.get(titleId);
    if (held === undefined) this.titles.set(titleId, held = new Set());
    held.add(relative);
  }

  delete(titleId: string, relative: string): void {
    this.titles.get(titleId)?.delete(relative);
  }
}

/** Whether the server answered the last request made of it, by the player or by the fetcher. */
export class Reach {
  unreachable = false;
  ok(): void { this.unreachable = false; }
  lost(): void { this.unreachable = true; }
}

/** The path a request has under the title's media, or null for a request that is not for the title's media. */
export function relativeOf(url: string, titleId: string): string | null {
  try {
    const base = new URL(environment().mediaUrl(titleId, ''), window.location.href);
    const asked = new URL(url, window.location.href);
    if (asked.origin !== base.origin || !asked.pathname.startsWith(base.pathname)) return null;
    return decodeURIComponent(asked.pathname.slice(base.pathname.length));
  } catch {
    return null;
  }
}

/** A title the fetcher keeps ahead: its rungs by playlist, its audio, and the stretch of it wanted. */
export interface AheadTitle {
  titleId: string;
  manifest: Manifest;
  /** The lowest rung's playlist and the one watched; the same playlist when there is one rung. */
  low: string;
  top: string;
  audio: string | null;
  /** Where the stretch wanted starts: the playhead for the title playing. */
  from: () => number;
  until: number;
}

interface Job {
  titleId: string;
  relative: string;
}

/**
 * Fetches what is wanted ahead of the playhead into the device, one file at a time, and deletes what
 * the playhead has passed. It runs until stopped, and waits out a connection that is not there.
 */
export class AheadFetcher {
  private current: AheadTitle | null = null;
  private next: AheadTitle | null = null;
  private readonly playlists = new Map<string, { parsed: ParsedPlaylist; at: number }>();
  private stopped = false;
  private wake: (() => void) | null = null;
  private trimmedAt = 0;

  constructor(
    private readonly store: AheadStore,
    private readonly held: AheadHeld,
    private readonly reach: Reach
  ) {}

  /** The title playing, and the one after it, or null for none. Starts fetching for them at once. */
  follow(current: AheadTitle, next: AheadTitle | null = this.next): void {
    this.current = current;
    this.next = next;
    void this.store.keepOnly([current.titleId, ...(next ? [next.titleId] : [])]);
    this.poke();
  }

  /** The title after the one playing, once it is known. */
  followNext(next: AheadTitle | null): void {
    if (this.current === null) return;
    this.follow(this.current, next);
  }

  start(): void {
    void this.run();
  }

  stop(): void {
    this.stopped = true;
    this.poke();
  }

  /** The segment of a rung's playlist that holds `seconds`, or null when the playlist is not known. */
  segmentAt(titleId: string, playlist: string, seconds: number): string | null {
    const parsed = this.playlists.get(`${titleId}|${playlist}`)?.parsed;
    return parsed?.segments.find((s) => s.start <= seconds + 0.01 && s.end > seconds + 0.01)?.uri ?? null;
  }

  private poke(): void {
    this.wake?.();
  }

  private sleep(ms: number): Promise<void> {
    return new Promise((resolve) => {
      const timer = window.setTimeout(() => { this.wake = null; resolve(); }, ms);
      this.wake = () => { window.clearTimeout(timer); this.wake = null; resolve(); };
    });
  }

  private async run(): Promise<void> {
    let backoff = 0;
    while (!this.stopped) {
      await this.trim();
      const spare = await this.store.spareBytes().catch(() => null);
      const job = spare !== null && spare < SpareBytes ? null : await this.nextJob();
      if (this.stopped) return;
      if (job === null) {
        await this.sleep(IdleMs);
        continue;
      }
      try {
        await this.fetchOne(job);
        this.reach.ok();
        backoff = 0;
      } catch {
        this.reach.lost();
        backoff = Math.min(backoff === 0 ? 2_000 : backoff * 2, MaxBackoffMs);
        await this.sleep(backoff);
      }
    }
  }

  /**
   * What to fetch next: the playlists, then the lowest rung with the audio from the playhead to the
   * end, then the start of the next episode the same way, then the rung watched for each.
   */
  private async nextJob(): Promise<Job | null> {
    const current = this.current;
    if (current === null) return null;
    const stretches: [AheadTitle, 'low' | 'top'][] = [[current, 'low']];
    if (this.next) stretches.push([this.next, 'low']);
    stretches.push([current, 'top']);
    if (this.next) stretches.push([this.next, 'top']);

    for (const [title, rung] of stretches) {
      const tracks = [title[rung], ...(title.audio ? [title.audio] : [])];
      const parsed: ParsedPlaylist[] = [];
      for (const playlist of tracks) {
        const known = await this.playlist(title, playlist);
        if (known === 'fetch') return { titleId: title.titleId, relative: playlist };
        if (known === null) return null;
        parsed.push(known);
      }
      for (const { init } of parsed) {
        if (init !== null && !this.held.has(title.titleId, init)) return { titleId: title.titleId, relative: init };
      }
      const from = title.from();
      const wanted = parsed
        .map((p) => p.segments.find((s) => s.end > from && s.start < title.until && !this.held.has(title.titleId, s.uri)))
        .filter((s): s is PlaylistSegment => s !== undefined)
        .sort((a, b) => a.start - b.start);
      if (wanted.length > 0) return { titleId: title.titleId, relative: wanted[0].uri };
    }
    return null;
  }

  /** A playlist as last read, read from the device when it holds it, or 'fetch' when it must be fetched. */
  private async playlist(title: AheadTitle, playlist: string): Promise<ParsedPlaylist | 'fetch' | null> {
    const key = `${title.titleId}|${playlist}`;
    const known = this.playlists.get(key);
    const growing = title.manifest.status !== 'ready';
    if (known && !(growing && Date.now() - known.at > GrowingPlaylistMs)) return known.parsed;
    if (!growing && this.held.has(title.titleId, playlist)) {
      const bytes = await this.store.read(title.titleId, playlist).catch(() => null);
      if (bytes !== null) {
        const parsed = parsePlaylist(new TextDecoder().decode(bytes), playlist);
        this.playlists.set(key, { parsed, at: Date.now() });
        return parsed;
      }
    }
    return 'fetch';
  }

  private async fetchOne(job: Job): Promise<void> {
    await ensureTicket(job.titleId);
    const url = ticketedMediaUrl(job.titleId, job.relative);
    const headers: Record<string, string> = {};
    const token = environment().authToken();
    if (!carriesTicket(url) && token !== null) headers.Authorization = `Bearer ${token}`;
    const response = await fetch(url, { headers, priority: 'low' } as RequestInit);
    if (!response.ok) throw new Error(`${response.status} for ${job.relative}`);
    const bytes = await response.arrayBuffer();
    if (job.relative.endsWith('.m3u8')) {
      this.playlists.set(`${job.titleId}|${job.relative}`,
        { parsed: parsePlaylist(new TextDecoder().decode(bytes), job.relative), at: Date.now() });
    }
    await this.store.write(job.titleId, job.relative, bytes);
    this.held.add(job.titleId, job.relative);
  }

  /** Deletes the segments of the title playing that the playhead has passed by more than a step back. */
  private async trim(): Promise<void> {
    const current = this.current;
    if (current === null || Date.now() - this.trimmedAt < IdleMs) return;
    this.trimmedAt = Date.now();
    const before = current.from() - BehindSeconds;
    const passed: string[] = [];
    for (const [key, { parsed }] of this.playlists) {
      if (!key.startsWith(`${current.titleId}|`)) continue;
      for (const segment of parsed.segments) {
        if (segment.end < before && this.held.has(current.titleId, segment.uri)) passed.push(segment.uri);
      }
    }
    if (passed.length === 0) return;
    for (const uri of passed) this.held.delete(current.titleId, uri);
    await this.store.remove(current.titleId, passed).catch(() => {});
  }
}

type Loader = typeof Hls.DefaultConfig.loader;
type LoadArguments = Parameters<InstanceType<Loader>['load']>;
type LoadContext = LoadArguments[0];
type LoadCallbacks = LoadArguments[2];

/**
 * The hls.js loader that answers a segment from the device when the device holds it, keeps every file
 * it fetches from the server, and answers a playlist from the device when the server cannot be
 * reached. A playlist is asked of the server first: one still being written grows. Whether the server
 * answered is told to `reach`.
 */
export function aheadLoader(titleId: string, store: AheadStore, held: AheadHeld, reach: Reach, Fallback: Loader): Loader {
  return class AheadLoader extends Fallback {
    private reading = 0;
    private pending: { context: LoadContext; callbacks: LoadCallbacks } | null = null;

    override load(context: LoadContext, config: LoadArguments[1], callbacks: LoadCallbacks): void {
      // A byte range of a file is not a file the device keeps; hls.js asks for the whole file as 0 to 0.
      const relative = (context.rangeStart ?? 0) > 0 || (context.rangeEnd ?? 0) > 0
        ? null
        : relativeOf(context.url, titleId);
      const playlist = relative?.endsWith('.m3u8') ?? false;
      if (relative !== null && !playlist && held.has(titleId, relative)) {
        this.fromDevice(relative, context, callbacks);
        return;
      }

      const fromDevice = (): boolean => {
        if (relative === null || !playlist || !held.has(titleId, relative)) return false;
        this.fromDevice(relative, context, callbacks);
        return true;
      };
      super.load(context, config, {
        ...callbacks,
        onSuccess: (response, stats, ctx, details) => {
          reach.ok();
          if (relative !== null) {
            const data = response.data;
            const bytes = typeof data === 'string' ? new TextEncoder().encode(data).buffer as ArrayBuffer
              : data instanceof ArrayBuffer ? data.slice(0) : null;
            if (bytes !== null) {
              void store.write(titleId, relative, bytes).then(() => held.add(titleId, relative), () => {});
            }
          }
          callbacks.onSuccess(response, stats, ctx, details);
        },
        onError: (error, ctx, details, stats) => {
          if (error.code === 0) reach.lost();
          if (!fromDevice()) callbacks.onError(error, ctx, details, stats);
        },
        onTimeout: (stats, ctx, details) => {
          reach.lost();
          if (!fromDevice()) callbacks.onTimeout(stats, ctx, details);
        }
      });
    }

    private fromDevice(relative: string, context: LoadContext, callbacks: LoadCallbacks): void {
      const token = ++this.reading;
      const stats = this.stats;
      this.context = context;
      this.pending = { context, callbacks };
      stats.aborted = false;
      stats.loading.start = performance.now();
      store.read(titleId, relative).then((bytes) => {
        if (token !== this.reading || stats.aborted) return;
        this.pending = null;
        if (bytes === null) {
          held.delete(titleId, relative);
          callbacks.onError({ code: 0, text: `${relative} is no longer on the device` }, context, null, stats);
          return;
        }
        const data = relative.endsWith('.m3u8') ? new TextDecoder().decode(bytes) : bytes;
        stats.loading.first = stats.loading.end = performance.now();
        stats.loaded = stats.total = bytes.byteLength;
        callbacks.onSuccess({ url: context.url, data }, stats, context, null);
      }, (error: unknown) => {
        if (token !== this.reading || stats.aborted) return;
        this.pending = null;
        callbacks.onError({ code: 0, text: error instanceof Error ? error.message : 'the kept file could not be read' },
          context, null, stats);
      });
    }

    override abort(): void {
      const pending = this.pending;
      if (pending === null) {
        super.abort();
        return;
      }
      this.reading++;
      this.pending = null;
      this.stats.aborted = true;
      pending.callbacks.onAbort?.(this.stats, pending.context, null);
    }

    override destroy(): void {
      this.reading++;
      this.pending = null;
      super.destroy();
    }
  };
}

/**
 * The highest level the device holds at `seconds`, for a player that cannot reach the server; -1 when
 * it holds none there, which leaves the choice where it was.
 */
export function aheadLevel(
  levels: { height: number }[], manifest: Manifest, fetcher: AheadFetcher, held: AheadHeld, seconds: number
): number {
  const ranked = levels.map((level, index) => ({ index, height: level.height })).sort((a, b) => b.height - a.height);
  for (const { index, height } of ranked) {
    const rendition = manifest.video.renditions.find((r) => (r.height ?? manifest.video.height) === height);
    if (rendition === undefined) continue;
    const segment = fetcher.segmentAt(manifest.id, rendition.uri, seconds);
    if (segment !== null && held.has(manifest.id, segment)) return index;
  }
  return -1;
}
