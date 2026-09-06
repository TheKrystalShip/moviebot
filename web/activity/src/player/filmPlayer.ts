import Hls from 'hls.js';
import videojs from 'video.js';
import type Player from 'video.js/dist/types/player';

import { environment } from '../environment';
import { prefs } from '../prefs';
import type { Manifest, SubtitleSearch, SubtitleTrack } from '../types';
import { buildMasterPlaylist, masterPlaylistUrl, type MasterPlaylist } from './masterPlaylist';
import { ScrubBar } from './scrubBar';
import { audioGroups, defaultAudioId, findSubtitle } from './tracks';
import { SubtitleMenu } from './subtitleMenu';
import { AudioPanel } from './audioPanel';
import { SettingsMenu } from './settingsMenu';
import { bindShortcuts } from './shortcuts';
import { KeyFlash } from './keyFlash';
import { VolumeControl, amplitudeFor } from './volume';

export interface FilmPlayerHooks {
  /** The media element holds the title and can be driven. */
  onReady(): void;
  onError(message: string): void;
  /** A person moved the scrub bar. Intent only: the playhead has not moved. */
  onSeekIntent(seconds: number): void;
  onAudioSelected(trackId: string): void;
  onSubtitleSelected(trackId: string | null): void;

  /** Costs no allowance, so the picker may call it whenever it is opened. */
  searchSubtitles(): Promise<SubtitleSearch>;
  /** Spends one of the day's downloads and adds the track for everyone in the room. */
  fetchSubtitle(fileId: number): Promise<SubtitleTrack>;
  pinSubtitle(trackId: string, positionSeconds: number): Promise<void>;
  unpinSubtitle(trackId: string): Promise<void>;
  /** Re-reads the film after the room's subtitles change, so the picker sees the new list. */
  refreshManifest(): Promise<Manifest | null>;
}

const PositionTickMs = 200;
const LoadTimeoutMs = 30_000;

/** A stall shorter than this is a stutter, and flashing a spinner at it is worse than ignoring it. */
const StallGraceMs = 400;

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
  readonly audioPanel: AudioPanel;
  readonly subtitleMenu: SubtitleMenu;
  readonly settings: SettingsMenu;

  private readonly player: Player;
  private hls: Hls | null = null;
  private manifest: Manifest | null = null;
  private master: MasterPlaylist | null = null;
  private masterUrl: string | null = null;
  private masterUrlIsBlob = false;
  private subtitleTrackEl: HTMLTrackElement | null = null;
  private subtitleUrl: string | null = null;
  private subtitleToken = 0;
  private subtitleId: string | null = null;
  private ticker: number | null = null;
  private stallTimer: number | null = null;
  private thumbnailUrl: string | null = null;
  private releaseShortcuts: (() => void) | null = null;
  private readonly spinner: HTMLElement;
  private readonly flash: KeyFlash;
  private holds = 0;
  private readonly volume: VolumeControl;

  constructor(container: HTMLElement, private readonly hooks: FilmPlayerHooks) {
    this.video = document.createElement('video');
    this.video.className = 'video-js vjs-big-play-centered';
    this.video.setAttribute('playsinline', '');
    // Shown when playback has stopped waiting for data rather than because anybody asked it to.
    // It matters more here than in most players: playback can outrun a transcode, and a stall
    // otherwise looks like nothing happening at all.
    this.spinner = document.createElement('div');
    this.spinner.className = 'mb-spinner';
    this.spinner.hidden = true;
    this.spinner.setAttribute('role', 'status');
    this.spinner.setAttribute('aria-label', 'Waiting for the film');

    // What a key press did to the playhead or the volume. Nothing else reports a seek that
    // lands inside the scene it started in, or a step the ear cannot be sure it heard.
    this.flash = new KeyFlash();

    container.replaceChildren(this.video, this.spinner, this.flash.el);

    this.video.addEventListener('waiting', () => this.stalled(true));
    this.video.addEventListener('stalled', () => this.stalled(true));
    for (const settled of ['playing', 'canplay', 'seeked', 'pause', 'error']) {
      this.video.addEventListener(settled, () => this.stalled(false));
    }

    this.scrub = new ScrubBar(
      (seconds) => this.hooks.onSeekIntent(seconds), (magnified) => this.holdControls(magnified));
    this.volume = new VolumeControl({
      onChange: (position, muted) => {
        this.player.volume(amplitudeFor(position));
        this.player.muted(muted);
        prefs.setVolume(position, muted);
      },
      onNudge: (step, position, muted) => this.flash.volume(step, position, muted)
    });
    const lock = (held: boolean) => this.holdControls(held);
    this.audioPanel = new AudioPanel((id) => {
      this.selectAudio(id);
      this.hooks.onAudioSelected(id);
      this.settings.close();
    });

    this.subtitleMenu = new SubtitleMenu({
      select: (id) => {
        void this.selectSubtitle(id);
        this.hooks.onSubtitleSelected(id);
      },
      close: () => this.settings.close(),
      search: () => this.hooks.searchSubtitles(),
      // The list is read again whenever it is looked at. What the film gains is pushed to the
      // room as it lands, and this is the guarantee behind the push: a menu opened after a
      // subtitle became available shows it available, whatever reached this page in between.
      refresh: () => this.refreshSubtitles(),
      fetch: (fileId) => this.hooks.fetchSubtitle(fileId).then(async (track) => {
        await this.refreshSubtitles();
        return track;
      }),
      // The moment matters: drift only shows up late, so what is recorded is how much of the film
      // this person had actually watched before saying the track fits.
      pin: (trackId) =>
        this.hooks.pinSubtitle(trackId, this.video.currentTime).then(() => this.refreshSubtitles()),
      unpin: (trackId) => this.hooks.unpinSubtitle(trackId).then(() => this.refreshSubtitles())
    });

    this.settings = new SettingsMenu(this.audioPanel, this.subtitleMenu, lock);

    this.player = videojs(this.video, {
      controls: true,
      preload: 'auto',
      fill: true,
      playsinline: true,
      // The library's big play button offers a press before anything can answer one, and pressing
      // it during the load is what starts the fight between a play and the corrections behind it.
      // Waiting is shown as waiting; the gate asks for a press only when the browser wants one.
      bigPlayButton: false,
      // A click toggles playback, which the library does itself and correctly — it knows not to
      // when the click was on a control. A double click does nothing: it would ask for fullscreen,
      // and the player already is the screen.
      userActions: { doubleClick: false },
      // The library's own volume panel is replaced: it writes the slider position straight to
      // the media element's amplitude, and those have to be different numbers for the slider to
      // be proportional to loudness.
      controlBar: { children: ['playToggle'] }
    });

    const bar = this.player.getChild('ControlBar');
    bar?.addChild('Component', { el: this.volume.el });
    bar?.addChild('Component', { el: this.scrub.el });
    bar?.addChild('Component', { el: this.settings.el });

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

    this.releaseShortcuts = bindShortcuts({
      togglePlay: () => this.togglePlay(),
      // Through the same route the scrub bar takes: the server decides where the room lands.
      seekBy: (seconds) => {
        const to = Math.min(this.scrubDuration, Math.max(0, this.video.currentTime + seconds));
        this.flash.seek(to - this.video.currentTime);
        this.hooks.onSeekIntent(to);
      },
      volumeBy: (step) => this.volume.nudge(step),
      toggleFullscreen: () => this.toggleFullscreen(),
      toggleMute: () => this.volume.toggleMute(),
      toggleSubtitles: () => this.toggleSubtitles()
    });
  }

  /** Loads a title. Resolves once the media element holds it. */
  async load(manifest: Manifest, startPosition?: number): Promise<void> {
    this.teardownSource();

    this.manifest = manifest;

    // Held up until something can be played, rather than left blank with a button over it.
    this.spinner.hidden = false;
    this.master = buildMasterPlaylist(manifest);

    // The ingest writes a master when it can, and that one is authoritative. Composing one here
    // covers a manifest that carries none, and is the same playlist either way.
    this.masterUrlIsBlob = manifest.master === undefined;
    this.masterUrl = manifest.master === undefined
      ? masterPlaylistUrl(this.master)
      : environment().mediaUrl(manifest.id, manifest.master);

    this.player.poster(manifest.poster ? environment().mediaUrl(manifest.id, manifest.poster) : '');
    this.scrub.setDuration(manifest.durationSeconds);
    this.scrub.setChapters(manifest.chapters ?? []);
    void this.loadThumbnails(manifest);
    this.setTranscodeHead(manifest.status === 'transcoding' ? (manifest.headSeconds ?? 0) : null);

    const stored = prefs.forTitle(manifest.id);
    const audioId = stored.audioTrackId ?? defaultAudioId(manifest);
    const subtitleId = stored.subtitleTrackId ?? null;

    this.audioPanel.setGroups(audioGroups(manifest), audioId ?? null);
    this.subtitleId = subtitleId;
    // A different film is a different search. What the index offered for the last one is
    // forgotten with it, or the menu goes on showing the old film's subtitles under the new one.
    this.subtitleMenu.reset();
    this.subtitleMenu.setTracks(manifest.subtitles, subtitleId, manifest.otherLanguages ?? []);
    this.settings.refresh();

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
      const hls = new Hls({
        enableWorker: true,
        lowLatencyMode: false,
        backBufferLength: 90,
        startPosition: startPosition ?? 0,
        // Playlists and segments are closed like everything else, and hls.js does its own
        // fetching, so the proof has to be attached to its requests rather than ours.
        xhrSetup: (xhr) => {
          const token = environment().authToken();
          if (token !== null) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
        }
      });
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

    // Native HLS has no startPosition config. For hls.js the startPosition is in the config,
    // but setting it here as well is harmless and covers the native path.
    if (startPosition !== undefined && startPosition > 0)
      this.video.currentTime = startPosition;

    // The control bar is hidden until video.js believes playback has begun, and this player is
    // driven from the room rather than from its own play button: the film is loaded, so the
    // controls belong on screen whether or not this viewer started it.
    this.player.hasStarted(true);

    if (subtitleId !== null) await this.selectSubtitle(subtitleId);
    this.hooks.onReady();
  }

  /**
   * Takes what the film gained since it was loaded.
   *
   * A manifest read while the film is still transcoding is missing what the transcode writes
   * after its main pass: the preview sheet always, and the subtitles too when the source was
   * still arriving. A viewer who opened the film early and is handed only the head from each
   * poll keeps a scrub bar that previews nothing and a subtitle menu with nothing in it, for the
   * length of the film. The head is the caller's to move; this takes the rest.
   */
  follow(fresh: Manifest): void {
    if (this.manifest === null || fresh.id !== this.manifest.id) return;

    if (this.manifest.thumbnails === undefined && fresh.thumbnails !== undefined) {
      this.manifest.thumbnails = fresh.thumbnails;
      void this.loadThumbnails(fresh);
    }

    if (JSON.stringify(fresh.subtitles) !== JSON.stringify(this.manifest.subtitles)) {
      this.adoptSubtitles(fresh);
    }
  }

  /**
   * Re-reads the film's tracks after the room's subtitles change.
   *
   * Only the subtitle list is taken. Everything else about the manifest is the transcode's to
   * report, and adopting a fresh copy wholesale would quietly move the head a seek is judged
   * against as a side effect of somebody picking a subtitle.
   */
  private async refreshSubtitles(): Promise<void> {
    const fresh = await this.hooks.refreshManifest();
    if (fresh === null || this.manifest === null || fresh.id !== this.manifest.id) return;

    this.adoptSubtitles(fresh);
  }

  private adoptSubtitles(fresh: Manifest): void {
    if (this.manifest === null) return;

    this.manifest.subtitles = fresh.subtitles;
    this.subtitleMenu.setTracks(fresh.subtitles, this.subtitleId, fresh.otherLanguages ?? []);
    this.settings.refresh();
  }

  /**
   * Fetches the sheet of preview frames and hands the bar a blob to draw from.
   *
   * It cannot be pointed at the media route directly: a background image is fetched by the
   * browser, which carries none of this Activity's token, and the answer is a 401 that renders as
   * an empty grey box. The poster is the one thing that route leaves open, because Discord's own
   * servers fetch it to draw an embed; a sheet of four hundred frames of the film is not that.
   */
  private async loadThumbnails(manifest: Manifest): Promise<void> {
    this.releaseThumbnails();

    if (manifest.thumbnails === undefined) {
      this.scrub.setThumbnails(null, null);
      return;
    }

    try {
      const authToken = environment().authToken();
      const response = await fetch(
        environment().mediaUrl(manifest.id, manifest.thumbnails.uri),
        authToken === null ? undefined : { headers: { authorization: `Bearer ${authToken}` } });

      if (!response.ok) throw new Error(`previews answered ${response.status}`);

      this.thumbnailUrl = URL.createObjectURL(await response.blob());
      this.scrub.setThumbnails(manifest.thumbnails, this.thumbnailUrl);
    } catch {
      // A bar that previews nothing is worth strictly more than one that previews a grey box.
      this.scrub.setThumbnails(null, null);
    }
  }

  private releaseThumbnails(): void {
    if (this.thumbnailUrl === null) return;

    URL.revokeObjectURL(this.thumbnailUrl);
    this.thumbnailUrl = null;
  }

  /**
   * Keeps the control bar up while something on it is being read.
   *
   * Counted rather than set, because a menu and the scrub bar's zoom lane both ask for it and
   * either can end while the other is still open. A boolean would let whichever finished first
   * drop the bar out from under the other.
   */
  private holdControls(held: boolean): void {
    this.holds = Math.max(0, this.holds + (held ? 1 : -1));
    this.player.toggleClass('mb-controls-locked', this.holds > 0);
  }

  private get scrubDuration(): number {
    return this.manifest?.durationSeconds ?? 0;
  }

  /**
   * Play and pause are published as intent like everything else. The controller watches the media
   * element and tells the room, so driving the element is what drives the film.
   */
  private togglePlay(): void {
    if (this.video.paused) void this.video.play().catch(() => {});
    else this.video.pause();
  }

  /**
   * Fullscreen only where the browser grants it. Inside Discord's iframe the API is not given to
   * an Activity, and the player already fills the frame, so this does nothing rather than failing.
   */
  private toggleFullscreen(): void {
    if (!document.fullscreenEnabled) return;

    if (this.player.isFullscreen()) void this.player.exitFullscreen();
    else void this.player.requestFullscreen();
  }

  /** Back to whatever was last chosen, or the first track the film offers. */
  private toggleSubtitles(): void {
    if (this.subtitleId !== null) {
      void this.selectSubtitle(null);
      this.hooks.onSubtitleSelected(null);
      return;
    }

    const first = this.manifest?.subtitles.find((track) => track.available && track.uri !== undefined);
    if (first === undefined) return;

    void this.selectSubtitle(first.id);
    this.hooks.onSubtitleSelected(first.id);
  }

  private stalled(waiting: boolean): void {
    if (this.stallTimer !== null) {
      window.clearTimeout(this.stallTimer);
      this.stallTimer = null;
    }

    if (!waiting) {
      this.spinner.hidden = true;
      return;
    }

    this.stallTimer = window.setTimeout(() => {
      this.stallTimer = null;
      this.spinner.hidden = false;
    }, StallGraceMs);
  }

  /** What somebody else did to the room, said in the middle of the screen for a moment. */
  announce(text: string): void {
    this.flash.action(text);
  }

  /** How far the transcode has reached, or null once the whole film is written. */
  setTranscodeHead(seconds: number | null): void {
    this.scrub.setReady(seconds);
  }

  /**
   * Shows the spinner and locks controls while the player seeks to the room's position.
   * Cleared by the caller once the seek lands.
   */
  setLoading(loading: boolean): void {
    this.holdControls(loading);
    this.spinner.hidden = !loading;
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

    if (this.manifest) this.audioPanel.setGroups(audioGroups(this.manifest), trackId);
    this.settings.refresh();
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
    this.subtitleId = trackId;
    this.clearSubtitle();
    this.subtitleMenu.setTracks(manifest.subtitles, trackId, manifest.otherLanguages ?? []);
    this.settings.refresh();

    const track = findSubtitle(manifest, trackId);
    if (!track || !track.available || track.uri === undefined) return;

    try {
      const authToken = environment().authToken();
      const response = await fetch(environment().mediaUrl(manifest.id, track.uri),
        authToken === null ? undefined : { headers: { authorization: `Bearer ${authToken}` } });
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
    this.releaseShortcuts?.();
    this.releaseShortcuts = null;
    this.releaseThumbnails();
    if (this.stallTimer !== null) window.clearTimeout(this.stallTimer);
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
