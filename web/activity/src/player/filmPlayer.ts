import Hls from 'hls.js';
import videojs from 'video.js';
import type Player from 'video.js/dist/types/player';

import { environment } from '../environment';
import { prefs } from '../prefs';
import type { Manifest } from '../types';
import { buildMasterPlaylist, masterPlaylistUrl, type MasterPlaylist } from './masterPlaylist';
import { ScrubBar } from './scrubBar';
import { TrackMenu } from './trackMenu';
import { audioGroups, defaultAudioId, findSubtitle, subtitleGroups } from './tracks';
import { VolumeControl, amplitudeFor } from './volume';

export interface FilmPlayerHooks {
  /** The media element holds the title and can be driven. */
  onReady(): void;
  onError(message: string): void;
  /** A person moved the scrub bar. Intent only: the playhead has not moved. */
  onSeekIntent(seconds: number): void;
  onAudioSelected(trackId: string): void;
  onSubtitleSelected(trackId: string | null): void;
}

const PositionTickMs = 200;
const LoadTimeoutMs = 30_000;

/**
 * The rendering surface, and nothing more.
 *
 * video.js owns the frame, the play toggle, the volume control and the cue display; hls.js feeds
 * it. Neither owns the timeline: the scrub bar publishes intent and the controller applies what
 * the server answers.
 */
export class FilmPlayer {
  readonly video: HTMLVideoElement;
  readonly scrub: ScrubBar;
  readonly audioMenu: TrackMenu;
  readonly subtitleMenu: TrackMenu;

  private readonly player: Player;
  private hls: Hls | null = null;
  private manifest: Manifest | null = null;
  private master: MasterPlaylist | null = null;
  private masterUrl: string | null = null;
  private masterUrlIsBlob = false;
  private subtitleTrackEl: HTMLTrackElement | null = null;
  private subtitleUrl: string | null = null;
  private subtitleToken = 0;
  private ticker: number | null = null;
  private readonly volume: VolumeControl;

  constructor(container: HTMLElement, private readonly hooks: FilmPlayerHooks) {
    this.video = document.createElement('video');
    this.video.className = 'video-js vjs-big-play-centered';
    this.video.setAttribute('playsinline', '');
    container.replaceChildren(this.video);

    this.scrub = new ScrubBar((seconds) => this.hooks.onSeekIntent(seconds));
    this.volume = new VolumeControl({
      onChange: (position, muted) => {
        this.player.volume(amplitudeFor(position));
        this.player.muted(muted);
        prefs.setVolume(position, muted);
      }
    });
    const lock = (open: boolean) => this.player.toggleClass('mb-controls-locked', open);
    this.audioMenu = new TrackMenu(
      'Audio',
      (id) => {
        if (id === null) return;
        this.selectAudio(id);
        this.hooks.onAudioSelected(id);
      },
      lock
    );
    this.subtitleMenu = new TrackMenu(
      'Subtitles',
      (id) => {
        void this.selectSubtitle(id);
        this.hooks.onSubtitleSelected(id);
      },
      lock
    );

    this.player = videojs(this.video, {
      controls: true,
      preload: 'auto',
      fill: true,
      playsinline: true,
      // The library's own volume panel is replaced: it writes the slider position straight to
      // the media element's amplitude, and those have to be different numbers for the slider to
      // be proportional to loudness.
      controlBar: { children: ['playToggle'] }
    });

    const bar = this.player.getChild('ControlBar');
    bar?.addChild('Component', { el: this.volume.el });
    bar?.addChild('Component', { el: this.scrub.el });
    bar?.addChild('Component', { el: this.audioMenu.el });
    bar?.addChild('Component', { el: this.subtitleMenu.el });

    // The Embedded App SDK has no fullscreen command and whether the browser API survives an
    // embedded frame depends on a permissions policy nobody documents, so the control appears
    // only where it works.
    // Real fullscreen where the browser allows it. Inside Discord's iframe it never does — the
    // Fullscreen API is not granted to an Activity — and there the player already fills the frame,
    // which is what the control would have been for.
    if (document.fullscreenEnabled) bar?.addChild('FullscreenToggle', {});

    // The stored number is the loudness that was asked for; the element is given the amplitude
    // that produces it.
    this.volume.set(prefs.volume(), prefs.muted());
    this.player.volume(amplitudeFor(prefs.volume()));
    this.player.muted(prefs.muted());

    this.ticker = window.setInterval(() => this.scrub.setPosition(this.video.currentTime), PositionTickMs);
  }

  /** Loads a title. Resolves once the media element holds it. */
  async load(manifest: Manifest): Promise<void> {
    this.teardownSource();

    this.manifest = manifest;
    this.master = buildMasterPlaylist(manifest);

    // The ingest writes a master when it can, and that one is authoritative. Composing one here
    // covers a manifest that carries none, and is the same playlist either way.
    this.masterUrlIsBlob = manifest.master === undefined;
    this.masterUrl = manifest.master === undefined
      ? masterPlaylistUrl(this.master)
      : environment().mediaUrl(manifest.id, manifest.master);

    this.player.poster(manifest.poster ? environment().mediaUrl(manifest.id, manifest.poster) : '');
    this.scrub.setDuration(manifest.durationSeconds);
    this.setTranscodeHead(manifest.status === 'transcoding' ? (manifest.headSeconds ?? 0) : null);

    const stored = prefs.forTitle(manifest.id);
    const audioId = stored.audioTrackId ?? defaultAudioId(manifest);
    const subtitleId = stored.subtitleTrackId ?? null;

    this.audioMenu.setGroups(audioGroups(manifest), audioId ?? null);
    this.subtitleMenu.setGroups(subtitleGroups(manifest), subtitleId);

    // A source that never reaches metadata would otherwise leave the caller waiting forever,
    // so the wait ends either way and the error path reports what happened.
    const ready = new Promise<boolean>((resolve) => {
      const timer = window.setTimeout(() => resolve(false), LoadTimeoutMs);
      this.video.addEventListener(
        'loadedmetadata',
        () => {
          window.clearTimeout(timer);
          resolve(true);
        },
        { once: true }
      );
    });

    if (Hls.isSupported()) {
      const hls = new Hls({ enableWorker: true, lowLatencyMode: false, backBufferLength: 90 });
      this.hls = hls;
      hls.on(Hls.Events.ERROR, (_event, data) => this.onHlsError(data));
      hls.on(Hls.Events.MANIFEST_PARSED, () => {
        if (audioId !== undefined) this.selectAudio(audioId);
      });
      hls.attachMedia(this.video);
      hls.loadSource(this.masterUrl);
    } else if (this.video.canPlayType('application/vnd.apple.mpegurl') !== '') {
      this.video.src = this.masterUrl;
    } else {
      this.hooks.onError('This browser cannot play HLS.');
      return;
    }

    if (!(await ready)) {
      this.hooks.onError(`${manifest.title} did not start playing.`);
      return;
    }

    // The control bar is hidden until video.js believes playback has begun, and this player is
    // driven from the room rather than from its own play button: the film is loaded, so the
    // controls belong on screen whether or not this viewer started it.
    this.player.hasStarted(true);

    if (subtitleId !== null) await this.selectSubtitle(subtitleId);
    this.hooks.onReady();
  }

  /** How far the transcode has reached, or null once the whole film is written. */
  setTranscodeHead(seconds: number | null): void {
    this.scrub.setReady(seconds);
  }

  selectAudio(trackId: string): void {
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

    if (this.manifest) this.audioMenu.setGroups(audioGroups(this.manifest), trackId);
  }

  /**
   * Subtitles are whole WebVTT files rather than an HLS rendition, so the chosen one is fetched
   * and attached as a track of its own. Only the chosen one is fetched: a Blu-ray rip carries
   * dozens, and loading them all to show one is a megabyte of nothing.
   */
  async selectSubtitle(trackId: string | null): Promise<void> {
    const manifest = this.manifest;
    if (!manifest) return;

    const token = ++this.subtitleToken;
    this.clearSubtitle();
    this.subtitleMenu.setGroups(subtitleGroups(manifest), trackId);

    const track = findSubtitle(manifest, trackId);
    if (!track || !track.available || track.uri === undefined) return;

    try {
      const response = await fetch(environment().mediaUrl(manifest.id, track.uri));
      if (!response.ok) throw new Error(`subtitle answered ${response.status}`);
      const blob = await response.blob();
      if (token !== this.subtitleToken) return;

      this.subtitleUrl = URL.createObjectURL(blob);
      this.subtitleTrackEl = this.player.addRemoteTextTrack(
        {
          kind: 'subtitles',
          src: this.subtitleUrl,
          srclang: track.language,
          label: track.label,
          default: true
        },
        true
      ) as unknown as HTMLTrackElement;
      this.subtitleTrackEl.track.mode = 'showing';
    } catch {
      this.hooks.onError(`Subtitle track ${track.label} could not be loaded.`);
    }
  }

  dispose(): void {
    if (this.ticker !== null) window.clearInterval(this.ticker);
    this.teardownSource();
    this.player.dispose();
  }

  private clearSubtitle(): void {
    if (this.subtitleTrackEl) {
      this.player.removeRemoteTextTrack(this.subtitleTrackEl as unknown as {});
      this.subtitleTrackEl = null;
    }
    if (this.subtitleUrl) {
      URL.revokeObjectURL(this.subtitleUrl);
      this.subtitleUrl = null;
    }
  }

  private teardownSource(): void {
    this.clearSubtitle();
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
    if (data.type === Hls.ErrorTypes.MEDIA_ERROR) {
      this.hls?.recoverMediaError();
      return;
    }
    this.hooks.onError(`Playback failed: ${data.details}`);
  }

}
