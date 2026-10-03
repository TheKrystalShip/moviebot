import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import { filmName, posterUrl } from "../api.js";
import { linkProps } from "../router.js";
import { timeLeft } from "../time.js";

/**
 * Where a film is watched: a room of its own for whoever opens it, or the room somebody is already
 * watching it in. The room is the address, so the link can be handed to somebody else. `at` is where
 * to start when the room is empty; a room already holding the film ignores it.
 */
export function watchHref(titleId, sessionId, at) {
  const room = sessionId || crypto.randomUUID().replaceAll("-", "").slice(0, 8);
  const query = [];
  if (titleId) query.push(`title=${encodeURIComponent(titleId)}`);
  if (titleId && at > 0) query.push(`at=${Math.floor(at)}`);
  return `/r/${encodeURIComponent(room)}` + (query.length ? "?" + query.join("&") : "");
}

/** A link into a room, opened in place so the way back is the page it was opened from. */
export function watchLink(titleId, sessionId, at) {
  return linkProps(watchHref(titleId, sessionId, at));
}

/** A film started and not finished: its artwork, how far in, how much is left, and a way back into it. */
export function ContinueCard({ title, progress, remainingSeconds, room }) {
  const art = (title.details && title.details.backdropUrl) || posterUrl(title);
  const fraction = progress.durationSeconds > 0 ? Math.min(1, progress.positionSeconds / progress.durationSeconds) : 0;
  return (
    <a className="kc-continue" {...watchLink(title.id, room && room.sessionId, progress.positionSeconds)}>
      <div className="kc-poster-art kc-poster-art--wide">
        {art ? <img src={art} alt="" loading="lazy" /> : <span className="kc-poster-art__name">{filmName(title)}</span>}
        <span className="kc-continue__play"><Icon name="play" size={16} /></span>
        <span className="kc-bar"><span style={{ width: `${Math.round(fraction * 100)}%` }} /></span>
      </div>
      <span className="kc-poster__name">{filmName(title)}</span>
      <span className="kc-poster__note">{timeLeft(remainingSeconds)}</span>
    </a>
  );
}

/**
 * A film's poster. The catalogue's artwork where there is some, and otherwise the name set on a
 * tinted card, so a film with no artwork is still a film on the shelf rather than a gap.
 */
export function PosterArt({ title, className = "" }) {
  const url = posterUrl(title);
  const [failed, setFailed] = React.useState(false);
  return (
    <div className={"kc-poster-art " + className}>
      {url && !failed
        ? <img src={url} alt="" loading="lazy" onError={() => setFailed(true)} />
        : <span className="kc-poster-art__name">{filmName(title)}</span>}
    </div>
  );
}

/** A film on a shelf: its poster, its name, and one line under it. */
export function PosterCard({ title, note, tone }) {
  return (
    <a className="kc-poster" {...linkProps("/title/" + encodeURIComponent(title.id))}>
      <PosterArt title={title} />
      <span className="kc-poster__name">{filmName(title)}</span>
      {note && <span className={"kc-poster__note" + (tone ? " kc-poster__note--" + tone : "")}>{note}</span>}
    </a>
  );
}

export function PageHead({ title, sub, children }) {
  return (
    <div className="kc-head">
      <div className="kc-head__text">
        <h1>{title}</h1>
        {sub && <span>{sub}</span>}
      </div>
      {children}
    </div>
  );
}

export function Empty({ icon = "clapperboard", title, children }) {
  return (
    <div className="kc-empty">
      <Icon name={icon} size={24} />
      <span className="kc-empty__title">{title}</span>
      {children && <span>{children}</span>}
    </div>
  );
}

/** What went wrong reading the API, said where the page would have been. */
export function Failed({ error, onRetry }) {
  if (!error) return null;
  return (
    <div className="kc-failed">
      <Icon name="triangle-alert" size={16} />
      <span>{error.message}</span>
      {onRetry && <button type="button" className="kc-btn kc-btn--ghost" onClick={onRetry}>Try again</button>}
    </div>
  );
}

export function Shelf({ title, icon, more, children }) {
  return (
    <section className="kc-shelf">
      <div className="kc-shelf__head">
        <h2>{icon && <Icon name={icon} size={18} />}{title}</h2>
        {more && <a className="kc-shelf__more" {...linkProps(more.href)}>{more.label} →</a>}
      </div>
      <div className="kc-shelf__row">{children}</div>
    </section>
  );
}
