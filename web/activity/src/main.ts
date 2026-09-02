import 'video.js/dist/video-js.css';
import './styles.css';

import { api } from './api';
import { createDiscordEnvironment, isDiscordActivity } from './discordEnvironment';
import { environment, setEnvironment } from './environment';
import { FilmPlayer } from './player/filmPlayer';
import { prefs } from './prefs';
import { SessionHub } from './session/hub';
import { SyncController } from './session/sync';
import type { Manifest, Participant, SessionState } from './types';
import { Shell } from './ui/shell';

async function boot(): Promise<void> {
  await adoptDiscordEnvironmentIfEmbedded();

  const env = environment();

  // Discord is the only surface. A plain page has nothing to prove anyone came through it, so
  // rather than letting it fail one request at a time against a black screen, it says so.
  if (env.authToken() === null) {
    document.getElementById('app')!.innerHTML =
      '<div class="closed">'
      + '<p class="closed__line">MovieBot is watched inside Discord.</p>'
      + '<p class="closed__hint">Join a voice channel and ask the bot for a film with <code>/watch</code>.</p>'
      + '</div>';
    return;
  }
  // The room is named before the viewer is, so the link in the address bar is shareable while
  // the person is still typing their name into it.
  const sessionId = env.sessionId();
  const identity = await env.identity();

  let controller: SyncController | null = null;
  let manifest: Manifest | null = null;
  // Undefined until the room has said, so that the first state is always acted on. Starting at
  // null makes an empty room indistinguishable from one nothing has been heard about yet.
  let loadedTitleId: string | null | undefined;
  let previousState: SessionState | null = null;
  let participants: Participant[] = [];
  /** The film the launch named, which is the only thing that knows what an empty room should hold. */
  const launched = env.titleId();

  const shell = new Shell({
    onPickTitle: (titleId) => void hub.loadTitle(titleId),
    onJoinPlayback: () => controller?.resumeFromGesture()
  });
  shell.setSession(sessionId, identity.displayName);

  const hub = new SessionHub(identity, sessionId, {
    onState: (push, resync) => controller?.applyPush(push, resync),
    onSeekClamped: (clamp) => shell.clampNotice(clamp),
    onParticipants: (list) => {
      participants = list;
      shell.setParticipants(list);
      void showPresence();
    },
    onTitle: (fresh) => followTitle(fresh),
    onStatus: (status) => {
      shell.setConnection(status);
      // Nothing is derived forward from a room that cannot be heard from. Its anchor runs whether
      // or not the film does, so a state nothing has confirmed is a guess about where a film that
      // may have been stopped has got to.
      controller?.setLive(status === 'connected');

      // What the title gained while this page could not be heard from was pushed to nobody here.
      // A film still transcoding is the one that changes, so it is asked for again on the way back.
      if (status === 'connected' && manifest?.status === 'transcoding') {
        void api.manifest(manifest.id).then(followTitle).catch(() => {
          /* The next push carries it. */
        });
      }
    }
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
    },

    // Fetching and confirming both act on the film rather than on the viewer: one person doing
    // either does it for the whole room. Which track each person then watches with stays theirs.
    searchSubtitles: () => api.searchSubtitles(requireTitle()),
    fetchSubtitle: async (fileId) =>
      (await api.addSubtitle(requireTitle(), fileId, identity.displayName)).track,
    pinSubtitle: (trackId, positionSeconds) =>
      api.pinSubtitle(requireTitle(), trackId, identity.displayName, positionSeconds).then(() => undefined),
    unpinSubtitle: (trackId) => api.unpinSubtitle(requireTitle(), trackId),
    refreshManifest: () => (manifest === null ? Promise.resolve(null) : api.manifest(manifest.id))
  });

  function requireTitle(): string {
    if (manifest === null) throw new Error('No film is loaded.');
    return manifest.id;
  }

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
    void showPresence();

    // Normalised first: a room holding no film leaves the field out of the state altogether, so
    // what arrives is undefined and never the null it is compared against.
    const titleId = state.titleId ?? null;
    if (titleId !== loadedTitleId) {
      // What this viewer was watching until this state arrived, which is the only record of it
      // left once the room says it is showing nothing.
      const held = loadedTitleId ?? null;
      void loadTitle(titleId);

      // A room holding nothing, while this page is holding a film, is a room nobody has told yet:
      // a fresh link, or one the server built again after forgetting it. The link names the film
      // where there is a link; inside the Activity there is only an invite, which names nothing,
      // so what this viewer already has open is the answer. Only the empty case, because a room
      // showing something else was told by somebody.
      const restore = launched ?? held;
      if (restore !== null && titleId === null) void hub.loadTitle(restore);
      return;
    }

    if (manifest?.status === 'transcoding') {
      player.setTranscodeHead(state.transcodeHead ?? manifest.headSeconds ?? 0);
    }
  }

  /**
   * What this viewer is watching, beside their name. Derived from what the page already holds
   * whenever any of it changes, so the front door is told the whole of it each time and keeps
   * nothing.
   */
  async function showPresence(): Promise<void> {
    const state = previousState;
    if (manifest === null || state === null || (state.titleId ?? null) !== manifest.id) {
      await env.setPresence(null);
      return;
    }

    await env.setPresence({
      titleId: manifest.id,
      filmName: manifest.title,
      durationSeconds: manifest.durationSeconds,
      paused: state.paused,
      positionSeconds: controller?.roomPosition() ?? 0,
      others: Math.max(0, participants.filter((p) => p.userId !== identity.userId).length),
      poster: manifest.poster ?? null
    });
  }

  async function loadTitle(titleId: string | null): Promise<void> {
    loadedTitleId = titleId;
    controller?.setPlayerReady(false);

    if (titleId === null) {
      manifest = null;
      shell.setCurrentTitle(null, null);
      void showPresence();
      return;
    }

    try {
      manifest = await api.manifest(titleId);
    } catch {
      shell.notice(`No manifest for ${titleId}.`, 'error');
      return;
    }

    shell.setCurrentTitle(manifest.id, manifest.title);
    void showPresence();
    if (manifest.status === 'failed') {
      shell.notice(manifest.error ?? 'The transcode failed.', 'error');
      return;
    }

    await player.load(manifest);
  }

  /**
   * The film as the server now has it, pushed whenever its manifest changes. The shaded region
   * grows as the transcode does, and the manifest that marks the film ready is the one that
   * names the preview sheet, and the subtitles when the source was still arriving.
   */
  function followTitle(fresh: Manifest): void {
    if (loadedTitleId !== fresh.id || manifest === null) return;
    manifest = fresh;
    player.setTranscodeHead(fresh.status === 'transcoding' ? (fresh.headSeconds ?? 0) : null);
    player.follow(fresh);
  }

  try {
    shell.setTitles(await api.titles());
  } catch {
    shell.notice('The library could not be read.', 'error');
  }

  const joined = await hub.start();
  controller.applyPush(joined, true);
  participants = await api.participants(sessionId);
  shell.setParticipants(participants);
  void showPresence();

  // Coming back to a tab that has been in the background — a floating window, another channel —
  // is the moment its picture of the room is least likely to be right, and the cheapest moment to
  // ask.
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') void hub.resync();
  });

  // A launch link names the film. The room is told once, by whoever arrives while it is showing
  // something else; a viewer joining a room that already holds the title says nothing, because
  // loading it again would send everyone back to the beginning.
  if (launched !== null && (joined.state.titleId ?? null) !== null && joined.state.titleId !== launched) {
    await hub.loadTitle(launched);
  }
}

/**
 * Inside Discord, the room and the viewer come from the Activity rather than from a link and a
 * typed name. Failing to sign in there is fatal and says so: falling back to the browser
 * environment would put someone in a room named by a query parameter Discord never set, alone
 * and wondering why nobody else is there.
 */
async function adoptDiscordEnvironmentIfEmbedded(): Promise<void> {
  if (!isDiscordActivity()) return;

  const response = await fetch('/api/config');
  if (!response.ok) throw new Error(`The server would not say which application this is (${response.status}).`);

  const { discordClientId, publicBaseUrl } = (await response.json()) as {
    discordClientId: string;
    publicBaseUrl?: string;
  };
  if (!discordClientId) throw new Error('This server has no Discord application configured.');

  setEnvironment(await createDiscordEnvironment(discordClientId, publicBaseUrl ?? null));
}

void boot();
