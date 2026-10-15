import React from "react";
import { IconButton, Slider } from "@thekrystalship/krystal";
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
  const rootRef = React.useRef(null);
  const shown = muted ? 0 : position;

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

  // Touching the slider is how a person unmutes; leaving it silent would look broken.
  const apply = (percent) => {
    const value = percent / 100;
    onChange(value, value > 0 ? false : muted);
  };

  return (
    <div ref={rootRef} className="mp-volume">
      <IconButton variant="ghost" size="lg" className="mp-volume__mute" label={muted ? "Unmute (m)" : "Mute (m)"}
        icon={muted || shown === 0 ? "volume-x" : shown < 0.5 ? "volume-1" : "volume-2"}
        onClick={() => onChange(position, !muted)} />
      <Slider className="mp-volume__track" size="sm" label="Volume" min={0} max={100} step={VolumeStep * 100}
        value={Math.round(shown * 100)} onChange={apply} />
    </div>
  );
}
