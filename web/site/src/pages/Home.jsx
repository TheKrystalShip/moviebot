import React from "react";
import { Icon, Rail } from "@thekrystalship/krystal";
import { api, filmName } from "../api.js";
import { useLoad } from "../data.js";
import { ANIME, isAnime, libraryPath, listPath } from "../paths.js";
import { navigate } from "../router.js";
import { ago, clock } from "../time.js";
import { useCan } from "../session.js";
import { useOnline } from "../connectivity.js";
import { useDeviceLibrary } from "../offline/hooks.js";
import { Off } from "./Offline.jsx";
import { useDownloadMarks } from "../components/DownloadButton.jsx";
import { ContinueCard, Empty, Failed, FilmCard, NextEpisodeCard, PosterArt, ShowCard, watchLink } from "../components/Film.jsx";
import { AnticipatedCard, MoreTile } from "../components/TitleCards.jsx";
import { Spotlight } from "../components/Spotlight.jsx";
import { useHomeSlides, useSlides } from "../heroes.js";
import { BellButton } from "../components/Bell.jsx";
import { SavedCard } from "./Saved.jsx";
import markUrl from "@thekrystalship/krystal/assets/tks-mark.png";
import { inActivity } from "../discord/launch.js";
import { useDriving } from "../components/WatchOnTv.jsx";

const RECENT_DAYS = 14;
/** How many of the films downloaded last the top turns through when there is no connection. */
const DOWNLOADED_SLIDES = 3;
const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * The front page: its top turning through what is worth watching now (`heroes.js`): the film people
 * are watching together, the newest film, the title most watched here, the week's most popular film
 * and show, and the film most waited on. Under it, where this person left off, the rooms watching something now,
 * their Watch later, what arrived lately, the films people are waiting on, and the shelf split into
 * films, shows and anime. Every row is something the API measured; a row with nothing in it is left
 * out rather than drawn empty.
 * `fromDevice` is the page opened with no connection, drawing what is downloaded (`DeviceHome`).
 */
export function Home({ fromDevice = false }) {
  return fromDevice ? <DeviceHome /> : <ServerHome />;
}

/**
 * The front page from the downloads on this device: the films downloaded last turning at the top, where this
 * person left off in what is here, and the films, the shows and the anime here, each leading to the
 * library. A row that would list what is not here is not drawn.
 */
function DeviceHome() {
  const device = useDeviceLibrary();
  const marks = useDownloadMarks();
  const slides = useSlides(React.useMemo(() => device.data && downloadedSlides(device.data.films), [device.data]));
  if (!device.data) return <div className="kc-page"><PhoneHead /></div>;
  const { films, shows, titles, continuing } = device.data;
  if (films.length === 0 && shows.length === 0) {
    return (
      <div className="kc-page">
        <PhoneHead />
        <Empty icon="download" title="Nothing downloaded" />
      </div>
    );
  }
  const latest = (a, b) => String(b.addedAt || "").localeCompare(String(a.addedAt || ""));
  const anime = [...films.filter(isAnime).map((t) => ({ film: t, at: t.addedAt })), ...shows.filter(isAnime).map((s) => ({ show: s, at: s.addedAt }))]
    .sort((a, b) => String(b.at || "").localeCompare(String(a.at || "")));
  const generalFilms = films.filter((t) => !isAnime(t));
  const generalShows = shows.filter((s) => !isAnime(s));
  return (
    <div className="kc-page kc-home">
      <PhoneHead />
      <Spotlight slides={slides} />

      {continuing.length > 0 && (
        <Rail
          variant="wide" icon="history" title="Continue watching" count={continuing.length}
          items={continuing} itemKey={(p) => p.titleId}
          renderItem={(p) => <ContinueCard title={titles.get(p.titleId)} progress={p} remainingSeconds={p.remainingSeconds} />} />
      )}

      {generalFilms.length > 0 && (
        <Rail
          variant="posters" icon="clapperboard" title="Movies" count={generalFilms.length}
          onViewAll={() => navigate(libraryPath("film"))} viewAllLabel="All movies"
          items={[...generalFilms].sort(latest)} itemKey={(t) => t.id}
          renderItem={(t) => <FilmCard title={t} download={marks.film(t.id)} />} />
      )}

      {generalShows.length > 0 && (
        <Rail
          variant="posters" icon="tv" title="TV Shows" count={generalShows.length}
          onViewAll={() => navigate(libraryPath("show"))} viewAllLabel="All shows"
          items={[...generalShows].sort(latest)} itemKey={showKey}
          renderItem={(s) => <ShowCard show={s} downloaded={marks.episodes(s.imdbId)} />} />
      )}

      {anime.length > 0 && (
        <Rail
          variant="posters" icon="swords" title="Anime" count={anime.length}
          onViewAll={() => navigate(libraryPath("show", ANIME))} viewAllLabel="All anime"
          items={anime} itemKey={itemKey}
          renderItem={(item) => (item.show
            ? <ShowCard show={item.show} downloaded={marks.episodes(item.show.imdbId)} />
            : <FilmCard title={item.film} download={marks.film(item.film.id)} />)} />
      )}
    </div>
  );
}

function ServerHome() {
  const titles = useLoad(() => api.titles(), []);
  // In the Discord Activity everything is watched in the call's room, which the call bar shows, so
  // other rooms are not offered.
  const rooms = useLoad(() => (inActivity ? Promise.resolve([]) : api.rooms()), [], { every: 15000 });
  const marks = useDownloadMarks();
  // While the phone drives a television, carrying on with something puts it on the television.
  const driving = useDriving();
  const progress = useLoad(() => api.progress().catch(() => []), []);
  const showList = useLoad(() => api.shows().catch(() => []), []);
  const later = useLoad(() => api.list("later").catch(() => null), []);
  const canWish = useCan("cinema:wish");
  const anticipated = useLoad(() => (canWish ? api.anticipated().catch(() => []) : Promise.resolve([])), [canWish]);
  const waitedOn = anticipated.data || [];
  // The Movies, TV Shows and Anime rows are dealt in a new order on every visit, and keep it while the
  // page is open, so a read answered again never moves a poster out from under somebody.
  const [deal] = React.useState(() => Math.random().toString(36).slice(2));
  const saved = (later.data && later.data.entries) || [];

  const library = (titles.data || []).filter((t) => t.status !== "failed");
  const byId = new Map(library.map((t) => [t.id, t]));
  const shows = showList.data || [];
  // Watching now means somebody is there: an empty room is kept a while after the last person leaves.
  const watching = (rooms.data || []).filter((r) => r.titleId && r.participants > 0 && byId.has(r.titleId));
  // An episode the library does not hold yet comes with no title, and is fetched when chosen.
  const continuing = (progress.data || []).filter((p) => (p.titleId ? byId.has(p.titleId) : p.episode && p.showImdbId));
  const showOf = new Map(shows.filter((s) => s.imdbId).map((s) => [s.imdbId, s]));
  const isRecent = (at) => at && Date.now() - Date.parse(at) <= RECENT_DAYS * DAY_MS;
  const films = library.filter((t) => t.kind !== "show");
  const recent = [
    ...films.filter((t) => isRecent(t.addedAt)).map((t) => ({ film: t, at: t.addedAt })),
    ...shows.filter((s) => isRecent(s.addedAt)).map((s) => ({ show: s, at: s.addedAt })),
  ].sort((a, b) => b.at.localeCompare(a.at));
  const recentIds = new Set(recent.filter((r) => r.film).map((r) => r.film.id));
  const card = (t) => (
    <FilmCard title={t} room={watching.find((r) => r.titleId === t.id)} isNew={recentIds.has(t.id)} download={marks.film(t.id)} />
  );
  const either = (item) => (item.show ? <ShowCard show={item.show} downloaded={marks.episodes(item.show.imdbId)} /> : card(item.film));
  // Each shelf's row holds its own: anime, films and shows together, is a row of its own.
  const generalFilms = films.filter((t) => !isAnime(t));
  const generalShows = shows.filter((s) => !isAnime(s));
  const anime = [...films.filter(isAnime).map((t) => ({ film: t })), ...shows.filter(isAnime).map((s) => ({ show: s }))];

  const slides = useHomeSlides({
    library: titles.data ? library : null, shows: showList.data, rooms: rooms.data, waitedOn: anticipated.data,
  });

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
      <Spotlight slides={slides} />

      {continuing.length > 0 && (
        <Rail
          variant="wide" icon="history" title="Continue watching" count={continuing.length}
          items={continuing} itemKey={continueKey}
          renderItem={(p) => !p.titleId ? <NextEpisodeCard progress={p} show={showOf.get(p.showImdbId)} onTv={driving} /> : (
            <ContinueCard
              title={byId.get(p.titleId)}
              progress={p}
              remainingSeconds={p.remainingSeconds}
              room={watching.find((r) => r.titleId === p.titleId)}
              onTv={driving} />
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
          items={recent} itemKey={itemKey}
          renderItem={either} />
      )}

      {waitedOn.length > 0 && (
        <Rail
          variant="posters" icon="bell" title="Anticipated" count={waitedOn.length}
          items={waitedOn} itemKey={(f) => f.imdbId}
          renderItem={(f) => <AnticipatedCard film={f} />} />
      )}

      {generalFilms.length > 0 && (
        <Rail
          variant="posters" icon="clapperboard" title="Movies" count={generalFilms.length}
          onViewAll={() => navigate(libraryPath("film"))} viewAllLabel="All movies"
          items={[...dealt(generalFilms, (t) => t.id, deal), MORE]} itemKey={(t) => (t === MORE ? "more" : t.id)}
          renderItem={(t) => (t === MORE ? <MoreTile label="Any movie" /> : card(t))} />
      )}

      {generalShows.length > 0 && (
        <Rail
          variant="posters" icon="tv" title="TV Shows" count={generalShows.length}
          onViewAll={() => navigate(libraryPath("show"))} viewAllLabel="All shows"
          items={[...dealt(generalShows, showKey, deal), MORE]} itemKey={(s) => (s === MORE ? "more" : showKey(s))}
          renderItem={(s) => (s === MORE ? <MoreTile label="Any show" /> : <ShowCard show={s} downloaded={marks.episodes(s.imdbId)} />)} />
      )}

      {anime.length > 0 && (
        <Rail
          variant="posters" icon="swords" title="Anime" count={anime.length}
          onViewAll={() => navigate(libraryPath("show", ANIME))} viewAllLabel="All anime"
          items={dealt(anime, itemKey, deal)} itemKey={itemKey}
          renderItem={either} />
      )}
    </div>
  );
}

/** The films downloaded last, newest first, as the top's slides. */
function downloadedSlides(films) {
  return [...films]
    .sort((a, b) => String(b.savedAt || "").localeCompare(String(a.savedAt || "")))
    .slice(0, DOWNLOADED_SLIDES)
    .map((t) => ({ key: "saved:" + t.id, eyebrow: `Downloaded ${ago(t.savedAt)}`, icon: "download", kind: "film", imdbId: t.film && t.film.imdbId, title: t }));
}

/** The last place on a shelf of the library, held by the tile that leads past it. */
const MORE = { more: true };

const showKey = (s) => s.imdbId || s.firstTitleId;

/** A row's key for a film or a show held as `{ film }` or `{ show }`. */
const itemKey = (item) => (item.show ? "show:" + showKey(item.show) : item.film.id);

/** A Continue watching entry's key: its title, or for an episode not here yet the show and the episode. */
export const continueKey = (p) => p.titleId || `${p.showImdbId}:${p.episode.season}:${p.episode.number}`;

/**
 * `items` in an order fixed by `deal`: each is ranked by a hash of the deal and its key, so one deal
 * always lays out the same items the same way, and an item that arrives or leaves moves nothing else.
 */
export function dealt(items, keyOf, deal) {
  const rank = (item) => {
    let h = 0x811c9dc5;
    for (const c of deal + keyOf(item)) h = Math.imul(h ^ c.charCodeAt(0), 0x01000193);
    return h >>> 0;
  };
  return items.map((item) => [rank(item), item]).sort((a, b) => a[0] - b[0]).map(([, item]) => item);
}

/**
 * The top of the front page on a phone, where there is no sidebar: the mark, the name, and the bell
 * at the right. A wide screen has all three in the sidebar and does not draw it.
 */
function PhoneHead() {
  const online = useOnline();
  return (
    <div className="kc-phonehead">
      <img src={markUrl} width="26" height="26" alt="" />
      <span className="kc-phonehead__name">Krystal Cinema</span>
      {online ? <BellButton /> : <Off><BellButton /></Off>}
    </div>
  );
}
