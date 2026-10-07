import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import { PlayerRoom } from "./engine/room.ts";
import { api } from "./engine/api.ts";
import { environment } from "./engine/environment.ts";
import { formatTime } from "./engine/format.ts";
import { prefs } from "./engine/prefs.ts";
import { amplitudeFor, volumeIsSettable } from "./engine/media/volume.ts";
import { bindShortcuts } from "./engine/media/shortcuts.ts";
import { Remote } from "./engine/media/remote.ts";
import { Rank, onBack as onTvBack, onMenu as onTvMenu } from "../tv/tv.js";
import { Cues } from "./Cues.jsx";
import { GestureHud, useStageGestures } from "./Gestures.jsx";
import { ScrubBar } from "./ScrubBar.jsx";
import { Volume } from "./Volume.jsx";
import { ago } from "../time.js";
import { Drawer } from "../components/Drawer.jsx";
import {
  Menu, MenuTitles, audioValue, episodeValue, hasEpisodes, hasQualityChoice, qualityValue, subtitleValue,
} from "./Menus.jsx";

const SkipSeconds = 10;
/** Controls stay this long after the last sign of somebody reaching for them. */
const IdleMs = 2800;
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

const Tints = ["teal", "info", "warning", "success"];

/** A person's mark: the first letter of their name on a colour that stays theirs. */
function tintFor(userId) {
  let hash = 0;
  for (const ch of String(userId)) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
  return Tints[hash % Tints.length];
}

export function Avatar({ person, className = "" }) {
  const initial = (person.displayName || "?").trim().charAt(0).toUpperCase() || "?";
  const waiting = person.buffering ? " is-buffering" : "";
  return (
    <span className={`mp-avatar mp-avatar--${tintFor(person.userId)}${waiting} ${className}`}
      title={person.buffering ? `${person.displayName} · buffering` : person.displayName}>{initial}</span>
  );
}

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
 * The artwork behind the frame, always from this server: the poster saved beside the title, else the
 * copy the API keeps from the film database for the film, or for the show an episode belongs to.
 */
function posterOf(manifest) {
  if (!manifest) return null;
  if (manifest.poster) return environment().mediaUrl(manifest.id, manifest.poster);
  const imdbId = manifest.film && manifest.film.imdbId;
  if (!imdbId) return null;
  return environment().apiUrl(`/${manifest.episode ? "shows" : "films"}/${encodeURIComponent(imdbId)}/poster.jpg`);
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
  const key = view.solo ? null : heldKey(view.state);
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

/**
 * The stretch at the end of an episode where its next is offered in front of everything: from the
 * last chapter when the last chapter is the credits, else the last two minutes.
 */
function inClosingStretch(manifest, position) {
  const duration = manifest.durationSeconds;
  const chapters = manifest.chapters || [];
  const last = chapters.length > 1 ? chapters[chapters.length - 1] : null;
  const from = last && last.startSeconds >= duration * 0.85 ? last.startSeconds : duration - 120;
  return duration > 0 && position >= from;
}

/** A playhead this close to a stretch's start got there by playing; further in, somebody seeked. */
const EnterSlackSeconds = 2;

const SkipLabels = { intro: "Skip intro", recap: "Skip recap" };

/**
 * The marker this viewer is offered to skip, or null. A stretch is offered only when playback
 * entered it by playing through, and once skipped it is not offered again. With the viewer's
 * preference on and nobody else in the room, entering a stretch skips it.
 */
function useSkipOffer(room, view, position, alone) {
  const markers = view.manifest && view.manifest.markers;
  const titleId = view.state && view.state.titleId;
  const auto = Boolean(view.preferences && view.preferences.skipIntros);
  const stretch = React.useRef({ titleId: null, index: -1, offered: false, handled: new Set() });
  const [offer, setOffer] = React.useState(-1);

  const skip = React.useCallback((index) => {
    const s = stretch.current;
    s.handled.add(index);
    s.offered = false;
    setOffer(-1);
    room.seek(markers[index].endSeconds);
  }, [room, markers]);

  React.useEffect(() => {
    if (stretch.current.titleId !== titleId) stretch.current = { titleId, index: -1, offered: false, handled: new Set() };
    const s = stretch.current;
    const index = view.ready && markers ? markers.findIndex((m) => position >= m.startSeconds && position < m.endSeconds) : -1;
    if (index !== s.index) {
      s.index = index;
      s.offered = index >= 0 && !s.handled.has(index) && position <= markers[index].startSeconds + EnterSlackSeconds;
      if (s.offered && auto && alone) { skip(index); return; }
    }
    setOffer(s.offered ? index : -1);
  }, [position, markers, titleId, view.ready, auto, alone, skip]);

  return offer >= 0 && markers && markers[offer] ? { marker: markers[offer], skip: () => skip(offer) } : null;
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
 * pop-out — is one row with an overflow menu. `embed` is the Activity: no way back to a site it
 * was never opened from, and a link to the room in place of the invite, since Discord does the
 * inviting.
 */
export function Player({ embed = false, online = true, onBack, onEmpty, onCarryOn }) {
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
      {room && view && <Stage room={room} view={view} size={size} embed={embed} online={online} frameRef={frameRef} videoRef={videoRef}
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

function Stage({ room, view, size, embed, online, frameRef, videoRef, onBack, onEmpty, onCarryOn }) {
  const video = videoRef.current;
  const manifest = view.manifest;
  const solo = view.solo;
  const lost = useRoomLost(view, online);
  const compact = size !== "wide";
  const [menu, setMenu] = React.useState(null);
  // The subtitle menu's style editor is open: a sample line stands in while the film says nothing.
  const [styling, setStyling] = React.useState(false);
  React.useEffect(() => { if (menu !== "subs") setStyling(false); }, [menu]);
  const [overflow, setOverflow] = React.useState(false);
  const [invite, setInvite] = React.useState(false);
  const [held, setHeld] = React.useState(0);
  const [active, setActive] = React.useState(true);
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
    idleTimer.current = window.setTimeout(() => setActive(false), IdleMs);
  }, []);
  React.useEffect(() => { wake(); return () => window.clearTimeout(idleTimer.current); }, [wake]);
  const hold = React.useCallback((on) => setHeld((n) => Math.max(0, n + (on ? 1 : -1))), []);
  const shown = active || held > 0 || menu !== null || overflow || invite || clock.paused || !view.ready;

  // A television is driven by its remote (`engine/media/remote.ts`): the volume is the set's own, the
  // screen is already whole, and nobody across the room is handed a link to copy.
  const tv = environment().tv;
  const playRef = React.useRef(null);
  const [remoteHud, setRemoteHud] = React.useState(null);
  const [remoteScrub, setRemoteScrub] = React.useState(null);
  const live = React.useRef(null);
  live.current = {
    shown, active, paused: clock.paused, position: clock.position,
    duration: manifest ? manifest.durationSeconds : 0, open: menu !== null || overflow || invite,
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

  const closeAll = React.useCallback(() => { setMenu(null); setOverflow(false); setInvite(false); }, []);
  const openMenu = (kind) => { setOverflow(false); setInvite(false); setMenu((m) => (m === kind ? null : kind)); };

  React.useEffect(() => tv ? undefined : bindShortcuts({
    togglePlay: () => { room.togglePlay(); wake(); },
    seekBy: (seconds) => { room.seekBy(seconds); wake(); },
    volumeBy: (step) => { nudgeVolume(step); wake(); },
    toggleFullscreen,
    toggleMute: () => { setVolume((v) => { prefs.setVolume(v.position, !v.muted); return { ...v, muted: !v.muted }; }); wake(); },
    toggleSubtitles: () => { room.toggleSubtitles(); wake(); },
  }), [tv, room, wake, nudgeVolume, toggleFullscreen]);

  // The remote, ahead of the page's navigator: it listens while the key is still on its way down.
  // Back closes what is open, then puts the controls away, then leaves; Menu is the subtitles.
  React.useEffect(() => {
    if (!tv) return undefined;
    const frame = frameRef.current;
    // A skip or a scrub brings the bar up with focus on it, so the next press of Left or Right goes on
    // moving the film however slowly it comes, and Down reaches the buttons.
    const toBar = () => {
      const bar = frame.querySelector(".mp-scrub");
      if (bar && document.activeElement !== bar) bar.focus({ focusVisible: true });
    };
    const remote = new Remote({
      shown: () => live.current.shown,
      reveal: () => {
        wake();
        const focused = document.activeElement;
        if (playRef.current && (!focused || focused === document.body || !frame.contains(focused))) {
          playRef.current.focus({ focusVisible: true });
        }
      },
      scrubFocused: () => Boolean(document.activeElement && document.activeElement.classList.contains("mp-scrub")),
      position: () => live.current.position,
      duration: () => live.current.duration,
      togglePlay: () => { room.togglePlay(); wake(); },
      play: () => { if (live.current.paused) room.togglePlay(); wake(); },
      pause: () => { if (!live.current.paused) room.togglePlay(); wake(); },
      skipBy: (seconds) => { room.seekBy(seconds); wake(); toBar(); },
      gathering: (seconds) => setRemoteHud(seconds === null ? null
        : { kind: "skip", side: seconds < 0 ? "left" : "right", seconds, key: Date.now() }),
      scrubbing: (at) => { setRemoteScrub(at); if (at !== null) { wake(); toBar(); } },
      seek: (seconds) => room.seek(seconds),
    });
    const typing = (target) => target instanceof HTMLElement
      && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));
    const down = (e) => {
      if (e.ctrlKey || e.metaKey || e.altKey || typing(e.target)) return;
      if (remote.keydown(e.key, e.repeat)) { e.preventDefault(); e.stopPropagation(); }
    };
    const up = (e) => {
      if (remote.keyup(e.key)) { e.preventDefault(); e.stopPropagation(); }
    };
    window.addEventListener("keydown", down, true);
    window.addEventListener("keyup", up, true);
    const offBack = onTvBack(() => {
      const now = live.current;
      if (now.open) { closeAll(); return true; }
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
      setMenu((m) => (m === "subs" ? null : "subs"));
      wake();
      return true;
    }, Rank.screen);
    return () => {
      remote.dispose();
      window.removeEventListener("keydown", down, true);
      window.removeEventListener("keyup", up, true);
      offBack();
      offMenu();
    };
  }, [tv, room, wake, closeAll, onBack, frameRef]);

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
    if (menu !== null || overflow || invite) { closeAll(); return; }
    if (e.pointerType === "mouse") { room.togglePlay(); wake(); return; }
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
  const skipOffer = useSkipOffer(room, view, clock.position, others.length === 0);
  const titleHeld = view.state &&(view.state.titleId ?? null) !== null;
  const duration = manifest ? manifest.durationSeconds : 0;
  const preparing = manifest && !view.ready && !view.unplayable && !view.gate && !view.fatal;
  const waiting = !view.fatal && !view.unplayable && !view.gate && !preparing
    && ((titleHeld && !manifest) || view.catchingUp || view.stalled);
  const sheet = compact;
  const swipe = remoteScrub ?? (gestures.hud && gestures.hud.kind === "seek" ? gestures.hud.at : null);

  const backdrop = solo ? view.artwork : posterOf(manifest);
  const frameStyle = { "--mp-poster": backdrop ? `url("${backdrop}")` : "none" };
  const carryOn = lost && view.saved && manifest && onCarryOn ? () => onCarryOn(manifest.id, clock.position) : null;

  return (
    <div className={"mp-stage" + (shown ? " is-shown" : " is-idle") + (view.ready ? " is-playing-film" : "")} style={frameStyle}
      onPointerDown={pressStage} onPointerMove={moveOnStage} onPointerUp={liftFromStage}
      onPointerCancel={(e) => { if (e.pointerType !== "mouse") gestures.onPointerCancel(e); }}>
      {brightness < 1 && <div className="mp-dim" style={{ opacity: 1 - brightness }} aria-hidden="true" />}
      <div className="mp-backdrop" aria-hidden="true" />
      <div className="mp-shade" aria-hidden="true" />

      {view.ready && <Cues track={view.subtitleTrack} raised={shown} lifted={styling && sheet} beside={styling && !sheet}
        style={view.preferences.subtitleStyle} sample={styling} />}

      {/* ── Top bar ── */}
      {manifest && (
        <div className="mp-top">
          {!embed && onBack && size !== "tiny" && (
            <button type="button" className="mp-round" aria-label="Back" title="Back" onClick={onBack}><Icon name="arrow-left" size={18} /></button>
          )}
          <div className="mp-top__title">
            <span className="mp-top__name">{filmName(manifest)}</span>
            {size !== "tiny" && <span className="mp-top__sub">{filmLine(manifest, around)}</span>}
          </div>
          {!solo && (
            <div className="mp-top__room">
              <div className="mp-avatars">{everyone.map((p) => <Avatar key={p.userId} person={p} />)}</div>
              {size === "wide" && <span className="mp-top__count">{everyone.length > 1 ? `${everyone.length} watching` : "Just you"}</span>}
              {size !== "tiny" && !tv && <InviteButton embed={embed} compact={compact} open={invite} manifest={manifest}
                onToggle={() => { setMenu(null); setOverflow(false); setInvite((o) => !o); }} />}
            </div>
          )}
        </div>
      )}

      {lost && (
        <div className="mp-lost" role="status">
          <Icon name="wifi-off" size={16} />
          <span className="mp-lost__text">The room can't be reached</span>
          {carryOn && <button type="button" className="mp-lost__go" onClick={carryOn}>Carry on from this device</button>}
        </div>
      )}

      {announcement && (
        <div key={announcement.key} className="mp-event" role="status">
          {announcement.actor && <Avatar person={announcement.actor} className="mp-avatar--small" />}
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
      {waiting && <div className="mp-spinner" role="status" aria-label="Waiting for the film"><Icon name="loader-circle" size={36} className="mp-spin" /></div>}
      {preparing && <Preparing manifest={manifest} head={view.transcodeHead} />}
      {view.gate && manifest && <JoinGate view={view} others={others} position={clock.position} focused={tv} solo={solo} onJoin={() => { takeScreen(); room.joinPlayback(); }} />}
      {view.unplayable && <Message icon="circle-alert" title="This cannot play here" text={view.unplayable} />}
      {view.fatal && <Message icon="circle-alert" title={view.fatal} action={!embed && onBack ? { label: "Back", go: onBack } : null} />}
      {/* What the room holds while it arrives. */}
      {!view.fatal && view.state && view.state.preparing && view.state.preparing.stage !== "failed" && (
        <Fetching preparing={view.state.preparing} onBack={!embed ? onBack : null} />
      )}
      {!view.fatal && view.state && view.state.preparing && view.state.preparing.stage === "failed" && (
        <Message icon="circle-alert" title={`${view.state.preparing.name} could not be fetched`}
          text={fetchProblem(view.state.preparing)}
          action={!embed && onBack ? { label: "Back", go: onBack } : null} />
      )}
      {!view.fatal && view.state && !titleHeld && !view.state.preparing && (
        <Message icon="clapperboard" title="Nothing playing yet"
          text={embed ? "Ask the bot for a film with /watch." : null}
          action={!embed && onEmpty ? { label: "Choose something to watch", go: onEmpty } : null} />
      )}

      {/* ── Bottom controls ── */}
      {manifest && view.ready && (size === "tiny"
        ? <TinyControls room={room} view={view} clock={clock} duration={duration} onHold={hold} swipe={swipe} onMore={() => { setMenu(null); setOverflow((o) => !o); }} />
        : (
          <div className="mp-bottom">
            <ScrubBar duration={duration} position={clock.position} ready={view.transcodeHead} buffered={clock.buffered}
              thumbnails={view.thumbnails} chapters={manifest.chapters} onSeek={(s) => room.seek(s)} onHold={hold} swipe={swipe} />
            <div className="mp-controls">
              <button ref={playRef} type="button" className="mp-play" aria-label={clock.paused ? "Play" : "Pause"} onClick={() => room.togglePlay()}>
                <Icon name={clock.paused ? "play" : "pause"} size={compact ? 18 : 20} />
              </button>
              <button type="button" className="mp-icon" aria-label={`Back ${SkipSeconds} seconds`} title={`Back ${SkipSeconds} seconds (j)`} onClick={() => room.seekBy(-SkipSeconds)}>
                <Icon name="rotate-ccw" size={19} />
              </button>
              <button type="button" className="mp-icon" aria-label={`Forward ${SkipSeconds} seconds`} title={`Forward ${SkipSeconds} seconds (l)`} onClick={() => room.seekBy(SkipSeconds)}>
                <Icon name="rotate-cw" size={19} />
              </button>
              {manifest.episode && (
                <>
                  <button type="button" className="mp-icon" disabled={!(around && around.previous)}
                    aria-label={around && around.previous ? `Previous episode: ${episodeWords(around.previous)}` : "No previous episode here"}
                    title={around && around.previous ? `Previous episode · ${episodeWords(around.previous)}` : "No previous episode here"}
                    onClick={() => room.previousEpisode()}>
                    <Icon name="skip-back" size={19} />
                  </button>
                  <button type="button" className="mp-icon" disabled={!next}
                    aria-label={next ? `Next episode: ${episodeWords(next)}` : "No next episode here"}
                    title={next ? `Next episode · ${episodeWords(next)}` : "No next episode here"}
                    onClick={goNext}>
                    <Icon name="skip-forward" size={19} />
                  </button>
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
              {canFullscreen && (
                <button type="button" className="mp-icon" aria-label={fullscreen ? "Exit full screen" : "Full screen"} title="Full screen (f)" onClick={toggleFullscreen}>
                  <Icon name={fullscreen ? "minimize" : "maximize"} size={19} />
                </button>
              )}
            </div>
          </div>
        ))}

      {manifest && view.ready && next && !(view.state && view.state.preparing) && size !== "tiny" && inClosingStretch(manifest, clock.position) && (
        <button type="button" className="mp-nextup" onClick={goNext}>
          <span className="mp-nextup__label">Next episode</span>
          <span className="mp-nextup__name">{episodeWords(next)}</span>
          <Icon name="skip-forward" size={18} />
        </button>
      )}

      {skipOffer && !(view.state && view.state.preparing) && size !== "tiny" && (
        <button type="button" className="mp-nextup mp-skip" onClick={skipOffer.skip}>
          <span className="mp-nextup__name">{SkipLabels[skipOffer.marker.kind]}</span>
          <Icon name="skip-forward" size={18} />
        </button>
      )}

      {menu !== null && manifest && <Menu kind={menu} view={view} room={room} sheet={sheet} position={clock.position}
        styling={styling} onStyling={setStyling} onClose={() => setMenu(null)} />}
      {overflow && manifest && (
        <Overflow view={view} volume={volume} onVolume={changeVolume} onNudge={nudgeVolume} embed={embed}
          onPick={(kind) => { setOverflow(false); setMenu(kind); }} onClose={() => setOverflow(false)} />
      )}
      {invite && !compact && <InvitePanel everyone={everyone} />}
    </div>
  );
}

function MenuButton({ kind, icon, value, open, compact, onClick }) {
  return (
    <button type="button" className={"mp-menu-button" + (open ? " is-open" : "") + (compact ? " is-icon" : "")}
      aria-haspopup="true" aria-expanded={open} aria-label={`${MenuTitles[kind]}: ${value}`} title={MenuTitles[kind]} onClick={onClick}>
      <Icon name={icon} size={18} />
      {!compact && <span className="mp-menu-button__value">{value}</span>}
    </button>
  );
}

/** The single row a popped-out frame has room for. */
function TinyControls({ room, view, clock, duration, onHold, swipe, onMore }) {
  return (
    <div className="mp-tiny">
      <button type="button" className="mp-play mp-play--small" aria-label={clock.paused ? "Play" : "Pause"} onClick={() => room.togglePlay()}>
        <Icon name={clock.paused ? "play" : "pause"} size={16} />
      </button>
      <ScrubBar thin duration={duration} position={clock.position} ready={view.transcodeHead} buffered={clock.buffered}
        thumbnails={null} onSeek={(s) => room.seek(s)} onHold={onHold} swipe={swipe} />
      <span className="mp-tiny__left">-{formatTime(Math.max(0, duration - clock.position))}</span>
      <button type="button" className="mp-icon" aria-label="More" onClick={onMore}><Icon name="ellipsis" size={18} /></button>
    </div>
  );
}

/** Everything a tiny frame folds away: the menus, the volume, and the way to hand the room on. */
function Overflow({ view, volume, onVolume, onNudge, onPick, onClose }) {
  const link = environment().roomUrl();
  const entries = [
    hasEpisodes(view) ? ["episodes", "list-video", episodeValue(view)] : null,
    ["subs", "captions", subtitleValue(view)],
    view.manifest.audio.length > 1 ? ["audio", "audio-lines", audioValue(view)] : null,
    hasQualityChoice(view) ? ["quality", "gauge", qualityValue(view)] : null,
  ].filter(Boolean);
  return (
    <Drawer contained role="menu" label="More" onClose={onClose} className="mp-menu">
      <div className="mp-menu__list">
        {entries.map(([kind, icon, value]) => (
          <button key={kind} type="button" className="mp-option" onClick={() => onPick(kind)}>
            <span className="mp-option__check"><Icon name={icon} size={16} /></span>
            <span className="mp-option__text"><span className="mp-option__label">{MenuTitles[kind]}</span></span>
            <span className="mp-option__value">{value}</span>
          </button>
        ))}
        <div className="mp-option mp-option--volume">
          <Volume position={volume.position} muted={volume.muted} onChange={onVolume} onNudge={onNudge} />
        </div>
        {link && !view.solo && <CopyLink link={link} className="mp-option" />}
      </div>
    </Drawer>
  );
}

function CopyLink({ link, className, label = "Copy room link" }) {
  const [copied, setCopied] = React.useState(false);
  React.useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 1600);
    return () => window.clearTimeout(timer);
  }, [copied]);
  return (
    <button type="button" className={className} onClick={() => {
      navigator.clipboard.writeText(link).then(() => setCopied(true), () => {});
    }}>
      <span className="mp-option__check"><Icon name={copied ? "check" : "link"} size={16} /></span>
      <span className="mp-option__text"><span className="mp-option__label">{copied ? "Copied" : label}</span></span>
    </button>
  );
}

/**
 * Handing the room to somebody else. On a phone that is the phone's own share sheet; in a wide
 * frame it is a panel with the link; in the Activity, where Discord does the inviting, it is the
 * link alone, copied.
 */
function InviteButton({ embed, compact, open, manifest, onToggle }) {
  const link = environment().roomUrl();
  const [copied, setCopied] = React.useState(false);
  React.useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 1600);
    return () => window.clearTimeout(timer);
  }, [copied]);
  if (!link) return null;

  const copy = () => navigator.clipboard.writeText(link).then(() => setCopied(true), () => {});
  if (embed) {
    return (
      <button type="button" className="mp-round mp-round--small" aria-label={copied ? "Copied" : "Copy room link"} title="Copy room link" onClick={copy}>
        <Icon name={copied ? "check" : "link"} size={15} />
      </button>
    );
  }
  const share = () => {
    if (compact && navigator.share) {
      navigator.share({ title: `Watch ${filmName(manifest)}`, url: link }).catch(() => {});
    } else if (compact) {
      void copy();
    } else {
      onToggle();
    }
  };
  return (
    <button type="button" className={"mp-invite" + (open ? " is-open" : "")} aria-expanded={!compact ? open : undefined} onClick={share}>
      <Icon name={copied ? "check" : "user-plus"} size={17} />{compact && copied ? "Copied" : "Invite"}
    </button>
  );
}

/**
 * The room's link, and everybody who can be asked in. An invite is a push to their devices, or a
 * Discord message where no push reaches them, and the row says which went out; it says nothing about
 * whether they saw it. Somebody already in the room is marked as watching.
 */
function InvitePanel({ everyone }) {
  const link = environment().roomUrl();
  const [copied, setCopied] = React.useState(false);
  const [people, setPeople] = React.useState(null);
  const [sent, setSent] = React.useState({});
  React.useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 1600);
    return () => window.clearTimeout(timer);
  }, [copied]);
  React.useEffect(() => {
    let live = true;
    api.people().then((list) => live && setPeople(list), () => live && setPeople([]));
    return () => { live = false; };
  }, []);

  const here = new Set(everyone.map((p) => p.userId));
  const invite = async (person) => {
    setSent((s) => ({ ...s, [person.userId]: "sending" }));
    try {
      const reply = await api.invite(environment().sessionId(), person.userId);
      setSent((s) => ({ ...s, [person.userId]: reply.delivery }));
    } catch {
      setSent((s) => ({ ...s, [person.userId]: "failed" }));
    }
  };
  const status = (person) => {
    if (here.has(person.userId)) return "In this room";
    const state = sent[person.userId];
    if (state === "push") return "Push sent · waiting for them";
    if (state === "bell") return "In their notifications · waiting for them";
    if (state === "failed") return "Could not be invited";
    return person.lastSeenAt ? `Active ${ago(person.lastSeenAt)}` : "Not seen on the site";
  };

  return (
    <div className="mp-invite-panel">
      <div className="mp-invite-panel__head">
        <span className="mp-invite-panel__title">Watch together</span>
        <span className="mp-invite-panel__text">Everyone in the room shares play, pause and position. Volume, audio, subtitles and quality stay your own.</span>
      </div>
      <div className="mp-invite-panel__link">
        <span className="mp-menu__section">Room link</span>
        <div className="mp-invite-panel__row">
          <span className="mp-invite-panel__url">{link}</span>
          <button type="button" className="mp-copy" onClick={() => navigator.clipboard.writeText(link).then(() => setCopied(true), () => {})}>
            <Icon name={copied ? "check" : "copy"} size={14} />{copied ? "Copied" : "Copy"}
          </button>
        </div>
      </div>
      {people && people.length > 0 && (
        <div className="mp-invite__people">
          <span className="mp-invite__label">Invite people</span>
          {people.map((person) => {
            const state = sent[person.userId];
            return (
              <div key={person.userId} className="mp-invite__person">
                <Avatar person={{ userId: person.userId, displayName: person.name }} className="mp-invite__avatar" />
                <div className="mp-invite__who">
                  <span className="mp-invite__name">{person.name}</span>
                  <span className="mp-invite__status">{status(person)}</span>
                </div>
                {here.has(person.userId)
                  ? <span className="mp-invite__here">watching</span>
                  : state === "push" || state === "bell"
                    ? <span className="mp-invite__sent">invited</span>
                    : <button type="button" className="mp-invite__send" disabled={state === "sending"} onClick={() => invite(person)}>Invite</button>}
              </div>
            );
          })}
        </div>
      )}
    </div>
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
      {others.length > 0 && <div className="mp-avatars mp-avatars--large">{others.map((p) => <Avatar key={p.userId} person={p} />)}</div>}
      <div className="mp-gate__text">
        {others.length > 0 && <span className="mp-gate__who">{namesOf(others)} {others.length === 1 ? "is" : "are"} watching</span>}
        <span className="mp-gate__name">{filmName(manifest)}</span>
        <span className="mp-gate__where">{formatTime(position)} of {formatTime(manifest.durationSeconds)}</span>
      </div>
      <div className="mp-gate__bar"><div style={{ width: `${manifest.durationSeconds > 0 ? Math.min(100, (position / manifest.durationSeconds) * 100) : 0}%` }} /></div>
      <button type="button" className="mp-gate__join" autoFocus={focused} onClick={onJoin}>
        <Icon name={solo ? "play" : "users"} size={18} />{solo ? "Play" : "Join"}
      </button>
      {view.identity && (
        <span className="mp-gate__me">Signed in as {view.identity.displayName}{brings.length > 0 ? ` · ${brings.join(", ")} from your settings` : ""}</span>
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
          <button type="button" className="mp-prepare__leave" onClick={onBack}>Keep downloading in background</button>
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

function Message({ icon, title, text, action }) {
  return (
    <div className="mp-message">
      <Icon name={icon} size={24} />
      <span className="mp-message__title">{title}</span>
      {text && <span className="mp-message__text">{text}</span>}
      {action && <button type="button" className="mp-message__action" onClick={action.go}>{action.label}</button>}
    </div>
  );
}
