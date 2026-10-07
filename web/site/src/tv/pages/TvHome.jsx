import React from "react";
import { Button, TvCard, TvRow, TvSpotlight, TvTile } from "@thekrystalship/krystal-ui";
import { api, episodeCode, posterUrl } from "../../api.js";
import { useLoad } from "../../data.js";
import { discoverFilmPath, libraryPath, listPath, moviePath, showPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { since, timeLeft } from "../../time.js";
import { Empty, Failed, watchLink } from "../../components/Film.jsx";
import { SaveButton } from "../../components/SaveButton.jsx";
import { continueKey, dealt, newest } from "../../pages/Home.jsx";
import { SpotFacts, SpotStage, entrySpot, showSpot, titleSpot, useSpotlight } from "../spot.jsx";
import { watchNow } from "../../watchNow.js";

const RECENT_DAYS = 14;
const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * The front page on a television. It opens on the newest film, large, with Watch focused. Down goes
 * into the rows: Continue watching, Watching now, Watch later, Recently added, then the shelf as
 * Movies and TV Shows, each ending in the tile that leads to all of it. Once focus is in the rows the
 * top of the screen is a spotlight on the card it rests on, the focused row holds one line on the
 * screen, and the rows above it are put away. A row with nothing in it is left out.
 */
export function TvHome() {
  const titles = useLoad(() => api.titles(), []);
  const rooms = useLoad(() => api.rooms(), [], { every: 15000 });
  const progress = useLoad(() => api.progress().catch(() => []), []);
  const showList = useLoad(() => api.shows().catch(() => []), []);
  const later = useLoad(() => api.list("later").catch(() => null), []);
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
  const saved = (later.data && later.data.entries) || [];
  const isRecent = (at) => at && Date.now() - Date.parse(at) <= RECENT_DAYS * DAY_MS;
  const recent = [
    ...films.filter((t) => isRecent(t.addedAt)).map((t) => ({ film: t, at: t.addedAt })),
    ...shows.filter((s) => isRecent(s.addedAt)).map((s) => ({ show: s, at: s.addedAt })),
  ].sort((a, b) => b.at.localeCompare(a.at));
  const featured = newest(films);

  if (titles.error && !titles.data) return <div className="kc-tv-page"><Failed error={titles.error} onRetry={titles.reload} /></div>;
  if (titles.loading && !titles.data) return <div className="kc-tv-page" />;
  if (library.length === 0) return <div className="kc-tv-page"><Empty title="The library is empty" /></div>;

  const rows = [];
  const showArt = (s) => s.details && s.details.posterUrl;
  const either = (row, item, eyebrow) => item.show
    ? <TvCard key={"show:" + showKey(item.show)} art={showArt(item.show)} label={item.show.title}
      {...linkProps(showPath(item.show.imdbId))} {...card(row, showSpot(item.show, eyebrow))} />
    : <TvCard key={item.film.id} art={posterUrl(item.film)} label={item.film.title}
      {...linkProps(moviePath(item.film.id))} {...card(row, titleSpot(item.film, eyebrow))} />;

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
              key: "next:" + continueKey(p), title: name, overview: p.episodeName || (show && show.details && show.details.overview),
            };
            if (p.stillUrl) s.art = p.stillUrl;
            return <TvNextEpisode key={continueKey(p)} progress={p} art={s.art} label={name} focus={card(row, s)} />;
          }
          const t = byId.get(p.titleId);
          const room = watching.find((r) => r.titleId === p.titleId);
          const fraction = p.durationSeconds > 0 ? Math.min(1, p.positionSeconds / p.durationSeconds) : 0;
          const s = { ...titleSpot(t, "Continue watching"), progress: { fraction, left: timeLeft(p.remainingSeconds) } };
          return (
            <TvCard key={p.titleId} shape="wide" art={(t.details && t.details.backdropUrl) || posterUrl(t)} label={t.title}
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
            <TvCard key={r.sessionId} shape="wide" art={(t.details && t.details.backdropUrl) || posterUrl(t)} label={t.title}
              badge={<><span className="kc-tv-live" />{people}</>}
              {...watchLink(r.titleId, r.sessionId)} {...card(row, titleSpot(t, "Watching now · " + people))} />
          );
        })}
      </TvRow>,
    );
  }
  if (saved.length > 0) {
    const row = rows.length;
    const end = { key: "end:later", eyebrow: "See all", title: "Watch later", line: count(saved.length, "title") };
    rows.push(
      <TvRow key="later" icon="bookmark" title="Watch later" count={saved.length}
        end={<TvTile label="See all" {...linkProps(listPath("later"))} {...card(row, end)} />}>
        {saved.map((e) => {
          const held = e.kind !== "show" ? byImdb.get(e.imdbId) : null;
          const href = e.kind === "show" ? showPath(e.imdbId) : held ? moviePath(held.id) : discoverFilmPath(e.imdbId);
          return (
            <TvCard key={e.imdbId} art={e.posterUrl} label={e.title} {...linkProps(href)}
              {...card(row, entrySpot(e, held, "Watch later · saved " + since(e.addedAt).toLowerCase()))} />
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
  if (films.length > 0) {
    const row = rows.length;
    const end = { key: "end:films", eyebrow: "See all", title: "Movies", line: count(films.length, "film") };
    rows.push(
      <TvRow key="films" icon="clapperboard" title="Movies" count={films.length}
        end={<TvTile label="See all" {...linkProps(libraryPath("film"))} {...card(row, end)} />}>
        {dealt(films, (t) => t.id, deal).map((t) => either(row, { film: t }, "Movies"))}
      </TvRow>,
    );
  }
  if (shows.length > 0) {
    const row = rows.length;
    const end = { key: "end:shows", eyebrow: "See all", title: "TV Shows", line: count(shows.length, "show") };
    rows.push(
      <TvRow key="shows" icon="tv" title="TV Shows" count={shows.length}
        end={<TvTile label="See all" {...linkProps(libraryPath("show"))} {...card(row, end)} />}>
        {dealt(shows, showKey, deal).map((s) => either(row, { show: s }, "TV Shows"))}
      </TvRow>,
    );
  }

  const hero = featured && titleSpot(featured, featured.addedAt ? "Added " + since(featured.addedAt).toLowerCase() : "From the library");
  const room = featured && watching.find((r) => r.titleId === featured.id);

  return (
    <div className="kc-tv-page kc-tvh" ref={pageRef}>
      <SpotStage store={store} art={hero && hero.art} />

      {hero && (
        <section className="kc-tvh__hero" data-tv-top="">
          <TvSpotlight size="lg" eyebrow={room ? "Watching now" : hero.eyebrow} title={hero.title} meta={<SpotFacts facts={hero.facts} />}>
            {hero.overview && <p className="tv-spot__overview">{hero.overview}</p>}
            <div className="tv-spot__actions">
              <Button variant="primary" size="lg" icon={room ? "users" : "play"} data-tv-initial="" {...watchLink(featured.id, room && room.sessionId)}>
                {room ? "Join the room" : "Watch"}
              </Button>
              <Button size="lg" icon="info" {...linkProps(moviePath(featured.id))}>More info</Button>
              {featured.film && featured.film.imdbId && <SaveButton imdbId={featured.film.imdbId} kind="film" title={hero.title} />}
            </div>
          </TvSpotlight>
        </section>
      )}

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
