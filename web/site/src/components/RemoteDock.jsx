import React from "react";
import { Button, Drawer, EmptyState, Icon, OptionList, Rail, Toggle, toast, useDockDrag } from "@thekrystalship/krystal";
import { WIDE } from "../railDensity.js";
import { episodeCode, filmName, libraryApi, meApi } from "../api/index.js";
import { useLoad } from "../data.js";
import { setRemoteMode, chooseScreen, useDrivenScreen, usePairedScreens, useRemoteMode, useRemoteOpen } from "../remote.js";
import { useRoute } from "../router.js";
import { roomCommands, useRemotePosition, useRemoteRoom } from "../remoteRoom.js";
import { stillSources, useRemoteFilm } from "../remoteFilm.js";
import { GestureHud, useStageGestures } from "../player/Gestures.jsx";
import { MenuButton, PlayerMenu, audioValue, episodeValue, hasQualityChoice, qualityValue, subtitleValue } from "../player/Menus.jsx";
import { useTvRoom, useTvView } from "../remoteMenus.js";
import { ScrubBar } from "../player/ScrubBar.jsx";
import { SkipLabels, useSkipOffer } from "../player/stretches.js";
import { ContinueCard, NextEpisodeCard } from "./Film.jsx";
import "../player/player.css";
import "./download.css";
import "./remote.css";

const SkipSeconds = 10;
/** The open dock's top edge stands no higher than this share of the screen's height above its bottom edge. */
const OpenShare = 0.75;
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

/** The dock and its scrim: the collapsed row and, while drawn, the open remote over it. */
function Dock({ screen }) {
  const [open, setOpen] = useRemoteOpen();
  const [picking, setPicking] = React.useState(false);
  const tv = screen.name || "TV";
  const row = React.useRef(null);
  const remote = React.useRef(null);
  const content = React.useRef(null);
  const dock = useDockDrag({
    open,
    onOpenChange: setOpen,
    onAway: () => turnOff(tv),
    haptics: false,
    heldClass: "is-held",
    // The scrub bar reads a finger of its own; the still answers no vertical sweep, so it takes the pull.
    pullIgnore: ".mp-scrub",
    measure: ({ edge }) => {
      if (!row.current) return null;
      const shut = row.current.offsetHeight + edge;
      let natural = null;
      if (remote.current && dock.head.current && content.current) {
        const foot = parseFloat(getComputedStyle(remote.current).paddingBottom) || 0;
        natural = edge + dock.head.current.offsetHeight + content.current.offsetHeight + foot;
      }
      const bottom = parseFloat(document.body.style.getPropertyValue("--remote-strip-bottom")) || 0;
      const cap = Math.round(document.documentElement.clientHeight * OpenShare) - bottom;
      return { shut, natural, cap };
    },
  });
  const { measure, rest, grabbing } = dock;
  const shelf = useShelf(open);
  const collapse = React.useCallback(() => setOpen(false), [setOpen]);

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
    const bar = document.querySelector('nav[aria-label="Main"]');
    const place = () => {
      const standing = bar && getComputedStyle(bar).display !== "none" ? bar.getBoundingClientRect().height : 0;
      document.body.style.setProperty("--remote-strip-bottom", `${standing}px`);
      measure();
      const nominal = bar ? parseFloat(getComputedStyle(bar).getPropertyValue("--tabbar-h")) + parseFloat(getComputedStyle(bar).paddingBottom) : 0;
      const shortfall = bar && nominal ? Math.max(0, bar.getBoundingClientRect().height - nominal) : 0;
      document.body.style.setProperty("--remote-strip-inset", `${dock.size.current.shut + shortfall}px`);
      if (!grabbing()) rest(placed.current);
      placed.current = true;
    };
    place();
    window.addEventListener("resize", place);
    const observer = typeof ResizeObserver === "function" ? new ResizeObserver(place) : null;
    if (observer) for (const node of [bar, row.current, dock.head.current, content.current]) if (node) observer.observe(node);
    return () => {
      window.removeEventListener("resize", place);
      if (observer) observer.disconnect();
    };
  }, [dock.live, dock.head, dock.size, measure, rest, grabbing]);
  React.useEffect(() => () => {
    document.body.classList.remove("has-remote-strip");
    document.body.style.removeProperty("--remote-strip-inset");
    document.body.style.removeProperty("--remote-strip-bottom");
  }, []);
  React.useEffect(() => {
    document.body.classList.toggle("has-remote-open", open);
    return () => document.body.classList.remove("has-remote-open");
  }, [open]);

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
      <div ref={dock.panel} className={"kc-remote-dock" + (open ? " is-open" : "")} {...dock.handlers}>
        <div className="kc-remote__grab" />
        <Strip rowRef={row} screen={screen} hidden={open} onOpen={() => setOpen(true)} />
        {dock.live && <Remote dock={dock} remoteRef={remote} contentRef={content} shelf={shelf} screen={screen} hidden={!open} onCollapse={collapse} onPick={() => setPicking(true)} />}
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
        <Button variant="bare" className="kc-remote-strip__open" aria-label={`Open the remote: ${tv}`} onClick={onOpen}>
          <Art manifest={film.manifest} className="kc-dl-head__still"
            fallback={<span className="kc-dl-head__still kc-remote__nostill"><Icon name="tv" size={18} /></span>} />
          <span className="kc-dl-head__text">
            <span className="kc-dl-head__eyebrow"><Icon name="tv" size={12} />{tv}</span>
            <span className="kc-dl-head__name">{shown.name}</span>
          </span>
        </Button>
        {shown.playing && (
          <Button variant="bare" className="mp-play mp-play--small" aria-label={paused ? "Play" : "Pause"} disabled={!room.live}
            onClick={() => run(paused ? roomCommands.play : roomCommands.pause)}>
            <Icon name={paused ? "play" : "pause"} size={16} />
          </Button>
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
  const titles = useLoad(() => libraryApi.titles(), [round]);
  const progress = useLoad(() => meApi.progress().catch(() => []), [round]);
  const shows = useLoad(() => libraryApi.shows().catch(() => []), [round]);
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
      <EmptyState className="kc-remote__empty" icon="tv" title="Nothing playing" />
      {continuing.length > 0 && (
        <Rail className="kc-remote__rail"
          perView={WIDE} icon="history" title="Continue watching" count={continuing.length}
          items={continuing} itemKey={(p) => p.titleId || `${p.showImdbId}:${p.episode.season}:${p.episode.number}`}
          renderItem={(p) => !p.titleId
            ? <NextEpisodeCard progress={p} show={showOf.get(p.showImdbId)} onTv />
            : <ContinueCard title={byId.get(p.titleId)} progress={p} remainingSeconds={p.remainingSeconds} onTv />} />
      )}
    </>
  );
}

/** The open remote: the TV and the switch, then what plays, with the player's scrub bar and controls at the bottom. */
function Remote({ dock, remoteRef, contentRef, shelf, screen, hidden, onCollapse, onPick }) {
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
      <div ref={remoteRef} className="kc-remote" role="dialog" aria-label={`Remote: ${tv}`} aria-hidden={hidden || undefined} inert={hidden ? "" : undefined}>
        <div ref={dock.head} className="kc-remote__top">
          <div className="kc-remote__head">
            <span className="kc-remote__title">
              <Button variant="bare" className="kc-remote__tv" aria-label={`TV: ${tv}. Choose another`} onClick={onPick}>
                <Icon name="tv" size={13} />{tv}<Icon name="chevron-down" size={12} />
              </Button>
            </span>
            <span className="kc-remote__action">
              <span className="kc-remote__switch">Remote<Toggle className="kc-remote__toggle" on={mode} onChange={(on) => { if (on) setRemoteMode(true); else { onCollapse(); turnOff(tv); } }} label="Remote mode" /></span>
            </span>
          </div>
        </div>
        <div ref={dock.body} className="kc-remote__body">
          <div ref={contentRef} className="kc-remote__content">
            {!holding && <Idle shelf={shelf} />}
            {holding && (
              <>
                <Still className="kc-remote__still" manifest={manifest} {...stageProps}>
                  <GestureHud hud={gestures.hud} />
                  {skipOffer && (
                    <Button variant="bare" className="mp-nextup mp-skip" onClick={skipOffer.skip}>
                      <span className="mp-nextup__name">{SkipLabels[skipOffer.marker.kind]}</span>
                      <Icon name="skip-forward" size={18} />
                    </Button>
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
                          <Button variant="bare" className="mp-icon" disabled={!live} aria-label={`Back ${SkipSeconds} seconds`}
                            onClick={() => run(() => roomCommands.skip(-SkipSeconds))}>
                            <Icon name="rotate-ccw" size={19} />
                          </Button>
                          {manifest.episode && (
                            <Button variant="bare" className="mp-icon" disabled={!live || !previous || stepping}
                              aria-label={previous ? `Previous episode: ${words(previous)}` : "No previous episode here"}
                              onClick={() => step(roomCommands.previous)}>
                              <Icon name="skip-back" size={19} />
                            </Button>
                          )}
                          <Button variant="bare" className="mp-play" aria-label={paused ? "Play" : "Pause"} disabled={!live} onClick={toggle}>
                            <Icon name={paused ? "play" : "pause"} size={18} />
                          </Button>
                          {manifest.episode && (
                            <Button variant="bare" className="mp-icon" disabled={!live || !next || stepping}
                              aria-label={next ? `Next episode: ${words(next)}` : "No next episode here"}
                              onClick={() => step(roomCommands.next)}>
                              <Icon name="skip-forward" size={19} />
                            </Button>
                          )}
                          <Button variant="bare" className="mp-icon" disabled={!live} aria-label={`Forward ${SkipSeconds} seconds`}
                            onClick={() => run(() => roomCommands.skip(SkipSeconds))}>
                            <Icon name="rotate-cw" size={19} />
                          </Button>
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
        <PlayerMenu kind="episodes" view={episodeView} sheet contained={false} aimedAt={tv} onClose={() => setMenu(null)}
          room={{ playEpisode: (season, number) => run(() => roomCommands.playEpisode(season, number)) }} />
      )}
      {(menu === "subs" || menu === "audio" || menu === "quality") && tvView && (
        <PlayerMenu kind={menu} view={tvView} room={tvRoom} sheet contained={false} aimedAt={tv}
          styling={styling} onStyling={setStyling} onClose={() => setMenu(null)} />
      )}
    </>
  );
}
