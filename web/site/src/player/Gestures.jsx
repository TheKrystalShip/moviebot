import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import { formatTime } from "./engine/format.ts";
import { MinBrightness } from "./engine/prefs.ts";

/** A second tap on the same side this soon after the first makes a double tap. */
const DoubleTapMs = 300;
/** Taps keep adding to a skip while they come this close together; then the skip is sent. */
const SkipSettleMs = 650;
/** How far a finger travels before a touch is a swipe rather than a tap. */
const SwipeSlopPx = 12;
/** A swipe that ends this close to where it began in the film goes nowhere. */
const SwipeNowhereSeconds = 1;
/** A vertical swipe this fraction of the frame's height runs a level from one end to the other. */
const LevelSpan = 0.6;
/** How long a level stays on screen after the finger lifts. */
const LevelLingerMs = 700;

const clamp = (value, low, high) => Math.min(high, Math.max(low, value));

/** Brightness as a level along its own range: 0 at its dimmest, 1 as the film was made. */
const levelOfBrightness = (brightness) => (brightness - MinBrightness) / (1 - MinBrightness);
const brightnessOfLevel = (level) => MinBrightness + level * (1 - MinBrightness);

function signed(seconds) {
  return `${seconds < 0 ? "−" : "+"}${formatTime(Math.abs(seconds))}`;
}

/**
 * What a finger does on the film, which is not what a mouse does.
 *
 * The film is three columns. A tap in the middle plays or pauses it; a tap at either side shows or
 * hides the controls, and two taps there skip back or forward. Each further tap while a skip is
 * still gathering adds another step, and the skip is sent once, when the taps stop: a seek is a
 * question to the room, and a second one asked before the first is answered is measured from a
 * playhead that has not moved yet.
 *
 * A finger swept sideways anywhere is the scrub bar under the thumb: the width of the film is its
 * whole length, the bar says where letting go takes everybody, and nothing moves until it does.
 * Swept up or down at the right it is this viewer's volume, where the browser lets a page set
 * one, and at the left how bright the picture is drawn; both are this viewer's own and move nobody
 * else.
 *
 * A surface that has no volume or brightness of its own, the phone's remote, passes `levels: false`
 * and has its vertical sweeps read as nothing.
 *
 * Where the film is not this viewer's to move (`seekable` false, a guest's), a tap anywhere only
 * shows or hides the controls, and only the sweeps up and down are read.
 *
 * Every handler here is a touch on the bare film; the page decides that before it calls them.
 */
export function useStageGestures(props) {
  const latest = React.useRef(props);
  latest.current = props;
  const [hud, setHud] = React.useState(null);
  const touch = React.useRef(null);
  const pendingTap = React.useRef(null);
  const run = React.useRef(null);
  const lingerTimer = React.useRef(null);

  React.useEffect(() => () => {
    window.clearTimeout(pendingTap.current && pendingTap.current.timer);
    window.clearTimeout(run.current && run.current.timer);
    window.clearTimeout(lingerTimer.current);
  }, []);

  return React.useMemo(() => {
    const commitSkip = () => {
      const r = run.current;
      if (!r) return;
      window.clearTimeout(r.timer);
      run.current = null;
      if (r.total !== 0) latest.current.onSeek(r.base + r.total);
      setHud((h) => (h && h.kind === "skip" ? null : h));
    };

    const addSkip = () => {
      const { duration, skipSeconds } = latest.current;
      const r = run.current;
      const step = r.side === "left" ? -skipSeconds : skipSeconds;
      r.total = clamp(r.base + r.total + step, 0, duration) - r.base;
      r.taps += 1;
      window.clearTimeout(r.timer);
      r.timer = window.setTimeout(commitSkip, SkipSettleMs);
      setHud({ kind: "skip", side: r.side, seconds: r.total, key: r.taps });
    };

    const tap = (zone) => {
      if (latest.current.seekable === false) {
        latest.current.onToggleControls();
        return;
      }
      const now = performance.now();
      if (run.current) {
        if (zone === run.current.side) { addSkip(); return; }
        commitSkip();
      }
      const pending = pendingTap.current;
      if (pending) {
        window.clearTimeout(pending.timer);
        pendingTap.current = null;
        if (pending.side === zone && now - pending.at < DoubleTapMs) {
          run.current = { side: zone, base: latest.current.position, total: 0, taps: 0, timer: null };
          addSkip();
          return;
        }
      }
      if (zone === "middle") { latest.current.onTogglePlay(); return; }
      // A tap at the side waits to learn whether it is the first of two.
      pendingTap.current = {
        side: zone, at: now,
        timer: window.setTimeout(() => { pendingTap.current = null; latest.current.onToggleControls(); }, DoubleTapMs),
      };
    };

    const showLevel = (kind, level) => {
      window.clearTimeout(lingerTimer.current);
      setHud({ kind, level });
    };
    const lingerLevel = () => {
      window.clearTimeout(lingerTimer.current);
      lingerTimer.current = window.setTimeout(() => setHud((h) => (h && (h.kind === "volume" || h.kind === "brightness") ? null : h)), LevelLingerMs);
    };

    const onPointerDown = (e) => {
      if (touch.current) return;
      const box = e.currentTarget.getBoundingClientRect();
      const x = e.clientX - box.left;
      const zone = x < box.width / 3 ? "left" : x > (box.width * 2) / 3 ? "right" : "middle";
      try { e.currentTarget.setPointerCapture(e.pointerId); } catch { /* the touch already ended */ }
      touch.current = { id: e.pointerId, x0: e.clientX, y0: e.clientY, width: box.width, height: box.height, zone, axis: null, from: 0, to: 0 };
    };

    const onPointerMove = (e) => {
      const t = touch.current;
      if (!t || t.id !== e.pointerId) return;
      const dx = e.clientX - t.x0;
      const dy = e.clientY - t.y0;
      const p = latest.current;
      if (t.axis === null) {
        if (Math.hypot(dx, dy) < SwipeSlopPx) return;
        if (Math.abs(dx) > Math.abs(dy)) {
          t.axis = p.duration > 0 && p.seekable !== false ? "seek" : "none";
          t.from = p.position;
          if (t.axis === "seek") p.onHold(true);
        } else if (p.levels === false) {
          t.axis = "none";
        } else if (t.zone === "right" && p.volumeSettable) {
          t.axis = "volume";
          t.from = p.volume;
        } else if (t.zone === "left") {
          t.axis = "brightness";
          t.from = levelOfBrightness(p.brightness);
        } else {
          t.axis = "none";
        }
      }
      if (t.axis === "seek") {
        t.to = clamp(t.from + (dx / t.width) * p.duration, 0, p.duration);
        setHud({ kind: "seek", at: t.to });
      } else if (t.axis === "volume" || t.axis === "brightness") {
        const level = clamp(t.from - dy / (t.height * LevelSpan), 0, 1);
        if (t.axis === "volume") p.onVolume(level);
        else p.onBrightness(brightnessOfLevel(level));
        showLevel(t.axis, level);
      }
    };

    const end = (e, completed) => {
      const t = touch.current;
      if (!t || t.id !== e.pointerId) return;
      touch.current = null;
      const p = latest.current;
      if (t.axis === "seek") {
        p.onHold(false);
        setHud(null);
        if (completed && Math.abs(t.to - t.from) >= SwipeNowhereSeconds) p.onSeek(t.to);
      } else if (t.axis === "volume" || t.axis === "brightness") {
        lingerLevel();
      } else if (t.axis === null && completed) {
        tap(t.zone);
      }
    };

    return {
      hud,
      onPointerDown,
      onPointerMove,
      onPointerUp: (e) => end(e, true),
      onPointerCancel: (e) => end(e, false),
    };
  }, [hud]);
}

/** What a gesture is doing, drawn over the film while it does it. */
export function GestureHud({ hud }) {
  if (!hud) return null;
  if (hud.kind === "skip") {
    return (
      <div key={hud.key} className={`mp-gesture-skip mp-gesture-skip--${hud.side}`} aria-hidden="true">
        <Icon name={hud.side === "left" ? "rotate-ccw" : "rotate-cw"} size={22} />
        <span>{signed(hud.seconds)}</span>
      </div>
    );
  }
  // A sweep is drawn by the scrub bar, which already previews where letting go lands.
  if (hud.kind === "seek") return null;
  const icon = hud.kind === "volume"
    ? (hud.level === 0 ? "volume-x" : hud.level < 0.5 ? "volume-1" : "volume-2")
    : (hud.level < 0.5 ? "sun-dim" : "sun");
  return (
    <div className={`mp-gesture-level mp-gesture-level--${hud.kind === "volume" ? "right" : "left"}`} aria-hidden="true">
      <Icon name={icon} size={18} />
      <div className="mp-gesture-level__track"><div style={{ height: `${hud.level * 100}%` }} /></div>
    </div>
  );
}
