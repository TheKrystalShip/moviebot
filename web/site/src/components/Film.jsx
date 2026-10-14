import React from "react";
import { Button, Icon, ShowcaseCard } from "@thekrystalship/krystal";
import { episodeCode, filmName, posterUrl } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useSave } from "../offline/hooks.js";
import { useSaveRibbon } from "./SaveButton.jsx";
import { isAnime, moviePath, playSavedPath, roomPath, showPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { clock, timeLeft } from "../time.js";
import { watchNow } from "../watchNow.js";
import { watchOnTv } from "../remoteWatch.js";
import { activityRoom } from "../discord/launch.js";

/**
 * Where a film is watched: a room of its own for whoever opens it, or the room somebody is already
 * watching it in. The room is the address, so the link can be handed to somebody else. `at` is where
 * to start; a room already holding the film ignores it. In the Discord Activity every film is
 * watched in the call's room, which it puts in place of whatever the call was watching.
 */
export function watchHref(titleId, sessionId, at) {
  const room = activityRoom || sessionId || crypto.randomUUID().replaceAll("-", "").slice(0, 8);
  const query = [];
  if (titleId) query.push(`title=${encodeURIComponent(titleId)}`);
  if (titleId && at > 0) query.push(`at=${Math.floor(at)}`);
  return roomPath(room) + (query.length ? "?" + query.join("&") : "");
}

/** A link into a room, opened in place so the way back is the page it was opened from. */
export function watchLink(titleId, sessionId, at) {
  return linkProps(watchHref(titleId, sessionId, at));
}

/**
 * What Watch is for a title, as a control's props: its room while there is a connection; with none,
 * the film played from this device at `at` when a finished save of it is here, and disabled when not.
 */
export function useWatchLink(titleId, sessionId, at) {
  const online = useOnline();
  const save = useSave(online ? null : titleId);
  if (online) return watchLink(titleId, sessionId, at);
  if (save.data && save.data.complete) return linkProps(playSavedPath(titleId, at));
  return { disabled: true };
}

/** Watch as a button: `useWatchLink`'s target for the title, with the button's own props and words. */
export function WatchButton({ titleId, sessionId, at, children, ...props }) {
  return <Button {...props} {...useWatchLink(titleId, sessionId, at)}>{children}</Button>;
}

/**
 * A film started and not finished: its artwork, how far in, how much is left, and a way back into it.
 * `onTv` is the card on a phone driving a television, on its remote and on Home: choosing it puts the
 * film on the television, from where this person got to, and the glyph on it is the television's.
 */
export function ContinueCard({ title, progress, remainingSeconds, room, onTv = false }) {
  const art = (title.details && title.details.backdropUrl) || posterUrl(title);
  const fraction = progress.durationSeconds > 0 ? Math.min(1, progress.positionSeconds / progress.durationSeconds) : 0;
  const link = useWatchLink(title.id, room && room.sessionId, progress.positionSeconds);
  const [starting, setStarting] = React.useState(false);
  const body = (
    <>
      <div className="kc-poster-art kc-poster-art--wide">
        {art ? <img src={art} alt="" loading="lazy" /> : <span className="kc-poster-art__name">{filmName(title)}</span>}
        <span className="kc-continue__play"><Icon name={onTv ? "tv" : "play"} size={16} /></span>
        <span className="kc-bar"><span style={{ width: `${Math.round(fraction * 100)}%` }} /></span>
      </div>
      <span className="kc-poster__name">{filmName(title)}</span>
      <span className="kc-poster__note">{timeLeft(remainingSeconds)}</span>
    </>
  );
  if (onTv) {
    const start = async () => {
      setStarting(true);
      await watchOnTv({
        titleId: title.id, startSeconds: progress.positionSeconds > 0 ? progress.positionSeconds : null,
        name: filmName(title), meta: remainingSeconds > 0 ? timeLeft(remainingSeconds) : null, art,
      });
      setStarting(false);
    };
    return <Button variant="bare" className="kc-continue" disabled={starting} aria-label={`Watch on TV: ${filmName(title)}`} onClick={start}>{body}</Button>;
  }
  return <a className="kc-continue" {...(link.disabled ? { "aria-disabled": "true" } : link)}>{body}</a>;
}

/**
 * The episode after one somebody finished, when the library does not hold it (`GET /api/me/progress`
 * with no `titleId`): its still, else the show's backdrop, the show's name and its place in it.
 * Choosing it is Watch now, which fetches it into a room and goes there.
 */
export function NextEpisodeCard({ progress, show, onTv = false }) {
  const [starting, setStarting] = React.useState(false);
  const online = useOnline();
  const details = show && show.details;
  const art = progress.stillUrl || (details && (details.backdropUrl || details.posterUrl));
  const name = `${progress.name} · ${episodeCode(progress.episode)}`;
  const start = async () => {
    if (starting) return;
    setStarting(true);
    const request = { imdbId: progress.showImdbId, episodes: [{ season: progress.episode.season, episode: progress.episode.number }] };
    const started = onTv
      ? await watchOnTv({ request, name, meta: progress.episodeName || null, art })
      : await watchNow(request);
    if (!started || onTv) setStarting(false);
  };
  return (
    <Button variant="bare" className="kc-continue" disabled={starting || !online} onClick={start}
      aria-label={onTv ? `Watch on TV: ${name}` : undefined}>
      <div className="kc-poster-art kc-poster-art--wide">
        {art ? <img src={art} alt="" loading="lazy" /> : <span className="kc-poster-art__name">{name}</span>}
        <span className="kc-continue__play"><Icon name={onTv ? "tv" : "play"} size={16} /></span>
      </div>
      <span className="kc-poster__name">{name}</span>
      <span className="kc-poster__note">
        {starting ? "Starting…" : progress.remainingSeconds > 0 ? timeLeft(progress.remainingSeconds) : progress.episodeName}
      </span>
    </Button>
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
 * What kind of title a card holds, as the line on its poster above the name: a film, or a series in
 * the accent, either one named anime on that shelf. `kind` is the API's `film` or `show`.
 */
export function kindLine(kind, title) {
  const anime = isAnime(title);
  return kind === "show"
    ? { icon: "tv", label: anime ? "Anime series" : "Series", tone: "accent" }
    : { icon: "clapperboard", label: anime ? "Anime movie" : "Movie" };
}

/** A card's marks for a download on this device: the pill on its poster, and its progress while under way. */
function downloadMarks(download) {
  if (!download) return {};
  if (download.kind === "done") return { corner: { icon: "circle-check", label: "Downloaded" }, className: "kc-card--dl kc-card--dl-done" };
  return { corner: { label: `Downloading ${download.percent}%` }, progress: download.percent / 100, className: "kc-card--dl kc-card--dl-underway" };
}

/**
 * A film on a shelf, as krystal's ShowcaseCard: the poster with the name on it, its rating, year
 * and length, one line saying where it stands, Watch, and a ribbon that saves it. What a page knows
 * beside the film — a room watching it, whether it is new — is optional, and a card without it says
 * less rather than guessing. How far the person got comes with the title itself.
 *
 * The one status line, in precedence order: a film not ready says so; then a room watching it; then
 * how far this person got; then its genres. A film downloaded to this device (`download`, from
 * `useDownloads`) has a pill on its poster, and one downloading has its progress along the poster's foot.
 */
export function FilmCard({ title, room, isNew, download }) {
  const film = title.film || {};
  const d = title.details;
  const progress = title.progress && !title.progress.finished && title.progress.positionSeconds > 0 ? title.progress : null;
  const poster = posterUrl(title);
  const ready = title.status === "ready";
  const link = useWatchLink(title.id, room && room.sessionId, progress && progress.positionSeconds);
  const [ribbon, lists] = useSaveRibbon(film.imdbId, "film", film.name || title.title);

  const status =
    title.status === "transcoding" ? { tone: "notice", icon: "loader", label: "Still preparing" }
    : title.status === "failed" ? { tone: "warning", icon: "triangle-alert", label: "Could not be prepared" }
    : room ? { tone: "active", icon: "users", label: room.paused ? "A room has it paused" : "A room is watching" }
    : progress ? { tone: "active", icon: "history", label: timeLeft(Math.max(0, title.durationSeconds - progress.positionSeconds)) }
    : d && d.genres.length > 0 ? { label: d.genres.slice(0, 2).join(" · ") }
    : null;

  return (
    <>
      <ShowcaseCard
        name={film.name || title.title}
        eyebrow={kindLine("film", title)}
        art={poster ? `url("${poster}")` : null}
        onPick={() => navigate(moviePath(title.id, title.category))}
        badge={isNew ? { tone: "info", label: "New" } : null}
        {...downloadMarks(download)}
        specs={[
          { icon: "star", value: d && d.rating ? d.rating.toFixed(1) : null, label: "Rating" },
          { icon: "calendar", value: film.year || null, label: "Year" },
          { icon: "clock", value: title.durationSeconds ? clock(title.durationSeconds) : null, label: "Length" },
        ]}
        status={status}
        action={ready
          ? { label: room ? "Join" : progress ? "Resume" : "Watch", onClick: link.disabled ? undefined : () => navigate(link.href) }
          : { label: "View" }}
        ribbon={ribbon} />
      {lists}
    </>
  );
}

/**
 * A show on a shelf: its poster named a series, a short bar per season along the poster's foot filled
 * as far as this person watched it, its rating, seasons and episodes, where this person picks up,
 * Watch or Resume into the episode that is next, and its ribbon. A show with episodes downloaded to
 * this device (`downloaded`) says how many on its poster.
 */
export function ShowCard({ show, downloaded = 0 }) {
  const d = show.details;
  const resume = show.resume;
  const status =
    resume ? { tone: "active", icon: "history", label: `${episodeCode({ season: resume.season, number: resume.episode })}${resume.positionSeconds > 0 ? " · " + timeLeft(resume.durationSeconds - resume.positionSeconds) : ""}` }
    : d && d.genres.length > 0 ? { label: d.genres.slice(0, 2).join(" · ") }
    : null;
  const to = resume ? resume.titleId : show.firstTitleId;
  const link = useWatchLink(to, null, resume && resume.positionSeconds);
  const [ribbon, lists] = useSaveRibbon(show.imdbId, "show", show.title);

  return (
    <>
      <ShowcaseCard
        name={show.title}
        eyebrow={kindLine("show", show)}
        segments={show.seasonProgress ? show.seasonProgress.map((x) => (x.episodes > 0 ? x.watched / x.episodes : 0)) : null}
        art={d && d.posterUrl ? `url("${d.posterUrl}")` : null}
        onPick={() => navigate(show.imdbId ? showPath(show.imdbId, { category: show.category }) : moviePath(show.firstTitleId, show.category))}
        {...(downloaded > 0 ? { corner: { icon: "circle-check", label: `${downloaded} downloaded` }, className: "kc-card--dl kc-card--dl-done" } : {})}
        specs={[
          { icon: "star", value: d && d.rating ? d.rating.toFixed(1) : null, label: "Rating" },
          { icon: "layers", value: show.seasons.length, label: show.seasons.length === 1 ? "Season" : "Seasons" },
          { icon: "list-video", value: show.episodes, label: show.episodes === 1 ? "Episode" : "Episodes" },
        ]}
        status={status}
        action={{ label: resume ? "Resume" : "Watch", onClick: link.disabled ? undefined : () => navigate(link.href) }}
        ribbon={ribbon} />
      {lists}
    </>
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
      {onRetry && <Button variant="outline" size="lg" onClick={onRetry}>Try again</Button>}
    </div>
  );
}

