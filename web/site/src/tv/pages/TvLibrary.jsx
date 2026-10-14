import { TvCard, TvRow, TvSpotlight, TvTile } from "@thekrystalship/krystal";
import { api, posterUrl } from "../../api.js";
import { useLoad } from "../../data.js";
import { ANIME, discoverGenrePath, isAnime, moviePath, showPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { Empty, Failed } from "../../components/Film.jsx";
import { SpotStage, useSpot, useSpotlight } from "../spot.jsx";
import { showSpot, titleSpot } from "../../spots.js";
import { useBarAway } from "../TvFrame.jsx";

/**
 * Movies, TV Shows or Anime on a television: what the server holds of that shelf as one row per genre,
 * the genres most held first, each row best rated first, with the spotlight on the focused card as on
 * Home. A title is in every row of its genres; one with none is in a last row of its own. Anime
 * (`category`) holds films and shows together, and is the one shelf whose rows mix the two.
 */
export function TvLibrary({ kind, category = null }) {
  const anime = category === ANIME;
  const wantsFilms = anime || kind === "film";
  const wantsShows = anime || kind === "show";
  const titles = useLoad(() => (wantsFilms ? api.titles() : Promise.resolve(null)), [wantsFilms]);
  const showList = useLoad(() => (wantsShows ? api.shows() : Promise.resolve(null)), [wantsShows]);
  const { store, pageRef, regionRef, focusOn } = useSpotlight();

  const sources = [wantsFilms && titles, wantsShows && showList].filter(Boolean);
  const failed = sources.find((s) => s.error && !s.data);
  if (failed) return <div className="kc-tv-page kc-tv-pad"><Failed error={failed.error} onRetry={failed.reload} /></div>;
  if (sources.some((s) => !s.data)) return <div className="kc-tv-page" />;

  const onShelf = (t) => isAnime(t) === anime;
  const items = [
    ...(wantsShows ? showList.data.filter(onShelf)
      .map((s) => ({ key: "show:" + (s.imdbId || s.firstTitleId), genres: (s.details && s.details.genres) || [], rating: (s.details && s.details.rating) || 0, show: s })) : []),
    ...(wantsFilms ? titles.data.filter((t) => t.kind !== "show" && t.status !== "failed" && onShelf(t))
      .map((t) => ({ key: t.id, genres: (t.details && t.details.genres) || [], rating: (t.details && t.details.rating) || 0, film: t })) : []),
  ];
  const label = anime ? "Anime" : kind === "show" ? "TV Shows" : "Movies";
  const unit = anime ? ["title", "titles"] : kind === "show" ? ["show", "shows"] : ["film", "films"];
  if (items.length === 0) return <div className="kc-tv-page kc-tv-pad"><Empty title={anime ? "No anime yet" : kind === "show" ? "No shows yet" : "No movies yet"} /></div>;

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
      {...linkProps(showPath(item.show.imdbId, { category: item.show.category }))} {...focusOn(row, showSpot(item.show, genre || label))} />
    : <TvCard key={item.key} art={posterUrl(item.film)} label={item.film.title}
      {...linkProps(moviePath(item.film.id, item.film.category))} {...focusOn(row, titleSpot(item.film, genre || label))} />;

  return (
    <div className="kc-tv-page kc-tvl" ref={pageRef}>
      <BarAwayInRows store={store} />
      <SpotStage store={store} art={null} idleSize="short" />
      <div className="kc-tvl__title">
        <TvSpotlight size="md" title={label} meta={`${items.length} ${items.length === 1 ? unit[0] : unit[1]}`} />
      </div>
      <div className="kc-tvh__rows" ref={regionRef}>
        {rows.map((r, i) => (
          <div key={r.genre || "none"} className="kc-tvh__row" data-spot-row="">
            <TvRow title={r.genre || "Everything else"} count={r.list.length}
              end={r.genre && !anime && (
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
