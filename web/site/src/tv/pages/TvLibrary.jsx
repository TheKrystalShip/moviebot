import { TvCard, TvRow, TvSpotlight, TvTile } from "@thekrystalship/krystal-ui";
import { api, posterUrl } from "../../api.js";
import { useLoad } from "../../data.js";
import { discoverGenrePath, moviePath, showPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { Empty, Failed } from "../../components/Film.jsx";
import { SpotStage, showSpot, titleSpot, useSpot, useSpotlight } from "../spot.jsx";
import { useBarAway } from "../TvFrame.jsx";

/**
 * Movies or TV Shows on a television: what the server holds of that kind as one row per genre, the
 * genres most held first, each row best rated first, with the spotlight on the focused card as on
 * Home. A title is in every row of its genres; one with none is in a last row of its own.
 */
export function TvLibrary({ kind }) {
  const shows = kind === "show";
  const titles = useLoad(() => (shows ? Promise.resolve(null) : api.titles()), [shows]);
  const showList = useLoad(() => (shows ? api.shows() : Promise.resolve(null)), [shows]);
  const { store, pageRef, regionRef, focusOn } = useSpotlight();

  const source = shows ? showList : titles;
  if (source.error && !source.data) return <div className="kc-tv-page kc-tv-pad"><Failed error={source.error} onRetry={source.reload} /></div>;
  if (!source.data) return <div className="kc-tv-page" />;

  const items = shows
    ? source.data.map((s) => ({ key: "show:" + (s.imdbId || s.firstTitleId), genres: (s.details && s.details.genres) || [], rating: (s.details && s.details.rating) || 0, show: s }))
    : source.data.filter((t) => t.kind !== "show" && t.status !== "failed")
      .map((t) => ({ key: t.id, genres: (t.details && t.details.genres) || [], rating: (t.details && t.details.rating) || 0, film: t }));
  const label = shows ? "TV Shows" : "Movies";
  if (items.length === 0) return <div className="kc-tv-page kc-tv-pad"><Empty title={shows ? "No shows yet" : "No movies yet"} /></div>;

  const byGenre = new Map();
  for (const item of items) {
    for (const genre of item.genres.length > 0 ? item.genres : [null]) {
      if (!byGenre.has(genre)) byGenre.set(genre, []);
      byGenre.get(genre).push(item);
    }
  }
  const rows = [...byGenre.entries()]
    .sort(([a, x], [b, y]) => (a === null) - (b === null) || y.length - x.length || String(a).localeCompare(String(b)))
    .map(([genre, list]) => ({ genre, list: list.sort((x, y) => y.rating - x.rating) }));

  const card = (item, row, genre) => item.show
    ? <TvCard key={item.key} art={item.show.details && item.show.details.posterUrl} label={item.show.title}
      {...linkProps(showPath(item.show.imdbId))} {...focusOn(row, showSpot(item.show, genre || label))} />
    : <TvCard key={item.key} art={posterUrl(item.film)} label={item.film.title}
      {...linkProps(moviePath(item.film.id))} {...focusOn(row, titleSpot(item.film, genre || label))} />;

  return (
    <div className="kc-tv-page kc-tvl" ref={pageRef}>
      <BarAwayInRows store={store} />
      <SpotStage store={store} art={null} idleSize="short" />
      <div className="kc-tvl__title">
        <TvSpotlight size="md" title={label} meta={`${items.length} ${shows ? (items.length === 1 ? "show" : "shows") : (items.length === 1 ? "film" : "films")}`} />
      </div>
      <div className="kc-tvh__rows" ref={regionRef}>
        {rows.map((r, i) => (
          <div key={r.genre || "none"} className="kc-tvh__row" data-spot-row="">
            <TvRow title={r.genre || "Everything else"} count={r.list.length}
              end={r.genre && (
                <TvTile icon="compass" label="Discover more" {...linkProps(discoverGenrePath(kind, r.genre))}
                  {...focusOn(i, { key: "more:" + r.genre, eyebrow: "Discover more", title: r.genre, line: label })} />
              )}>
              {r.list.map((item) => card(item, i, r.genre))}
            </TvRow>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Puts the bar away while focus is in the rows, where the spotlight takes the top of the screen. */
function BarAwayInRows({ store }) {
  useBarAway(useSpot(store).row >= 0);
  return null;
}
