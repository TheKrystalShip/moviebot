import Hls from 'hls.js';
import { environment } from '../environment';
import type { Manifest } from '../types';
import { isIndex, mediaPlaylist, trackFileUri, type SegmentIndex } from './devicePlaylist';

/** One track of a film saved on this device: its file and where each segment lies in it. */
export interface DeviceTrack extends SegmentIndex {
  /** The manifest's uri for the rendition the track is, `v1/index.m3u8`, which ties the two together. */
  uri: string;
  /** The track whole, as the device holds it. */
  file: Blob;
}

/**
 * A film saved whole on this device: the manifest as it was saved (the one rung, the audio and the
 * subtitles picked), each track's file with its segment index, and the other files the save holds by
 * the manifest-relative path each stands for (`s3.vtt`, `thumbs.jpg`).
 */
export interface DeviceCopy {
  manifest: Manifest;
  tracks: DeviceTrack[];
  file(relative: string): Promise<Blob | null>;
  /** The poster saved with it, drawn behind the frame; null when none was saved. */
  poster: Blob | null;
}

type Loader = typeof Hls.DefaultConfig.loader;
type LoadArguments = Parameters<InstanceType<Loader>['load']>;
type LoadContext = LoadArguments[0];
type LoadCallbacks = LoadArguments[2];

/** A URL as hls.js resolves it, so a playlist's relative segment and the address built here compare equal. */
const normal = (url: string): string => new URL(url, window.location.href).href;

/**
 * What the device answers for a film's addresses: each saved track's playlist, built from its segment
 * index, and each byte range of its file. Everything else is not on the device.
 */
export class DeviceAnswers {
  private readonly playlists = new Map<string, string>();
  private readonly files = new Map<string, Blob>();
  /** The height of the saved rung, which is the one the device can play. */
  readonly height: number | null;

  constructor(titleId: string, copy: DeviceCopy) {
    const media = (relative: string) => normal(environment().mediaUrl(titleId, relative));
    for (const track of copy.tracks) {
      if (!isIndex(track)) continue;
      this.playlists.set(media(track.uri), mediaPlaylist(track));
      this.files.set(media(trackFileUri(track.uri)), track.file);
    }
    const rung = copy.manifest.video.renditions.find((r) => this.playlists.has(media(r.uri)));
    this.height = rung === undefined ? null : rung.height ?? copy.manifest.video.height;
  }

  /** Whether the device holds anything to play. */
  get playable(): boolean {
    return this.height !== null && this.playlists.size > 1;
  }

  /**
   * The device's answer for a request, or null when the device holds nothing at that address. A
   * segment is a slice of its track's file: a slice is a view, and only the range asked for is read.
   */
  answer(context: LoadContext): Promise<string | ArrayBuffer> | null {
    const url = normal(context.url);
    const playlist = this.playlists.get(url);
    if (playlist !== undefined) return Promise.resolve(playlist);

    const file = this.files.get(url);
    if (file === undefined) return null;
    const start = context.rangeStart ?? 0;
    const end = context.rangeEnd !== undefined && context.rangeEnd > 0 ? context.rangeEnd : file.size;
    if (start < 0 || end > file.size || start >= end) {
      return Promise.reject(new Error(`bytes ${start}-${end} are not in the saved file of ${file.size}`));
    }
    return file.slice(start, end).arrayBuffer();
  }
}

/**
 * The hls.js loader that answers from the device what the device holds, and hands everything else to
 * `Fallback`, the network's loader. Alone, with no connection, nothing reaches the fallback; in a room,
 * a track the device does not hold streams as it would for anybody.
 */
export function deviceLoader(answers: DeviceAnswers, Fallback: Loader): Loader {
  return class DeviceLoader extends Fallback {
    /** Counts requests, so an answer that lands after an abort or a newer request is dropped. */
    private reading = 0;
    private pending: { context: LoadContext; callbacks: LoadCallbacks } | null = null;

    override load(context: LoadArguments[0], config: LoadArguments[1], callbacks: LoadArguments[2]): void {
      const answer = answers.answer(context);
      if (answer === null) {
        super.load(context, config, callbacks);
        return;
      }

      const token = ++this.reading;
      const stats = this.stats;
      this.context = context;
      this.pending = { context, callbacks };
      stats.aborted = false;
      stats.loading.start = performance.now();

      answer.then((data) => {
        if (token !== this.reading || stats.aborted) return;
        this.pending = null;
        const size = typeof data === 'string' ? data.length : data.byteLength;
        stats.loading.first = stats.loading.end = performance.now();
        stats.loaded = stats.total = size;
        callbacks.onSuccess({ url: context.url, data }, stats, context, null);
      }, (error: unknown) => {
        if (token !== this.reading || stats.aborted) return;
        this.pending = null;
        stats.loading.end = performance.now();
        callbacks.onError({ code: 0, text: error instanceof Error ? error.message : 'the saved file could not be read' },
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
