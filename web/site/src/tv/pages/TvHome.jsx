import React from "react";
import { TvCard, TvRow, TvTile } from "@thekrystalship/krystal";
import { episodeCode, libraryApi, meApi, posterUrl, roomsApi } from "../../api/index.js";
import { useLoad } from "../../data.js";
import { ANIME, discoverFilmPath, isAnime, libraryPath, moviePath, showPath, wantsPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { since, timeLeft } from "../../time.js";
import { Empty, Failed, watchLink } from "../../components/Film.jsx";
import { waitersWords } from "../../components/TitleCards.jsx";
import { useCan } from "../../session.js";
import { useWantList } from "../../wants.js";
import { continueKey, dealt } from "../../pages/Home.jsx";
import { useHomeSlides } from "../../heroes.js";
import { TvHero } from "../hero.jsx";
import { SpotStage, useSpotlight } from "../spot.jsx";
import { entrySpot, showSpot, titleSpot } from "../../spots.js";
import { watchNow } from "../../watchNow.js";

const RECENT_DAYS = 14;
const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * The front page on a television. It opens on the top's slides (`heroes.js`), large, turning through
 * what is worth watching now, with the first one's first action focused. Down goes
 * into the rows: Continue watching, Watching now, Want to watch, Recently added, Anticipated (the films
 * not out yet that people wait on, for a person who may wait on one), then the shelf as Movies, TV
 * Shows and Anime, each ending in the tile that leads to all of it. Once focus is in the rows the
 * top of the screen is a spotlight on the card it rests on, the focused row holds one line on the
 * screen, and the rows above it are put away. A row with nothing in it is left out.
 */
export function TvHome() {
  const titles = useLoad(() => libraryApi.titles(), []);
  const rooms = useLoad(() => roomsApi.list(), [], { every: 15000 });
  const progress = useLoad(() => meApi.progress().catch(() => []), []);
  const showList = useLoad(() => libraryApi.shows().catch(() => []), []);
  const wanted = useWantList().list;
  const canWish = useCan("cinema:wish");
  const anticipated = useLoad(() => (canWish ? libraryApi.anticipated().catch(() => []) : Promise.resolve([])), [canWish]);
  const [deal] = React.useState(() => Math.random().toString(36).slice(2));
  const { store, pageRef, regionRef, focusOn: card } = useSpotlight();

  const library = (titles.data || []).filter((t) => t.status !== "failed");
  const byId = new Map(library.map((t) => [t.id, t]));
  const byImdb = new Map(library.filter((t) => !t.episode && t.film && t.film.imdbId).map((t) => [t.film.imdbId, t]));
  const shows = showList.data || [];
  const films = library.filter((t) => t.kind !== "show");
  const watching = (rooms.data || []).filter((r) => r.titleId && r.participants > 0 && byId.has(r.titleId));
  // An episode the library does not hold yet comes with no title, and is fetched when chosen.
  const continuing = (progress.data || []).filter((p) => (p.titleId ? byId.has(p.titleId) : p.episode && p.showImdbId));
  const wants = wanted || [];
  const isRecent = (at) => at && Date.now() - Date.parse(at) <= RECENT_DAYS * DAY_MS;
  const recent = [
    ...films.filter((t) => isRecent(t.addedAt)).map((t) => ({ film: t, at: t.addedAt })),
    ...shows.filter((s) => isRecent(s.addedAt)).map((s) => ({ show: s, at: s.addedAt })),
  ].sort((a, b) => b.at.localeCompare(a.at));
  const slides = useHomeSlides({
    library: titles.data ? library : null, shows: showList.data, rooms: rooms.data, waitedOn: anticipated.data,
  });

  if (titles.error && !titles.data) return <div className="kc-tv-page"><Failed error={titles.error} onRetry={titles.reload} /></div>;
  if (titles.loading && !titles.data) return <div className="kc-tv-page" />;
  if (library.length === 0) return <div className="kc-tv-page"><Empty title="The library is empty" /></div>;

  const rows = [];
  const showArt = (s) => s.card && s.card.poster;
  const either = (row, item, eyebrow) => item.show
    ? <TvCard key={"show:" + showKey(item.show)} art={showArt(item.show)} label={item.show.title}
      {...linkProps(showPath(item.show.imdbId, { category: item.show.card && item.show.card.category }))} {...card(row, showSpot(item.show, eyebrow))} />
    : <TvCard key={item.film.id} art={posterUrl(item.film)} label={item.film.title}
      {...linkProps(moviePath(item.film.id, item.film.card && item.film.card.category))} {...card(row, titleSpot(item.film, eyebrow))} />;

  if (continuing.length > 0) {
    const row = rows.length;
    rows.push(
      <TvRow key="continue" icon="history" title="Continue watching" count={continuing.length}>
        {continuing.map((p) => {
          if (!p.titleId) {
            const show = shows.find((s) => s.imdbId === p.showImdbId);
            const name = `${p.name} · ${episodeCode(p.episode)}`;
            const s = {
              ...(show ? showSpot(show, "Continue watching") : { eyebrow: "Continue watching", facts: {} }),
              key: "next:" + continueKey(p), title: name, overview: p.episodeName || (show && show.card && show.card.plot),
            };
            if (p.stillUrl) s.art = p.stillUrl;
            return <TvNextEpisode key={continueKey(p)} progress={p} art={s.art} label={name} focus={card(row, s)} />;
          }
          const t = byId.get(p.titleId);
          const room = watching.find((r) => r.titleId === p.titleId);
          const fraction = p.durationSeconds > 0 ? Math.min(1, p.positionSeconds / p.durationSeconds) : 0;
          const s = { ...titleSpot(t, "Continue watching"), progress: { fraction, left: timeLeft(p.remainingSeconds) } };
          return (
            <TvCard key={p.titleId} shape="wide" art={(t.card && t.card.backdrop) || posterUrl(t)} label={t.title}
              progress={fraction} {...watchLink(t.id, room && room.sessionId, p.positionSeconds)} {...card(row, s)} />
          );
        })}
      </TvRow>,
    );
  }
  if (watching.length > 0) {
    const row = rows.length;
    rows.push(
      <TvRow key="watching" icon="users" title="Watching now" count={watching.length}>
        {watching.map((r) => {
          const t = byId.get(r.titleId);
          const people = `${r.participants} watching`;
          return (
            <TvCard key={r.sessionId} shape="wide" art={(t.card && t.card.backdrop) || posterUrl(t)} label={t.title}
              badge={<><span className="kc-tv-live" />{people}</>}
              {...watchLink(r.titleId, r.sessionId)} {...card(row, titleSpot(t, "Watching now · " + people))} />
          );
        })}
      </TvRow>,
    );
  }
  if (wants.length > 0) {
    const row = rows.length;
    const end = { key: "end:wants", eyebrow: "See all", title: "Want to watch", line: count(wants.length, "title") };
    rows.push(
      <TvRow key="wants" icon="bookmark" title="Want to watch" count={wants.length}
        end={<TvTile label="See all" {...linkProps(wantsPath)} {...card(row, end)} />}>
        {wants.map((e) => {
          const held = e.kind !== "show" ? byImdb.get(e.imdbId) : null;
          const category = e.card && e.card.category;
          const href = e.kind === "show" ? showPath(e.imdbId, { category }) : held ? moviePath(held.id, category) : discoverFilmPath(e.imdbId);
          return (
            <TvCard key={e.imdbId} art={e.card && e.card.poster} label={e.title} {...linkProps(href)}
              {...card(row, entrySpot(e, held, "Want to watch · " + since(e.since).toLowerCase()))} />
          );
        })}
      </TvRow>,
    );
  }
  if (recent.length > 0) {
    const row = rows.length;
    rows.push(
      <TvRow key="recent" icon="sparkles" title="Recently added" count={recent.length}>
        {recent.map((item) => either(row, item, "New · added " + since(item.at).toLowerCase()))}
      </TvRow>,
    );
  }
  const waitedOn = anticipated.data || [];
  if (waitedOn.length > 0) {
    const row = rows.length;
    rows.push(
      <TvRow key="anticipated" icon="bell" title="Anticipated" count={waitedOn.length}>
        {waitedOn.map((f) => (
          <TvCard key={f.imdbId} art={f.posterUrl} label={f.title} {...linkProps(discoverFilmPath(f.imdbId))}
            {...card(row, { key: "anticipated:" + f.imdbId, art: f.posterUrl, eyebrow: waitersWords(f.waiters, f.waiting), title: f.title, facts: { year: f.year } })} />
        ))}
      </TvRow>,
    );
  }
  const generalFilms = films.filter((t) => !isAnime(t));
  const generalShows = shows.filter((s) => !isAnime(s));
  const anime = [...films.filter(isAnime).map((t) => ({ film: t })), ...shows.filter(isAnime).map((s) => ({ show: s }))];
  if (generalFilms.length > 0) {
    const row = rows.length;
    const end = { key: "end:films", eyebrow: "See all", title: "Movies", line: count(generalFilms.length, "film") };
    rows.push(
      <TvRow key="films" icon="clapperboard" title="Movies" count={generalFilms.length}
        end={<TvTile label="See all" {...linkProps(libraryPath("film"))} {...card(row, end)} />}>
        {dealt(generalFilms, (t) => t.id, deal).map((t) => either(row, { film: t }, "Movies"))}
      </TvRow>,
    );
  }
  if (generalShows.length > 0) {
    const row = rows.length;
    const end = { key: "end:shows", eyebrow: "See all", title: "TV Shows", line: count(generalShows.length, "show") };
    rows.push(
      <TvRow key="shows" icon="tv" title="TV Shows" count={generalShows.length}
        end={<TvTile label="See all" {...linkProps(libraryPath("show"))} {...card(row, end)} />}>
        {dealt(generalShows, showKey, deal).map((s) => either(row, { show: s }, "TV Shows"))}
      </TvRow>,
    );
  }
  if (anime.length > 0) {
    const row = rows.length;
    const end = { key: "end:anime", eyebrow: "See all", title: "Anime", line: count(anime.length, "title") };
    const animeKey = (item) => (item.show ? "show:" + showKey(item.show) : item.film.id);
    rows.push(
      <TvRow key="anime" icon="swords" title="Anime" count={anime.length}
        end={<TvTile label="See all" {...linkProps(libraryPath("show", ANIME))} {...card(row, end)} />}>
        {dealt(anime, animeKey, deal).map((item) => either(row, item, "Anime"))}
      </TvRow>,
    );
  }

  return (
    <div className="kc-tv-page kc-tvh" ref={pageRef}>
      {/* The top's place is held while its slides are described, so the rows never move under it. */}
      {slides === null
        ? <div className="kc-tvh__hero" aria-hidden="true" />
        : slides.length > 0
          ? <TvHero slides={slides} store={store} initial className="kc-tvh__hero" data-tv-top="" />
          : <SpotStage store={store} />}

      <div className="kc-tvh__rows" ref={regionRef}>
        {rows.map((r) => <div key={r.key} className="kc-tvh__row" data-spot-row="">{r}</div>)}
      </div>
    </div>
  );
}

const showKey = (s) => s.imdbId || s.firstTitleId;

/**
 * The episode after one somebody finished, when the library does not hold it: OK is Watch now, which
 * fetches it into a room and goes there. The card is a link to the show, which is where it leads when
 * it is opened any other way.
 */
function TvNextEpisode({ progress, art, label, focus }) {
  const starting = React.useRef(false);
  const start = async (e) => {
    e.preventDefault();
    if (starting.current) return;
    starting.current = true;
    const started = await watchNow({
      imdbId: progress.showImdbId, episodes: [{ season: progress.episode.season, episode: progress.episode.number }],
    });
    if (!started) starting.current = false;
  };
  return <TvCard shape="wide" art={art} label={label} progress={0} {...linkProps(showPath(progress.showImdbId))} onClick={start} {...focus} />;
}

const count = (n, word) => `${n} ${n === 1 ? word : word + "s"}`;
