import React from "react";
import { EmptyState, Icon, OptionList, Rail, Toggle, toast } from "@thekrystalship/krystal-ui";
import { api, episodeCode, filmName } from "../api.js";
import { useLoad } from "../data.js";
import { setRemoteMode, chooseScreen, useDrivenScreen, usePairedScreens, useRemoteMode, useRemoteOpen } from "../remote.js";
import { roomCommands, useRemotePosition, useRemoteRoom } from "../remoteRoom.js";
import { stillSources, useRemoteFilm } from "../remoteFilm.js";
import { GestureHud, useStageGestures } from "../player/Gestures.jsx";
import { Menu, MenuButton, episodeValue } from "../player/Menus.jsx";
import { ScrubBar } from "../player/ScrubBar.jsx";
import { SkipLabels, useSkipOffer } from "../player/stretches.js";
import { ContinueCard, NextEpisodeCard } from "./Film.jsx";
import { Drawer } from "./Drawer.jsx";
import "../player/player.css";
import "./download.css";
import "./remote.css";

const SkipSeconds = 10;
/** How far up a push on the strip opens the remote, and how fast a flick that is shorter does. */
const PushPx = 36;
const FlickSpeed = 0.5; // px per ms
/** How far a sideways swipe on the strip turns remote mode off, as a fraction of its width, and how fast a flick does. */
const AwayFraction = 0.3;
const AwayFloor = 72;
const Slop = 8;
const SettleMs = 180;
const BuzzMs = 15;

/** A short vibration where the phone has one: a gesture has landed on the TV. */
function buzz() {
  try {
    if (typeof navigator.vibrate === "function") navigator.vibrate(BuzzMs);
  } catch {
    // A phone that refuses to vibrate still has the TV to show it.
  }
}

/** Runs a command on the room and says when the TV did not take it. */
async function run(command, then) {
  try {
    await command();
    if (then) then();
  } catch (error) {
    toast.fromError(error, "The TV did not take that");
  }
}

/** The picture standing for what the TV plays, trying each place the API keeps one in turn; `fallback` stands when it has none. */
function Art({ manifest, className, fallback = null }) {
  const sources = manifest ? stillSources(manifest) : [];
  const key = sources.join("|");
  const [at, setAt] = React.useState(0);
  React.useEffect(() => setAt(0), [key]);
  return sources[at]
    ? <img className={className} src={sources[at]} alt="" draggable={false} onError={() => setAt((i) => i + 1)} />
    : fallback;
}

/** The still under a finger. */
function Still({ manifest, children, ...rest }) {
  return (
    <div {...rest}>
      <Art manifest={manifest} fallback={<div className="kc-remote__bare" />} />
      {children}
    </div>
  );
}

/** What the strip and the drawer say of what the TV holds: an eyebrow and a name. */
function describe(room, film) {
  const { state } = room;
  const manifest = film.manifest;
  if (!state || (!state.titleId && !state.preparing)) return { eyebrow: null, name: "Nothing playing", playing: false };
  if (state.preparing && !state.titleId) return { eyebrow: "Getting it ready", name: state.preparing.name, playing: false };
  if (!manifest) return { eyebrow: null, name: "", playing: false };
  if (manifest.episode) {
    const current = film.around && film.around.current;
    return {
      eyebrow: [manifest.film && manifest.film.name, episodeCode(manifest.episode)].filter(Boolean).join(" · "),
      name: (current && current.name) || filmName(manifest),
      playing: true,
    };
  }
  const minutes = Math.round(manifest.durationSeconds / 60);
  const length = minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes}m`;
  return { eyebrow: [manifest.film && manifest.film.year, length].filter(Boolean).join(" · "), name: manifest.film && manifest.film.name ? manifest.film.name : manifest.title, playing: true };
}

/**
 * The phone's remote, while remote mode is on and a paired screen is connected: let down above the tab
 * bar as its head, and opened by tapping it or pushing it up. Everything it shows is the room the screen
 * is in, as the remote hears it (`remoteRoom.js`), and everything it does is intent sent to that room.
 */
export function RemoteDrawer() {
  const screen = useDrivenScreen();
  const [open, setOpen] = useRemoteOpen();
  if (!screen) return null;
  return open ? <Remote screen={screen} onCollapse={() => setOpen(false)} /> : <Strip screen={screen} onOpen={() => setOpen(true)} />;
}

/** The pull to open and the swipe away that the collapsed strip answers, with the tap that opens it left to its button. */
function useStripGestures(onOpen, onAway) {
  const el = React.useRef(null);
  const grab = React.useRef(null);
  const place = (dx, dy, animate) => {
    const node = el.current;
    if (!node) return;
    node.style.transition = animate ? "" : "none";
    node.style.transform = dx || dy ? `translate(${dx}px, ${dy}px)` : "";
    node.style.opacity = dx ? String(Math.max(0.4, 1 - Math.abs(dx) / (node.offsetWidth || 1) * 0.9)) : "";
  };
  const reduced = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  const down = (e) => {
    if (e.button > 0 || !e.isPrimary) return;
    grab.current = { id: e.pointerId, x: e.clientX, y: e.clientY, axis: null, samples: [{ t: e.timeStamp, x: e.clientX, y: e.clientY }] };
  };
  const move = (e) => {
    const g = grab.current;
    if (!g || g.id !== e.pointerId) return;
    const dx = e.clientX - g.x, dy = e.clientY - g.y;
    if (!g.axis) {
      if (Math.hypot(dx, dy) < Slop) return;
      g.axis = Math.abs(dx) > Math.abs(dy) ? "x" : dy < 0 ? "up" : "down";
      if (g.axis === "down") { grab.current = null; return; }
      e.currentTarget.setPointerCapture(e.pointerId);
      e.currentTarget.classList.add("is-held");
    }
    g.samples.push({ t: e.timeStamp, x: e.clientX, y: e.clientY });
    while (g.samples.length > 2 && e.timeStamp - g.samples[0].t > 100) g.samples.shift();
    if (g.axis === "x") place(dx, 0, false);
    else place(0, Math.max(dy, -PushPx), false);
  };
  const finish = (e, cancelled) => {
    const g = grab.current;
    if (!g || g.id !== e.pointerId) return;
    grab.current = null;
    if (!g.axis) return;
    e.currentTarget.classList.remove("is-held");
    // The press that was a drag is not a tap on the button it started on.
    const swallow = (ev) => { ev.stopPropagation(); ev.preventDefault(); };
    window.addEventListener("click", swallow, { capture: true, once: true });
    setTimeout(() => window.removeEventListener("click", swallow, { capture: true }), 0);

    const first = g.samples[0], last = g.samples[g.samples.length - 1];
    const span = Math.max(1, last.t - first.t);
    const width = el.current ? el.current.offsetWidth : 360;
    if (g.axis === "x") {
      const dx = e.clientX - g.x;
      const speed = (last.x - first.x) / span;
      const away = !cancelled && (Math.abs(dx) >= Math.max(AwayFloor, width * AwayFraction) || (Math.abs(speed) >= FlickSpeed && Math.abs(dx) >= 16));
      if (!away) { place(0, 0, true); return; }
      const sign = dx < 0 ? -1 : 1;
      if (reduced()) { onAway(); return; }
      place(sign * width, 0, true);
      if (el.current) el.current.style.opacity = "0";
      setTimeout(onAway, SettleMs);
      return;
    }
    const dy = e.clientY - g.y;
    const speed = (last.y - first.y) / span;
    const opens = !cancelled && (dy <= -PushPx || (speed <= -FlickSpeed && dy <= -12));
    place(0, 0, true);
    if (opens) onOpen();
  };
  return {
    ref: el,
    onPointerDown: down, onPointerMove: move,
    onPointerUp: (e) => finish(e, false),
    onPointerCancel: (e) => finish(e, true),
  };
}

/** Turns remote mode off, and says so with a way to turn it back on: nothing is unpaired. */
function turnOff(tv) {
  setRemoteMode(false);
  toast.info(`Remote off · ${tv}`, { action: { label: "Undo", onClick: () => setRemoteMode(true) } });
}

/** The remote let down above the tab bar: the still, the TV's name, what plays, and Pause. */
function Strip({ screen, onOpen }) {
  const room = useRemoteRoom();
  const film = useRemoteFilm();
  const shown = describe(room, film);
  const paused = Boolean(room.state && room.state.paused);
  const tv = screen.name || "TV";

  const gestures = useStripGestures(onOpen, () => turnOff(tv));
  const stripRef = React.useRef(null);

  // It stands on the tab bar wherever the bar's top is, measured rather than computed from the bar's height:
  // the bar's border, its safe-area inset and a phone's fractional pixels all move that edge.
  React.useLayoutEffect(() => {
    document.body.classList.add("has-remote-strip");
    const bar = document.querySelector(".tabbar");
    const place = () => {
      const node = stripRef.current;
      if (!node) return;
      const top = bar && getComputedStyle(bar).display !== "none" ? bar.getBoundingClientRect().top : document.documentElement.clientHeight;
      node.style.setProperty("--remote-strip-bottom", `${document.documentElement.clientHeight - top}px`);
      // What the strip takes above the tab bar is room the page and the assistant leave, as the bar's own is.
      // The inset the page and the dock leave for the bar is its nominal height (`--tabbar-h` and the safe
      // area); the bar itself is as tall as it measures, its border included, and the strip stands on it.
      const shell = document.querySelector(".app--tabbed");
      const nominal = bar && shell ? parseFloat(getComputedStyle(shell).getPropertyValue("--tabbar-h")) + parseFloat(getComputedStyle(bar).paddingBottom) : 0;
      const shortfall = bar && nominal ? Math.max(0, bar.getBoundingClientRect().height - nominal) : 0;
      document.body.style.setProperty("--remote-strip-inset", `${node.offsetHeight + shortfall}px`);
    };
    place();
    window.addEventListener("resize", place);
    const observer = typeof ResizeObserver === "function" ? new ResizeObserver(place) : null;
    if (observer) {
      if (bar) observer.observe(bar);
      observer.observe(stripRef.current);
    }
    return () => {
      document.body.classList.remove("has-remote-strip");
      document.body.style.removeProperty("--remote-strip-inset");
      window.removeEventListener("resize", place);
      if (observer) observer.disconnect();
    };
  }, []);

  return (
    <div className="kc-drawer kc-remote-strip" data-theme="dark" {...gestures} ref={(node) => { stripRef.current = node; gestures.ref.current = node; }}>
      <div className="kc-drawer__grabber" />
      <div className="kc-dl-head">
        <button type="button" className="kc-remote-strip__open" aria-label={`Open the remote: ${tv}`} onClick={onOpen}>
          <Art manifest={film.manifest} className="kc-dl-head__still"
            fallback={<span className="kc-dl-head__still kc-remote__nostill"><Icon name="tv" size={18} /></span>} />
          <span className="kc-dl-head__text">
            <span className="kc-dl-head__eyebrow"><Icon name="tv" size={12} />{tv}</span>
            <span className="kc-dl-head__name">{shown.name}</span>
          </span>
        </button>
        {shown.playing && (
          <button type="button" className="mp-play mp-play--small" aria-label={paused ? "Play" : "Pause"} disabled={!room.live}
            onClick={() => run(paused ? roomCommands.play : roomCommands.pause)}>
            <Icon name={paused ? "play" : "pause"} size={16} />
          </button>
        )}
      </div>
    </div>
  );
}

/** The TV to drive, among those paired: where each is, "Nothing playing", or "Off" for one that is not connected. */
function TvPicker({ screen, onDone }) {
  const screens = usePairedScreens() || [];
  const options = screens.map((s) => ({
    value: s.id,
    label: s.name || "TV",
    detail: !s.connected ? "Off" : s.title || "Nothing playing",
    disabled: !s.connected,
    aside: s.connected && s.titleId
      ? <img src={`/media/${encodeURIComponent(s.titleId)}/poster.jpg`} alt="" style={{ display: "block", width: 64, aspectRatio: "16 / 9", objectFit: "cover", borderRadius: "var(--r-sm)" }}
        onError={(e) => { e.currentTarget.style.display = "none"; }} />
      : null,
  }));
  return (
    <Drawer theme="dark" title="TVs" back={{ label: "Back to the remote", onClick: onDone }} onClose={onDone}>
      <div style={{ padding: "0 10px 6px" }}>
        <OptionList options={options} value={screen.id} label="TV to control" onChange={(id) => { chooseScreen(id); onDone(); }} />
      </div>
    </Drawer>
  );
}

/** Nothing is on the TV: the shelf of what to carry on with, each of which goes onto the TV when chosen. */
function Idle() {
  const titles = useLoad(() => api.titles(), []);
  const progress = useLoad(() => api.progress().catch(() => []), []);
  const byId = new Map((titles.data || []).filter((t) => t.status !== "failed").map((t) => [t.id, t]));
  const shows = useLoad(() => api.shows().catch(() => []), []);
  const showOf = new Map((shows.data || []).filter((s) => s.imdbId).map((s) => [s.imdbId, s]));
  const continuing = (progress.data || []).filter((p) => (p.titleId ? byId.has(p.titleId) : p.episode && p.showImdbId));
  return (
    <>
      <EmptyState icon="tv" title="Nothing playing" />
      {continuing.length > 0 && (
        <Rail
          variant="wide" icon="history" title="Continue watching" count={continuing.length}
          items={continuing} itemKey={(p) => p.titleId || `${p.showImdbId}:${p.episode.season}:${p.episode.number}`}
          renderItem={(p) => !p.titleId
            ? <NextEpisodeCard progress={p} show={showOf.get(p.showImdbId)} onTv />
            : <ContinueCard title={byId.get(p.titleId)} progress={p} remainingSeconds={p.remainingSeconds} onTv />} />
      )}
    </>
  );
}

/** The drawer, open: the TV and the switch, then what plays, with the player's scrub bar and controls at the bottom. */
function Remote({ screen, onCollapse }) {
  const room = useRemoteRoom();
  const film = useRemoteFilm();
  const position = useRemotePosition();
  const [mode] = useRemoteMode();
  const [picking, setPicking] = React.useState(false);
  const [menu, setMenu] = React.useState(false);
  const [stepping, setStepping] = React.useState(false);
  const [, setHeld] = React.useState(false);
  const tv = screen.name || "TV";
  const { state, live } = room;
  const { manifest, around, thumbnails } = film;
  const paused = Boolean(state && state.paused);
  const holding = Boolean(state && (state.titleId || state.preparing));
  const duration = manifest ? manifest.durationSeconds : 0;
  const shown = describe(room, film);
  const at = position ?? 0;

  const toggle = () => run(paused ? roomCommands.play : roomCommands.pause, buzz);
  const seek = (seconds) => run(() => roomCommands.seek(seconds), buzz);
  const gestures = useStageGestures({
    duration, position: at, skipSeconds: SkipSeconds, volume: 0, volumeSettable: false, brightness: 1, levels: false,
    seekable: live && Boolean(manifest),
    onTogglePlay: toggle, onToggleControls: () => {}, onSeek: seek, onHold: setHeld, onVolume: () => {}, onBrightness: () => {},
  });
  const skipOffer = useSkipOffer((seconds) => seek(seconds), {
    markers: manifest && manifest.markers, titleId: state && state.titleId, ready: Boolean(manifest), auto: false,
  }, at, false);

  // The film's own gestures: a touch is read in full, and a mouse press plays or pauses.
  const stageProps = {
    onPointerDown: (e) => {
      if (e.target.closest("button")) return;
      if (e.pointerType === "mouse") { if (live && manifest) toggle(); return; }
      if (manifest && live) gestures.onPointerDown(e);
    },
    onPointerMove: (e) => { if (e.pointerType !== "mouse") gestures.onPointerMove(e); },
    onPointerUp: (e) => { if (e.pointerType !== "mouse") gestures.onPointerUp(e); },
    onPointerCancel: (e) => { if (e.pointerType !== "mouse") gestures.onPointerCancel(e); },
  };

  const step = (command) => {
    setStepping(true);
    run(command).finally(() => window.setTimeout(() => setStepping(false), 800));
  };

  if (picking) return <TvPicker screen={screen} onDone={() => setPicking(false)} />;

  const swipe = gestures.hud && gestures.hud.kind === "seek" ? gestures.hud.at : null;
  const episodeView = manifest && { manifest, state, solo: false, viewOnly: false };
  const offersEpisodes = Boolean(manifest && manifest.episode && manifest.film && manifest.film.imdbId);
  const previous = around && around.previous;
  const next = around && around.next;
  const words = (e) => [episodeCode(e), e.name].filter(Boolean).join(" · ");

  return (
    <>
      <Drawer theme="dark" label={`Remote: ${tv}`} className="kc-remote" onClose={onCollapse}
        title={(
          <button type="button" className="kc-remote__tv" aria-label={`TV: ${tv}. Choose another`} onClick={() => setPicking(true)}>
            <Icon name="tv" size={13} />{tv}<Icon name="chevron-down" size={12} />
          </button>
        )}
        action={<span className="kc-remote__switch">Remote<Toggle on={mode} onChange={(on) => { if (on) setRemoteMode(true); else { onCollapse(); turnOff(tv); } }} label="Remote mode" /></span>}>
        {!holding && <Idle />}
        {holding && (
          <>
            <Still className="kc-remote__still" manifest={manifest} {...stageProps}>
              <GestureHud hud={gestures.hud} />
              {skipOffer && (
                <button type="button" className="mp-nextup mp-skip" onClick={skipOffer.skip}>
                  <span className="mp-nextup__name">{SkipLabels[skipOffer.marker.kind]}</span>
                  <Icon name="skip-forward" size={18} />
                </button>
              )}
            </Still>
            <span className="kc-dl-head__text kc-remote__film">
              {shown.eyebrow && <span className="kc-dl-head__eyebrow">{shown.eyebrow}</span>}
              <span className="kc-dl-head__name">{shown.name}</span>
            </span>
            {manifest && (
              <div className="mp-frame mp-frame--compact kc-remote__frame">
                <div className="mp-bottom">
                  <ScrubBar duration={duration} position={at} ready={state && state.transcodeHead != null ? state.transcodeHead : null}
                    buffered={0} thumbnails={thumbnails} chapters={manifest.chapters} onSeek={seek} onHold={setHeld} swipe={swipe} readOnly={!live} />
                  <div className="mp-controls">
                    <button type="button" className="mp-play" aria-label={paused ? "Play" : "Pause"} disabled={!live} onClick={toggle}>
                      <Icon name={paused ? "play" : "pause"} size={18} />
                    </button>
                    <button type="button" className="mp-icon" disabled={!live} aria-label={`Back ${SkipSeconds} seconds`}
                      onClick={() => run(() => roomCommands.skip(-SkipSeconds))}>
                      <Icon name="rotate-ccw" size={19} />
                    </button>
                    <button type="button" className="mp-icon" disabled={!live} aria-label={`Forward ${SkipSeconds} seconds`}
                      onClick={() => run(() => roomCommands.skip(SkipSeconds))}>
                      <Icon name="rotate-cw" size={19} />
                    </button>
                    {manifest.episode && (
                      <>
                        <button type="button" className="mp-icon" disabled={!live || !previous || stepping}
                          aria-label={previous ? `Previous episode: ${words(previous)}` : "No previous episode here"}
                          onClick={() => step(roomCommands.previous)}>
                          <Icon name="skip-back" size={19} />
                        </button>
                        <button type="button" className="mp-icon" disabled={!live || !next || stepping}
                          aria-label={next ? `Next episode: ${words(next)}` : "No next episode here"}
                          onClick={() => step(roomCommands.next)}>
                          <Icon name="skip-forward" size={19} />
                        </button>
                      </>
                    )}
                    <span className="mp-controls__gap" />
                    {offersEpisodes && <MenuButton kind="episodes" icon="list-video" value={episodeValue({ manifest })} open={menu} compact onClick={() => setMenu(true)} />}
                  </div>
                </div>
              </div>
            )}
          </>
        )}
      </Drawer>
      {menu && episodeView && (
        <Menu kind="episodes" view={episodeView} sheet contained={false} aimedAt={tv} onClose={() => setMenu(false)}
          room={{ playEpisode: (season, number) => run(() => roomCommands.playEpisode(season, number)) }} />
      )}
    </>
  );
}
