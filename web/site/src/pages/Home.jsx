import { Icon } from "@thekrystalship/krystal-ui";
import { api, clock, filmName, posterUrl } from "../api.js";
import { useLoad, staysFor } from "../data.js";
import { linkProps } from "../router.js";
import { Empty, Failed, PosterArt, PosterCard, Shelf, watchHref } from "../components/Film.jsx";

const LEAVING_SOON_DAYS = 3;

/**
 * The front page: a film to watch, the rooms watching something now, what is on its way, what is
 * about to leave, and the shelf. Every row is something the API measured; a row with nothing in it is
 * left out rather than drawn empty.
 */
export function Home() {
  const titles = useLoad(() => api.titles(), []);
  const rooms = useLoad(() => api.rooms(), [], { every: 15000 });
  const downloads = useLoad(() => api.downloads().catch(() => []), [], { every: 10000 });
  const keeps = useLoad(() => api.keeps().catch(() => []), []);

  const library = (titles.data || []).filter((t) => t.status !== "failed");
  const byId = new Map(library.map((t) => [t.id, t]));
  const watching = (rooms.data || []).filter((r) => r.titleId && byId.has(r.titleId));
  const arriving = (downloads.data || []).filter((d) => !d.isFinished);
  const leaving = (keeps.data || [])
    .filter((k) => !k.keeper && k.isFinished && k.libraryId && byId.has(k.libraryId)
      && k.remainingSeconds < LEAVING_SOON_DAYS * 86400)
    .sort((a, b) => a.remainingSeconds - b.remainingSeconds);

  const featured = watching.length > 0 ? byId.get(watching[0].titleId) : pickOfTheDay(library);

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

      {watching.length > 0 && (
        <Shelf title="Watching now" icon="users">
          {watching.map((room) => (
            <a key={room.sessionId} className="kc-room" href={watchHref(room.titleId, room.sessionId)}>
              <PosterArt title={byId.get(room.titleId)} className="kc-poster-art--wide" />
              <span className="kc-room__join"><Icon name="users" size={14} />Join</span>
              <span className="kc-poster__name">{filmName(byId.get(room.titleId))}</span>
              <span className="kc-poster__note">
                {room.participants} watching · {room.paused ? "paused" : "playing"} at{" "}
                <span className="kc-mono">{clock(room.positionSeconds)}</span>
              </span>
            </a>
          ))}
        </Shelf>
      )}

      {arriving.length > 0 && (
        <Shelf title="On its way" icon="download" more={{ href: "/downloads", label: "All downloads" }}>
          {arriving.map((d) => (
            <div key={d.hash} className="kc-arriving">
              <span className="kc-poster__name">{d.name}</span>
              <span className="kc-poster__note">{d.summary}{d.isWatchable ? " · watchable now" : ""}</span>
            </div>
          ))}
        </Shelf>
      )}

      {leaving.length > 0 && (
        <Shelf title="Leaving soon" icon="hourglass" more={{ href: "/leaving", label: "See all" }}>
          {leaving.map((k) => (
            <PosterCard key={k.hash} title={byId.get(k.libraryId)} note={staysFor(k)} tone="warn" />
          ))}
        </Shelf>
      )}

      <Shelf title="In the library" icon="clapperboard" more={{ href: "/library", label: `All ${library.length}` }}>
        {library.slice(0, 18).map((t) => (
          <PosterCard key={t.id} title={t} note={t.status === "transcoding" ? "Still preparing" : clock(t.durationSeconds)} />
        ))}
      </Shelf>
    </div>
  );
}

/** The film at the top: the one a room is watching, or otherwise one from the shelf for the day. */
function Featured({ title, room }) {
  const poster = posterUrl(title);
  const film = title.film || {};
  return (
    <section className="kc-featured">
      {poster && <div className="kc-featured__art" style={{ backgroundImage: `url("${poster}")` }} />}
      <div className="kc-featured__scrim" />
      <div className="kc-featured__body">
        <span className="kc-eyebrow">{room ? "Watching now" : "From the library"}</span>
        <h1>{film.name || title.title}</h1>
        <div className="kc-featured__meta">
          {film.year && <span>{film.year}</span>}
          <span>{clock(title.durationSeconds)}</span>
        </div>
        {film.starring && <span className="kc-featured__starring">Starring <b>{film.starring}</b></span>}
        <div className="kc-actions">
          <a className="kc-btn kc-btn--primary" href={watchHref(title.id, room && room.sessionId)}>
            <Icon name={room ? "users" : "play"} size={18} />{room ? "Join the room" : "Watch"}
          </a>
          <a className="kc-btn" {...linkProps("/title/" + encodeURIComponent(title.id))}>
            <Icon name="info" size={18} />More about it
          </a>
        </div>
      </div>
    </section>
  );
}

/** One ready film for the day: the same all day, a different one tomorrow. */
function pickOfTheDay(library) {
  const ready = library.filter((t) => t.status === "ready");
  if (ready.length === 0) return library[0] || null;
  const day = Math.floor(Date.now() / 86400000);
  return ready[day % ready.length];
}
