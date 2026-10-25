import { TvCard, TvRow, TvSpotlight, TvTile } from "@thekrystalship/krystal";
import { libraryApi, posterUrl } from "../../api/index.js";
import { ANIME, discoverGenrePath, moviePath, showPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { Empty, Failed } from "../../components/Film.jsx";
import { FilterField, useListedFilter } from "../../components/FilterField.jsx";
import { SORTED } from "../../sorts.js";
import { SpotStage, useSpot, useSpotlight } from "../spot.jsx";
import { showSpot, titleSpot } from "../../spots.js";
import { useBarAway } from "../TvFrame.jsx";

/**
 * Movies, TV Shows or Anime on a television: what the server holds of that shelf as one row per genre,
 * the genres most held first, each row in the order the API serves the shelf in (`/api/library/<scope>`),
 * with the spotlight on the focused card as on Home. A title is in every row of its genres; one with none is in a last row of its own. Anime
 * (`category`) holds films and shows together, and is the one shelf whose rows mix the two, so it is
 * the one whose filter has a Kind, and it keeps its order as shows do. The field at the top searches the
 * shelf by name and its filter drawer orders and narrows it, the API answering both, in the order the
 * site's page of the shelf keeps; the rows under the open drawer are inert.
 */
export function TvLibrary({ kind, category = null }) {
  const anime = category === ANIME;
  const scope = anime ? "all" : kind === "show" ? "shows" : "films";
  const films = kind === "film" && !anime;
  const { store, pageRef, regionRef, focusOn } = useSpotlight();
  // A film that could not be prepared cannot be played, so a television leaves it off.
  const filter = useListedFilter(
    (params) => libraryApi.page(scope, { ...params, shelf: anime ? "anime" : "general" })
      .then((page) => ({ titles: page.items.map((i) => i.film || i.show), total: page.total, facets: page.facets, sorts: page.sorts })),
    { surface: films ? SORTED.films : SORTED.shows, keys: films ? FILM_KEYS : SHOW_KEYS, fixed: anime ? [] : ["kind"],
      passes: (t) => t.status !== "failed", deps: [scope] });
  // On Anime every title is anime, so its Kind offers films and series alone; a television picks no state.
  const sections = filter.field.sections
    .filter((s) => s.id !== "state")
    .map((s) => (s.id === "kind" ? { ...s, options: s.options.filter((o) => o.value !== ANIME) } : s));

  if (filter.error && !filter.titles) return <div className="kc-tv-page kc-tv-pad"><Failed error={filter.error} onRetry={filter.reload} /></div>;
  if (!filter.titles) return <div className="kc-tv-page" />;

  const items = filter.titles.map((t) => (t.kind === "show" || t.seasons
    ? { key: "show:" + (t.imdbId || t.firstTitleId), genres: (t.card && t.card.genres) || [], show: t }
    : { key: t.id, genres: (t.card && t.card.genres) || [], film: t }));
  const label = anime ? "Anime" : kind === "show" ? "TV Shows" : "Movies";
  const unit = anime ? ["title", "titles"] : kind === "show" ? ["show", "shows"] : ["film", "films"];
  if (filter.total === 0) return <div className="kc-tv-page kc-tv-pad"><Empty title={anime ? "No anime yet" : kind === "show" ? "No shows yet" : "No movies yet"} /></div>;

  const byGenre = new Map();
  for (const item of items) {
    for (const genre of item.genres.length > 0 ? item.genres : [null]) {
      if (!byGenre.has(genre)) byGenre.set(genre, []);
      byGenre.get(genre).push(item);
    }
  }
  const rows = [...byGenre.entries()]
    .sort(([a, x], [b, y]) => (a === null) - (b === null) || y.length - x.length || String(a).localeCompare(String(b)))
    .map(([genre, list]) => ({ genre, list }));

  // The page opens on its first card, the field above it a press of Up away.
  const first = rows.length > 0 ? rows[0].list[0] : null;
  const card = (item, row, genre) => item.show
    ? <TvCard key={item.key} art={item.show.card && item.show.card.poster} label={item.show.title} data-tv-initial={item === first && row === 0 ? "" : undefined}
      {...linkProps(showPath(item.show.imdbId, { category: item.show.card && item.show.card.category }))} {...focusOn(row, showSpot(item.show, genre || label))} />
    : <TvCard key={item.key} art={posterUrl(item.film)} label={item.film.title} data-tv-initial={item === first && row === 0 ? "" : undefined}
      {...linkProps(moviePath(item.film.id, item.film.card && item.film.card.category))} {...focusOn(row, titleSpot(item.film, genre || label))} />;

  return (
    <div className="kc-tv-page kc-tvl" ref={pageRef}>
      <BarAwayInRows store={store} />
      <SpotStage store={store} art={null} idleSize="short" />
      <div className="kc-tvl__field">
        <FilterField placeholder={`Search ${label}`} {...filter.field} sections={sections} />
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

/** The keys a shelf keeps its order under (`useSort`), as the site's page of the shelf keeps them. */
const SHOW_KEYS = [
  { value: "added", defaultDir: "desc" },
  { value: "name", defaultDir: "asc" },
  { value: "rating", defaultDir: "desc" },
  { value: "year", defaultDir: "desc" },
];
const FILM_KEYS = [...SHOW_KEYS, { value: "length", defaultDir: "asc" }];

/** Puts the bar away while focus is in the rows, where the spotlight takes the top of the screen. */
function BarAwayInRows({ store }) {
  useBarAway(useSpot(store).row >= 0);
  return null;
}
