import React from "react";
import { Button, Drawer, Icon, MenuItem, Toggle } from "@thekrystalship/krystal";
import { PlayerRoom } from "./engine/room.ts";
import { api } from "./engine/api.ts";
import { environment } from "./engine/environment.ts";
import { formatTime } from "./engine/format.ts";
import { prefs } from "./engine/prefs.ts";
import { amplitudeFor, volumeIsSettable } from "./engine/media/volume.ts";
import { bindShortcuts } from "./engine/media/shortcuts.ts";
import { Remote } from "./engine/media/remote.ts";
import { Rank, onBack as onTvBack, onMenu as onTvMenu } from "../tv/tv.js";
import { PausedCode } from "../tv/TvRemote.jsx";
import { useRemoteAttached } from "../tv/screen.js";
import { Cues } from "./Cues.jsx";
import { GestureHud, useStageGestures } from "./Gestures.jsx";
import { ScrubBar } from "./ScrubBar.jsx";
import { SkipLabels, inClosingStretch, useSkipOffer } from "./stretches.js";
import { Volume } from "./Volume.jsx";
import { Person } from "../components/Person.jsx";
import { InvitePeople } from "../components/InvitePeople.jsx";
import { AboutButton, AboutPanel, PauseCard, useAbout, usePausedLong } from "./About.jsx";
import { PlaybackStats } from "./PlaybackStats.jsx";
import { keepStatsOpen, statsKeptOpen } from "./engine/media/playbackStats.ts";
import {
  MenuButton, MenuTitles, PlayerMenu, reach, audioValue, episodeValue, hasEpisodes, hasQualityChoice, qualityValue, subtitleValue,
} from "./Menus.jsx";

const SkipSeconds = 10;
/** Controls stay this long after the last sign of somebody reaching for them. */
const IdleMs = 2800;
/** A remote walks focus one press at a time from across a room, so a television holds them longer. */
const TvIdleMs = 6000;
const NavigationKeys = new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Enter"]);
const AnnouncementMs = 2800;
const PositionTickMs = 250;

/** Below this width controls go icon-only and menus become sheets. */
const CompactWidth = 720;
/**
 * A popped-out view is small both ways, and its controls are one row: a frame at or below TinyWidth
 * in each direction, or at or below TinyHeight tall. A phone held upright is as narrow as one but
 * several times taller, and is compact.
 */
const TinyWidth = 440;
const TinyHeight = 260;

const noSubscribe = () => () => {};
const noView = () => null;

/**
 * The people in the room, once each. A person on two devices is one person watching, and waiting
 * for the film on either of them.
 */
function people(view) {
  const seen = new Map();
  for (const p of view.participants) {
    const known = seen.get(p.userId);
    seen.set(p.userId, known ? { ...known, buffering: known.buffering || p.buffering } : p);
  }
  if (view.identity && !seen.has(view.identity.userId)) seen.set(view.identity.userId, view.identity);
  return [...seen.values()];
}

function namesOf(list) {
  const names = list.map((p) => p.displayName);
  if (names.length <= 2) return names.join(" and ");
  return `${names.slice(0, -1).join(", ")} and ${names[names.length - 1]}`;
}

/**
 * What the line under the name says: a film's year and length; an episode's place in its show and
 * its own name, "S2 · E4 · Woe's Hollow".
 */
function filmLine(manifest, around) {
  if (manifest.episode) {
    return [episodeCode(manifest.episode), around && around.current && around.current.name].filter(Boolean).join(" · ");
  }
  const minutes = Math.round(manifest.durationSeconds / 60);
  const length = minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes}m`;
  return [manifest.film && manifest.film.year, length].filter(Boolean).join(" · ");
}

const episodeCode = (episode) => `S${episode.season} · E${episode.number}`;

/**
 * The artwork behind the frame, always from this server: the title's poster as the library serves it,
 * else the catalogue's poster for the film, or for the show an episode belongs to, by its IMDb id.
 */
function posterOf(manifest) {
  if (!manifest) return null;
  if (manifest.poster) return environment().mediaUrl(manifest.id, manifest.poster);
  const imdbId = manifest.film && manifest.film.imdbId;
  if (!imdbId) return null;
  return environment().apiUrl(`/images/titles/${encodeURIComponent(imdbId)}/poster`);
}

/** An episode either side, as a button names it: "S2 · E5 · Defiant Jazz". */
function episodeWords(episode) {
  return [episodeCode(episode), episode.name].filter(Boolean).join(" · ");
}

/**
 * What the room holds, as a key that changes whenever the room is given something else: its title,
 * or the episode arriving.
 */
function heldKey(state) {
  if (!state) return null;
  if (state.titleId) return state.titleId;
  const p = state.preparing;
  return p ? `${p.imdbId}:${p.season ?? ""}:${p.episode ?? ""}` : null;
}

/**
 * The episode the room holds and the ones either side, as the server decides them, asked again
 * whenever the room is given something else. A film has none, and a question that fails answers with
 * none: the controls it feeds are an addition to the player.
 */
function useAround(view) {
  const key = view.solo || view.viewOnly ? null : heldKey(view.state);
  const [around, setAround] = React.useState(null);
  React.useEffect(() => {
    setAround(null);
    if (!key) return undefined;
    let live = true;
    api.around(view.sessionId).then((a) => live && setAround(a), () => {});
    return () => { live = false; };
  }, [key, view.sessionId]);
  return around;
}

/** The chapter a moment is in, or null for a film that records none. */
export function chapterAt(chapters, seconds) {
  if (!chapters || chapters.length < 2) return null;
  let at = null;
  for (const c of chapters) if (c.startSeconds <= seconds + 0.5) at = c;
  return at;
}

const filmName = (manifest) => (manifest.film && manifest.film.name) || manifest.title;

function useFrameSize(ref) {
  const [size, setSize] = React.useState("wide");
  React.useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return undefined;
    const measure = () => {
      const { width, height } = el.getBoundingClientRect();
      const popped = (width <= TinyWidth && height <= TinyWidth) || height <= TinyHeight;
      setSize(popped ? "tiny" : width < CompactWidth ? "compact" : "wide");
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    return () => observer.disconnect();
  }, [ref]);
  return size;
}

/**
 * The player: one room, one film, everybody on one timeline.
 *
 * The page draws what the room says and calls the room for everything a person does; it never
 * touches the timeline itself. Every control that moves the film moves it for the whole room, and
 * every menu holds this viewer's own choice and says so.
 *
 * The frame decides the layout rather than the device: a wide frame has labelled controls and
 * popovers, a compact one has icons and sheets from the bottom edge, and a tiny one — a Discord
 * pop-out — is one row with an overflow menu.
 */
export function Player({ online = true, onBack, onEmpty, onCarryOn }) {
  const frameRef = React.useRef(null);
  const videoRef = React.useRef(null);
  const [room, setRoom] = React.useState(null);
  const size = useFrameSize(frameRef);

  React.useEffect(() => {
    const next = new PlayerRoom(videoRef.current);
    setRoom(next);
    void next.start();
    return () => next.dispose();
  }, []);

  const view = React.useSyncExternalStore(room ? room.subscribe : noSubscribe, room ? room.snapshot : noView);

  return (
    <div ref={frameRef} className={`mp-frame mp-frame--${size}`} data-size={size}>
      <video ref={videoRef} className="mp-video" playsInline preload="auto" />
      {room && view && <Stage room={room} view={view} size={size} online={online} frameRef={frameRef} videoRef={videoRef}
        onBack={onBack} onEmpty={onEmpty} onCarryOn={onCarryOn} />}
    </div>
  );
}

/** A dropped connection shorter than this is the hub reconnecting, and not worth saying. */
const LostGraceMs = 4000;

/**
 * Whether the room cannot be heard from: the hub has been reconnecting past a moment, or the page
 * knows the server cannot be reached at all. A film played alone has no room to lose.
 */
function useRoomLost(view, online) {
  const down = !view.solo && !view.fatal && view.state !== null
    && (view.connection === "reconnecting" || view.connection === "disconnected" || !online);
  const [lost, setLost] = React.useState(false);
  React.useEffect(() => {
    if (!down) { setLost(false); return undefined; }
    if (!online) { setLost(true); return undefined; }
    const timer = window.setTimeout(() => setLost(true), LostGraceMs);
    return () => window.clearTimeout(timer);
  }, [down, online]);
  return lost;
}

function Stage({ room, view, size, online, frameRef, videoRef, onBack, onEmpty, onCarryOn }) {
  const video = videoRef.current;
  const manifest = view.manifest;
  const solo = view.solo;
  // A guest watches: nothing that moves the room is drawn or answered, and there is no way out of the player.
  const viewOnly = view.viewOnly;
  const lost = useRoomLost(view, online);
  const compact = size !== "wide";
  const [menu, setMenu] = React.useState(null);
  const bottomRef = React.useRef(null);
  // The subtitle menu's style editor is open: a sample line stands in while the film says nothing.
  const [styling, setStyling] = React.useState(false);
  React.useEffect(() => { if (menu !== "subs") setStyling(false); }, [menu]);
  const [overflow, setOverflow] = React.useState(false);
  const [invite, setInvite] = React.useState(false);
  const [info, setInfo] = React.useState(false);
  // The stats panel, which this browser keeps open across a reload made to try again.
  const [stats, setStats] = React.useState(statsKeptOpen);
  const toggleStats = React.useCallback(() => setStats((open) => { keepStatsOpen(!open); return !open; }), []);
  const [held, setHeld] = React.useState(0);
  const [active, setActive] = React.useState(true);
  // A phone's screen locked against a hand resting on it: no touch reaches the film or the controls,
  // and a tap shows only the way to unlock.
  const [locked, setLocked] = React.useState(false);
  const [announcement, setAnnouncement] = React.useState(null);
  const [clock, setClock] = React.useState({ position: 0, buffered: 0, paused: true });
  const [volume, setVolume] = React.useState(() => ({ position: prefs.volume(), muted: prefs.muted() }));
  const [brightness, setBrightness] = React.useState(() => prefs.brightness());
  const [fullscreen, setFullscreen] = React.useState(false);
  const idleTimer = React.useRef(null);
  // A film played alone is the one the device holds, and has no episodes either side to move to.
  const around = useAround(view);
  const next = around && around.next;
  const goNext = () => room.nextEpisode();
  const about = useAbout(view);

  // The stored number is the loudness asked for; the element is given the amplitude that makes it.
  React.useEffect(() => {
    if (!video) return;
    video.volume = amplitudeFor(volume.position);
    video.muted = volume.muted;
  }, [video, volume]);

  const changeVolume = React.useCallback((position, muted) => {
    const next = { position: Math.min(1, Math.max(0, position)), muted };
    prefs.setVolume(next.position, next.muted);
    setVolume(next);
  }, []);
  const nudgeVolume = React.useCallback((step) => {
    setVolume((current) => {
      const position = Math.min(1, Math.max(0, current.position + step));
      const muted = position > 0 ? false : current.muted;
      prefs.setVolume(position, muted);
      return { position, muted };
    });
  }, []);

  // Where this viewer's playhead is, read off the element; the room's position while it holds no film.
  React.useEffect(() => {
    if (!video) return undefined;
    const tick = () => {
      const holds = room.holdsFilm;
      const position = holds ? video.currentTime : room.roomPosition();
      let buffered = 0;
      for (let i = 0; i < video.buffered.length; i++) {
        if (video.buffered.start(i) <= position + 0.5) buffered = Math.max(buffered, video.buffered.end(i));
      }
      setClock({ position, buffered, paused: holds ? video.paused : !!(view.state && view.state.paused) });
    };
    tick();
    const timer = window.setInterval(tick, PositionTickMs);
    return () => window.clearInterval(timer);
  }, [video, room, view.state]);

  // An act on the room, anybody's, said over the film with their mark beside it.
  React.useEffect(() => room.onAnnounce((said) => {
    setAnnouncement({ ...said, key: Date.now() });
  }), [room]);

  // A film the room was waiting on has arrived far enough to play; the rest is still coming.
  const waitedOn = React.useRef(false);
  const arrivedTitle = view.state && view.state.titleId;
  const preparingNow = Boolean(view.state && view.state.preparing && view.state.preparing.stage === "downloading");
  React.useEffect(() => {
    if (preparingNow) waitedOn.current = true;
    else if (waitedOn.current && arrivedTitle) {
      waitedOn.current = false;
      setAnnouncement({ text: "Playing while it finishes downloading.", actor: null, key: Date.now() });
    }
  }, [preparingNow, arrivedTitle]);
  React.useEffect(() => {
    if (!announcement) return undefined;
    const timer = window.setTimeout(() => setAnnouncement(null), AnnouncementMs);
    return () => window.clearTimeout(timer);
  }, [announcement]);

  // Controls go when nobody is reaching for them and the film is playing; anything open holds them.
  const wake = React.useCallback(() => {
    setActive(true);
    if (idleTimer.current !== null) window.clearTimeout(idleTimer.current);
    idleTimer.current = window.setTimeout(() => setActive(false), environment().tv ? TvIdleMs : IdleMs);
  }, []);
  React.useEffect(() => { wake(); return () => window.clearTimeout(idleTimer.current); }, [wake]);
  const hold = React.useCallback((on) => setHeld((n) => Math.max(0, n + (on ? 1 : -1))), []);
  const shown = !locked && (active || held > 0 || menu !== null || overflow || invite || info || clock.paused || !view.ready);

  // A television is driven by its remote (`engine/media/remote.ts`): the volume is the set's own, the
  // screen is already whole, and nobody across the room is handed a link to copy.
  const tv = environment().tv;
  const remoteAttached = useRemoteAttached();
  const playRef = React.useRef(null);
  const [remoteHud, setRemoteHud] = React.useState(null);
  const [remoteScrub, setRemoteScrub] = React.useState(null);
  // Whether the scrub bar is seeking: only then do Left and Right move the film from it. Read by the
  // remote as the keys arrive, so it is kept in a ref as well as drawn.
  const [seeking, setSeekingDrawn] = React.useState(false);
  const seekingRef = React.useRef(false);
  const setSeeking = React.useCallback((on) => { seekingRef.current = on; setSeekingDrawn(on); }, []);
  React.useEffect(() => { if (!shown) setSeeking(false); }, [shown, setSeeking]);
  const live = React.useRef(null);
  live.current = {
    shown, active, paused: clock.paused, position: clock.position,
    duration: manifest ? manifest.durationSeconds : 0, open: menu !== null || overflow || invite || info,
  };

  // A phone holds the film sideways and full screen from the first press, so it has no button for
  // either; leaving full screen gives both up until the next press on the film takes them back.
  const phone = React.useMemo(() => !tv && window.matchMedia("(pointer: coarse)").matches, [tv]);
  const landscape = React.useRef(null);
  React.useEffect(() => {
    const held = environment().landscape(frameRef.current);
    landscape.current = held;
    return () => held.release();
  }, [frameRef]);
  const takeScreen = () => { if (landscape.current) landscape.current.enter(); };

  const canFullscreen = typeof document !== "undefined" && document.fullscreenEnabled && !phone && !tv;
  React.useEffect(() => {
    const changed = () => setFullscreen(document.fullscreenElement === frameRef.current);
    document.addEventListener("fullscreenchange", changed);
    return () => document.removeEventListener("fullscreenchange", changed);
  }, [frameRef]);
  const toggleFullscreen = React.useCallback(() => {
    if (!document.fullscreenEnabled) return;
    if (document.fullscreenElement) void document.exitFullscreen();
    else void frameRef.current.requestFullscreen().catch(() => {});
  }, [frameRef]);

  const closeAll = React.useCallback(() => { setMenu(null); setOverflow(false); setInvite(false); setInfo(false); }, []);
  const openMenu = (kind) => { setOverflow(false); setInvite(false); setInfo(false); setMenu((m) => (m === kind ? null : kind)); };
  // What is playing is offered wherever a pane fits beside the film and a pointer can scroll it.
  const offersAbout = Boolean(about) && size !== "tiny" && !tv;
  const toggleInfo = React.useCallback(() => {
    if (!offersAbout) return;
    setMenu(null); setOverflow(false); setInvite(false); setInfo((o) => !o);
  }, [offersAbout]);

  React.useEffect(() => tv ? undefined : bindShortcuts({
    togglePlay: () => { room.togglePlay(); wake(); },
    seekBy: (seconds) => { room.seekBy(seconds); wake(); },
    volumeBy: (step) => { nudgeVolume(step); wake(); },
    toggleFullscreen,
    toggleMute: () => { setVolume((v) => { prefs.setVolume(v.position, !v.muted); return { ...v, muted: !v.muted }; }); wake(); },
    toggleSubtitles: () => { room.toggleSubtitles(); wake(); },
    toggleAbout: () => { toggleInfo(); wake(); },
  }), [tv, room, wake, nudgeVolume, toggleFullscreen, toggleInfo]);

  // Ctrl+Alt+Shift+D, the chord the big streaming players open their stats with. The key's code,
  // not its character: Alt and Shift change the character D types on a Mac's keyboard.
  React.useEffect(() => {
    const onKey = (event) => {
      if (!(event.ctrlKey && event.altKey && event.shiftKey && event.code === "KeyD")) return;
      event.preventDefault();
      toggleStats();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [toggleStats]);

  // The remote, ahead of the page's navigator: it listens while the key is still on its way down.
  // Back closes what is open, then puts the controls away, then leaves; Menu is the subtitles.
  React.useEffect(() => {
    if (!tv) return undefined;
    const frame = frameRef.current;
    const bar = () => frame.querySelector(".mp-scrub");
    const onBar = () => Boolean(document.activeElement && document.activeElement === bar());
    const toPlay = () => { if (playRef.current) playRef.current.focus({ focusVisible: true }); };
    // The control under the bar that focus last left it for, where Down from the bar goes back to.
    let underBar = null;
    const remember = (e) => {
      if (e.target instanceof HTMLElement && e.target.closest(".mp-controls")) underBar = e.target;
      // Focus leaving the bar ends its seeking.
      if (seekingRef.current && e.target !== bar()) setSeeking(false);
    };
    document.addEventListener("focusin", remember);
    const remote = new Remote({
      shown: () => live.current.shown,
      reveal: () => {
        wake();
        const focused = document.activeElement;
        if (!focused || focused === document.body || !frame.contains(focused)) toPlay();
      },
      onBar,
      seeking: () => seekingRef.current,
      setSeeking: (on) => {
        if (on) {
          wake();
          const scrub = bar();
          if (scrub && document.activeElement !== scrub) scrub.focus({ focusVisible: true });
        }
        setSeeking(on);
      },
      below: () => {
        if (underBar && underBar.isConnected && frame.contains(underBar) && !underBar.disabled) underBar.focus({ focusVisible: true });
        else toPlay();
      },
      position: () => live.current.position,
      duration: () => live.current.duration,
      // OK or play-pause with the controls away leaves focus on play, so the next OK answers it again.
      togglePlay: () => { const away = !live.current.shown; room.togglePlay(); wake(); if (away) toPlay(); },
      play: () => { if (live.current.paused) room.togglePlay(); wake(); },
      pause: () => { if (!live.current.paused) room.togglePlay(); wake(); },
      skipBy: (seconds) => { room.seekBy(seconds); wake(); },
      // A skip being gathered previews where it lands on the bar, frame and time, as a held key does.
      gathering: (seconds) => {
        setRemoteHud(seconds === null ? null : { kind: "skip", side: seconds < 0 ? "left" : "right", seconds, key: Date.now() });
        setRemoteScrub(seconds === null ? null
          : Math.min(live.current.duration, Math.max(0, live.current.position + seconds)));
        if (seconds !== null) wake();
      },
      scrubbing: (at) => { setRemoteScrub(at); if (at !== null) wake(); },
      seek: (seconds) => room.seek(seconds),
    });
    const typing = (target) => target instanceof HTMLElement
      && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));
    const down = (e) => {
      if (e.ctrlKey || e.metaKey || e.altKey || typing(e.target)) return;
      // Any press among the controls up, whether the remote takes it or leaves it to the browser to walk
      // focus, is somebody reaching for one, and holds them up afresh.
      const taken = remote.keydown(e.key, e.repeat);
      if (live.current.shown && (taken || NavigationKeys.has(e.key))) wake();
      if (taken) { e.preventDefault(); e.stopPropagation(); }
    };
    const up = (e) => {
      if (remote.keyup(e.key)) { e.preventDefault(); e.stopPropagation(); }
    };
    window.addEventListener("keydown", down, true);
    window.addEventListener("keyup", up, true);
    const offBack = onTvBack(() => {
      const now = live.current;
      if (now.open) { closeAll(); return true; }
      if (remote.stopSeeking()) { wake(); return true; }
      if (now.active && !now.paused) {
        window.clearTimeout(idleTimer.current);
        setActive(false);
        return true;
      }
      if (onBack) { onBack(); return true; }
      return false;
    }, Rank.screen);
    const offMenu = onTvMenu(() => {
      setOverflow(false);
      setInvite(false);
      setInfo(false);
      setMenu((m) => (m === "subs" ? null : "subs"));
      wake();
      return true;
    }, Rank.screen);
    return () => {
      remote.dispose();
      document.removeEventListener("focusin", remember);
      window.removeEventListener("keydown", down, true);
      window.removeEventListener("keyup", up, true);
      offBack();
      offMenu();
    };
  }, [tv, room, wake, closeAll, onBack, frameRef, setSeeking]);

  // On a television the play button takes focus once the film is ready, so OK answers from the first
  // press, unless somebody has already pressed a key in the player: until then, whatever focus found
  // while the film was arriving, such as the way back, was nobody's choice.
  const pressed = React.useRef(false);
  React.useEffect(() => {
    if (!tv) return undefined;
    const mark = () => { pressed.current = true; };
    window.addEventListener("keydown", mark, true);
    return () => window.removeEventListener("keydown", mark, true);
  }, [tv]);
  React.useEffect(() => {
    if (!tv || !view.ready || pressed.current || !playRef.current) return;
    playRef.current.focus({ focusVisible: true });
  }, [tv, view.ready]);

  React.useEffect(() => {
    const escape = (e) => { if (e.key === "Escape") closeAll(); };
    document.addEventListener("keydown", escape);
    return () => document.removeEventListener("keydown", escape);
  }, [closeAll]);

  const toggleControls = () => {
    if (active) setActive(false);
    else wake();
  };
  const settable = React.useMemo(() => volumeIsSettable(), []);
  const gestures = useStageGestures({
    duration: manifest ? manifest.durationSeconds : 0,
    position: clock.position,
    skipSeconds: SkipSeconds,
    volume: volume.muted ? 0 : volume.position,
    volumeSettable: settable,
    brightness,
    seekable: !viewOnly,
    middlePlays: false,
    sweepSeeks: false,
    onTogglePlay: () => room.togglePlay(),
    onToggleControls: toggleControls,
    onSeek: (s) => room.seek(s),
    onHold: hold,
    onVolume: (level) => changeVolume(level, level > 0 ? false : volume.muted),
    onBrightness: (level) => { prefs.setBrightness(level); setBrightness(level); },
  });

  // A mouse click on the film plays or pauses it, or closes whatever was open over it. A finger
  // is read by the gestures once the film is in the element; before that, a touch only shows or
  // hides what is over the frame.
  const pressStage = (e) => {
    if (e.target !== e.currentTarget) return;
    if (locked) { wake(); return; }
    if (menu !== null || overflow || invite || info) { closeAll(); return; }
    if (e.pointerType === "mouse") { if (!viewOnly) room.togglePlay(); wake(); return; }
    if (view.ready) gestures.onPointerDown(e);
    else toggleControls();
  };
  const moveOnStage = (e) => {
    if (e.pointerType === "mouse") wake();
    else gestures.onPointerMove(e);
  };
  const liftFromStage = (e) => {
    if (e.pointerType === "mouse" || e.target !== e.currentTarget) return;
    takeScreen();
    gestures.onPointerUp(e);
  };

  const everyone = solo ? [] : people(view);
  const others = view.identity ? everyone.filter((p) => p.userId !== view.identity.userId) : everyone;
  const skipOffer = useSkipOffer((seconds) => room.seek(seconds), {
    markers: manifest && manifest.markers, titleId: view.state && view.state.titleId, ready: view.ready,
    auto: Boolean(view.preferences && view.preferences.skipIntros),
  }, clock.position, others.length === 0);
  const titleHeld = view.state &&(view.state.titleId ?? null) !== null;
  const duration = manifest ? manifest.durationSeconds : 0;
  const preparing = manifest && !view.ready && !view.unplayable && !view.gate && !view.fatal;
  const waiting = !view.fatal && !view.unplayable && !view.gate && !preparing
    && ((titleHeld && !manifest) || view.catchingUp || view.stalled);
  const sheet = compact;
  // A phone's play and pause is the large button in the middle of the film, the skips either side of
  // it, as the big streaming apps draw them; its row under the bar opens menus.
  const statsEntry = { kind: "stats", icon: "activity", value: stats ? "On" : "Off" };
  const centred = phone && Boolean(manifest) && view.ready && !view.gate && !viewOnly && size !== "tiny" && !locked;
  const settings = manifest ? [
    (manifest.chapters || []).length > 1
      ? { kind: "chapters", icon: "list", value: (chapterAt(manifest.chapters, clock.position) || {}).title || MenuTitles.chapters } : null,
    hasQualityChoice(view) ? { kind: "quality", icon: "gauge", value: qualityValue(view) } : null,
    statsEntry,
  ].filter(Boolean) : [];
  // A phone's Settings holds what a wide row has a button for; everywhere else it holds the stats alone.
  const menuSettings = phone ? settings : [statsEntry];
  const pickSetting = (kind) => {
    if (kind !== "stats") { setMenu(kind); return; }
    setMenu(null);
    toggleStats();
  };
  const lock = () => {
    closeAll();
    window.clearTimeout(idleTimer.current);
    setActive(false);
    setLocked(true);
  };
  const swipe = remoteScrub ?? (gestures.hud && gestures.hud.kind === "seek" ? gestures.hud.at : null);
  // A room left paused a moment says what is playing over the left of the film, while nothing else is open over it.
  const pausedLong = usePausedLong(Boolean(view.ready && clock.paused));
  const pauseCard = pausedLong && about && view.ready && !waiting && !view.fatal && !view.unplayable && !view.gate
    && size !== "tiny" && menu === null && !overflow && !invite && !info;
  // The room stopped by somebody in it, which a guest has no button to say.
  const roomPaused = viewOnly && view.ready && !view.gate && Boolean(view.state && view.state.paused);

  const backdrop = solo ? view.artwork : posterOf(manifest);
  const frameStyle = { "--mp-poster": backdrop ? `url("${backdrop}")` : "none" };
  const carryOn = lost && view.saved && manifest && onCarryOn ? () => onCarryOn(manifest.id, clock.position) : null;

  if (view.guestEnded) {
    return (
      <div className="mp-stage is-shown" style={frameStyle}>
        <div className="mp-backdrop" aria-hidden="true" />
        <div className="mp-shade" aria-hidden="true" />
        <GuestEnded reason={view.guestEnded} />
      </div>
    );
  }

  return (
    <div className={"mp-stage" + (shown ? " is-shown" : " is-idle") + (view.ready ? " is-playing-film" : "")
      + (phone ? " is-touch" : "") + (locked ? " is-locked" : "")
      + (info && offersAbout && !sheet ? " has-about" : "")} style={frameStyle}
      onPointerDown={pressStage} onPointerMove={moveOnStage} onPointerUp={liftFromStage}
      onPointerCancel={(e) => { if (e.pointerType !== "mouse") gestures.onPointerCancel(e); }}
      // A touch on the bare film is the gestures' alone: the click a browser makes of it afterwards would
      // land on whatever the touch just brought up under the finger, such as the play button.
      onTouchEnd={(e) => { if (e.target === e.currentTarget && e.cancelable) e.preventDefault(); }}>
      {brightness < 1 && <div className="mp-dim" style={{ opacity: 1 - brightness }} aria-hidden="true" />}
      <div className="mp-backdrop" aria-hidden="true" />
      <div className="mp-shade" aria-hidden="true" />

      {view.ready && <Cues track={view.subtitleTrack} raised={shown} lifted={styling && sheet} beside={styling && !sheet}
        style={view.preferences.subtitleStyle} sample={styling || view.styleSample} />}

      {/* ── Top bar ── */}
      {manifest && (
        <div className="mp-top">
          {!viewOnly && onBack && size !== "tiny" && (
            <Button variant="bare" className="mp-round" aria-label="Back" title="Back" onClick={onBack}><Icon name="arrow-left" size={18} /></Button>
          )}
          <div className="mp-top__title">
            <span className="mp-top__name">{filmName(manifest)}</span>
            {size !== "tiny" && <span className="mp-top__sub">{filmLine(manifest, around)}</span>}
          </div>
          {!solo && (
            <div className="mp-top__room">
              {tv && remoteAttached && clock.paused && (
                <span className="mp-top__remote" role="img" aria-label="A remote is attached"><Icon name="smartphone" size={18} /></span>
              )}
              <div className="mp-avatars">{everyone.map((p) => <Person key={p.userId} person={p} />)}</div>
              {size === "wide" && <span className="mp-top__count">{everyone.length > 1 ? `${everyone.length} watching` : "Just you"}</span>}
              {size !== "tiny" && !tv && !viewOnly && <InviteButton open={invite} icon={phone}
                onToggle={() => { setMenu(null); setOverflow(false); setInfo(false); setInvite((o) => !o); }} />}
            </div>
          )}
          {offersAbout && <AboutButton open={info} onToggle={toggleInfo} />}
        </div>
      )}

      {lost && (
        <div className="mp-lost" role="status">
          <Icon name="wifi-off" size={16} />
          <span className="mp-lost__text">The room can't be reached</span>
          {carryOn && <Button variant="bare" className="mp-lost__go" onClick={carryOn}>Carry on from this device</Button>}
        </div>
      )}

      {announcement && (
        <div key={announcement.key} className="mp-event" role="status">
          {announcement.actor && <Person person={announcement.actor} className="mp-avatar--small" />}
          {announcement.text}
        </div>
      )}
      <GestureHud hud={gestures.hud || remoteHud} />
      {view.notices.length > 0 && (
        <div className="mp-notices">
          {view.notices.map((n) => <div key={n.id} className={"mp-notice mp-notice--" + n.kind}>{n.text}</div>)}
        </div>
      )}

      {/* ── What stands in the middle of the frame ── */}
      {waiting && !centred && <div className="mp-spinner" role="status" aria-label="Waiting for the film"><Icon name="loader-circle" size={36} className="mp-spin" /></div>}
      {centred && (
        <div className="mp-centre">
          <Button variant="bare" className="mp-centre__skip" aria-label={`Back ${SkipSeconds} seconds`} onClick={() => { room.seekBy(-SkipSeconds); wake(); }}>
            <Icon name="rotate-ccw" size={28} />
          </Button>
          <Button variant="bare" ref={playRef} className="mp-centre__play" aria-label={clock.paused ? "Play" : "Pause"} onClick={() => { room.togglePlay(); wake(); }}>
            {waiting ? <Icon name="loader-circle" size={32} className="mp-spin" /> : <Icon name={clock.paused ? "play" : "pause"} size={32} />}
          </Button>
          <Button variant="bare" className="mp-centre__skip" aria-label={`Forward ${SkipSeconds} seconds`} onClick={() => { room.seekBy(SkipSeconds); wake(); }}>
            <Icon name="rotate-cw" size={28} />
          </Button>
        </div>
      )}
      {locked && active && (
        <Button variant="bare" className="mp-unlock" aria-label="Unlock" onClick={() => { setLocked(false); wake(); }}>
          <Icon name="lock-open" size={18} />Unlock
        </Button>
      )}
      {pauseCard && <PauseCard about={about} />}
      {pauseCard && tv && !viewOnly && <PausedCode />}
      {roomPaused && !waiting && !pauseCard && <div className="mp-paused" role="status"><Icon name="pause" size={18} />Paused</div>}
      {preparing && <Preparing manifest={manifest} head={view.transcodeHead} />}
      {view.gate && manifest && <JoinGate view={view} others={others} position={clock.position} focused={tv} solo={solo} onJoin={() => { takeScreen(); room.joinPlayback(); }} />}
      {view.unplayable && <Message icon="circle-alert" title="This cannot play here" text={view.unplayable} />}
      {view.fatal && <Message icon="circle-alert" title={view.fatal} action={onBack ? { label: "Back", go: onBack } : null} />}
      {!view.fatal && view.entry !== null && view.entry !== "in" && (
        <AtTheDoor entry={view.entry} onAsk={() => room.askToJoin()} onBack={onBack} />
      )}
      {view.entry === "in" && view.door && view.door.knocking.length > 0 && (
        <Knocking knocks={view.door.knocking} onAnswer={(userId, admit) => room.answerKnock(userId, admit)} />
      )}
      {/* What the room holds while it arrives. */}
      {!view.fatal && view.state && view.state.preparing && view.state.preparing.stage !== "failed" && (
        <Fetching preparing={view.state.preparing} onBack={onBack} />
      )}
      {!view.fatal && view.state && view.state.preparing && view.state.preparing.stage === "failed" && (
        <Message icon="circle-alert" title={`${view.state.preparing.name} could not be fetched`}
          text={fetchProblem(view.state.preparing)}
          action={onBack ? { label: "Back", go: onBack } : null} />
      )}
      {!view.fatal && view.state && !titleHeld && !view.state.preparing && (
        <Message icon="clapperboard" title="Nothing playing yet"
          action={onEmpty ? { label: "Choose something to watch", go: onEmpty } : null} />
      )}

      {/* ── Bottom controls ── */}
      {manifest && view.ready && (size === "tiny"
        ? <TinyControls room={room} view={view} clock={clock} duration={duration} onHold={hold} swipe={swipe} onMore={() => { setMenu(null); setOverflow((o) => !o); }} />
        : (
          <div className="mp-bottom" ref={bottomRef}>
            <ScrubBar duration={duration} position={clock.position} ready={view.transcodeHead} buffered={clock.buffered}
              thumbnails={view.thumbnails} chapters={manifest.chapters} onSeek={(s) => room.seek(s)} onHold={hold} swipe={swipe}
              readOnly={viewOnly} seeking={seeking} remaining={phone} />
            {phone ? (
              <div className="mp-controls">
                <Button variant="bare" className={"mp-menu-button" + (compact ? " is-icon" : "")} aria-label="Lock" title="Lock" onClick={lock}>
                  <Icon name="lock" size={18} />
                  {!compact && <span className="mp-menu-button__value">Lock</span>}
                </Button>
                <span className="mp-controls__gap" />
                {hasEpisodes(view) && (
                  <MenuButton kind="episodes" icon="list-video" open={menu === "episodes"} compact={compact} onClick={() => openMenu("episodes")} />
                )}
                <MenuButton kind="listen" icon="captions" open={menu === "listen"} compact={compact} onClick={() => openMenu("listen")} />
                {settings.length > 0 && (
                  <MenuButton kind="settings" icon="settings" open={menu === "settings"} compact={compact} onClick={() => openMenu("settings")} />
                )}
                {manifest.episode && !viewOnly && next && (
                  <Button variant="bare" className={"mp-menu-button" + (compact ? " is-icon" : "")} disabled={view.stepping}
                    aria-label={`Next episode: ${episodeWords(next)}`} title={`Next episode · ${episodeWords(next)}`} onClick={goNext}>
                    <Icon name="skip-forward" size={18} />
                    {!compact && <span className="mp-menu-button__value">Next episode</span>}
                  </Button>
                )}
              </div>
            ) : (
            <div className="mp-controls">
              {!viewOnly && (
                <>
                  <Button variant="bare" ref={playRef} className="mp-play" aria-label={clock.paused ? "Play" : "Pause"} onClick={() => { room.togglePlay(); wake(); }}>
                    <Icon name={clock.paused ? "play" : "pause"} size={compact ? 18 : 20} />
                  </Button>
                  <Button variant="bare" className="mp-icon" aria-label={`Back ${SkipSeconds} seconds`} title={`Back ${SkipSeconds} seconds (j)`} onClick={() => room.seekBy(-SkipSeconds)}>
                    <Icon name="rotate-ccw" size={19} />
                  </Button>
                  <Button variant="bare" className="mp-icon" aria-label={`Forward ${SkipSeconds} seconds`} title={`Forward ${SkipSeconds} seconds (l)`} onClick={() => room.seekBy(SkipSeconds)}>
                    <Icon name="rotate-cw" size={19} />
                  </Button>
                </>
              )}
              {manifest.episode && !viewOnly && (
                <>
                  <Button variant="bare" className="mp-icon" disabled={!(around && around.previous) || view.stepping}
                    aria-label={around && around.previous ? `Previous episode: ${episodeWords(around.previous)}` : "No previous episode here"}
                    title={around && around.previous ? `Previous episode · ${episodeWords(around.previous)}` : "No previous episode here"}
                    onClick={() => room.previousEpisode()}>
                    <Icon name="skip-back" size={19} />
                  </Button>
                  <Button variant="bare" className="mp-icon" disabled={!next || view.stepping}
                    aria-label={next ? `Next episode: ${episodeWords(next)}` : "No next episode here"}
                    title={next ? `Next episode · ${episodeWords(next)}` : "No next episode here"}
                    onClick={goNext}>
                    <Icon name="skip-forward" size={19} />
                  </Button>
                </>
              )}
              {!tv && <Volume position={volume.position} muted={volume.muted} onChange={changeVolume} onNudge={nudgeVolume} />}
              <span className="mp-controls__gap" />
              {(manifest.chapters || []).length > 1 && (
                <MenuButton kind="chapters" icon="list" value={(chapterAt(manifest.chapters, clock.position) || {}).title || "Chapters"}
                  open={menu === "chapters"} compact={compact} onClick={() => openMenu("chapters")} />
              )}
              {hasEpisodes(view) && (
                <MenuButton kind="episodes" icon="list-video" value={episodeValue(view)}
                  open={menu === "episodes"} compact={compact} onClick={() => openMenu("episodes")} />
              )}
              <MenuButton kind="subs" icon="captions" value={subtitleValue(view)} open={menu === "subs"} compact={compact} onClick={() => openMenu("subs")} />
              {manifest.audio.length > 1 && <MenuButton kind="audio" icon="audio-lines" value={audioValue(view)} open={menu === "audio"} compact={compact} onClick={() => openMenu("audio")} />}
              {hasQualityChoice(view) && <MenuButton kind="quality" icon="gauge" value={qualityValue(view)} open={menu === "quality"} compact={compact} onClick={() => openMenu("quality")} />}
              <MenuButton kind="settings" icon="settings" open={menu === "settings"} compact onClick={() => openMenu("settings")} />
              {canFullscreen && (
                <Button variant="bare" className="mp-icon" aria-label={fullscreen ? "Exit full screen" : "Full screen"} title="Full screen (f)" onClick={toggleFullscreen}>
                  <Icon name={fullscreen ? "minimize" : "maximize"} size={19} />
                </Button>
              )}
            </div>
            )}
          </div>
        ))}

      {manifest && view.ready && next && !(view.state && view.state.preparing) && size !== "tiny" && inClosingStretch(manifest, clock.position) && (
        <Button variant="bare" className="mp-nextup" disabled={view.stepping} onClick={goNext}>
          <span className="mp-nextup__label">Next episode</span>
          <span className="mp-nextup__name">{episodeWords(next)}</span>
          <Icon name="skip-forward" size={18} />
        </Button>
      )}

      {skipOffer && !viewOnly && !(view.state && view.state.preparing) && size !== "tiny" && (
        <Button variant="bare" className="mp-nextup mp-skip" onClick={skipOffer.skip}>
          <span className="mp-nextup__name">{SkipLabels[skipOffer.marker.kind]}</span>
          <Icon name="skip-forward" size={18} />
        </Button>
      )}

      {menu !== null && manifest && <PlayerMenu kind={menu} view={view} room={room} sheet={sheet} position={clock.position}
        styling={styling} onStyling={setStyling} onClose={() => setMenu(null)} settings={menuSettings} onPick={pickSetting}
        anchorRef={bottomRef} container={() => frameRef.current} />}
      {overflow && manifest && (
        <Overflow view={view} volume={volume} onVolume={changeVolume} onNudge={nudgeVolume} stats={stats}
          onPick={(kind) => { setOverflow(false); pickSetting(kind); }} onClose={() => setOverflow(false)} />
      )}
      {stats && <PlaybackStats room={room} solo={solo} onClose={toggleStats} />}
      {invite && <InvitePanel view={view} room={room} everyone={everyone} compact={compact} />}
      {info && offersAbout && <AboutPanel about={about} sheet={sheet} onClose={() => setInfo(false)} />}
    </div>
  );
}

/** The single row a popped-out frame has room for. */
function TinyControls({ room, view, clock, duration, onHold, swipe, onMore }) {
  return (
    <div className="mp-tiny">
      {!view.viewOnly && (
        <Button variant="bare" className="mp-play mp-play--small" aria-label={clock.paused ? "Play" : "Pause"} onClick={() => room.togglePlay()}>
          <Icon name={clock.paused ? "play" : "pause"} size={16} />
        </Button>
      )}
      <ScrubBar thin duration={duration} position={clock.position} ready={view.transcodeHead} buffered={clock.buffered}
        thumbnails={null} onSeek={(s) => room.seek(s)} onHold={onHold} swipe={swipe} readOnly={view.viewOnly} />
      <span className="mp-tiny__left">-{formatTime(Math.max(0, duration - clock.position))}</span>
      <Button variant="bare" className="mp-icon" aria-label="More" onClick={onMore}><Icon name="ellipsis" size={18} /></Button>
    </div>
  );
}

/**
 * The link that hands the room to somebody, which an open room has and a private one does not: the
 * way into a private room is being let in, and a link to it would lead anybody given it to a door.
 */
function roomLink(view) {
  return view.door && view.door.open ? environment().roomUrl() : null;
}

/** Everything a tiny frame folds away: the menus, the volume, the stats and the way to hand the room on. */
function Overflow({ view, volume, onVolume, onNudge, stats, onPick, onClose }) {
  const link = roomLink(view);
  const entries = [
    hasEpisodes(view) ? ["episodes", "list-video", episodeValue(view)] : null,
    ["subs", "captions", subtitleValue(view)],
    view.manifest.audio.length > 1 ? ["audio", "audio-lines", audioValue(view)] : null,
    hasQualityChoice(view) ? ["quality", "gauge", qualityValue(view)] : null,
    ["stats", "activity", stats ? "On" : "Off"],
  ].filter(Boolean);
  return (
    <Drawer contained role="menu" label="More" onClose={onClose} className="mp-menu">
      <div className="mp-menu__list">
        {entries.map(([kind, icon, value]) => (
          <MenuItem key={kind} icon={icon} aside={value} closeOnSelect={false} {...reach} onSelect={() => onPick(kind)}>{MenuTitles[kind]}</MenuItem>
        ))}
        <div className="mp-menu__volume">
          <Volume position={volume.position} muted={volume.muted} onChange={onVolume} onNudge={onNudge} />
        </div>
        {link && !view.solo && <CopyLink link={link} />}
      </div>
    </Drawer>
  );
}

function CopyLink({ link, label = "Copy room link" }) {
  const [copied, setCopied] = React.useState(false);
  React.useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 1600);
    return () => window.clearTimeout(timer);
  }, [copied]);
  return (
    <MenuItem icon={copied ? "check" : "link"} closeOnSelect={false} {...reach} onSelect={() => {
      navigator.clipboard.writeText(link).then(() => setCopied(true), () => {});
    }}>{copied ? "Copied" : label}</MenuItem>
  );
}

/** Opens the panel that hands the room to somebody else and says who may come in. */
/** Invite, a word beside its icon, or on a phone the icon alone. */
function InviteButton({ open, onToggle, icon = false }) {
  if (icon) {
    return (
      <Button variant="bare" className={"mp-round" + (open ? " is-open" : "")} aria-expanded={open} aria-label="Invite" title="Invite" onClick={onToggle}>
        <Icon name="user-plus" size={18} />
      </Button>
    );
  }
  return (
    <Button variant="bare" className={"mp-invite" + (open ? " is-open" : "")} aria-expanded={open} onClick={onToggle}>
      <Icon name="user-plus" size={17} />Invite
    </Button>
  );
}

/**
 * Who may come into the room, and everybody who can be asked in. The room is open until somebody
 * closes it, and only an open room has a link to hand out: on a phone it goes to the phone's own
 * share sheet, elsewhere it is copied. An invite lets the person in (`InvitePeople`). Somebody already
 * in the room is marked as watching.
 */
function InvitePanel({ view, room, everyone, compact }) {
  const link = roomLink(view);
  const open = Boolean(view.door && view.door.open);
  const here = new Set(everyone.map((p) => p.userId));
  const invite = React.useCallback(
    (person) => api.invite(environment().sessionId(), person.userId).then((reply) => reply.delivery), []);
  const share = Boolean(compact && navigator.share);
  const title = `Watch ${filmName(view.manifest)}`;

  return (
    <div className="mp-invite-panel">
      <div className="mp-invite-panel__head">Watch together</div>
      <section className="mp-invite-panel__section mp-invite-panel__door">
        <div className="mp-invite-panel__access">
          <span className={"mp-invite-panel__badge" + (open ? " is-open" : "")}><Icon name={open ? "lock-open" : "lock"} size={16} /></span>
          <span className="mp-invite-panel__access-name">{open ? "Open room" : "Private room"}</span>
          <Toggle on={open} label="Open room" onChange={(on) => room.setOpen(on)} />
        </div>
        {link && <LinkRow url={link} share={share} title={title} />}
      </section>
      <GuestAccess view={view} everyone={everyone} share={share} title={title} />
      <InvitePeople load={api.people} invite={invite} here={here} hereMark="Watching" hereStatus="In this room"
        className="mp-invite-panel__section mp-invite-panel__people" />
    </div>
  );
}

/** A link to hand on: shown whole, and shared to the phone's sheet or copied. */
function LinkRow({ url, share, title }) {
  const [copied, setCopied] = React.useState(false);
  React.useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 1600);
    return () => window.clearTimeout(timer);
  }, [copied]);
  const handOn = () => {
    if (share) navigator.share({ title, url }).catch(() => {});
    else navigator.clipboard.writeText(url).then(() => setCopied(true), () => {});
  };
  return (
    <div className="mp-invite-panel__row">
      <span className="mp-invite-panel__url">{url}</span>
      <Button variant="primary" icon={share ? "share-2" : copied ? "check" : "copy"} onClick={handOn}>
        {share ? "Share" : copied ? "Copied" : "Copy"}
      </Button>
    </div>
  );
}

/**
 * The room's guest link, for somebody who may hand it out: anybody holding it watches without an
 * account and changes nothing about the room. The room has one link, so asking for it again hands out
 * the same one; it is shown once asked for, and copied or shared at once where the browser allows.
 * Somebody in the room who may not hand it out sees only that guests can come in.
 */
function GuestAccess({ view, everyone, share, title }) {
  const may = environment().holds("cinema:guests");
  const active = Boolean(view.door && view.door.guests);
  const [link, setLink] = React.useState(null);
  const [state, setState] = React.useState(null);
  React.useEffect(() => { if (!active) setLink(null); }, [active]);
  if (!may && !active) return null;

  const guests = everyone.filter((p) => p.guest).length;
  const until = active ? new Date(view.door.guests.expiresAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : null;
  const ask = async () => {
    setState("asking");
    try {
      const made = await api.guestLink(environment().sessionId());
      const url = window.location.origin + made.path;
      setLink(url);
      setState(null);
      if (share) navigator.share({ title, url }).catch(() => {});
      else navigator.clipboard.writeText(url).catch(() => {});
    } catch {
      setState("failed");
    }
  };
  const end = async () => {
    setState("ending");
    try {
      await api.endGuests(environment().sessionId());
      setState(null);
    } catch {
      setState("failed");
    }
  };

  return (
    <section className="mp-invite-panel__section mp-invite-panel__guests">
      <div className="mp-invite-panel__label">
        Guests
        {active && <span className="mp-invite-panel__meta">{guests === 1 ? "1 guest" : `${guests} guests`} · until {until}</span>}
      </div>
      {link
        ? <LinkRow url={link} share={share} title={title} />
        : may && (
          <Button variant="secondary" icon="ticket" wide busy={state === "asking"} className="mp-invite-panel__ask" onClick={ask}>
            {active ? "Guest link" : "Invite guest"}
          </Button>
        )}
      {active && may && (
        <Button variant="ghost" size="xs" tone="danger" busy={state === "ending"} className="mp-invite-panel__end" onClick={end}>
          Stop guest access
        </Button>
      )}
      {state === "failed" && <span className="mp-invite-panel__status is-failed">That did not reach the room.</span>}
    </section>
  );
}

/**
 * The browser would not start the film without a press. Who is watching and where they are is
 * what makes pressing it worth it, and the line underneath says what this viewer brings with them.
 * A film played alone has nobody to join, and the press is Play.
 */
function JoinGate({ view, others, position, focused, solo, onJoin }) {
  const manifest = view.manifest;
  const subtitle = subtitleValue(view);
  const brings = [
    subtitle !== "Off" ? `${subtitle} subtitles` : null,
    hasQualityChoice(view) ? `${view.quality === null ? "Auto" : `${view.quality}p`} quality` : null,
  ].filter(Boolean);
  return (
    <div className="mp-gate">
      {others.length > 0 && <div className="mp-avatars mp-avatars--large">{others.map((p) => <Person key={p.userId} person={p} />)}</div>}
      <div className="mp-gate__text">
        {others.length > 0 && <span className="mp-gate__who">{namesOf(others)} {others.length === 1 ? "is" : "are"} watching</span>}
        <span className="mp-gate__name">{filmName(manifest)}</span>
        <span className="mp-gate__where">{formatTime(position)} of {formatTime(manifest.durationSeconds)}</span>
      </div>
      <div className="mp-gate__bar"><div style={{ width: `${manifest.durationSeconds > 0 ? Math.min(100, (position / manifest.durationSeconds) * 100) : 0}%` }} /></div>
      <Button variant="bare" className="mp-gate__join" autoFocus={focused} onClick={onJoin}>
        <Icon name={solo ? "play" : "users"} size={18} />{solo ? "Play" : "Join"}
      </Button>
      {view.identity && (
        <span className="mp-gate__me">{view.viewOnly ? "Watching as" : "Signed in as"} {view.identity.displayName}{brings.length > 0 ? ` · ${brings.join(", ")} from your settings` : ""}</span>
      )}
    </div>
  );
}

/**
 * The film is not in the element yet. A film still transcoding plays as soon as its opening is
 * written, so the bar is how much of it is, and the last step is the element taking it.
 */
function Preparing({ manifest, head }) {
  const transcoding = manifest.status === "transcoding";
  const written = transcoding && manifest.durationSeconds > 0 ? Math.min(1, (head ?? 0) / manifest.durationSeconds) : 1;
  const steps = [
    { label: "Preparing the video", sub: transcoding ? `${Math.floor(written * 100)}% · transcoding live` : "ready", done: true },
    { label: "Starting playback", sub: "as soon as enough is buffered", active: true },
  ];
  return (
    <div className="mp-prepare">
      <div className="mp-prepare__head">
        <span className="mp-prepare__eyebrow">Watch</span>
        <span className="mp-prepare__name">{filmName(manifest)}</span>
      </div>
      <div className="mp-prepare__steps">
        {steps.map((s) => (
          <div key={s.label} className={"mp-step" + (s.done ? " is-done" : "") + (s.active ? " is-active" : "")}>
            <span className="mp-step__dot">
              {s.done && <Icon name="check" size={12} />}
              {s.active && <Icon name="loader-circle" size={13} className="mp-spin" />}
            </span>
            <span className="mp-step__label">{s.label}</span>
            <span className="mp-step__sub">{s.sub}</span>
          </div>
        ))}
      </div>
      <div className="mp-prepare__bar"><div style={{ width: `${written * 100}%` }} /></div>
    </div>
  );
}

/**
 * A room waiting on a film somebody pressed Watch now on: it being found, arriving, and starting, as
 * the room says, so everybody in it sees the same panel. It starts on its own; leaving does not stop
 * the download.
 */
function Fetching({ preparing, onBack }) {
  const finding = preparing.stage === "finding";
  const progress = preparing.progress || 0;
  const steps = [
    { label: "Finding a release", sub: finding ? "Searching the tracker by IMDb ID" : `Matched on IMDb${preparing.release ? ` · ${preparing.release}` : ""}`, done: !finding, active: finding },
    { label: "Downloading", sub: finding ? "" : `${Math.floor(progress * 100)}% · transcoding live as it arrives`, active: !finding },
    { label: "Starting playback", sub: "" },
  ];
  return (
    <div className="mp-prepare" role="status">
      <div className="mp-prepare__head">
        <span className="mp-prepare__eyebrow">Watch</span>
        <span className="mp-prepare__name">{preparing.name}</span>
        <span className="mp-prepare__text">Playback starts on its own as soon as the server can stream it.</span>
      </div>
      <div className="mp-prepare__steps">
        {steps.map((s) => (
          <div key={s.label} className={"mp-step" + (s.done ? " is-done" : "") + (s.active ? " is-active" : "")}>
            <span className="mp-step__dot">
              {s.done && <Icon name="check" size={12} />}
              {s.active && <Icon name="loader-circle" size={13} className="mp-spin" />}
            </span>
            <span className="mp-step__label">{s.label}</span>
            {s.sub && <span className="mp-step__sub">{s.sub}</span>}
          </div>
        ))}
      </div>
      <div className="mp-prepare__bar"><div style={{ width: `${finding ? 8 : 24 + progress * 76}%` }} /></div>
      {onBack && (
        <div className="mp-prepare__foot">
          <span className="mp-prepare__text">Closing won't cancel the download.</span>
          <Button variant="bare" className="mp-prepare__leave" onClick={onBack}>Keep downloading in background</Button>
        </div>
      )}
    </div>
  );
}

/** Why a film a room waited on will not arrive, in words. */
function fetchProblem(preparing) {
  switch (preparing.problem) {
    case "notOffered": return "The tracker has no release of it at a quality worth watching.";
    case "refused": return preparing.refusal || "The download was refused.";
    case "notWatchable": return "It arrived, but it could not be made watchable.";
    default: return "The tracker or the torrent client is not answering. Try again in a minute.";
  }
}

/** A room private to this viewer: asking the people in it to be let in, and what they answered. */
function AtTheDoor({ entry, onAsk, onBack }) {
  const asking = entry === "asking";
  const title = asking ? "Waiting for someone to let you in" : entry === "refused" ? "You were not let in" : "This room is private";
  return (
    <div className="mp-message" role="status">
      <Icon name={asking ? "loader-circle" : "lock"} size={24} className={asking ? "mp-spin" : undefined} />
      <span className="mp-message__title">{title}</span>
      {!asking && (
        <Button variant="bare" className="mp-message__action" onClick={onAsk}>{entry === "refused" ? "Ask again" : "Ask to join"}</Button>
      )}
      {onBack && <Button variant="bare" className="mp-message__back" onClick={onBack}>Back</Button>}
    </div>
  );
}

/**
 * A guest who is not watching, and why: the link ended, nobody with an account is in the room, or the
 * room has as many guests as it takes. The last two can change, so they offer to try again.
 */
function GuestEnded({ reason }) {
  const said = reason === "full" ? { icon: "users", title: "This room is full" }
    : reason === "unattended" ? { icon: "door-closed", title: "Nobody is in this room right now" }
      : { icon: "link-2-off", title: "This invite has ended" };
  return (
    <Message icon={said.icon} title={said.title}
      action={reason === "ended" ? null : { label: "Try again", go: () => window.location.reload() }} />
  );
}

/** Who is asking to be let into the room, for anybody in it to answer. */
function Knocking({ knocks, onAnswer }) {
  return (
    <div className="mp-knocks" role="status">
      {knocks.map((knock) => (
        <div key={knock.userId} className="mp-knock">
          <Person person={knock} className="mp-avatar--small" />
          <span className="mp-knock__text">{knock.displayName} wants to join</span>
          <Button variant="bare" className="mp-knock__no" onClick={() => onAnswer(knock.userId, false)}>Not now</Button>
          <Button variant="bare" className="mp-knock__yes" onClick={() => onAnswer(knock.userId, true)}>Let in</Button>
        </div>
      ))}
    </div>
  );
}

function Message({ icon, title, text, action }) {
  return (
    <div className="mp-message">
      <Icon name={icon} size={24} />
      <span className="mp-message__title">{title}</span>
      {text && <span className="mp-message__text">{text}</span>}
      {action && <Button variant="bare" className="mp-message__action" onClick={action.go}>{action.label}</Button>}
    </div>
  );
}
