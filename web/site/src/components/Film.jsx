import React from "react";
import { Icon, ShowcaseCard } from "@thekrystalship/krystal-ui";
import { filmName, posterUrl } from "../api.js";
import { SOON_DAYS, stayWords } from "../data.js";
import { linkProps, navigate } from "../router.js";
import { clock, timeLeft } from "../time.js";

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
export function PosterArt({ title, className = "", children }) {
  const url = posterUrl(title);
  const [failed, setFailed] = React.useState(false);
  return (
    <div className={"kc-poster-art " + className}>
      {url && !failed
        ? <img src={url} alt="" loading="lazy" onError={() => setFailed(true)} />
        : <span className="kc-poster-art__name">{filmName(title)}</span>}
      {children}
    </div>
  );
}

/**
 * A film on a shelf, as krystal-ui's ShowcaseCard: the poster with the name on it, its rating, year
 * and length, one line saying where it stands, and Watch. What a page knows beside the film — its
 * stay, a room watching it, whether it is new — is optional, and a card without it says less rather
 * than guessing. How far the person got comes with the title itself.
 *
 * The one status line, in precedence order: a film not ready says so; then a room watching it; then
 * how far this person got; then its stay (a keep, or days left on the clock); then its genres.
 */
export function FilmCard({ title, stay, me, room, isNew }) {
  const film = title.film || {};
  const d = title.details;
  const progress = title.progress && !title.progress.finished && title.progress.positionSeconds > 0 ? title.progress : null;
  const poster = posterUrl(title);
  const ready = title.status === "ready";
  const leavingSoon = stay && stay.settled && !stay.keeperId && stay.daysLeft != null && stay.daysLeft <= SOON_DAYS;

  const status =
    title.status === "transcoding" ? { tone: "notice", icon: "loader", label: "Still preparing" }
    : title.status === "failed" ? { tone: "warning", icon: "triangle-alert", label: "Could not be prepared" }
    : room ? { tone: "active", icon: "users", label: room.paused ? "A room has it paused" : "A room is watching" }
    : progress ? { tone: "active", icon: "history", label: timeLeft(Math.max(0, title.durationSeconds - progress.positionSeconds)) }
    : stay && stay.keeperId ? { tone: "success", icon: "bookmark-check", label: stayWords(stay, me) }
    : stay && stay.settled && stay.daysLeft != null ? { tone: leavingSoon ? "warning" : null, icon: "hourglass", label: stayWords(stay, me) }
    : d && d.genres.length > 0 ? { label: d.genres.slice(0, 2).join(" · ") }
    : null;

  return (
    <ShowcaseCard
      name={film.name || title.title}
      art={poster ? `url("${poster}")` : null}
      onPick={() => navigate("/title/" + encodeURIComponent(title.id))}
      corner={leavingSoon ? { icon: "hourglass", label: stayWords(stay, me), title: "Leaves unless somebody plays or keeps it" } : null}
      badge={isNew ? { tone: "info", label: "New" } : null}
      specs={[
        { icon: "star", value: d && d.rating ? d.rating.toFixed(1) : null, label: "Rating" },
        { icon: "calendar", value: film.year || null, label: "Year" },
        { icon: "clock", value: title.durationSeconds ? clock(title.durationSeconds) : null, label: "Length" },
      ]}
      status={status}
      action={ready
        ? { label: room ? "Join" : progress ? "Resume" : "Watch", onClick: () => navigate(watchHref(title.id, room && room.sessionId, progress && progress.positionSeconds)) }
        : { label: "View" }} />
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

