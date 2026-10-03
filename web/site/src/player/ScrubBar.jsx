import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import { formatTime } from "./engine/format.ts";

const KeyboardStepSeconds = 5;
const FrameWidth = 208;

const clamp = (value, low, high) => Math.min(high, Math.max(low, value));
const percent = (seconds, duration) => `${duration > 0 ? clamp(seconds / duration, 0, 1) * 100 : 0}%`;

/** A signed distance in time, as the hint under a drag says it. */
function signed(seconds) {
  return `${seconds < 0 ? "−" : "+"}${formatTime(Math.abs(seconds))}`;
}

/** The frame of the preview sheet nearest a moment, as a background drawn at a given width. */
function frameStyle(strip, url, at, width) {
  const height = Math.round((width * strip.height) / strip.width);
  const index = clamp(Math.floor(at / strip.intervalSeconds), 0, strip.count - 1);
  return {
    width, height,
    backgroundImage: `url("${url}")`,
    backgroundSize: `${strip.columns * width}px ${strip.rows * height}px`,
    backgroundPosition: `-${(index % strip.columns) * width}px -${Math.floor(index / strip.columns) * height}px`,
  };
}

/**
 * The film's scrub bar: elapsed, the bar, the length.
 *
 * Drawn at the true duration from the manifest, with what the transcode has written shaded behind
 * what is buffered and what has been played, so "still preparing" is legible without a sentence:
 * an EVENT playlist advertises only what has been written so far, and a bar drawn from it would
 * shrink a three-hour film to whatever the transcode has reached.
 *
 * Pointing at the bar previews that moment; a moment the transcode has not reached says so over
 * its frame. Dragging never moves the playhead. It publishes a seek when let go and waits for the
 * server's answer, because there is one playhead for the whole room and the server decides where
 * it lands. While a drag is held the bar says where letting go takes everybody, measured from
 * where the film is now.
 *
 * The thin variant is the bar alone, for a frame too small to carry anything beside it.
 */
export function ScrubBar({ duration, position, ready, buffered, thumbnails, chapters, onSeek, onHold, thin = false }) {
  const barRef = React.useRef(null);
  const [hover, setHover] = React.useState(null);
  const [drag, setDrag] = React.useState(null);
  const dragRef = React.useRef(null);
  dragRef.current = drag;

  const at = (clientX) => {
    const box = barRef.current.getBoundingClientRect();
    return box.width === 0 ? 0 : clamp((clientX - box.left) / box.width, 0, 1) * duration;
  };

  const down = (e) => {
    if (duration <= 0 || (e.pointerType === "mouse" && e.button !== 0)) return;
    barRef.current.setPointerCapture(e.pointerId);
    setDrag(at(e.clientX));
    onHold(true);
  };
  const move = (e) => {
    if (dragRef.current !== null) setDrag(at(e.clientX));
    else if (e.pointerType === "mouse") setHover(at(e.clientX));
  };
  const up = (e) => {
    const to = dragRef.current;
    if (to === null) return;
    try { barRef.current.releasePointerCapture(e.pointerId); } catch { /* already released */ }
    setDrag(null);
    onHold(false);
    onSeek(to);
  };
  const cancel = () => {
    if (dragRef.current === null) return;
    setDrag(null);
    onHold(false);
  };

  const key = (e) => {
    const to = e.key === "ArrowRight" ? position + KeyboardStepSeconds
      : e.key === "ArrowLeft" ? position - KeyboardStepSeconds
        : e.key === "Home" ? 0 : e.key === "End" ? duration : null;
    if (to === null) return;
    e.preventDefault();
    onSeek(clamp(to, 0, duration));
  };

  const shown = drag ?? position;
  const pointed = drag ?? hover;
  const unreached = (seconds) => ready !== null && seconds > ready;
  // A chapter's start is a mark on the bar, and pointing anywhere names the chapter it is in.
  const marks = (chapters || []).length > 1 ? chapters.filter((c) => c.startSeconds > 0.5 && c.startSeconds < duration) : [];
  const chapterOf = (seconds) => {
    if ((chapters || []).length < 2) return null;
    let at = null;
    for (const c of chapters) if (c.startSeconds <= seconds + 0.5) at = c;
    return at && at.title ? at.title : null;
  };

  const bar = (
    <div ref={barRef} className={"mp-scrub" + (pointed !== null ? " is-pointed" : "") + (drag !== null ? " is-dragging" : "")}
      role="slider" tabIndex={0} aria-label="Seek" aria-valuemin={0} aria-valuemax={Math.round(duration)}
      aria-valuenow={Math.round(shown)} aria-valuetext={`${formatTime(shown)} of ${formatTime(duration)}`}
      onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerCancel={cancel}
      onPointerLeave={() => setHover(null)} onKeyDown={key}>
      <div className="mp-scrub__track">
        {ready !== null && <div className="mp-scrub__ready" style={{ width: percent(ready, duration) }} />}
        <div className="mp-scrub__buffered" style={{ width: percent(buffered, duration) }} />
        <div className="mp-scrub__played" style={{ width: percent(shown, duration) }} />
        {!thin && marks.map((c) => <div key={c.startSeconds} className="mp-scrub__chapter" style={{ left: percent(c.startSeconds, duration) }} />)}
        {hover !== null && drag === null && <div className="mp-scrub__marker" style={{ left: percent(hover, duration) }} />}
        <div className="mp-scrub__knob" style={{ left: percent(shown, duration) }} />
      </div>
      {!thin && pointed !== null && (
        <div className="mp-scrub__preview" style={{ left: percent(pointed, duration) }}>
          {thumbnails && (
            <div className="mp-scrub__frame" style={frameStyle(thumbnails.strip, thumbnails.url, pointed, FrameWidth)}>
              {unreached(pointed) && <div className="mp-scrub__unreached"><Icon name="hourglass" size={16} />Not ready yet</div>}
            </div>
          )}
          {chapterOf(pointed) && <span className="mp-scrub__chapter-name">{chapterOf(pointed)}</span>}
          <span className="mp-scrub__time">{formatTime(pointed)}</span>
        </div>
      )}
    </div>
  );

  if (thin) return bar;
  return (
    <div className="mp-scrub-row">
      <div className="mp-scrub-row__line">
        <span className="mp-scrub-row__time">{formatTime(shown)}</span>
        {bar}
        <span className="mp-scrub-row__time">{formatTime(duration)}</span>
      </div>
      {drag !== null && (
        <span className="mp-scrub-row__hint">
          {unreached(drag) ? "Not ready yet · lands as far as is prepared" : `Release to jump · ${signed(drag - position)} from current position`}
        </span>
      )}
    </div>
  );
}
