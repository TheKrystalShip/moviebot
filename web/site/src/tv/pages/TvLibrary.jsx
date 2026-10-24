import { TvCard, TvRow, TvSpotlight, TvTile } from "@thekrystalship/krystal";
import { api, filmName, posterUrl } from "../../api.js";
import { useLoad } from "../../data.js";
import { ANIME, discoverGenrePath, isAnime, moviePath, showPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { Empty, Failed } from "../../components/Film.jsx";
import { FilterField, useTitleFilter } from "../../components/FilterField.jsx";
import { FILM_ORDERS, SHOW_ORDERS, SHOW_SORTS, SORTS } from "../../pages/Library.jsx";
import { withOrder } from "../../orders.js";
import { SORTED, useSort } from "../../sorts.js";
import { SpotStage, useSpot, useSpotlight } from "../spot.jsx";
import { showSpot, titleSpot } from "../../spots.js";
import { useBarAway } from "../TvFrame.jsx";

/**
 * Movies, TV Shows or Anime on a television: what the server holds of that shelf as one row per genre,
 * the genres most held first, each row in the shelf's order, with the spotlight on the focused card as
 * on Home. A title is in every row of its genres; one with none is in a last row of its own. Anime
 * (`category`) holds films and shows together, and is the one shelf whose rows mix the two, so it is
 * the one whose filter has a Kind, and it is ordered as shows are. The field at the top searches the
 * shelf by name and its filter drawer orders and narrows it, the order being the one the site's page of
 * the shelf keeps; the rows under the open drawer are inert.
 */
export function TvLibrary({ kind, category = null }) {
  const anime = category === ANIME;
  const wantsFilms = anime || kind === "film";
  const wantsShows = anime || kind === "show";
  const titles = useLoad(() => (wantsFilms ? api.titles() : Promise.resolve(null)), [wantsFilms]);
  const showList = useLoad(() => (wantsShows ? api.shows() : Promise.resolve(null)), [wantsShows]);
  const { store, pageRef, regionRef, focusOn } = useSpotlight();

  const onShelf = (t) => isAnime(t) === anime;
  const all = [
    ...(wantsShows && showList.data ? showList.data.filter(onShelf) : []),
    ...(wantsFilms && titles.data ? titles.data.filter((t) => t.kind !== "show" && t.status !== "failed" && onShelf(t)) : []),
  ];
  const filter = useTitleFilter(all, { fixed: anime ? [] : ["kind"], named: (t) => t.title || "" });
  const films = kind === "film" && !anime;
  const [sort, setSort] = useSort(films ? SORTED.films : SORTED.shows, films ? SORTS : SHOW_SORTS);
  // On Anime every title is anime, so its Kind offers films and series alone.
  const sections = filter.field.sections.map((s) => (s.id === "kind" ? { ...s, options: s.options.filter((o) => o.value !== ANIME) } : s));

  const sources = [wantsFilms && titles, wantsShows && showList].filter(Boolean);
  const failed = sources.find((s) => s.error && !s.data);
  if (failed) return <div className="kc-tv-page kc-tv-pad"><Failed error={failed.error} onRetry={failed.reload} /></div>;
  if (sources.some((s) => !s.data)) return <div className="kc-tv-page" />;

  const items = filter.shown.map((t) => (t.kind === "show" || t.seasons
    ? { key: "show:" + (t.imdbId || t.firstTitleId), genres: (t.details && t.details.genres) || [], show: t }
    : { key: t.id, genres: (t.details && t.details.genres) || [], film: t }));
  const label = anime ? "Anime" : kind === "show" ? "TV Shows" : "Movies";
  const unit = anime ? ["title", "titles"] : kind === "show" ? ["show", "shows"] : ["film", "films"];
  if (all.length === 0) return <div className="kc-tv-page kc-tv-pad"><Empty title={anime ? "No anime yet" : kind === "show" ? "No shows yet" : "No movies yet"} /></div>;

  const byGenre = new Map();
  for (const item of items) {
    for (const genre of item.genres.length > 0 ? item.genres : [null]) {
      if (!byGenre.has(genre)) byGenre.set(genre, []);
      byGenre.get(genre).push(item);
    }
  }
  const rows = [...byGenre.entries()]
    .sort(([a, x], [b, y]) => (a === null) - (b === null) || y.length - x.length || String(a).localeCompare(String(b)))
    .map(([genre, list]) => ({ genre, list: list.sort(inOrder(sort)) }));

  // The page opens on its first card, the field above it a press of Up away.
  const first = rows.length > 0 ? rows[0].list[0] : null;
  const card = (item, row, genre) => item.show
    ? <TvCard key={item.key} art={item.show.details && item.show.details.posterUrl} label={item.show.title} data-tv-initial={item === first && row === 0 ? "" : undefined}
      {...linkProps(showPath(item.show.imdbId, { category: item.show.category }))} {...focusOn(row, showSpot(item.show, genre || label))} />
    : <TvCard key={item.key} art={posterUrl(item.film)} label={item.film.title} data-tv-initial={item === first && row === 0 ? "" : undefined}
      {...linkProps(moviePath(item.film.id, item.film.category))} {...focusOn(row, titleSpot(item.film, genre || label))} />;

  return (
    <div className="kc-tv-page kc-tvl" ref={pageRef}>
      <BarAwayInRows store={store} />
      <SpotStage store={store} art={null} idleSize="short" />
      <div className="kc-tvl__field">
        <FilterField placeholder={`Search ${label}`} {...filter.field}
          {...withOrder({ ...filter.field, sections }, films ? FILM_ORDERS : SHOW_ORDERS, sort, setSort)} />
      </div>
      <div className="kc-tvl__title">
        <TvSpotlight size="md" title={label} meta={`${items.length} ${items.length === 1 ? unit[0] : unit[1]}`} />
      </div>
      {items.length === 0 && <div className="kc-tv-pad"><Empty title="Nothing here" /></div>}
      <div className="kc-tvh__rows" ref={regionRef} inert={filter.drawer.open ? "" : undefined}>
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

// What each order compares in a row's cards, which hold a film or a show; each ascends.
const KEYS = {
  added: (i) => (i.film || i.show).addedAt || "",
  name: (i) => (i.film ? filmName(i.film) : i.show.title) || "",
  rating: (i) => ((i.film || i.show).details && (i.film || i.show).details.rating) || 0,
  year: (i) => (i.film ? (i.film.film && i.film.film.year) : i.show.firstYear) || 0,
  length: (i) => (i.film ? i.film.durationSeconds : 0) || 0,
};

/** A comparator of a row's cards in `sort`, the order `useSort` keeps. */
function inOrder({ key, dir }) {
  const of = KEYS[key] || KEYS.added;
  const sign = dir === "asc" ? 1 : -1;
  return (x, y) => {
    const a = of(x);
    const b = of(y);
    return sign * (typeof a === "string" ? a.localeCompare(b) : a - b);
  };
}

/** Puts the bar away while focus is in the rows, where the spotlight takes the top of the screen. */
function BarAwayInRows({ store }) {
  useBarAway(useSpot(store).row >= 0);
  return null;
}
