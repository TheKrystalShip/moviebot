import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import { VolumeStep } from "./engine/media/volume.ts";

/**
 * The volume control: a mute button and a slider holding the loudness a person asked for. The page
 * gives the media element the amplitude that produces it (`amplitudeFor`), because loudness is not
 * linear in amplitude and a slider wired straight to the element spends its lower half doing almost
 * nothing.
 *
 * A wheel over the control is the same gesture as the arrow keys: one notch is one step.
 */
export function Volume({ position, muted, onChange, onNudge }) {
  const trackRef = React.useRef(null);
  const rootRef = React.useRef(null);
  const dragging = React.useRef(false);
  const shown = muted ? 0 : position;

  // Touching the slider is how a person unmutes; leaving it silent would look broken.
  const apply = (next) => {
    const value = Math.min(1, Math.max(0, next));
    onChange(value, value > 0 ? false : muted);
  };
  const fromEvent = (e) => {
    const box = trackRef.current.getBoundingClientRect();
    return (e.clientX - box.left) / box.width;
  };

  // A wheel listener that may cancel the page's scroll has to be registered as not passive.
  React.useEffect(() => {
    const el = rootRef.current;
    if (!el) return undefined;
    const wheel = (e) => {
      if (e.deltaY === 0) return;
      e.preventDefault();
      onNudge(e.deltaY < 0 ? VolumeStep : -VolumeStep);
    };
    el.addEventListener("wheel", wheel, { passive: false });
    return () => el.removeEventListener("wheel", wheel);
  }, [onNudge]);

  return (
    <div ref={rootRef} className="mp-volume">
      <button type="button" className="mp-btn" aria-label={muted ? "Unmute" : "Mute"} title={muted ? "Unmute (m)" : "Mute (m)"}
        onClick={() => onChange(position, !muted)}>
        <Icon name={muted || shown === 0 ? "volume-x" : shown < 0.5 ? "volume-1" : "volume-2"} size={20} />
      </button>
      <div ref={trackRef} className="mp-volume__track" role="slider" tabIndex={0} aria-label="Volume"
        aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(shown * 100)}
        onPointerDown={(e) => { dragging.current = true; trackRef.current.setPointerCapture(e.pointerId); apply(fromEvent(e)); }}
        onPointerMove={(e) => { if (dragging.current) apply(fromEvent(e)); }}
        onPointerUp={(e) => { dragging.current = false; try { trackRef.current.releasePointerCapture(e.pointerId); } catch {} }}
        onKeyDown={(e) => {
          const step = e.key === "ArrowRight" || e.key === "ArrowUp" ? VolumeStep
            : e.key === "ArrowLeft" || e.key === "ArrowDown" ? -VolumeStep : 0;
          if (step === 0) return;
          e.preventDefault();
          onNudge(step);
        }}>
        <div className="mp-volume__fill" style={{ width: `${shown * 100}%` }} />
      </div>
    </div>
  );
}
