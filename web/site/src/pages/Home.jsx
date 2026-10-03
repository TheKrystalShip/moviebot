import { Icon, Rail } from "@thekrystalship/krystal-ui";
import { api, filmName, posterUrl } from "../api.js";
import { SOON_DAYS, useLoad, stayWords } from "../data.js";
import { useSession } from "../session.js";
import { linkProps, navigate } from "../router.js";
import { clock, since } from "../time.js";
import { ContinueCard, Empty, Failed, PosterArt, PosterCard, watchLink } from "../components/Film.jsx";
import { FilmScore } from "../components/FilmDetails.jsx";

const RECENT_DAYS = 14;
const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * The front page: a film to watch, where this person left off, the rooms watching something now,
 * what arrived lately, what leaves next, what is on its way, and the shelf split into films and
 * shows. Every row is something the API measured; a row with nothing in it is left out rather than
 * drawn empty.
 */
export function Home() {
  const session = useSession();
  const me = session && session.user;
  const titles = useLoad(() => api.titles(), []);
  const rooms = useLoad(() => api.rooms(), [], { every: 15000 });
  const downloads = useLoad(() => api.downloads().catch(() => []), [], { every: 10000 });
  const stays = useLoad(() => api.stays().catch(() => []), []);
  const progress = useLoad(() => api.progress().catch(() => []), []);

  const library = (titles.data || []).filter((t) => t.status !== "failed");
  const byId = new Map(library.map((t) => [t.id, t]));
  // Watching now means somebody is there: an empty room is kept a while after the last person leaves.
  const watching = (rooms.data || []).filter((r) => r.titleId && r.participants > 0 && byId.has(r.titleId));
  const arriving = (downloads.data || []).filter((d) => !d.isFinished);
  // What leaves next: every settled film nobody has kept, soonest first.
  const leaving = (stays.data || [])
    .filter((s) => s.settled && !s.keeperId && s.daysLeft != null && byId.has(s.titleId))
    .sort((a, b) => a.daysLeft - b.daysLeft);
  const continuing = (progress.data || []).filter((p) => byId.has(p.titleId));
  const recent = library
    .filter((t) => t.addedAt && Date.now() - Date.parse(t.addedAt) <= RECENT_DAYS * DAY_MS)
    .sort((a, b) => b.addedAt.localeCompare(a.addedAt));
  const films = library.filter((t) => t.kind !== "show");
  const shows = library.filter((t) => t.kind === "show");

  const featured = newest(library);

  if (titles.error && !titles.data) return <div className="kc-page"><Failed error={titles.error} onRetry={titles.reload} /></div>;
  if (titles.loading && !titles.data) return <div className="kc-page" />;
  if (library.length === 0) {
    return (
      <div className="kc-page">
        <Empty title="The library is empty">Search for a film to fetch the first one.</Empty>
      </div>
    );
  }

  return (
    <div className="kc-page kc-home">
      {featured && <Featured title={featured} room={watching.find((r) => r.titleId === featured.id)} />}

      {continuing.length > 0 && (
        <Rail
          variant="wide" icon="history" title="Continue watching" count={continuing.length}
          items={continuing} itemKey={(p) => p.titleId}
          renderItem={(p) => (
            <ContinueCard
              title={byId.get(p.titleId)}
              progress={p}
              remainingSeconds={p.remainingSeconds}
              room={watching.find((r) => r.titleId === p.titleId)} />
          )} />
      )}

      {watching.length > 0 && (
        <Rail
          variant="wide" icon="users" title="Watching now" count={watching.length}
          items={watching} itemKey={(room) => room.sessionId}
          renderItem={(room) => (
            <a className="kc-room" {...watchLink(room.titleId, room.sessionId)}>
              <PosterArt title={byId.get(room.titleId)} className="kc-poster-art--wide">
                <span className="kc-room__join"><Icon name="users" size={14} />Join</span>
              </PosterArt>
              <span className="kc-poster__name">{filmName(byId.get(room.titleId))}</span>
              <span className="kc-poster__note">
                {room.participants} watching · {room.paused ? "paused" : "playing"} at{" "}
                <span className="kc-mono">{clock(room.positionSeconds)}</span>
              </span>
            </a>
          )} />
      )}

      {recent.length > 0 && (
        <Rail
          variant="posters" icon="sparkles" title="Recently added" count={recent.length}
          items={recent} itemKey={(t) => t.id}
          renderItem={(t) => (
            <PosterCard title={t} note={t.status === "transcoding" ? "Still preparing" : since(t.addedAt)} />
          )} />
      )}

      {leaving.length > 0 && (
        <Rail
          variant="posters" icon="hourglass" title="Leaving soon" count={leaving.length}
          onViewAll={() => navigate("/leaving")} viewAllLabel="See all"
          items={leaving} itemKey={(s) => s.titleId}
          renderItem={(s) => (
            <PosterCard title={byId.get(s.titleId)} note={stayWords(s, me)} tone={s.daysLeft <= SOON_DAYS ? "warn" : undefined} />
          )} />
      )}

      {arriving.length > 0 && (
        <Rail
          variant="arriving" icon="download" title="On its way" count={arriving.length}
          onViewAll={() => navigate("/downloads")} viewAllLabel="All downloads"
          items={arriving} itemKey={(d) => d.hash}
          renderItem={(d) => (
            <div className="kc-arriving">
              <span className="kc-poster__name">{d.name}</span>
              <span className="kc-poster__note">{d.summary}{d.isWatchable ? " · watchable now" : ""}</span>
            </div>
          )} />
      )}

      {films.length > 0 && (
        <Rail
          variant="posters" icon="clapperboard" title="Movies" count={films.length}
          onViewAll={() => navigate("/library")} viewAllLabel="All movies"
          items={films} itemKey={(t) => t.id}
          renderItem={(t) => <ShelfCard title={t} />} />
      )}

      {shows.length > 0 && (
        <Rail
          variant="posters" icon="tv" title="TV Shows" count={shows.length}
          items={shows} itemKey={(t) => t.id}
          renderItem={(t) => <ShelfCard title={t} />} />
      )}
    </div>
  );
}

/** A title on the shelf, with how long it runs, or that it is not ready yet. */
function ShelfCard({ title }) {
  return <PosterCard title={title} note={title.status === "transcoding" ? "Still preparing" : clock(title.durationSeconds)} />;
}

/** The film at the top: the newest on the shelf, with what the film database says about it. */
function Featured({ title, room }) {
  const d = title.details;
  const art = (d && d.backdropUrl) || posterUrl(title);
  const film = title.film || {};
  return (
    <section className="kc-featured">
      {art && <div className="kc-featured__art" style={{ backgroundImage: `url("${art}")` }} />}
      <div className="kc-featured__scrim" />
      <div className="kc-featured__body">
        <span className="kc-eyebrow">{room ? "Watching now" : title.addedAt ? `Added ${since(title.addedAt).toLowerCase()}` : "From the library"}</span>
        <h1>{film.name || title.title}</h1>
        <div className="kc-featured__meta">
          {d && d.rating && <FilmScore rating={d.rating} votes={d.votes} compact />}
          {film.year && <span>{film.year}</span>}
          {d && d.certification && <span className="kc-cert">{d.certification}</span>}
          <span>{clock(title.durationSeconds)}</span>
          {d && d.genres.length > 0 && <span>{d.genres.join(" · ")}</span>}
        </div>
        {d && d.overview && <p className="kc-featured__overview">{d.overview}</p>}
        {d && d.directors.length > 0
          ? <span className="kc-featured__starring">Directed by <b>{d.directors.join(", ")}</b></span>
          : film.starring && <span className="kc-featured__starring">Starring <b>{film.starring}</b></span>}
        <div className="kc-actions">
          <a className="kc-btn kc-btn--primary" {...watchLink(title.id, room && room.sessionId)}>
            <Icon name={room ? "users" : "play"} size={18} />{room ? "Join the room" : "Watch"}
          </a>
          <a className="kc-btn" {...linkProps("/title/" + encodeURIComponent(title.id))}>
            <Icon name="info" size={18} />More info
          </a>
        </div>
      </div>
    </section>
  );
}

/** The newest ready film: what arrived last is what is worth putting at the top. */
function newest(library) {
  const ready = library.filter((t) => t.status === "ready");
  const pool = ready.length > 0 ? ready : library;
  return pool.reduce((best, t) => (!best || (t.addedAt || "") > (best.addedAt || "") ? t : best), null);
}
