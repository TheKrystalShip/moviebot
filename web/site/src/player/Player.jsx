import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import { PlayerRoom } from "./engine/room.ts";
import { environment } from "./engine/environment.ts";
import { formatTime } from "./engine/format.ts";
import { prefs } from "./engine/prefs.ts";
import { amplitudeFor } from "./engine/media/volume.ts";
import { bindShortcuts } from "./engine/media/shortcuts.ts";
import { Cues } from "./Cues.jsx";
import { ScrubBar } from "./ScrubBar.jsx";
import { Volume } from "./Volume.jsx";
import {
  Menu, MenuTitles, audioValue, hasQualityChoice, qualityValue, subtitleValue,
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
      title={person.buffering ? `${person.displayName} · waiting for the film` : person.displayName}>{initial}</span>
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

/** The film's year and length, which is what the line under its name says. */
function filmLine(manifest) {
  const minutes = Math.round(manifest.durationSeconds / 60);
  const length = minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes}m`;
  return [manifest.film && manifest.film.year, length].filter(Boolean).join(" · ");
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
export function Player({ embed = false, onBack, onEmpty }) {
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
      {room && view && <Stage room={room} view={view} size={size} embed={embed} frameRef={frameRef} videoRef={videoRef} onBack={onBack} onEmpty={onEmpty} />}
    </div>
  );
}

function Stage({ room, view, size, embed, frameRef, videoRef, onBack, onEmpty }) {
  const video = videoRef.current;
  const manifest = view.manifest;
  const compact = size !== "wide";
  const [menu, setMenu] = React.useState(null);
  const [overflow, setOverflow] = React.useState(false);
  const [invite, setInvite] = React.useState(false);
  const [held, setHeld] = React.useState(0);
  const [active, setActive] = React.useState(true);
  const [announcement, setAnnouncement] = React.useState(null);
  const [clock, setClock] = React.useState({ position: 0, buffered: 0, paused: true });
  const [volume, setVolume] = React.useState(() => ({ position: prefs.volume(), muted: prefs.muted() }));
  const [fullscreen, setFullscreen] = React.useState(false);
  const idleTimer = React.useRef(null);

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

  // Somebody else's act on the room, said over the film with their mark beside it.
  React.useEffect(() => room.onAnnounce((said) => {
    setAnnouncement({ ...said, key: Date.now() });
  }), [room]);
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

  const canFullscreen = typeof document !== "undefined" && document.fullscreenEnabled;
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

  React.useEffect(() => bindShortcuts({
    togglePlay: () => { room.togglePlay(); wake(); },
    seekBy: (seconds) => { room.seekBy(seconds); wake(); },
    volumeBy: (step) => { nudgeVolume(step); wake(); },
    toggleFullscreen,
    toggleMute: () => { setVolume((v) => { prefs.setVolume(v.position, !v.muted); return { ...v, muted: !v.muted }; }); wake(); },
    toggleSubtitles: () => { room.toggleSubtitles(); wake(); },
  }), [room, wake, nudgeVolume, toggleFullscreen]);

  React.useEffect(() => {
    const escape = (e) => { if (e.key === "Escape") closeAll(); };
    document.addEventListener("keydown", escape);
    return () => document.removeEventListener("keydown", escape);
  }, [closeAll]);

  // A mouse click on the film plays or pauses it, or closes whatever was open over it. A touch
  // shows the controls, or hides them when they are up: a thumb reaching for the screen is
  // reaching for the controls, and pausing everybody's film is not what it meant.
  const pressStage = (e) => {
    if (e.target !== e.currentTarget) return;
    if (menu !== null || overflow || invite) { closeAll(); return; }
    if (e.pointerType === "mouse") { room.togglePlay(); wake(); return; }
    if (active) setActive(false);
    else wake();
  };

  const everyone = people(view);
  const others = view.identity ? everyone.filter((p) => p.userId !== view.identity.userId) : everyone;
  const titleHeld = view.state && (view.state.titleId ?? null) !== null;
  const duration = manifest ? manifest.durationSeconds : 0;
  const preparing = manifest && !view.ready && !view.unplayable && !view.gate && !view.fatal;
  const waiting = !view.fatal && !view.unplayable && !view.gate && !preparing
    && ((titleHeld && !manifest) || view.catchingUp || view.stalled);
  const sheet = compact;

  const frameStyle = { "--mp-poster": manifest && manifest.film && manifest.film.posterUrl ? `url("${manifest.film.posterUrl}")` : "none" };

  return (
    <div className={"mp-stage" + (shown ? " is-shown" : " is-idle") + (view.ready ? " is-playing-film" : "")} style={frameStyle}
      onPointerMove={(e) => { if (e.pointerType === "mouse") wake(); }} onPointerDown={pressStage}>
      <div className="mp-backdrop" aria-hidden="true" />
      <div className="mp-shade" aria-hidden="true" />

      {view.ready && <Cues track={view.subtitleTrack} raised={shown} />}

      {/* ── Top bar ── */}
      {manifest && (
        <div className="mp-top">
          {!embed && onBack && size !== "tiny" && (
            <button type="button" className="mp-round" aria-label="Back" title="Back" onClick={onBack}><Icon name="arrow-left" size={18} /></button>
          )}
          <div className="mp-top__title">
            <span className="mp-top__name">{filmName(manifest)}</span>
            {size !== "tiny" && <span className="mp-top__sub">{filmLine(manifest)}</span>}
          </div>
          <div className="mp-top__room">
            <div className="mp-avatars">{everyone.map((p) => <Avatar key={p.userId} person={p} />)}</div>
            {size === "wide" && <span className="mp-top__count">{everyone.length > 1 ? `${everyone.length} watching` : "Just you"}</span>}
            {size !== "tiny" && <InviteButton embed={embed} compact={compact} open={invite} manifest={manifest}
              onToggle={() => { setMenu(null); setOverflow(false); setInvite((o) => !o); }} />}
          </div>
        </div>
      )}

      {announcement && (
        <div key={announcement.key} className="mp-event" role="status">
          {announcement.actor && <Avatar person={announcement.actor} className="mp-avatar--small" />}
          {announcement.text}
        </div>
      )}
      {view.notices.length > 0 && (
        <div className="mp-notices">
          {view.notices.map((n) => <div key={n.id} className={"mp-notice mp-notice--" + n.kind}>{n.text}</div>)}
        </div>
      )}

      {/* ── What stands in the middle of the frame ── */}
      {waiting && <div className="mp-spinner" role="status" aria-label="Waiting for the film"><Icon name="loader-circle" size={36} className="mp-spin" /></div>}
      {preparing && <Preparing manifest={manifest} head={view.transcodeHead} />}
      {view.gate && manifest && <JoinGate view={view} others={others} position={clock.position} onJoin={() => room.joinPlayback()} />}
      {view.unplayable && <Message icon="circle-alert" title="This film cannot play here" text={view.unplayable} />}
      {view.fatal && <Message icon="circle-alert" title={view.fatal} action={!embed && onBack ? { label: "Back", go: onBack } : null} />}
      {!view.fatal && view.state && !titleHeld && (
        <Message icon="clapperboard" title="Nothing playing yet"
          text={embed ? "Ask the bot for a film with /watch." : null}
          action={!embed && onEmpty ? { label: "Choose a film", go: onEmpty } : null} />
      )}

      {/* ── Bottom controls ── */}
      {manifest && view.ready && (size === "tiny"
        ? <TinyControls room={room} view={view} clock={clock} duration={duration} onHold={hold} onMore={() => { setMenu(null); setOverflow((o) => !o); }} />
        : (
          <div className="mp-bottom">
            <ScrubBar duration={duration} position={clock.position} ready={view.transcodeHead} buffered={clock.buffered}
              thumbnails={view.thumbnails} onSeek={(s) => room.seek(s)} onHold={hold} />
            <div className="mp-controls">
              <button type="button" className="mp-play" aria-label={clock.paused ? "Play" : "Pause"} onClick={() => room.togglePlay()}>
                <Icon name={clock.paused ? "play" : "pause"} size={compact ? 18 : 20} />
              </button>
              <button type="button" className="mp-icon" aria-label={`Back ${SkipSeconds} seconds`} title={`Back ${SkipSeconds} seconds (j)`} onClick={() => room.seekBy(-SkipSeconds)}>
                <Icon name="rotate-ccw" size={19} />
              </button>
              <button type="button" className="mp-icon" aria-label={`Forward ${SkipSeconds} seconds`} title={`Forward ${SkipSeconds} seconds (l)`} onClick={() => room.seekBy(SkipSeconds)}>
                <Icon name="rotate-cw" size={19} />
              </button>
              <Volume position={volume.position} muted={volume.muted} onChange={changeVolume} onNudge={nudgeVolume} />
              <span className="mp-controls__gap" />
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

      {menu !== null && manifest && <Menu kind={menu} view={view} room={room} sheet={sheet} onClose={() => setMenu(null)} />}
      {overflow && manifest && (
        <Overflow view={view} volume={volume} onVolume={changeVolume} onNudge={nudgeVolume} embed={embed}
          onPick={(kind) => { setOverflow(false); setMenu(kind); }} onClose={() => setOverflow(false)} />
      )}
      {invite && !compact && <InvitePanel />}
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
function TinyControls({ room, view, clock, duration, onHold, onMore }) {
  return (
    <div className="mp-tiny">
      <button type="button" className="mp-play mp-play--small" aria-label={clock.paused ? "Play" : "Pause"} onClick={() => room.togglePlay()}>
        <Icon name={clock.paused ? "play" : "pause"} size={16} />
      </button>
      <ScrubBar thin duration={duration} position={clock.position} ready={view.transcodeHead} buffered={clock.buffered}
        thumbnails={null} onSeek={(s) => room.seek(s)} onHold={onHold} />
      <span className="mp-tiny__left">-{formatTime(Math.max(0, duration - clock.position))}</span>
      <button type="button" className="mp-icon" aria-label="More" onClick={onMore}><Icon name="ellipsis" size={18} /></button>
    </div>
  );
}

/** Everything a tiny frame folds away: the menus, the volume, and the way to hand the room on. */
function Overflow({ view, volume, onVolume, onNudge, onPick, onClose }) {
  const link = environment().roomUrl();
  const entries = [
    ["subs", "captions", subtitleValue(view)],
    view.manifest.audio.length > 1 ? ["audio", "audio-lines", audioValue(view)] : null,
    hasQualityChoice(view) ? ["quality", "gauge", qualityValue(view)] : null,
  ].filter(Boolean);
  return (
    <>
      <div className="mp-sheet-scrim" onClick={onClose} />
      <div className="mp-sheet mp-menu" role="menu">
        <div className="mp-sheet__grabber" />
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
          {link && <CopyLink link={link} className="mp-option" />}
        </div>
      </div>
    </>
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

function InvitePanel() {
  const link = environment().roomUrl();
  const [copied, setCopied] = React.useState(false);
  React.useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 1600);
    return () => window.clearTimeout(timer);
  }, [copied]);
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
    </div>
  );
}

/**
 * The browser would not start the film without a press. Who is watching and where they are is
 * what makes pressing it worth it, and the line underneath says what this viewer brings with them.
 */
function JoinGate({ view, others, position, onJoin }) {
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
      <button type="button" className="mp-gate__join" onClick={onJoin}><Icon name="users" size={18} />Join</button>
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
    { label: "Preparing the film", sub: transcoding ? `${Math.floor(written * 100)}% · transcoding live` : "ready", done: true },
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
