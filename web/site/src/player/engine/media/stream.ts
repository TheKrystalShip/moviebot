import Hls from 'hls.js';

import { environment } from '../environment';
import type { Manifest, ThumbnailStrip } from '../types';
import { buildMasterPlaylist, masterPlaylistUrl, type MasterPlaylist } from './masterPlaylist';
import { covers, rungReach, wantedHeight } from './heads';
import { DeviceAnswers, deviceLoader, type DeviceCopy } from './deviceLoader';
import { carriesTicket, ensureTicket, ticketedLoader, ticketedMediaUrl } from './mediaTicket';
import { findSubtitle } from './tracks';
import { BufferAheadLevels, bufferAhead, bufferConfig } from './bufferAhead';
import { AheadFetcher, AheadHeld, Reach, aheadLevel, aheadLoader, NextEpisodeSeconds, type AheadStore, type AheadTitle } from './ahead';
import { Horizon } from './aheadHorizon';

export interface StreamEvents {
  /** The media element holds the title and can be driven. */
  onReady(): void;
  /** Something went wrong that the person should hear about, and playback goes on. */
  onError(message: string): void;
  /** This viewer cannot play the film at all. The stream has stopped trying and says why. */
  onUnplayable(message: string): void;
  /** Whether the element has been waiting for data long enough to be worth showing. */
  onStall(waiting: boolean): void;
  /** The sheet of preview frames and where it is drawn from, or nothing. */
  onThumbnails(strip: ThumbnailStrip | null, url: string | null): void;
  /** The chosen subtitle's cues, as a hidden text track the page draws from, or none. */
  onSubtitleTrack(track: TextTrack | null): void;
  /**
   * The rungs this viewer can choose between, tallest first, and the one being played. Empty where
   * the browser plays the stream itself, because it chooses for itself and says nothing about it.
   */
  onQuality(levels: QualityLevel[], playingHeight: number | null): void;
  /**
   * The rung this viewer plays and the rung the connection would have them on with nothing holding
   * it back, raised when either changes and on a slow heartbeat. Never raised where the browser
   * plays the stream itself: it says nothing about what it chooses.
   */
  onViewerQuality(playingHeight: number | null, wantedHeight: number | null): void;
}

/** One rung of the film's ladder. */
export interface QualityLevel {
  height: number;
  bitrateKbps: number;
}

const LoadTimeoutMs = 30_000;

/**
 * What the ingest writes: H.264 High at level 4.2 and stereo AAC-LC. Asked about before anything
 * is fetched, because a client with no decoder for them fails every append, and hls.js recovering
 * from each failure resets the element about ten times a second for as long as the page is open.
 */
const VideoCodec = 'video/mp4; codecs="avc1.64002a"';
const AudioCodec = 'audio/mp4; codecs="mp4a.40.2"';

/**
 * Fatal media errors recovered from before giving up: one plain recovery, then one that swaps the
 * audio codec, which is the sequence hls.js recommends. Counted per stretch of playback, so a film
 * that plays between two hiccups has both recoveries again for the second one.
 */
const MediaRecoveries = 2;

/**
 * Discord's desktop client decodes H.264 only on the graphics card. With hardware acceleration
 * off it has no decoder at all, and this is the one thing the viewer can do about it.
 */
const NoDecoderMessage =
  'This copy of Discord cannot decode the film. Turn on Hardware Acceleration under '
  + 'Settings → Advanced, then restart Discord.';

/** A stall shorter than this is a stutter, and showing a spinner at it is worse than ignoring it. */
const StallGraceMs = 400;

/** How often the rungs the player may use are checked against the playhead, and the viewer's choice re-read. */
const WatchIntervalMs = 2_000;

/** A change in what the viewer wants waits this long after the last report, so a bandwidth estimate hovering at a boundary is not a stream of messages. */
const ReportSettleMs = 5_000;

/** The viewer's quality is said again this often while it holds still, so the server never relies on one message. */
const ReportHeartbeatMs = 30_000;

/**
 * What a media fetch outside hls.js carries.
 *
 * A ticket on the URL and nothing else, so the response is one a shared cache will store. The
 * header is the fallback for a viewer whose ticket never arrived, and is never sent alongside
 * one: a shared cache refuses to store a response to a request carrying it.
 */
function mediaFetchInit(url: string): RequestInit | undefined {
  if (carriesTicket(url)) return undefined;

  const token = environment().authToken();
  return token === null ? undefined : { headers: { authorization: `Bearer ${token}` } };
}

/**
 * The film in one media element: hls.js fed from the transcode, the audio rendition chosen, the
 * subtitle file attached, and the preview sheet fetched.
 *
 * Where the film is saved whole on this device, the device answers first: the saved rung's and the
 * saved audio's playlists and segments, the saved subtitles and the preview sheet are read from it,
 * and the film is held on the saved rung. Played alone, the device is all there is; in a room,
 * anything the device does not hold streams from the server as it would for anybody.
 *
 * It owns no timeline and no controls. The room's position is the sync controller's to apply, and
 * what a person sees and presses is the page's: this answers whether the element holds the film,
 * and says when it cannot.
 */
export class FilmStream {
  private hls: Hls | null = null;
  private manifest: Manifest | null = null;
  /** The film as this device holds it, or null to stream every byte. */
  private device: DeviceCopy | null = null;
  private answers: DeviceAnswers | null = null;
  /** What the device keeps of the film ahead of the playhead, where this device keeps it (`ahead.ts`). */
  private ahead: { store: AheadStore; held: AheadHeld; reach: Reach; fetcher: AheadFetcher; horizon: Horizon } | null = null;
  /** The rung hls.js was last held to because the server could not be reached, or -1 while it can be. */
  private heldToDevice = -1;
  private master: MasterPlaylist | null = null;
  private masterUrl: string | null = null;
  private masterUrlIsBlob = false;
  private subtitleEl: HTMLTrackElement | null = null;
  private subtitleUrl: string | null = null;
  private subtitleToken = 0;
  private thumbnailUrl: string | null = null;
  /** The height this viewer holds the film at, or null to let the connection decide. */
  private quality: number | null = null;
  /**
   * The tracks this viewer wants, kept from `load` and moved by every choice after it, so a choice made
   * while the film is still loading is the one the load ends on.
   */
  private audioChoice: string | undefined;
  private subtitleChoice: string | null = null;
  /** Set while the player holds a rung other than the one the viewer or the connection chose, because the chosen one is behind the playhead. */
  private stepDown = false;
  private watchTimer: number | null = null;
  private reported: { playing: number | null; wanted: number | null; at: number } | null = null;
  private stallTimer: number | null = null;
  /** Set once the loaded film reached metadata, and cleared by the next load or by giving up. */
  private ready = false;
  private recoveries = 0;
  /** Ends the wait in `load` early, when the stream gives up before metadata ever arrives. */
  private settleLoad: ((ok: boolean) => void) | null = null;
  private readonly listeners: [string, () => void][] = [];

  constructor(readonly video: HTMLVideoElement, private readonly events: StreamEvents) {
    this.listen('playing', () => { this.recoveries = 0; });
    this.listen('waiting', () => this.stalled(true));
    this.listen('stalled', () => this.stalled(true));
    for (const settled of ['playing', 'canplay', 'seeked', 'pause', 'error']) {
      this.listen(settled, () => this.stalled(false));
    }
  }

  /**
   * Whether the media element holds the loaded film, so that what it does is somebody acting on
   * it. An element that never loaded, or one a recovery has just emptied, sits at zero and raises
   * play and pause as machinery.
   */
  get holdsFilm(): boolean {
    return this.ready && this.video.readyState >= HTMLMediaElement.HAVE_METADATA;
  }

  /**
   * Loads a title, from `device` first where this device holds it whole. Resolves once the media
   * element holds it, or once it has given up.
   */
  async load(manifest: Manifest, startPosition: number | undefined, audioId: string | undefined,
             subtitleId: string | null, quality: number | null, device: DeviceCopy | null = null): Promise<void> {
    this.teardownSource();
    this.quality = quality;
    this.audioChoice = audioId;
    this.subtitleChoice = subtitleId;
    this.events.onQuality([], null);

    this.manifest = manifest;
    this.ready = false;
    this.recoveries = 0;

    // Only hls.js can be handed a saved file: the browser's own HLS fetches every byte itself.
    const solo = environment().solo;
    const mse = Hls.isSupported();
    if (solo && !mse) {
      this.giveUp('This browser cannot play a film saved on the device.');
      return;
    }
    const answers = device === null || !mse ? null : new DeviceAnswers(manifest.id, device);
    this.answers = answers !== null && answers.playable ? answers : null;
    this.device = this.answers === null ? null : device;
    if (solo && this.answers === null) {
      this.giveUp('This film is not saved whole on this device, so it cannot be played here.');
      return;
    }

    // Before anything asks the server for a byte of the film, so every request for it carries the
    // ticket. A film played alone asks the server for nothing.
    if (!solo) await ensureTicket(manifest.id);

    this.master = buildMasterPlaylist(manifest);

    // The ingest writes a master when it can, and that one is authoritative. Composing one here
    // covers a manifest that carries none, and is the same playlist either way. A film the device
    // holds has its master composed too, so not even that is asked of the server; alone, it is the
    // saved manifest's, naming only what was saved.
    this.masterUrlIsBlob = manifest.master === undefined || this.answers !== null;
    this.masterUrl = this.masterUrlIsBlob
      ? masterPlaylistUrl(this.master)
      : environment().mediaUrl(manifest.id, manifest.master!);

    void this.loadThumbnails(manifest);

    // A source that never reaches metadata would otherwise leave the caller waiting forever, so
    // the wait ends either way and the failure says what happened.
    const ready = new Promise<boolean>((resolve) => {
      const settle = (ok: boolean) => {
        window.clearTimeout(timer);
        this.video.removeEventListener('loadedmetadata', loaded);
        if (this.settleLoad === settle) this.settleLoad = null;
        resolve(ok);
      };
      const loaded = () => settle(true);
      const timer = window.setTimeout(() => settle(false), LoadTimeoutMs);
      this.video.addEventListener('loadedmetadata', loaded);
      this.settleLoad = settle;
    });

    if (mse && !(MediaSource.isTypeSupported(VideoCodec) && MediaSource.isTypeSupported(AudioCodec))) {
      this.giveUp(NoDecoderMessage);
      return;
    }

    if (mse) {
      const titleId = manifest.id;
      const answers = this.answers;
      const level = bufferAhead();
      const ahead = await this.keepAhead(titleId, level);
      // A film loaded while the device was asked what it holds is the one that stands.
      if (this.manifest !== manifest) {
        ahead?.fetcher.stop();
        return;
      }
      this.ahead = ahead;

      const hls = new Hls({
        enableWorker: true,
        lowLatencyMode: false,
        backBufferLength: 90,
        ...bufferConfig(level),
        startPosition: startPosition ?? 0,
        // A film's own bytes are opened by a ticket on the URL, which is what lets a shared cache
        // answer a whole room from one copy. The loader is where it goes: hls.js resolves a
        // segment against its playlist, and relative resolution drops the query string, so a
        // ticket put on the playlist URL reaches none of the segments underneath it. A film the
        // device holds is answered from it first.
        loader: answers !== null ? deviceLoader(answers, ticketedLoader(titleId))
          : ahead !== null ? aheadLoader(titleId, ahead.store, ahead.held, ahead.reach, ticketedLoader(titleId))
          : ticketedLoader(titleId),
        // The fallback, for a viewer whose ticket never arrived. Never sent alongside a ticket: a
        // shared cache refuses to store a response to a request carrying it.
        xhrSetup: (xhr, url) => {
          if (carriesTicket(url)) return;
          const token = environment().authToken();
          if (token !== null) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
        },
        // Each request carries the player's own account of playback (CTA-5004): buffer left,
        // measured throughput, the rung playing, a stall. The ingress logs it beside the route's
        // round trip for the same request. Headers, not the query string: a segment's URL is its
        // cache key, and a query that differs per viewer would give every viewer their own copy.
        // The session id is hls.js's own, random per playback.
        cmcd: { useHeaders: true, contentId: titleId }
      });
      this.hls = hls;
      hls.on(Hls.Events.ERROR, (_event, data) => this.onHlsError(data));
      // hls.js trims how far ahead it fetches whenever it meets a fragment still being appended,
      // which a switch between rungs makes, and never puts it back: a long trip of switches halves it
      // again and again. It is put back once a fragment lands, unless the browser said its buffer was
      // full, which is the one limit the device really has.
      let full = false;
      hls.on(Hls.Events.ERROR, (_event, data) => { if (data.details === Hls.ErrorDetails.BUFFER_FULL_ERROR) full = true; });
      hls.on(Hls.Events.FRAG_BUFFERED, () => {
        const { maxMaxBufferLength } = bufferConfig(level);
        if (!full && maxMaxBufferLength !== undefined && hls.config.maxMaxBufferLength < maxMaxBufferLength) {
          hls.config.maxMaxBufferLength = maxMaxBufferLength;
        }
      });
      // hls.js fills a level's audio renditions when that level first loads, after the manifest is
      // parsed, and falls to the master's DEFAULT rendition unless one is chosen while it announces
      // them. Choosing here is what keeps a film whose default is a dub from starting on it.
      hls.on(Hls.Events.AUDIO_TRACKS_UPDATED, () => {
        if (this.audioChoice !== undefined) this.selectAudio(this.audioChoice);
      });
      hls.on(Hls.Events.MANIFEST_PARSED, () => {
        // The first playlist and segment are the saved rung's, not whatever the connection would
        // have started on.
        if (this.heldLevel() >= 0) hls.startLevel = this.heldLevel();
        this.selectQuality(this.quality);
        this.followAhead();
        this.watchTimer = window.setInterval(() => this.watch(), WatchIntervalMs);
      });
      hls.on(Hls.Events.LEVEL_SWITCHED, () => {
        this.reportQuality();
        this.reportViewerQuality();
      });
      hls.attachMedia(this.video);
      hls.loadSource(this.masterUrl);
    } else if (this.video.canPlayType('application/vnd.apple.mpegurl') !== '') {
      this.video.src = this.masterUrl;
    } else {
      this.giveUp('This browser cannot play the film.');
      return;
    }

    if (!(await ready)) {
      // Giving up has already said why, and says it for as long as it holds.
      if (this.manifest === manifest && !this.ready && this.hls !== null) {
        this.events.onError(`${manifest.title} did not start playing.`);
      }
      return;
    }

    // Native HLS has no start position in its configuration. For hls.js it is in the
    // configuration, and setting it here as well is harmless.
    if (startPosition !== undefined && startPosition > 0) this.video.currentTime = startPosition;

    // Native HLS lists the renditions on the element once it has the metadata, enabling the default.
    if (this.hls === null && this.audioChoice !== undefined) this.selectAudio(this.audioChoice);

    if (this.subtitleChoice !== null) await this.selectSubtitle(this.subtitleChoice);
    this.ready = true;
    this.events.onReady();
  }

  /**
   * Takes what the film gained since it was loaded. A manifest read while the film is still
   * transcoding is missing the preview sheet, which the transcode writes after its main pass.
   */
  follow(fresh: Manifest): void {
    if (this.manifest === null || fresh.id !== this.manifest.id) return;

    if (this.manifest.thumbnails === undefined && fresh.thumbnails !== undefined) {
      this.manifest = { ...this.manifest, thumbnails: fresh.thumbnails };
      void this.loadThumbnails(fresh);
    }
    this.manifest = {
      ...this.manifest,
      subtitles: fresh.subtitles,
      status: fresh.status,
      headSeconds: fresh.headSeconds,
      audioHeadSeconds: fresh.audioHeadSeconds,
      video: fresh.video
    };
    this.limitLevels();
  }

  selectAudio(trackId: string): void {
    this.audioChoice = trackId;
    this.followAhead();
    const name = this.master?.audioNames[trackId];
    if (this.hls) {
      // Renditions are written in manifest order, so the position is the reliable key; the name
      // is tried first because it survives a master that lists them in some other order.
      const byName = this.hls.audioTracks.findIndex((track) => track.name === name);
      const index = byName >= 0
        ? byName
        : (this.manifest?.audio.findIndex((track) => track.id === trackId) ?? -1);
      if (index >= 0 && index < this.hls.audioTracks.length && this.hls.audioTrack !== index) {
        this.hls.audioTrack = index;
      }
    } else {
      // Native HLS keeps the renditions on the element rather than in hls.js.
      const native = (this.video as unknown as { audioTracks?: { length: number; [i: number]: { label: string; enabled: boolean } } }).audioTracks;
      if (native && name !== undefined) {
        for (let i = 0; i < native.length; i++) native[i].enabled = native[i].label === name;
      }
    }
  }

  /**
   * Sets the best rung the film may play at, by its height, or none when null. hls.js chooses for
   * the connection beneath it, so a connection that cannot carry the chosen rung steps down rather
   * than stopping the film. Taken from the next segment rather than at once: the buffer already
   * fetched still plays, so the change costs no stall. A height the film has no rung at is the
   * tallest rung below it, or the shortest rung when every one is taller.
   */
  selectQuality(height: number | null): void {
    this.quality = height;
    this.followAhead();
    const hls = this.hls;
    if (hls === null || hls.levels.length === 0) return;
    hls.nextLevel = this.heldLevel();
    this.stepDown = false;
    this.limitLevels();
    this.reportQuality();
  }

  /**
   * The best level the viewer's choice allows, or -1 when it allows any. A film the device holds
   * stays on the saved rung, which is the one that costs no connection.
   */
  private ceilingLevel(): number {
    const hls = this.hls;
    const held = this.heldLevel();
    if (held >= 0) return held;
    if (hls === null || this.quality === null) return -1;
    const height = this.quality;
    const ranked = hls.levels.map((level, index) => ({ index, height: level.height }));
    const fits = ranked.filter((level) => level.height <= height).sort((a, b) => b.height - a.height);
    const shortest = [...ranked].sort((a, b) => a.height - b.height);
    return (fits[0] ?? shortest[0]).index;
  }

  /** The level of the rung saved on this device, or -1 when the device holds none of the film's. */
  private heldLevel(): number {
    const height = this.answers?.height;
    if (this.hls === null || height === null || height === undefined) return -1;
    return this.hls.levels.findIndex((level) => level.height === height);
  }

  /**
   * Caps hls.js at the viewer's chosen rung, and keeps it, whether it is choosing or holding the
   * saved rung, on rungs written far enough past the playhead. A rung behind it stalls the film
   * until the transcode catches up, so the cap rises as rungs do, and a rung the playhead has
   * outrun is left for one that covers it before the buffer runs dry. Nothing is held back by the
   * transcode for a ready title or a manifest that records one head, and nothing is capped where
   * the browser plays the stream itself.
   */
  private limitLevels(): void {
    const hls = this.hls;
    const manifest = this.manifest;
    if (hls === null || manifest === null || hls.levels.length < 2) return;

    const reach = rungReach(manifest);
    if (reach === null) {
      const cap = this.ceilingLevel();
      if (hls.autoLevelCapping !== cap) hls.autoLevelCapping = cap;
      if (this.stepDown) {
        this.stepDown = false;
        hls.nextLevel = this.heldLevel();
      }
      return;
    }

    const playhead = this.video.currentTime;
    const covered = hls.levels
      .map((level, index) => ({ index, ok: covers(reach, manifest.headSeconds, level.height, playhead) }))
      .filter((level) => level.ok)
      .map((level) => level.index);

    // Nothing covers the playhead when the transcode is closer than the margin: the rung that has
    // got furthest is the best there is.
    const furthest = hls.levels.reduce((best, level, index) => {
      const seconds = reach.find((rung) => rung.height === level.height)?.seconds ?? manifest.headSeconds ?? 0;
      return seconds > best.seconds ? { index, seconds } : best;
    }, { index: 0, seconds: -1 }).index;
    const allowed = covered.length > 0 ? covered : [furthest];
    const ceiling = Math.max(...allowed);

    // The nearest allowed rung at or below the one in question, else the lowest above it.
    const nearest = (index: number) =>
      [...allowed].filter((level) => level <= index).sort((a, b) => b - a)[0] ?? Math.min(...allowed);

    const chosen = this.ceilingLevel();
    hls.autoLevelCapping = chosen >= 0 ? nearest(chosen) : ceiling;

    const held = this.heldLevel();
    if (held >= 0) {
      const target = nearest(held);
      this.stepDown = target !== held;
      if (hls.manualLevel !== target) hls.nextLevel = target;
      return;
    }

    // The connection is choosing. The cap holds what it picks from here, and a rung it is already
    // on that no longer covers is left at once.
    const current = hls.loadLevel >= 0 ? hls.loadLevel : hls.currentLevel;
    if (current >= 0 && !allowed.includes(current)) {
      hls.nextLevel = nearest(current);
      this.stepDown = true;
    } else if (this.stepDown && allowed.length === ceiling + 1) {
      hls.nextLevel = -1;
      this.stepDown = false;
    }
  }

  /** Once per interval: the cap against where the playhead has got to, and what the viewer would choose. */
  private watch(): void {
    this.limitLevels();
    this.holdToDevice();
    this.growAhead();
    this.reportViewerQuality();
  }

  /**
   * Dynamic's horizon against what the connection is doing (`aheadHorizon.ts`): the network's measured
   * throughput over the rate of the rung watched, the browser's data saver and whether the link is
   * metered. A server that cannot be reached counts as a stall. When it grows, the fetcher looks again.
   */
  private growAhead(): void {
    const hls = this.hls;
    const ahead = this.ahead;
    if (hls === null || ahead === null || ahead.horizon.whole || hls.levels.length === 0) return;
    if (ahead.reach.unreachable) ahead.horizon.stalled();
    const ceiling = this.ceilingLevel();
    const rate = hls.levels[ceiling >= 0 ? ceiling : hls.levels.length - 1]?.averageBitrate ?? 0;
    const connection = (navigator as Navigator & { connection?: { saveData?: boolean; type?: string } }).connection;
    const link = {
      ratio: ahead.reach.throughput !== null && rate > 0 ? ahead.reach.throughput / rate : null,
      saveData: connection?.saveData === true,
      unmetered: connection?.type === 'wifi' || connection?.type === 'ethernet'
    };
    if (ahead.horizon.grow(link)) ahead.fetcher.poke();
  }

  /**
   * Where the device keeps the film ahead and the server cannot be reached, holds hls.js to the best
   * rung the device has where it is fetching next, so the film plays on from the device rather than
   * waiting on a rung it cannot fetch. Nothing is held while the server answers.
   */
  private holdToDevice(): void {
    const hls = this.hls;
    const ahead = this.ahead;
    const manifest = this.manifest;
    if (hls === null || ahead === null || manifest === null || hls.levels.length < 2) return;
    if (!ahead.reach.unreachable && navigator.onLine !== false) {
      this.heldToDevice = -1;
      return;
    }
    const level = aheadLevel(hls.levels, manifest, ahead.fetcher, ahead.held, this.bufferEnd());
    if (level < 0) return;
    if (hls.autoLevelCapping < 0 || hls.autoLevelCapping > level) hls.autoLevelCapping = level;
    if (level === this.heldToDevice) return;
    // hls.js waits on a fragment that failed for want of a connection until the browser is online
    // again, whatever the device holds. Loading again from where it was, on the device's rung, is
    // what takes the film from the device instead.
    this.heldToDevice = level;
    hls.nextLoadLevel = level;
    hls.stopLoad();
    hls.startLoad(-1);
  }

  /** Where what the element holds ahead of the playhead ends. */
  private bufferEnd(): number {
    const { buffered, currentTime } = this.video;
    for (let i = 0; i < buffered.length; i++) {
      if (buffered.start(i) <= currentTime + 0.5 && buffered.end(i) > currentTime) return buffered.end(i);
    }
    return currentTime;
  }

  /**
   * The device's store of what it keeps ahead, with what it holds of the title, where the level asks
   * for it and the device can keep it. Never for a film played from a save, which is all there already.
   */
  private async keepAhead(titleId: string, level: ReturnType<typeof bufferAhead>): Promise<FilmStream['ahead']> {
    const store = BufferAheadLevels[level].device && this.answers === null ? environment().aheadStore() : null;
    if (store === null) return null;
    const held = new AheadHeld();
    await held.load(store, titleId);
    const reach = new Reach();
    const fetcher = new AheadFetcher(store, held, reach);
    fetcher.start();
    return { store, held, reach, fetcher, horizon: new Horizon(!BufferAheadLevels[level].dynamic) };
  }

  /** The title playing as the device keeps it ahead: its lowest rung, the rung watched, and the audio chosen. */
  private aheadTitle(manifest: Manifest, audioId: string | undefined, from: () => number, until: () => number): AheadTitle | null {
    const renditions = [...manifest.video.renditions].sort((a, b) => (a.height ?? 0) - (b.height ?? 0));
    if (renditions.length === 0) return null;
    const height = (r: typeof renditions[number]) => r.height ?? manifest.video.height;
    const top = this.quality === null
      ? renditions[renditions.length - 1]
      : [...renditions].reverse().find((r) => height(r) <= this.quality!) ?? renditions[0];
    const audio = manifest.audio.find((track) => track.id === audioId)
      ?? manifest.audio.find((track) => track.default) ?? manifest.audio[0];
    return { titleId: manifest.id, manifest, low: renditions[0].uri, top: top.uri, audio: audio?.uri ?? null, from, until };
  }

  private followAhead(): void {
    const manifest = this.manifest;
    const ahead = this.ahead;
    if (ahead === null || manifest === null) return;
    const horizon = ahead.horizon;
    const title = this.aheadTitle(manifest, this.audioChoice, () => this.video.currentTime,
      () => Math.min(manifest.durationSeconds, this.video.currentTime + horizon.ahead));
    if (title !== null) ahead.fetcher.follow(title);
  }

  /**
   * The episode after the one playing, so its start is kept on the device too: the lowest rung and the
   * audio first, then the rung watched. Null when there is none, or none the library holds.
   */
  keepAheadNext(next: Manifest | null, audioId?: string): void {
    const ahead = this.ahead;
    if (ahead === null) return;
    const title = next === null || next.status === 'failed'
      ? null
      : this.aheadTitle(next, audioId, () => 0, () => Math.min(NextEpisodeSeconds, next.durationSeconds));
    ahead.fetcher.followNext(title);
  }

  /**
   * Says what rung this viewer plays and what their connection would have them on, to whoever is
   * listening: on a change that has held for a few seconds, and on a heartbeat while it holds still.
   */
  private reportViewerQuality(): void {
    const hls = this.hls;
    if (hls === null || hls.levels.length === 0) return;

    // A film played from the device holds the saved rung, so the saved rung is both what this viewer
    // plays and all it wants: it asks the server for none of the picture.
    const current = hls.currentLevel;
    const playing = current >= 0 ? hls.levels[current]?.height ?? null : null;
    const chosen = this.ceilingLevel();
    const wanted = wantedHeight(
      hls.levels.map((level) => ({ height: level.height, bitrate: level.bitrate })),
      hls.bandwidthEstimate,
      current,
      hls.config.abrBandWidthFactor,
      hls.config.abrBandWidthUpFactor,
      chosen < 0 ? null : hls.levels[chosen]?.height ?? null
    );

    const now = Date.now();
    const last = this.reported;
    const changed = last === null || last.playing !== playing || last.wanted !== wanted;
    if (last !== null && (!changed || now - last.at < ReportSettleMs) && now - last.at < ReportHeartbeatMs) return;

    this.reported = { playing, wanted, at: now };
    this.events.onViewerQuality(playing, wanted);
  }

  /** The rungs on offer: every one the film has, or only the saved one where the device holds the film. */
  private reportQuality(): void {
    const hls = this.hls;
    if (hls === null) return;
    const held = this.heldLevel();
    const levels = hls.levels
      .filter((_, index) => held < 0 || index === held)
      .map((level) => ({ height: level.height, bitrateKbps: Math.round(level.bitrate / 1000) }))
      .sort((a, b) => b.height - a.height);
    const playing = hls.currentLevel >= 0 ? hls.levels[hls.currentLevel]?.height ?? null : null;
    this.events.onQuality(levels, playing);
  }

  /**
   * Subtitles are whole WebVTT files rather than an HLS rendition, so the chosen one is fetched
   * and attached as a track of its own, hidden: the page draws its cues itself. Only the chosen
   * one is fetched: a Blu-ray rip carries dozens, and loading them all to show one is a megabyte
   * of nothing.
   */
  async selectSubtitle(trackId: string | null): Promise<void> {
    const manifest = this.manifest;
    if (!manifest) return;
    this.subtitleChoice = trackId;

    const token = ++this.subtitleToken;
    this.clearSubtitle();

    const track = findSubtitle(manifest, trackId);
    if (!track || !track.available || track.uri === undefined) return;

    try {
      const blob = await this.fileOf(manifest.id, track.uri);
      if (token !== this.subtitleToken) return;

      this.subtitleUrl = URL.createObjectURL(blob);
      const element = document.createElement('track');
      element.kind = 'subtitles';
      element.src = this.subtitleUrl;
      element.srclang = track.language;
      element.label = track.label;
      this.video.appendChild(element);
      element.track.mode = 'hidden';
      this.subtitleEl = element;
      this.events.onSubtitleTrack(element.track);
    } catch {
      this.events.onError(`The ${track.label} subtitles could not be loaded.`);
    }
  }

  dispose(): void {
    if (this.stallTimer !== null) window.clearTimeout(this.stallTimer);
    for (const [name, handler] of this.listeners) this.video.removeEventListener(name, handler);
    this.releaseThumbnails();
    this.teardownSource();
  }

  /**
   * Fetches the sheet of preview frames and hands the page a blob to draw from.
   *
   * It cannot be pointed at the media route directly: a background image is fetched by the
   * browser, which carries none of this viewer's token, and the answer is a 401 that renders as an
   * empty grey box. The poster is the one thing that route leaves open, because Discord's own
   * servers fetch it to draw an embed; a sheet of four hundred frames of the film is not that.
   */
  private async loadThumbnails(manifest: Manifest): Promise<void> {
    this.releaseThumbnails();

    if (manifest.thumbnails === undefined) {
      this.events.onThumbnails(null, null);
      return;
    }

    try {
      const blob = await this.fileOf(manifest.id, manifest.thumbnails.uri);
      if (this.manifest?.id !== manifest.id) return;
      this.thumbnailUrl = URL.createObjectURL(blob);
      this.events.onThumbnails(manifest.thumbnails, this.thumbnailUrl);
    } catch {
      // A bar that previews nothing is worth strictly more than one that previews a grey box.
      this.events.onThumbnails(null, null);
    }
  }

  /**
   * A file of the film beside its tracks, a subtitle or the preview sheet: the device's copy where it
   * holds one, else the server's, which a film played alone never asks for.
   */
  private async fileOf(titleId: string, relative: string): Promise<Blob> {
    const held = this.device === null ? null : await this.device.file(relative).catch(() => null);
    if (held !== null) return held;
    if (environment().solo) throw new Error(`${relative} is not saved on this device`);

    await ensureTicket(titleId);
    const url = ticketedMediaUrl(titleId, relative);
    const response = await fetch(url, mediaFetchInit(url));
    if (!response.ok) throw new Error(`${relative} answered ${response.status}`);
    return response.blob();
  }

  private releaseThumbnails(): void {
    if (this.thumbnailUrl === null) return;
    URL.revokeObjectURL(this.thumbnailUrl);
    this.thumbnailUrl = null;
  }

  private stalled(waiting: boolean): void {
    if (this.stallTimer !== null) {
      window.clearTimeout(this.stallTimer);
      this.stallTimer = null;
    }

    if (!waiting) {
      this.events.onStall(false);
      return;
    }

    this.stallTimer = window.setTimeout(() => {
      this.stallTimer = null;
      // A wait that outlived the grace while nobody was seeking is the connection falling behind.
      if (this.holdsFilm && !this.video.seeking) this.ahead?.horizon.stalled();
      this.events.onStall(true);
    }, StallGraceMs);
  }

  private clearSubtitle(): void {
    if (this.subtitleEl) {
      this.subtitleEl.track.mode = 'disabled';
      this.subtitleEl.remove();
      this.subtitleEl = null;
      this.events.onSubtitleTrack(null);
    }
    if (this.subtitleUrl) {
      URL.revokeObjectURL(this.subtitleUrl);
      this.subtitleUrl = null;
    }
  }

  private teardownSource(): void {
    this.clearSubtitle();
    if (this.watchTimer !== null) window.clearInterval(this.watchTimer);
    this.watchTimer = null;
    this.stepDown = false;
    this.reported = null;
    this.ahead?.fetcher.stop();
    this.ahead = null;
    this.heldToDevice = -1;
    this.hls?.destroy();
    this.hls = null;
    if (this.masterUrl !== null && this.masterUrlIsBlob) URL.revokeObjectURL(this.masterUrl);
    this.masterUrl = null;
  }

  private onHlsError(data: { fatal: boolean; type: string; details: string }): void {
    if (!data.fatal) return;

    if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
      this.hls?.startLoad();
      return;
    }
    if (data.type === Hls.ErrorTypes.MEDIA_ERROR && this.recoveries < MediaRecoveries) {
      if (this.recoveries > 0) this.hls?.swapAudioCodec();
      this.recoveries++;
      this.hls?.recoverMediaError();
      return;
    }

    // A buffer that refuses the codec is a client with no decoder for it. Any other media error
    // that outlasts both recoveries is the decoder failing on data every other viewer plays, and
    // on this surface that is most often the same missing decoder, so the fix is named with it.
    if (data.details === Hls.ErrorDetails.BUFFER_ADD_CODEC_ERROR
        || data.details === Hls.ErrorDetails.MANIFEST_INCOMPATIBLE_CODECS_ERROR) {
      this.giveUp(NoDecoderMessage);
    } else if (data.type === Hls.ErrorTypes.MEDIA_ERROR) {
      this.giveUp(`The film could not be decoded here (${data.details}). If Hardware Acceleration `
        + 'is off under Settings → Advanced, turning it on and restarting Discord usually fixes this.');
    } else {
      this.giveUp(`The film could not be played here (${data.details}).`);
    }
  }

  /**
   * Stops trying, and says why for as long as it holds. What follows a fatal error is the same
   * error again, and every attempt resets the element and fetches the film's opening over again.
   */
  private giveUp(message: string): void {
    this.teardownSource();
    this.ready = false;
    this.events.onStall(false);
    this.settleLoad?.(false);
    this.events.onUnplayable(message);
  }

  private listen(name: string, handler: () => void): void {
    this.video.addEventListener(name, handler);
    this.listeners.push([name, handler]);
  }
}
