import React from "react";
import { Icon, Rail } from "@thekrystalship/krystal-ui";
import { api, filmName, posterUrl } from "../api.js";
import { useLoad } from "../data.js";
import { useSession } from "../session.js";
import { discoverPath, leavingPath, libraryPath, listPath, moviePath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { clock, since } from "../time.js";
import { ContinueCard, Empty, Failed, FilmCard, PosterArt, ShowCard, watchLink } from "../components/Film.jsx";
import { FilmScore } from "../components/FilmDetails.jsx";
import { BellButton } from "../components/Bell.jsx";
import { SavedCard } from "./Saved.jsx";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";

const RECENT_DAYS = 14;
const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * The front page: a film to watch, where this person left off, the rooms watching something now,
 * their Watch later, what arrived lately, what leaves next, and the shelf split into films and shows.
 * Every row is something the API measured; a row with nothing in it is left out rather than drawn
 * empty.
 */
export function Home() {
  const session = useSession();
  const me = session && session.user;
  const titles = useLoad(() => api.titles(), []);
  const rooms = useLoad(() => api.rooms(), [], { every: 15000 });
  const stays = useLoad(() => api.stays().catch(() => []), []);
  const progress = useLoad(() => api.progress().catch(() => []), []);
  const showList = useLoad(() => api.shows().catch(() => []), []);
  const later = useLoad(() => api.list("later").catch(() => null), []);
  // The Movies and TV Shows rows are dealt in a new order on every visit, and keep it while the page
  // is open, so a read answered again never moves a poster out from under somebody.
  const [deal] = React.useState(() => Math.random().toString(36).slice(2));
  const saved = (later.data && later.data.entries) || [];

  const library = (titles.data || []).filter((t) => t.status !== "failed");
  const byId = new Map(library.map((t) => [t.id, t]));
  const shows = showList.data || [];
  const showByImdb = new Map(shows.filter((s) => s.imdbId).map((s) => [s.imdbId, s]));
  // An episode stands for its show on a shelf.
  const showOfTitle = (t) => t && t.episode && t.film && showByImdb.get(t.film.imdbId);
  // Watching now means somebody is there: an empty room is kept a while after the last person leaves.
  const watching = (rooms.data || []).filter((r) => r.titleId && r.participants > 0 && byId.has(r.titleId));
  // What leaves next: every settled film and season nobody has kept, soonest first. A season is one
  // entry, as the API lists it.
  const leaving = (stays.data || [])
    .filter((s) => s.settled && !s.keeperId && s.daysLeft != null && byId.has(s.titleId))
    .sort((a, b) => a.daysLeft - b.daysLeft);
  const continuing = (progress.data || []).filter((p) => byId.has(p.titleId));
  const isRecent = (at) => at && Date.now() - Date.parse(at) <= RECENT_DAYS * DAY_MS;
  const films = library.filter((t) => t.kind !== "show");
  const recent = [
    ...films.filter((t) => isRecent(t.addedAt)).map((t) => ({ film: t, at: t.addedAt })),
    ...shows.filter((s) => isRecent(s.addedAt)).map((s) => ({ show: s, at: s.addedAt })),
  ].sort((a, b) => b.at.localeCompare(a.at));
  const stayOf = new Map((stays.data || []).map((s) => [s.titleId, s]));
  const recentIds = new Set(recent.filter((r) => r.film).map((r) => r.film.id));
  const card = (t) => (
    <FilmCard title={t} stay={stayOf.get(t.id)} me={me} room={watching.find((r) => r.titleId === t.id)} isNew={recentIds.has(t.id)} />
  );
  const either = (item) => (item.show ? <ShowCard show={item.show} stay={item.stay} me={me} /> : card(item.film));
  const keyOf = (item) => (item.show ? "show:" + (item.show.imdbId || item.show.firstTitleId) : item.film.id);
  const leavingItems = leaving.map((s) => {
    const t = byId.get(s.titleId);
    const show = showOfTitle(t);
    return show ? { show, stay: s } : { film: t };
  });

  const featured = newest(films);

  if (titles.error && !titles.data) return <div className="kc-page"><PhoneHead /><Failed error={titles.error} onRetry={titles.reload} /></div>;
  if (titles.loading && !titles.data) return <div className="kc-page"><PhoneHead /></div>;
  if (library.length === 0) {
    return (
      <div className="kc-page">
        <PhoneHead />
        <Empty title="The library is empty" />
      </div>
    );
  }

  return (
    <div className="kc-page kc-home">
      <PhoneHead />
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

      {saved.length > 0 && (
        <Rail
          variant="posters" icon="bookmark" title="Watch later" count={saved.length}
          onViewAll={() => navigate(listPath("later"))} viewAllLabel="See all"
          items={saved} itemKey={(e) => e.imdbId}
          renderItem={(e) => <SavedCard entry={e} />} />
      )}

      {recent.length > 0 && (
        <Rail
          variant="posters" icon="sparkles" title="Recently added" count={recent.length}
          items={recent} itemKey={keyOf}
          renderItem={either} />
      )}

      {leavingItems.length > 0 && (
        <Rail
          variant="posters" icon="hourglass" title="Leaving soon" count={leavingItems.length}
          onViewAll={() => navigate(leavingPath)} viewAllLabel="See all"
          items={leavingItems} itemKey={(item) => (item.show ? "season:" + item.stay.unit : item.film.id)}
          renderItem={either} />
      )}

      {films.length > 0 && (
        <Rail
          variant="posters" icon="clapperboard" title="Movies" count={films.length}
          onViewAll={() => navigate(libraryPath("film"))} viewAllLabel="All movies"
          items={[...dealt(films, (t) => t.id, deal), MORE]} itemKey={(t) => (t === MORE ? "more" : t.id)}
          renderItem={(t) => (t === MORE ? <MoreTile label="Any movie" /> : card(t))} />
      )}

      {shows.length > 0 && (
        <Rail
          variant="posters" icon="tv" title="TV Shows" count={shows.length}
          onViewAll={() => navigate(libraryPath("show"))} viewAllLabel="All shows"
          items={[...dealt(shows, showKey, deal), MORE]} itemKey={(s) => (s === MORE ? "more" : showKey(s))}
          renderItem={(s) => (s === MORE ? <MoreTile label="Any show" /> : <ShowCard show={s} me={me} />)} />
      )}
    </div>
  );
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
          <a className="kc-btn" {...linkProps(moviePath(title.id))}>
            <Icon name="info" size={18} />More info
          </a>
        </div>
      </div>
    </section>
  );
}

/** The last place on a shelf of the library, held by the tile that leads past it. */
const MORE = { more: true };

/** The tile closing a shelf: past the last poster here is everything Discover can find. */
function MoreTile({ label }) {
  return (
    <a className="kc-more-tile" {...linkProps(discoverPath())}>
      <Icon name="compass" size={26} />
      <span>{label}</span>
    </a>
  );
}

const showKey = (s) => s.imdbId || s.firstTitleId;

/**
 * `items` in an order fixed by `deal`: each is ranked by a hash of the deal and its key, so one deal
 * always lays out the same items the same way, and an item that arrives or leaves moves nothing else.
 */
function dealt(items, keyOf, deal) {
  const rank = (item) => {
    let h = 0x811c9dc5;
    for (const c of deal + keyOf(item)) h = Math.imul(h ^ c.charCodeAt(0), 0x01000193);
    return h >>> 0;
  };
  return items.map((item) => [rank(item), item]).sort((a, b) => a[0] - b[0]).map(([, item]) => item);
}

/** The newest ready film: what arrived last is what is worth putting at the top. */
function newest(library) {
  const ready = library.filter((t) => t.status === "ready");
  const pool = ready.length > 0 ? ready : library;
  return pool.reduce((best, t) => (!best || (t.addedAt || "") > (best.addedAt || "") ? t : best), null);
}

/**
 * The top of the front page on a phone, where there is no sidebar: the mark, the name, and the bell
 * at the right. A wide screen has all three in the sidebar and does not draw it.
 */
function PhoneHead() {
  return (
    <div className="kc-phonehead">
      <img src={markUrl} width="26" height="26" alt="" />
      <span className="kc-phonehead__name">Krystal Cinema</span>
      <BellButton />
    </div>
  );
}
