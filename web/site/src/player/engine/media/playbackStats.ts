import type Hls from 'hls.js';

/**
 * What this player measures about its own playback, for the stats panel and for sending to the
 * server's log (`PlaybackStats.jsx`, `ReportStats`).
 *
 * Every value is measured or null, and null is drawn as unknown: a browser that does not offer a
 * measurement is never given a zero in its place. What accumulates (the drift graph, its marks, the
 * counters) runs from the moment a film loads, so opening the panel shows what led up to it; the two
 * measurements that cost something, a callback on every presented frame and an audio context for the
 * output device's latency, run only while the panel is open.
 */

/** How much of the drift graph is kept: the last five minutes of the sync controller's checks. */
export const HistoryMs = 5 * 60_000;
/** How much of the presented frames the picture offset is the median of. */
export const FrameWindowMs = 1000;
/** The most a snapshot sent to the server may be, which the server holds it to as well. */
export const SnapshotChars = 8192;
/** How often a snapshot may be sent, which the server holds a connection to as well. */
export const SendEveryMs = 10_000;

const OpenKey = 'cinema.playbackStats';

/** One check of the sync controller: how far the playhead was behind the room, and the element's rate. */
export interface DriftSample {
  /** `performance.now()` when it was taken. */
  at: number;
  /** The room's position minus the playhead, in seconds: positive while this player is behind. */
  drift: number;
  rate: number;
}

export type MarkKind =
  | 'nudge' | 'settled' | 'seek'
  | 'hidden' | 'shown' | 'reconnecting' | 'reconnected'
  | 'stall' | 'rung' | 'error';

/** Something that happened to playback, drawn on the drift graph's time axis. */
export interface Mark {
  at: number;
  kind: MarkKind;
  detail?: string;
}

/** A fetch hls.js finished: the last one is shown. */
export interface Fetch {
  kind: string;
  firstByteMs: number;
  totalMs: number;
  bytes: number;
}

/** What only the room knows at the moment of asking, handed in by `PlayerRoom` for each snapshot. */
export interface LiveFacts {
  titleId: string | null;
  frontDoor: string;
  bufferAhead: string;
  solo: boolean;
  /** Where the room is, or null while the room holds no state. */
  roomPosition: number | null;
  roomRate: number | null;
  clock: { offsetMs: number; roundTripMs: number; samples: number } | null;
  connection: string;
  playingHeight: number | null;
  wantedHeight: number | null;
  measuredBps: number | null;
  audio: { id: string; label: string; channels: number | null } | null;
}

export interface StatsSnapshot {
  takenAt: string;
  build: string;
  frontDoor: string;
  userAgent: string;
  titleId: string | null;
  playback: string | null;
  bufferAhead: string;
  sync: {
    /** How far the picture on screen is ahead of the sound the decoder is producing, in milliseconds. */
    pictureOffsetMs: number | null;
    /** What the default output device adds after the decoder, in milliseconds. */
    outputLatencyMs: number | null;
    /** How far the sound reaching the speakers trails the picture: the two above together. */
    soundBehindMs: number | null;
  };
  room: {
    driftSeconds: number | null;
    rate: number;
    roomRate: number | null;
    clockOffsetMs: number | null;
    roundTripMs: number | null;
    clockSamples: number;
    connection: string;
    reconnects: number;
    hidden: number;
  };
  network: {
    estimateBps: number | null;
    measuredBps: number | null;
    aheadVideoSeconds: number | null;
    aheadAudioSeconds: number | null;
    lastFetch: Fetch | null;
    failed: number;
    fatal: number;
    lastError: string | null;
  };
  media: {
    playingHeight: number | null;
    wantedHeight: number | null;
    width: number | null;
    height: number | null;
    videoCodec: string | null;
    audioCodec: string | null;
    audio: { id: string; label: string; channels: number | null } | null;
    framesDropped: number | null;
    framesShown: number | null;
    stalls: number;
    stalledMs: number;
    longestStallMs: number;
  };
  /** The drift graph: seconds before the snapshot, drift and rate; and the marks, seconds before it. */
  history: {
    samples: [number, number, number][];
    marks: [number, MarkKind, string?][];
  };
}

// ── The arithmetic, kept pure so it is tested on its own ─────────────────────────────────────

/**
 * How far a presented frame is from the sound, in seconds, positive when the picture is ahead.
 *
 * The element's `currentTime` follows the audio clock, so when the frame reaches the screen at
 * `expectedDisplayMs` the sound is at `currentTime` moved on by the time until then at `rate`; the
 * frame's own `mediaTime` minus that is how far the two disagree.
 */
export function pictureOffset(mediaTime: number, currentTime: number, expectedDisplayMs: number, nowMs: number, rate: number): number {
  const soundAt = currentTime + ((expectedDisplayMs - nowMs) / 1000) * rate;
  return mediaTime - soundAt;
}

export function median(values: readonly number[]): number | null {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const middle = sorted.length >> 1;
  return sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/** Drops what is older than `windowMs` before `nowMs` from a list kept in time order. */
export function trimOlder<T extends { at: number }>(items: T[], nowMs: number, windowMs: number): void {
  let drop = 0;
  while (drop < items.length && items[drop].at < nowMs - windowMs) drop++;
  if (drop > 0) items.splice(0, drop);
}

/**
 * Seconds buffered past `position` in one buffer: the end of the range holding the playhead, with
 * half a second of slack for a playhead sitting at a range's very start.
 */
export function aheadOf(ranges: readonly { start: number; end: number }[], position: number): number {
  for (const range of ranges) {
    if (range.start <= position + 0.5 && range.end > position) return range.end - position;
  }
  return 0;
}

/**
 * The snapshot as the server takes it: at most `maxChars` of JSON. The oldest of the drift graph goes
 * first, then its oldest marks, so what is cut is the furthest from the moment it was sent.
 */
export function fitSnapshot(snapshot: StatsSnapshot, maxChars = SnapshotChars): string | null {
  const samples = [...snapshot.history.samples];
  const marks = [...snapshot.history.marks];
  for (;;) {
    const json = JSON.stringify({ ...snapshot, history: { samples, marks } });
    if (json.length <= maxChars) return json;
    if (samples.length > 0) samples.splice(0, Math.max(1, samples.length >> 3));
    else if (marks.length > 0) marks.splice(0, Math.max(1, marks.length >> 3));
    else return null;
  }
}

/** Whether this browser keeps the panel open, which survives a reload made to try again. */
export function statsKeptOpen(): boolean {
  try {
    return window.localStorage.getItem(OpenKey) === 'open';
  } catch {
    return false;
  }
}

export function keepStatsOpen(open: boolean): void {
  try {
    if (open) window.localStorage.setItem(OpenKey, 'open');
    else window.localStorage.removeItem(OpenKey);
  } catch {
    /* A browser that keeps nothing opens it closed. */
  }
}

/** Rounded to `places`, so a snapshot carries what the measurement supports and not float noise. */
const round = (value: number, places = 3): number => {
  const scale = 10 ** places;
  return Math.round(value * scale) / scale;
};

const roundOrNull = (value: number | null, places = 3): number | null => (value === null ? null : round(value, places));

// ── The collector ────────────────────────────────────────────────────────────────────────────

interface FrameMetadata {
  mediaTime: number;
  expectedDisplayTime: number;
}

type FrameVideo = HTMLVideoElement & {
  requestVideoFrameCallback?(callback: (now: number, metadata: FrameMetadata) => void): number;
  cancelVideoFrameCallback?(handle: number): void;
};

export class PlaybackStats {
  private readonly video: FrameVideo;
  private samples: DriftSample[] = [];
  private marks: Mark[] = [];
  private offsets: { at: number; offset: number }[] = [];
  private playback: string | null = null;
  private hls: Hls | null = null;
  private ranges: { video: TimeRanges | null; audio: TimeRanges | null } = { video: null, audio: null };
  private lastFetch: Fetch | null = null;
  /** The codecs hls.js read from the film's init segments, which the playlists do not declare. */
  private codecs: { video: string | null; audio: string | null } = { video: null, audio: null };
  private failed = 0;
  private fatal = 0;
  private lastError: string | null = null;
  private reconnects = 0;
  private hidden = 0;
  private stalls = 0;
  private stalledMs = 0;
  private longestStallMs = 0;
  private stalledSince: number | null = null;
  private frameHandle: number | null = null;
  private audio: AudioContext | null = null;
  private watching = false;
  private lastSentMs = -Infinity;

  constructor(video: HTMLVideoElement) {
    this.video = video;
  }

  /** A new playback: what the last film gathered says nothing about this one. */
  reset(playback: string | null): void {
    this.playback = playback;
    this.samples = [];
    this.marks = [];
    this.offsets = [];
    this.ranges = { video: null, audio: null };
    this.lastFetch = null;
    this.codecs = { video: null, audio: null };
    this.failed = 0;
    this.fatal = 0;
    this.lastError = null;
    this.stalls = 0;
    this.stalledMs = 0;
    this.longestStallMs = 0;
    this.stalledSince = null;
  }

  /** Listens to the hls.js that plays the film, or lets go of it. */
  attachHls(hls: Hls | null, events: typeof Hls.Events | null): void {
    this.hls = hls;
    if (hls === null || events === null) return;
    hls.on(events.FRAG_LOADED, (_event, { frag }) => {
      const loading = frag.stats.loading;
      this.lastFetch = {
        kind: frag.type,
        firstByteMs: Math.max(0, loading.first - loading.start),
        totalMs: Math.max(0, loading.end - loading.start),
        bytes: frag.stats.loaded
      };
    });
    hls.on(events.BUFFER_CODECS, (_event, tracks) => {
      if (tracks.video) this.codecs.video = tracks.video.codec ?? null;
      if (tracks.audio) this.codecs.audio = tracks.audio.codec ?? null;
    });
    hls.on(events.BUFFER_APPENDED, (_event, { timeRanges }) => {
      if (timeRanges.video) this.ranges.video = timeRanges.video;
      if (timeRanges.audio) this.ranges.audio = timeRanges.audio;
      if (timeRanges.audiovideo) this.ranges.video = this.ranges.audio = timeRanges.audiovideo;
    });
    hls.on(events.ERROR, (_event, data) => {
      if (/Load(Error|TimeOut)$/.test(data.details)) this.failed++;
      if (data.fatal) this.fatal++;
      this.lastError = data.details;
      if (data.fatal) this.mark('error', data.details);
    });
    hls.on(events.LEVEL_SWITCHED, (_event, { level }) => {
      const height = hls.levels[level]?.height;
      if (height) this.mark('rung', `${height}p`);
    });
  }

  /** One check of the sync controller. */
  drift(drift: number, rate: number): void {
    const at = performance.now();
    this.samples.push({ at, drift, rate });
    trimOlder(this.samples, at, HistoryMs);
  }

  mark(kind: MarkKind, detail?: string): void {
    const at = performance.now();
    this.marks.push(detail === undefined ? { at, kind } : { at, kind, detail });
    trimOlder(this.marks, at, HistoryMs);
    if (kind === 'reconnected') this.reconnects++;
    if (kind === 'hidden') this.hidden++;
  }

  /** The element waited longer than a stutter, or stopped waiting. */
  stall(waiting: boolean): void {
    const now = performance.now();
    if (waiting && this.stalledSince === null) {
      this.stalledSince = now;
      this.stalls++;
      this.mark('stall');
    } else if (!waiting && this.stalledSince !== null) {
      const lasted = now - this.stalledSince;
      this.stalledMs += lasted;
      this.longestStallMs = Math.max(this.longestStallMs, lasted);
      this.stalledSince = null;
    }
  }

  /**
   * Starts or stops the measurements that cost something: a callback on every presented frame, and an
   * audio context that reads the output device's latency. Neither touches the film's own sound.
   */
  watch(on: boolean): void {
    if (on === this.watching) return;
    this.watching = on;
    if (on) {
      this.offsets = [];
      this.nextFrame();
      try {
        this.audio = new AudioContext();
        void this.audio.resume().catch(() => {});
      } catch {
        this.audio = null;
      }
    } else {
      if (this.frameHandle !== null) this.video.cancelVideoFrameCallback?.(this.frameHandle);
      this.frameHandle = null;
      void this.audio?.close().catch(() => {});
      this.audio = null;
    }
  }

  /** An audio context made without a press waits for one before it reports anything. */
  wake(): void {
    if (this.audio !== null && this.audio.state !== 'running') void this.audio.resume().catch(() => {});
  }

  /** Whether a snapshot may be sent now, and when the next may. */
  sendAllowedIn(): number {
    return Math.max(0, this.lastSentMs + SendEveryMs - performance.now());
  }

  markSent(): void {
    this.lastSentMs = performance.now();
  }

  snapshot(live: LiveFacts): StatsSnapshot {
    const now = performance.now();
    const video = this.video;
    const position = video.currentTime;

    trimOlder(this.offsets, now, FrameWindowMs);
    const pictureOffsetMs = this.watching ? roundOrNull(median(this.offsets.map((o) => o.offset * 1000)), 1) : null;
    const audio = this.audio;
    const outputLatency = audio !== null && audio.state === 'running' && typeof audio.outputLatency === 'number'
      ? audio.outputLatency * 1000 : null;
    const outputLatencyMs = roundOrNull(outputLatency, 1);
    const soundBehindMs = pictureOffsetMs !== null && outputLatencyMs !== null ? round(pictureOffsetMs + outputLatencyMs, 1) : null;

    const quality = typeof video.getVideoPlaybackQuality === 'function' ? video.getVideoPlaybackQuality() : null;
    const hls = this.hls;
    const level = hls !== null && hls.currentLevel >= 0 ? hls.levels[hls.currentLevel] : undefined;
    const track = hls !== null && hls.audioTrack >= 0 ? hls.audioTracks[hls.audioTrack] : undefined;
    const stalledNow = this.stalledSince === null ? 0 : now - this.stalledSince;

    return {
      takenAt: new Date().toISOString(),
      build: (import.meta.env?.VITE_CINEMA_BUILD as string | undefined) ?? 'dev',
      frontDoor: live.frontDoor,
      userAgent: navigator.userAgent,
      titleId: live.titleId,
      playback: this.playback,
      bufferAhead: live.bufferAhead,
      sync: { pictureOffsetMs, outputLatencyMs, soundBehindMs },
      room: {
        driftSeconds: live.roomPosition === null ? null : round(live.roomPosition - position),
        rate: video.playbackRate,
        roomRate: live.roomRate,
        clockOffsetMs: live.clock === null || live.clock.samples === 0 ? null : Math.round(live.clock.offsetMs),
        roundTripMs: live.clock === null || live.clock.samples === 0 ? null : Math.round(live.clock.roundTripMs),
        clockSamples: live.clock?.samples ?? 0,
        connection: live.connection,
        reconnects: this.reconnects,
        hidden: this.hidden
      },
      network: {
        estimateBps: hls !== null && Number.isFinite(hls.bandwidthEstimate) ? Math.round(hls.bandwidthEstimate) : null,
        measuredBps: live.measuredBps === null ? null : Math.round(live.measuredBps),
        aheadVideoSeconds: this.ahead(this.ranges.video, position),
        aheadAudioSeconds: this.ahead(this.ranges.audio, position),
        lastFetch: this.lastFetch === null ? null : {
          ...this.lastFetch, firstByteMs: Math.round(this.lastFetch.firstByteMs), totalMs: Math.round(this.lastFetch.totalMs)
        },
        failed: this.failed,
        fatal: this.fatal,
        lastError: this.lastError
      },
      media: {
        playingHeight: live.playingHeight,
        wantedHeight: live.wantedHeight,
        width: video.videoWidth > 0 ? video.videoWidth : null,
        height: video.videoHeight > 0 ? video.videoHeight : null,
        videoCodec: this.codecs.video ?? level?.videoCodec ?? null,
        audioCodec: this.codecs.audio ?? track?.audioCodec ?? level?.audioCodec ?? null,
        audio: live.audio,
        framesDropped: quality?.droppedVideoFrames ?? null,
        framesShown: quality?.totalVideoFrames ?? null,
        stalls: this.stalls,
        stalledMs: Math.round(this.stalledMs + stalledNow),
        longestStallMs: Math.round(Math.max(this.longestStallMs, stalledNow))
      },
      history: {
        samples: this.samples.map((s) => [round((s.at - now) / 1000, 1), round(s.drift), round(s.rate, 3)]),
        marks: this.marks.map((m) => (m.detail === undefined
          ? [round((m.at - now) / 1000, 1), m.kind]
          : [round((m.at - now) / 1000, 1), m.kind, m.detail]))
      }
    };
  }

  dispose(): void {
    this.watch(false);
    this.hls = null;
  }

  /**
   * Seconds ahead in one of the element's buffers, from the ranges hls.js last appended to. Null while
   * nothing has been appended, or where the browser plays the stream itself and says nothing of it.
   */
  private ahead(ranges: TimeRanges | null, position: number): number | null {
    if (ranges === null) return null;
    try {
      const list: { start: number; end: number }[] = [];
      for (let i = 0; i < ranges.length; i++) list.push({ start: ranges.start(i), end: ranges.end(i) });
      return round(aheadOf(list, position), 1);
    } catch {
      // A buffer the element has since dropped, after a recovery, says nothing until the next append.
      return null;
    }
  }

  private nextFrame(): void {
    const video = this.video;
    if (!this.watching || typeof video.requestVideoFrameCallback !== 'function') return;
    this.frameHandle = video.requestVideoFrameCallback((_frameTime, metadata) => {
      if (!video.paused && !video.seeking && video.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA) {
        // The clock read beside `currentTime`, not the frame's start time the callback is handed: the
        // two would put the moments a few milliseconds apart.
        const now = performance.now();
        this.offsets.push({
          at: now,
          offset: pictureOffset(metadata.mediaTime, video.currentTime, metadata.expectedDisplayTime, now, video.playbackRate)
        });
        trimOlder(this.offsets, now, FrameWindowMs);
      }
      this.nextFrame();
    });
  }
}
