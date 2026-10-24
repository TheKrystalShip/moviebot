import React from "react";
import { Icon, TvSpotlight } from "@thekrystalship/krystal";
import { TrailerContext, honoursLine } from "../components/TitleHero.jsx";
import { TrailerPlayer } from "../components/TrailerPlayer.jsx";

/**
 * A film's or a show's head on a television, the site's hero at a television's size: the kind, the name
 * and the facts line (`facts`), then the page's groups under them with room between them, over the
 * page's backdrop. With `trailer` the page's `TrailerButton` plays it in the head's place, which Back
 * closes.
 */
export function TvTitleHero({ eyebrow, title, facts, trailer, kind = "tvf", children }) {
  const [playing, setPlaying] = React.useState(false);
  const open = React.useCallback(() => setPlaying(true), []);
  const close = React.useCallback(() => setPlaying(false), []);
  return (
    <TrailerContext.Provider value={trailer ? { open, playing } : null}>
      <section className={`kc-${kind}__head` + (playing ? " is-trailer" : "")} data-tv-top="">
        <TvSpotlight className={`kc-${kind}__spot`} size="lg" titleSize="lg" eyebrow={eyebrow} title={title} meta={facts}>
          <div className="kc-tvf__groups" inert={playing ? "" : undefined}>{children}</div>
        </TvSpotlight>
        {playing && trailer && <TrailerPlayer trailer={trailer} title={title} onClose={close} />}
      </section>
    </TrailerContext.Provider>
  );
}

/** One group of the head's column: its parts close together, with room between it and the next group. */
export function TvGroup({ children }) {
  return <div className="kc-tvf__group">{children}</div>;
}

/** What a film won and what it is based on, as plain lines: a remote reaches the buttons, not these. */
export function TvHonours({ awards, basedOn = [] }) {
  const line = honoursLine(awards);
  const source = basedOn[0];
  if (!line && !source) return null;
  return (
    <TvGroup>
      {line && (
        <span className="kc-tvf__honour">
          <Icon name="trophy" size={18} />
          {line.lead && <b>{line.lead}</b>}
          <span>{line.counts}</span>
        </span>
      )}
      {source && (
        <span className="kc-tvf__honour">
          <Icon name="book-open" size={18} />
          <span>Based on <i>{source.work}</i>{source.author && <> by <b>{source.author}</b></>}</span>
        </span>
      )}
    </TvGroup>
  );
}

/**
 * The overview, clamped, and who made it and who is in it as the site's credits table, the names as
 * text: Director, Writers and Stars for a film, Creator and Stars for a show.
 */
export function TvAbout({ overview, credits }) {
  const rows = credits ? [
    [(credits.creators || []).length > 1 ? "Creators" : "Creator", credits.creators || [], ", "],
    [(credits.directors || []).length > 1 ? "Directors" : "Director", credits.directors || [], ", "],
    ["Writers", credits.writers || [], ", "],
    ["Stars", (credits.cast || []).slice(0, 4).map((c) => c.name), " · "],
  ].filter(([, names]) => names.length > 0) : [];
  if (!overview && rows.length === 0) return null;
  return (
    <TvGroup>
      {overview && <p className="kc-tvf__overview">{overview}</p>}
      {rows.length > 0 && (
        <div className="kc-tvf__credits">
          {rows.map(([label, names, between]) => (
            <React.Fragment key={label}>
              <span className="kc-tvf__credits-label">{label}</span>
              <span>{names.join(between)}</span>
            </React.Fragment>
          ))}
        </div>
      )}
    </TvGroup>
  );
}

/**
 * The episode a show's head leads with on a television: its still with how far into it somebody is, and
 * Continue or Up next with its number, its name, its story and its length. The head's first button
 * plays it.
 */
export function TvNextEpisode({ episode: e, at = 0, label, meta }) {
  const length = e.runtimeMinutes ? e.runtimeMinutes * 60 : e.durationSeconds || 0;
  return (
    <TvGroup>
      <div className="kc-tvf__next">
        <span className="kc-tvf__still" style={e.stillUrl ? { backgroundImage: `url("${e.stillUrl}")` } : undefined}>
          {at > 0 && length > 0 && <span className="kc-tvf__still-progress" style={{ width: `${Math.min(100, (at / length) * 100)}%` }} />}
        </span>
        <span className="kc-tvf__next-text">
          <span className="kc-tvf__next-label">{label}</span>
          <span className="kc-tvf__next-name">{e.name || `Episode ${e.number}`}</span>
          {e.overview && <span className="kc-tvf__next-story">{e.overview}</span>}
          {meta && <span className="kc-tvf__next-label">{meta}</span>}
        </span>
      </div>
    </TvGroup>
  );
}
