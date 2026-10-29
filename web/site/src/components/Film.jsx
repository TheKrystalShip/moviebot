import React from "react";
import { Button, Icon, IconButton, PosterCard } from "@thekrystalship/krystal";
import { cardRating, episodeCode, filmName, posterArt, posterUrl } from "../api/index.js";
import { useOnline } from "../connectivity.js";
import { useSave } from "../offline/hooks.js";
import { VotableCard } from "./VotableCard.jsx";
import { useWantRibbon } from "./WantButton.jsx";
import { moviePath, playSavedPath, roomPath, showPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { clock, timeLeft } from "../time.js";
import { counted } from "../counts.js";
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
 * A Continue watching card and, when `onRemove` is given, a button over its corner that takes it off
 * the list. A pointer that hovers sees the button on the card it is over; a touch screen always does.
 */
function Removable({ name, onRemove, children }) {
  if (!onRemove) return children;
  return (
    <div className="kc-continue-item">
      {children}
      <IconButton variant="ghost" size="sm" icon="x" className="kc-continue__remove"
        label={`Remove ${name} from Continue watching`} onClick={onRemove} />
    </div>
  );
}

/**
 * A film started and not finished: its artwork, how far in, how much is left, and a way back into it.
 * `onTv` is the card on a phone driving a television, on its remote and on Home: choosing it puts the
 * film on the television, from where this person got to, and the glyph on it is the television's.
 * `onRemove` takes it off Continue watching.
 */
export function ContinueCard({ title, progress, remainingSeconds, room, onTv = false, onRemove }) {
  const art = (title.card && title.card.backdrop) || posterUrl(title);
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
    return (
      <Removable name={filmName(title)} onRemove={onRemove}>
        <Button variant="bare" className="kc-continue" disabled={starting} aria-label={`Watch on TV: ${filmName(title)}`} onClick={start}>{body}</Button>
      </Removable>
    );
  }
  return (
    <Removable name={filmName(title)} onRemove={onRemove}>
      <a className="kc-continue" {...(link.disabled ? { "aria-disabled": "true" } : link)}>{body}</a>
    </Removable>
  );
}

/**
 * The episode after one somebody finished, when the library does not hold it (`GET /api/me/progress`
 * with no `titleId`): its still, else the show's backdrop, the show's name and its place in it.
 * Choosing it is Watch now, which fetches it into a room and goes there. `onRemove` takes the show off
 * Continue watching.
 */
export function NextEpisodeCard({ progress, show, onTv = false, onRemove }) {
  const [starting, setStarting] = React.useState(false);
  const online = useOnline();
  const card = show && show.card;
  const art = progress.stillUrl || (card && (card.backdrop || card.poster));
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
    <Removable name={progress.name} onRemove={onRemove}>
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
    </Removable>
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

/** A card's marks for a download on this device: the pill on its poster, and its progress while under way. */
function downloadMarks(download) {
  if (!download) return {};
  if (download.kind === "done") return { corner: { icon: "circle-check", label: "Downloaded", tone: "accent" } };
  return { corner: { label: `Downloading ${download.percent}%`, tone: "neutral", mono: true }, progress: download.percent / 100, className: "kc-card--dl-underway" };
}

/**
 * A film on a shelf, as krystal's PosterCard: the poster, and under it the name, its rating, year
 * and length, and one line saying where it stands; Watch over the poster, and a ribbon that wants it. What a page knows
 * beside the film — a room watching it, whether it is new — is optional, and a card without it says
 * less rather than guessing. How far the person got comes with the title itself.
 *
 * The one status line, in precedence order: a film not ready says so; then a room watching it; then
 * how far this person got; then its genres. A film downloaded to this device (`download`, from
 * `useDownloads`) has a pill on its poster, and one downloading has its progress along the poster's foot.
 */
export function FilmCard({ title, room, isNew, download }) {
  const film = title.film || {};
  const d = title.card;
  const progress = title.progress && !title.progress.finished && title.progress.positionSeconds > 0 ? title.progress : null;
  const poster = posterUrl(title);
  const ready = title.status === "ready";
  const link = useWatchLink(title.id, room && room.sessionId, progress && progress.positionSeconds);
  const [ribbon, wantMenu] = useWantRibbon(film.imdbId, "film", film.name || title.title);

  const status =
    title.status === "transcoding" ? { tone: "notice", icon: "loader", label: "Still preparing" }
    : title.status === "failed" ? { tone: "warning", icon: "triangle-alert", label: "Could not be prepared" }
    : room ? { tone: "active", icon: "users", label: room.paused ? "A room has it paused" : "A room is watching" }
    : progress ? { tone: "active", icon: "history", label: timeLeft(Math.max(0, title.durationSeconds - progress.positionSeconds)) }
    : d && d.genres.length > 0 ? { label: d.genres.slice(0, 2).join(" · ") }
    : null;

  const name = (d && d.title) || film.name || title.title;
  const to = moviePath(title.id, d && d.category);
  return (
    <>
      <VotableCard imdbId={film.imdbId} kind="film" name={name} year={(d && d.year) || film.year} category={d && d.category} art={poster ? `url("${poster}")` : null} to={to}>
      <PosterCard
        name={name}
        art={poster ? `url("${poster}")` : null}
        onPick={() => navigate(to)}
        badge={isNew ? { tone: "info", label: "New" } : null}
        {...downloadMarks(download)}
        facts={[
          { icon: "star", value: cardRating(d), label: "Rating" },
          { value: (d && d.year) || film.year || null, label: "Year" },
          { value: title.durationSeconds ? clock(title.durationSeconds) : null, label: "Length" },
        ]}
        status={status}
        action={ready && !link.disabled
          ? { label: room ? "Join" : progress ? "Resume" : "Watch", icon: room ? "users" : "play", onClick: () => navigate(link.href) }
          : undefined}
        ribbon={ribbon} />
      </VotableCard>
      {wantMenu}
    </>
  );
}

/**
 * A show on a shelf, as krystal's PosterCard: its poster, a short bar per season along the poster's foot filled
 * as far as this person watched it, its rating, seasons and episodes, where this person picks up,
 * Watch or Resume into the episode that is next, and its ribbon, which wants it. A show with episodes downloaded to
 * this device (`downloaded`) says how many on its poster.
 */
export function ShowCard({ show, downloaded = 0 }) {
  const d = show.card;
  const resume = show.resume;
  const status =
    resume ? { tone: "active", icon: "history", label: `${episodeCode({ season: resume.season, number: resume.episode })}${resume.positionSeconds > 0 ? " · " + timeLeft(resume.durationSeconds - resume.positionSeconds) : ""}` }
    : d && d.genres.length > 0 ? { label: d.genres.slice(0, 2).join(" · ") }
    : null;
  const to = resume ? resume.titleId : show.firstTitleId;
  const link = useWatchLink(to, null, resume && resume.positionSeconds);
  const [ribbon, wantMenu] = useWantRibbon(show.imdbId, "show", show.title);

  const page = show.imdbId ? showPath(show.imdbId, { category: d && d.category }) : moviePath(show.firstTitleId, d && d.category);
  return (
    <>
      <VotableCard imdbId={show.imdbId} kind="show" name={show.title} year={d && d.year} category={d && d.category} art={posterArt(d)} to={page}>
      <PosterCard
        name={show.title}
        segments={show.seasonProgress ? show.seasonProgress.map((x) => (x.episodes > 0 ? x.watched / x.episodes : 0)) : null}
        art={posterArt(d)}
        onPick={() => navigate(page)}
        {...(downloaded > 0 ? { corner: { icon: "circle-check", label: `${downloaded} downloaded`, tone: "accent" } } : {})}
        facts={[
          { icon: "star", value: cardRating(d), label: "Rating" },
          { value: counted(show.seasons.length, "season"), label: "Seasons" },
          { value: counted(show.episodes, "episode"), label: "Episodes" },
        ]}
        status={status}
        action={link.disabled ? undefined : { label: resume ? "Resume" : "Watch", onClick: () => navigate(link.href) }}
        ribbon={ribbon} />
      </VotableCard>
      {wantMenu}
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

