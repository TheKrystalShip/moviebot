import 'video.js/dist/video-js.css';
import './styles.css';

import { api } from './api';
import { environment } from './environment';
import { FilmPlayer } from './player/filmPlayer';
import { prefs } from './prefs';
import { SessionHub } from './session/hub';
import { SyncController } from './session/sync';
import type { Manifest, SessionState } from './types';
import { Shell } from './ui/shell';

const ManifestPollMs = 5000;

async function boot(): Promise<void> {
  const env = environment();
  const identity = await env.identity();
  const sessionId = env.sessionId();

  let controller: SyncController | null = null;
  let manifest: Manifest | null = null;
  let loadedTitleId: string | null = null;
  let previousState: SessionState | null = null;
  let pollTimer: number | null = null;

  const shell = new Shell({
    onPickTitle: (titleId) => void hub.loadTitle(titleId),
    onJoinPlayback: () => controller?.resumeFromGesture()
  });
  shell.setSession(sessionId, identity.displayName);

  const hub = new SessionHub(identity, sessionId, {
    onState: (push) => controller?.applyPush(push),
    onSeekClamped: (clamp) => shell.clampNotice(clamp),
    onParticipants: (list) => shell.setParticipants(list),
    onStatus: (status) => shell.setConnection(status)
  });

  const player = new FilmPlayer(shell.playerHost, {
    onReady: () => controller?.setPlayerReady(true),
    onError: (message) => shell.notice(message, 'error'),
    onSeekIntent: (seconds) => void hub.seek(seconds),
    onAudioSelected: (trackId) => {
      if (manifest) prefs.setAudioTrack(manifest.id, trackId);
    },
    onSubtitleSelected: (trackId) => {
      if (manifest) prefs.setSubtitleTrack(manifest.id, trackId);
    }
  });

  controller = new SyncController(player.video, hub.clock, {
    play: (at) => void hub.play(at),
    pause: (at) => void hub.pause(at),
    seek: (to) => void hub.seek(to)
  }, {
    onState: (state) => onState(state),
    onPlaybackBlocked: () => shell.showGate(true)
  });

  function onState(state: SessionState): void {
    shell.setActor(state, previousState);
    previousState = state;

    if (state.titleId !== loadedTitleId) {
      void loadTitle(state.titleId ?? null);
      return;
    }

    if (manifest?.status === 'transcoding') {
      player.setTranscodeHead(state.transcodeHead ?? manifest.headSeconds ?? 0);
    }
  }

  async function loadTitle(titleId: string | null): Promise<void> {
    loadedTitleId = titleId;
    controller?.setPlayerReady(false);
    stopPolling();

    if (titleId === null) {
      manifest = null;
      shell.setCurrentTitle(null, null);
      return;
    }

    try {
      manifest = await api.manifest(titleId);
    } catch {
      shell.notice(`No manifest for ${titleId}.`, 'error');
      return;
    }

    shell.setCurrentTitle(manifest.id, manifest.title);
    if (manifest.status === 'failed') {
      shell.notice(manifest.error ?? 'The transcode failed.', 'error');
      return;
    }

    await player.load(manifest);
    if (manifest.status === 'transcoding') startPolling(manifest.id);
  }

  /**
   * The shaded region has to grow while the film plays, and the head only rides along on a
   * state push — which happens when somebody acts, not while the encoder works.
   */
  function startPolling(titleId: string): void {
    pollTimer = window.setInterval(async () => {
      try {
        const fresh = await api.manifest(titleId);
        if (loadedTitleId !== titleId) return;
        manifest = fresh;
        player.setTranscodeHead(fresh.status === 'transcoding' ? (fresh.headSeconds ?? 0) : null);
        if (fresh.status !== 'transcoding') stopPolling();
      } catch {
        // A missed poll leaves the last known head standing until the next one.
      }
    }, ManifestPollMs);
  }

  function stopPolling(): void {
    if (pollTimer !== null) window.clearInterval(pollTimer);
    pollTimer = null;
  }

  try {
    shell.setTitles(await api.titles());
  } catch {
    shell.notice('The library could not be read.', 'error');
  }

  const joined = await hub.start();
  controller.applyPush(joined);
  shell.setParticipants(await api.participants(sessionId));
}

void boot();
