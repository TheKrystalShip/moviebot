import React from "react";
import { Drawer, EmptyState, Icon, OptionList, Rail, Toggle, toast } from "@thekrystalship/krystal";
import { api, episodeCode, filmName } from "../api.js";
import { useLoad } from "../data.js";
import { setRemoteMode, chooseScreen, useDrivenScreen, usePairedScreens, useRemoteMode, useRemoteOpen } from "../remote.js";
import { useRoute } from "../router.js";
import { roomCommands, useRemotePosition, useRemoteRoom } from "../remoteRoom.js";
import { stillSources, useRemoteFilm } from "../remoteFilm.js";
import { GestureHud, useStageGestures } from "../player/Gestures.jsx";
import { Menu, MenuButton, audioValue, episodeValue, hasQualityChoice, qualityValue, subtitleValue } from "../player/Menus.jsx";
import { useTvRoom, useTvView } from "../remoteMenus.js";
import { ScrubBar } from "../player/ScrubBar.jsx";
import { SkipLabels, useSkipOffer } from "../player/stretches.js";
import { ContinueCard, NextEpisodeCard } from "./Film.jsx";
import "../player/player.css";
import "./download.css";
import "./remote.css";

const SkipSeconds = 10;
/**
 * How far a drag moves the dock to its other height, as a fraction of the way between its two heights
 * (never under the floor or over the cap), and how fast a flick that is shorter does.
 */
const ShiftFraction = 0.25;
const ShiftFloor = 36;
const ShiftCap = 100;
const FlickSpeed = 0.5; // px per ms, over the last 100 ms of the finger's travel
const FlickFloor = 12;
/** How much of a push past its open height the dock follows, and how far at most. */
const Overdrag = 0.2;
const OverdragCap = 24;
/** The open dock's top edge stands no higher than this share of the screen's height above its bottom edge. */
const OpenShare = 0.75;
/** How far a sideways swipe on the collapsed dock turns remote mode off, as a fraction of its width, and how fast a flick does. */
const AwayFraction = 0.3;
const AwayFloor = 72;
const Slop = 8;
const SettleMs = 220;
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

/** What the collapsed row and the open remote say of what the TV holds: an eyebrow and a name. */
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
 * The phone's remote, while remote mode is on and a paired screen is connected: one dock standing on the
 * tab bar, its bottom edge on the bar's top whatever its height. Collapsed it is one row (the still, the
 * TV's name, what plays, Pause); a finger moving its top edge up or down grows or shrinks it, and as it
 * grows the row gives way to the open remote. Everything it shows is the room the screen is in, as the
 * remote hears it (`remoteRoom.js`), and everything it does is intent sent to that room.
 */
export function RemoteDock() {
  const screen = useDrivenScreen();
  if (!screen) return null;
  return <Dock screen={screen} />;
}

/** Turns remote mode off, and says so with a way to turn it back on: nothing is unpaired. */
function turnOff(tv) {
  setRemoteMode(false);
  toast.info(`Remote off · ${tv}`, { action: { label: "Undo", onClick: () => setRemoteMode(true) } });
}

const reducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/** Takes the click that ends a drag, which is not a tap on whatever the finger started on. */
function swallowClick() {
  const swallow = (ev) => { ev.stopPropagation(); ev.preventDefault(); };
  window.addEventListener("click", swallow, { capture: true, once: true });
  setTimeout(() => window.removeEventListener("click", swallow, { capture: true }), 0);
}

/**
 * The dock's one height and what follows it. The height runs from the collapsed row's to the open remote's
 * natural height (capped by `OpenShare`), and `--dock-p` on the dock and its scrim is how far along that
 * is, 0 to 1, which the stylesheet turns into the row giving way to the remote and the scrim darkening.
 *
 * It is moved by a finger on the collapsed row, on the open remote's head, and on the open remote's body
 * while that is scrolled to its top and the finger moves down; let go, it settles at whichever height the
 * drag's distance or speed says. A sideways swipe on the collapsed row turns remote mode off.
 * `live` says the open remote is drawn: while open, while a finger grows the dock, and while it settles.
 */
function useDock(open, setOpen, onAway) {
  const panel = React.useRef(null);
  const scrim = React.useRef(null);
  const row = React.useRef(null);
  const remote = React.useRef(null);
  const head = React.useRef(null);
  const body = React.useRef(null);
  const content = React.useRef(null);
  const size = React.useRef({ shut: 0, open: 0, cap: 0 });
  const height = React.useRef(null);
  const grab = React.useRef(null);
  const timer = React.useRef(0);
  const openNow = React.useRef(open);
  openNow.current = open;
  const [live, setLive] = React.useState(open);

  // Until the open remote is drawn and measured, the dock opens toward the highest it may stand.
  const ceiling = () => (remote.current && size.current.open > size.current.shut ? size.current.open : size.current.cap);
  const progress = (h) => {
    const { shut } = size.current, full = ceiling();
    if (full <= shut) return h > shut ? 1 : 0;
    return Math.min(1, Math.max(0, (h - shut) / (full - shut)));
  };
  const paint = (h, animate) => {
    const node = panel.current;
    if (!node) return;
    height.current = h;
    const moving = animate && !reducedMotion();
    node.style.transition = moving ? `height ${SettleMs}ms var(--ease-out), --dock-p ${SettleMs}ms var(--ease-out)` : "none";
    node.style.height = `${h}px`;
    node.style.setProperty("--dock-p", String(progress(h)));
    if (scrim.current) {
      scrim.current.style.transition = moving ? `--dock-p ${SettleMs}ms var(--ease-out)` : "none";
      scrim.current.style.setProperty("--dock-p", String(progress(h)));
    }
  };
  /** The heights at rest, measured: the row's, and the open remote's head, content and foot. */
  const measure = () => {
    const node = panel.current;
    if (!node || !row.current) return;
    const edge = node.clientTop;
    const shut = row.current.offsetHeight + edge;
    let natural = shut;
    if (remote.current && head.current && content.current) {
      const foot = parseFloat(getComputedStyle(remote.current).paddingBottom) || 0;
      natural = edge + head.current.offsetHeight + content.current.offsetHeight + foot;
    }
    const bottom = parseFloat(document.body.style.getPropertyValue("--remote-strip-bottom")) || 0;
    const cap = Math.round(document.documentElement.clientHeight * OpenShare) - bottom;
    const full = Math.max(shut, Math.min(natural, cap));
    size.current = { shut, open: full, cap: Math.max(shut, cap) };
    node.style.setProperty("--dock-open-h", `${full - edge}px`);
  };
  /** Settles at rest open or collapsed, and lets the open remote go once collapsed. */
  const rest = (animate) => {
    clearTimeout(timer.current);
    paint(openNow.current ? size.current.open : size.current.shut, animate);
    if (!openNow.current) timer.current = setTimeout(() => { if (!openNow.current && !grab.current) setLive(false); }, animate ? SettleMs : 0);
  };
  React.useEffect(() => () => clearTimeout(timer.current), []);

  // It stands on the tab bar, and the bar stands on the viewport's bottom, so the dock's bottom is the bar's
  // measured height: the bar's border, its safe-area inset and a phone's fractional pixels all move its top
  // edge, and a phone's viewport is often a fractional number of CSS pixels, which `clientHeight` rounds. The
  // room the page, the assistant and the toasts leave above the bar is the collapsed row's, however far the
  // dock is open. The inset the page and the assistant dock leave for the bar is its nominal height
  // (`--tabbar-h` and the safe area); the bar itself is as tall as it measures, its border included, and the
  // dock stands on it.
  const placed = React.useRef(false);
  React.useLayoutEffect(() => {
    document.body.classList.add("has-remote-strip");
    const bar = document.querySelector(".k-tabbar");
    const place = () => {
      const standing = bar && getComputedStyle(bar).display !== "none" ? bar.getBoundingClientRect().height : 0;
      document.body.style.setProperty("--remote-strip-bottom", `${standing}px`);
      measure();
      const shell = document.querySelector(".k-app--tabbed");
      const nominal = bar && shell ? parseFloat(getComputedStyle(shell).getPropertyValue("--tabbar-h")) + parseFloat(getComputedStyle(bar).paddingBottom) : 0;
      const shortfall = bar && nominal ? Math.max(0, bar.getBoundingClientRect().height - nominal) : 0;
      document.body.style.setProperty("--remote-strip-inset", `${size.current.shut + shortfall}px`);
      if (!grab.current) rest(placed.current);
      placed.current = true;
    };
    place();
    window.addEventListener("resize", place);
    const observer = typeof ResizeObserver === "function" ? new ResizeObserver(place) : null;
    if (observer) for (const node of [bar, row.current, head.current, content.current]) if (node) observer.observe(node);
    return () => {
      window.removeEventListener("resize", place);
      if (observer) observer.disconnect();
    };
    // The dock's functions read refs alone, so the ones of any render are the same.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [live]);
  React.useEffect(() => () => {
    document.body.classList.remove("has-remote-strip");
    document.body.style.removeProperty("--remote-strip-inset");
    document.body.style.removeProperty("--remote-strip-bottom");
  }, []);

  // Opened or collapsed by a tap, a key, the scrim or the page: the open remote is drawn first, then the dock moves.
  React.useEffect(() => {
    document.body.classList.toggle("has-remote-open", open);
    if (open && !live) { setLive(true); return; }
    if (!grab.current) { measure(); rest(true); }
    // The dock's functions read refs alone, so the ones of any render are the same.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, live]);
  React.useEffect(() => () => document.body.classList.remove("has-remote-open"), []);

  const begin = (x, y, t) => ({ x, y, axis: null, from: height.current ?? size.current.shut, opened: openNow.current, samples: [{ t, x, y }] });
  const track = (g, x, y, t) => {
    g.samples.push({ t, x, y });
    while (g.samples.length > 2 && t - g.samples[0].t > 100) g.samples.shift();
  };
  const follow = (g, y) => {
    const { shut } = size.current, full = ceiling();
    let h = g.from - (y - g.y);
    if (h > full) h = full + Math.min(OverdragCap, (h - full) * Overdrag);
    paint(Math.max(shut, h), false);
  };
  const settle = (g, cancelled) => {
    const { shut } = size.current, full = ceiling();
    const first = g.samples[0], last = g.samples[g.samples.length - 1];
    const growth = (first.y - last.y) / Math.max(1, last.t - first.t);
    const moved = (height.current ?? g.from) - g.from;
    const far = Math.min(ShiftCap, Math.max(ShiftFloor, (full - shut) * ShiftFraction));
    let target = g.opened;
    if (!cancelled && g.opened && (moved <= -far || (growth <= -FlickSpeed && moved <= -FlickFloor))) target = false;
    if (!cancelled && !g.opened && (moved >= far || (growth >= FlickSpeed && moved >= FlickFloor))) target = true;
    const before = openNow.current;
    openNow.current = target;
    rest(true);
    if (target !== before) setOpen(target);
  };
  /** The collapsed dock followed sideways, faded as it goes. */
  const slide = (dx, animate) => {
    const node = panel.current;
    if (!node) return;
    node.style.transition = animate ? `transform 180ms var(--ease-out), opacity 180ms var(--ease-out)` : "none";
    node.style.transform = dx ? `translateX(${dx}px)` : "";
    node.style.opacity = dx ? String(Math.max(0.4, 1 - Math.abs(dx) / (node.offsetWidth || 1) * 0.9)) : "";
  };

  // A finger or a pointer on the collapsed row, or on the open remote's head.
  const down = (e) => {
    if (e.button > 0 || !e.isPrimary) return;
    if (openNow.current && !(head.current && head.current.contains(e.target))) return;
    grab.current = { id: e.pointerId, ...begin(e.clientX, e.clientY, e.timeStamp) };
    // The open remote is drawn while the finger is still inside the slop, so the drag's first frames move the dock rather than draw it.
    if (!openNow.current) { clearTimeout(timer.current); setLive(true); }
  };
  const move = (e) => {
    const g = grab.current;
    if (!g || g.id !== e.pointerId) return;
    const dx = e.clientX - g.x, dy = e.clientY - g.y;
    if (!g.axis) {
      if (Math.hypot(dx, dy) < Slop) return;
      g.axis = Math.abs(dx) > Math.abs(dy) ? "x" : "y";
      // Sideways is the collapsed row's alone, and a collapsed row has nowhere lower to go.
      if ((g.axis === "x" && g.opened) || (g.axis === "y" && !g.opened && dy > 0)) { grab.current = null; if (!g.opened) rest(true); return; }
      e.currentTarget.setPointerCapture(e.pointerId);
      e.currentTarget.classList.add("is-held");
    }
    track(g, e.clientX, e.clientY, e.timeStamp);
    if (g.axis === "x") slide(dx, false);
    else follow(g, e.clientY);
  };
  const finish = (e, cancelled) => {
    const g = grab.current;
    if (!g || g.id !== e.pointerId) return;
    grab.current = null;
    // A press that was no drag: a tap on the row opens the dock, and anything else leaves it collapsed.
    if (!g.axis) { if (!openNow.current) rest(true); return; }
    e.currentTarget.classList.remove("is-held");
    swallowClick();
    if (g.axis === "y") { settle(g, cancelled); return; }
    const dx = e.clientX - g.x;
    const first = g.samples[0], last = g.samples[g.samples.length - 1];
    const speed = (last.x - first.x) / Math.max(1, last.t - first.t);
    const width = panel.current ? panel.current.offsetWidth : 360;
    const away = !cancelled && (Math.abs(dx) >= Math.max(AwayFloor, width * AwayFraction) || (Math.abs(speed) >= FlickSpeed && Math.abs(dx) >= 16));
    if (!away) { slide(0, true); return; }
    if (reducedMotion()) { onAway(); return; }
    slide((dx < 0 ? -1 : 1) * width, true);
    if (panel.current) panel.current.style.opacity = "0";
    setTimeout(onAway, 180);
  };

  // A finger pulling the open remote's body down from its top: touch events, since the body scrolls
  // natively until the dock takes the touch, and taking it means cancelling the first move.
  React.useEffect(() => {
    const node = body.current;
    if (!node) return;
    let g = null;
    const start = (e) => {
      g = null;
      if (e.touches.length !== 1 || !openNow.current || grab.current) return;
      // The scrub bar reads a finger of its own; the still's answers no vertical sweep, so it takes the pull.
      if (e.target.closest(".mp-scrub")) return;
      const t = e.touches[0];
      g = { ...begin(t.clientX, t.clientY, e.timeStamp), taken: null };
    };
    const moved = (e) => {
      if (!g) return;
      const t = e.touches[0];
      const dx = t.clientX - g.x, dy = t.clientY - g.y;
      if (g.taken === null) {
        if (Math.abs(dx) + Math.abs(dy) < 3) return;
        g.taken = node.scrollTop <= 0 && dy > 0 && dy >= Math.abs(dx);
        if (g.taken) {
          grab.current = g;
          panel.current?.classList.add("is-held");
        }
      }
      if (!g.taken) return;
      if (e.cancelable) e.preventDefault();
      track(g, t.clientX, t.clientY, e.timeStamp);
      follow(g, t.clientY);
    };
    const end = (cancelled) => () => {
      const was = g;
      g = null;
      if (!was || !was.taken) return;
      grab.current = null;
      panel.current?.classList.remove("is-held");
      swallowClick();
      settle(was, cancelled);
    };
    const ended = end(false), cancelled = end(true);
    node.addEventListener("touchstart", start, { passive: true });
    node.addEventListener("touchmove", moved, { passive: false });
    node.addEventListener("touchend", ended);
    node.addEventListener("touchcancel", cancelled);
    return () => {
      node.removeEventListener("touchstart", start);
      node.removeEventListener("touchmove", moved);
      node.removeEventListener("touchend", ended);
      node.removeEventListener("touchcancel", cancelled);
    };
    // The dock's functions read refs alone, so the ones of any render are the same.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [live]);

  return {
    live, panel, scrim, row, remote, head, body, content,
    handlers: { onPointerDown: down, onPointerMove: move, onPointerUp: (e) => finish(e, false), onPointerCancel: (e) => finish(e, true) },
  };
}

/** The dock and its scrim: the collapsed row and, while drawn, the open remote over it. */
function Dock({ screen }) {
  const [open, setOpen] = useRemoteOpen();
  const [picking, setPicking] = React.useState(false);
  const tv = screen.name || "TV";
  const dock = useDock(open, setOpen, () => turnOff(tv));
  const shelf = useShelf(open);
  const collapse = React.useCallback(() => setOpen(false), [setOpen]);

  React.useEffect(() => {
    if (!open) return undefined;
    const escape = (e) => { if (e.key === "Escape" && !picking) collapse(); };
    document.addEventListener("keydown", escape);
    return () => document.removeEventListener("keydown", escape);
  }, [open, picking, collapse]);
  // A tab or a link leaves the page the dock was opened over, and the dock collapses onto the next.
  const route = useRoute();
  const shown = React.useRef(route);
  React.useEffect(() => {
    if (shown.current !== route) { shown.current = route; collapse(); }
  }, [route, collapse]);

  return (
    <>
      <div ref={dock.scrim} className="kc-remote-scrim" onClick={collapse} />
      <div ref={dock.panel} className={"kc-remote-dock" + (open ? " is-open" : "")} data-theme="dark" {...dock.handlers}>
        <div className="kc-remote__grab" />
        <Strip rowRef={dock.row} screen={screen} hidden={open} onOpen={() => setOpen(true)} />
        {dock.live && <Remote dock={dock} shelf={shelf} screen={screen} hidden={!open} onCollapse={collapse} onPick={() => setPicking(true)} />}
      </div>
      {picking && <TvPicker screen={screen} onDone={() => setPicking(false)} />}
    </>
  );
}

/** The collapsed dock's row: the still, the TV's name, what plays, and Pause. */
function Strip({ rowRef, screen, hidden, onOpen }) {
  const room = useRemoteRoom();
  const film = useRemoteFilm();
  const shown = describe(room, film);
  const paused = Boolean(room.state && room.state.paused);
  const tv = screen.name || "TV";
  return (
    <div ref={rowRef} className="kc-remote-strip" aria-hidden={hidden || undefined} inert={hidden ? "" : undefined}>
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

/**
 * What the idle remote's shelf is drawn from, read while the dock stands and again each time it collapses:
 * the open remote finds it answered, so its height is settled before a finger moves it.
 */
function useShelf(open) {
  const [round, setRound] = React.useState(0);
  React.useEffect(() => { if (!open) setRound((r) => r + 1); }, [open]);
  const titles = useLoad(() => api.titles(), [round]);
  const progress = useLoad(() => api.progress().catch(() => []), [round]);
  const shows = useLoad(() => api.shows().catch(() => []), [round]);
  return { titles, progress, shows };
}

/** Nothing is on the TV: the shelf of what to carry on with, each of which goes onto the TV when chosen. */
function Idle({ shelf }) {
  const { titles, progress, shows } = shelf;
  const byId = new Map((titles.data || []).filter((t) => t.status !== "failed").map((t) => [t.id, t]));
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

/** The open remote: the TV and the switch, then what plays, with the player's scrub bar and controls at the bottom. */
function Remote({ dock, shelf, screen, hidden, onCollapse, onPick }) {
  const room = useRemoteRoom();
  const film = useRemoteFilm();
  const position = useRemotePosition();
  const [mode] = useRemoteMode();
  // Which of the player's menus is open, aimed at the TV: episodes, subs, audio or quality.
  const [menu, setMenu] = React.useState(null);
  // The subtitle menu's style editor is open.
  const [styling, setStyling] = React.useState(false);
  React.useEffect(() => { if (menu !== "subs") setStyling(false); }, [menu]);
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
  const tvView = useTvView();
  const tvRoom = useTvRoom(manifest);

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

  const swipe = gestures.hud && gestures.hud.kind === "seek" ? gestures.hud.at : null;
  const episodeView = manifest && { manifest, state, solo: false, viewOnly: false };
  const offersEpisodes = Boolean(manifest && manifest.episode && manifest.film && manifest.film.imdbId);
  const previous = around && around.previous;
  const next = around && around.next;
  const words = (e) => [episodeCode(e), e.name].filter(Boolean).join(" · ");

  return (
    <>
      <div ref={dock.remote} className="kc-remote" role="dialog" aria-label={`Remote: ${tv}`} aria-hidden={hidden || undefined} inert={hidden ? "" : undefined}>
        <div ref={dock.head} className="kc-remote__top">
          <div className="kc-remote__head">
            <span className="kc-remote__title">
              <button type="button" className="kc-remote__tv" aria-label={`TV: ${tv}. Choose another`} onClick={onPick}>
                <Icon name="tv" size={13} />{tv}<Icon name="chevron-down" size={12} />
              </button>
            </span>
            <span className="kc-remote__action">
              <span className="kc-remote__switch">Remote<Toggle on={mode} onChange={(on) => { if (on) setRemoteMode(true); else { onCollapse(); turnOff(tv); } }} label="Remote mode" /></span>
            </span>
          </div>
        </div>
        <div ref={dock.body} className="kc-remote__body">
          <div ref={dock.content} className="kc-remote__content">
            {!holding && <Idle shelf={shelf} />}
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
                      <div className="kc-remote__rows">
                        <div className="mp-controls kc-remote__row">
                          <button type="button" className="mp-icon" disabled={!live} aria-label={`Back ${SkipSeconds} seconds`}
                            onClick={() => run(() => roomCommands.skip(-SkipSeconds))}>
                            <Icon name="rotate-ccw" size={19} />
                          </button>
                          {manifest.episode && (
                            <button type="button" className="mp-icon" disabled={!live || !previous || stepping}
                              aria-label={previous ? `Previous episode: ${words(previous)}` : "No previous episode here"}
                              onClick={() => step(roomCommands.previous)}>
                              <Icon name="skip-back" size={19} />
                            </button>
                          )}
                          <button type="button" className="mp-play" aria-label={paused ? "Play" : "Pause"} disabled={!live} onClick={toggle}>
                            <Icon name={paused ? "play" : "pause"} size={18} />
                          </button>
                          {manifest.episode && (
                            <button type="button" className="mp-icon" disabled={!live || !next || stepping}
                              aria-label={next ? `Next episode: ${words(next)}` : "No next episode here"}
                              onClick={() => step(roomCommands.next)}>
                              <Icon name="skip-forward" size={19} />
                            </button>
                          )}
                          <button type="button" className="mp-icon" disabled={!live} aria-label={`Forward ${SkipSeconds} seconds`}
                            onClick={() => run(() => roomCommands.skip(SkipSeconds))}>
                            <Icon name="rotate-cw" size={19} />
                          </button>
                        </div>
                        <div className="mp-controls kc-remote__row">
                          {offersEpisodes && <MenuButton kind="episodes" icon="list-video" value={episodeValue({ manifest })} open={menu === "episodes"} compact onClick={() => setMenu("episodes")} />}
                          {tvView && <MenuButton kind="subs" icon="captions" value={subtitleValue(tvView)} open={menu === "subs"} compact onClick={() => setMenu("subs")} />}
                          {tvView && manifest.audio.length > 1 && <MenuButton kind="audio" icon="audio-lines" value={audioValue(tvView)} open={menu === "audio"} compact onClick={() => setMenu("audio")} />}
                          {tvView && hasQualityChoice(tvView) && <MenuButton kind="quality" icon="gauge" value={qualityValue(tvView)} open={menu === "quality"} compact onClick={() => setMenu("quality")} />}
                        </div>
                      </div>
                    </div>
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      </div>
      {menu === "episodes" && episodeView && (
        <Menu kind="episodes" view={episodeView} sheet contained={false} aimedAt={tv} onClose={() => setMenu(null)}
          room={{ playEpisode: (season, number) => run(() => roomCommands.playEpisode(season, number)) }} />
      )}
      {(menu === "subs" || menu === "audio" || menu === "quality") && tvView && (
        <Menu kind={menu} view={tvView} room={tvRoom} sheet contained={false} aimedAt={tv}
          styling={styling} onStyling={setStyling} onClose={() => setMenu(null)} />
      )}
    </>
  );
}
