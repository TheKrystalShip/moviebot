import React from "react";
import { cueVars } from "./subtitleStyle.js";
import "./cues.css";

/** The line shown while somebody is choosing a look and the film is saying nothing. */
export const SAMPLE_LINE = "I'll meet you at the station at nine.";

/**
 * Whether a cue sits against the top of the frame: a sign, a forced line over end credits. WebVTT
 * places a cue by `line`, a line number counted from the top when it is not negative, or a
 * percentage when lines are not snapped; anything it leaves on its default hangs off the bottom,
 * which is where dialogue sits.
 */
function placedHigh(cue) {
  if (typeof cue.line !== "number") return false;
  return cue.snapToLines ? cue.line >= 0 && cue.line < 4 : cue.line < 50;
}

/**
 * The text of a cue as the file marks it up — italics, bold, a voice — through the browser's own
 * parser, which is the one that knows the format.
 */
function CueText({ cue }) {
  const ref = React.useRef(null);
  React.useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    el.replaceChildren(cue.getCueAsHTML ? cue.getCueAsHTML() : document.createTextNode(cue.text || ""));
  }, [cue]);
  return <span ref={ref} className="mp-cue__text" />;
}

/**
 * The subtitles over the film, drawn here rather than by the browser.
 *
 * A cue the browser draws takes the operating system's caption settings and nothing this page can
 * say about it, so the track is attached hidden: the browser parses and times it and draws nothing,
 * and this draws the active cues, sized against the frame so they read the same on a phone and on a
 * television, in the person's own style (`subtitleStyle.js`). Dialogue moves up while the controls
 * are showing, so a line is never under them. With `sample` set and nothing being said, a sample
 * line stands in, so a style being chosen is seen over the film it is for; `lifted` puts dialogue
 * above the sheet the choosing happens in on a narrow frame, which would otherwise cover it, and
 * `beside` keeps it clear of the popover it happens in on a wide one.
 */
export function Cues({ track, raised, lifted, beside, style, sample }) {
  const [cues, setCues] = React.useState([]);

  React.useEffect(() => {
    if (!track) {
      setCues([]);
      return undefined;
    }
    const read = () => setCues(track.activeCues ? Array.from(track.activeCues) : []);
    read();
    track.addEventListener("cuechange", read);
    return () => track.removeEventListener("cuechange", read);
  }, [track]);

  const bottom = "mp-cues__bottom" + (lifted ? " is-lifted" : raised ? " is-raised" : "") + (beside ? " is-beside" : "");
  if (cues.length === 0) {
    return sample ? (
      <div className="mp-cues" aria-hidden="true" style={cueVars(style)}>
        <div className={bottom}>
          <div className="mp-cue"><span className="mp-cue__text">{SAMPLE_LINE}</span></div>
        </div>
      </div>
    ) : null;
  }
  const high = cues.filter(placedHigh);
  const low = cues.filter((cue) => !placedHigh(cue));

  return (
    <div className="mp-cues" aria-live="off" style={cueVars(style)}>
      {high.length > 0 && (
        <div className="mp-cues__top">
          {high.map((cue, i) => <div key={i} className="mp-cue"><CueText cue={cue} /></div>)}
        </div>
      )}
      {low.length > 0 && (
        <div className={bottom}>
          {low.map((cue, i) => <div key={i} className="mp-cue"><CueText cue={cue} /></div>)}
        </div>
      )}
    </div>
  );
}
