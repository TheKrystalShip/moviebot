import React from "react";

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
 * television. Dialogue moves up while the controls are showing, so a line is never under them.
 */
export function Cues({ track, raised }) {
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

  if (cues.length === 0) return null;
  const high = cues.filter(placedHigh);
  const low = cues.filter((cue) => !placedHigh(cue));

  return (
    <div className="mp-cues" aria-live="off">
      {high.length > 0 && (
        <div className="mp-cues__top">
          {high.map((cue, i) => <div key={i} className="mp-cue"><CueText cue={cue} /></div>)}
        </div>
      )}
      {low.length > 0 && (
        <div className={"mp-cues__bottom" + (raised ? " is-raised" : "")}>
          {low.map((cue, i) => <div key={i} className="mp-cue"><CueText cue={cue} /></div>)}
        </div>
      )}
    </div>
  );
}
