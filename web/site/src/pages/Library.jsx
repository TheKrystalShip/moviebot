import { RadioGroup } from "@thekrystalship/krystal";
import { api, filmName } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLibrary } from "../offline/hooks.js";
import { ANIME, discoverGenrePath, isAnime, libraryPath } from "../paths.js";
import { navigate } from "../router.js";
import { SORTED, useSort } from "../sorts.js";
import { orderOf, shownOf, withOrder } from "../orders.js";
import { Empty, Failed, FilmCard, PageHead, ShowCard } from "../components/Film.jsx";
import { FilterField, useListedFilter, useTitleFilter } from "../components/FilterField.jsx";
import { MoreTile } from "../components/TitleCards.jsx";
import { useDownloadMarks, useDownloads, useOffersSaving } from "../components/DownloadButton.jsx";

/** The Download section, where this browser downloads: only the titles downloaded to this device. */
const downloadedSection = { id: "downloaded", label: "Download", kind: "toggle", option: "Downloaded here" };

/** The keys a shelf keeps its order under (`useSort`), the first the one it opens in. */
const KEYS = [
  { value: "added", defaultDir: "desc" },
  { value: "name", defaultDir: "asc" },
  { value: "rating", defaultDir: "desc" },
  { value: "year", defaultDir: "desc" },
  { value: "length", defaultDir: "asc" },
];

// What every order compares on the device's saved titles, offline; each ascends.
const COMPARED = {
  added: (a, b) => (a.addedAt || "").localeCompare(b.addedAt || ""),
  name: (a, b) => (a.kind === "show" || a.seasons ? a.title : filmName(a)).localeCompare(b.kind === "show" || b.seasons ? b.title : filmName(b)),
  rating: (a, b) => ((a.details && a.details.rating) || 0) - ((b.details && b.details.rating) || 0),
  year: (a, b) => ((a.film && a.film.year) || a.firstYear || 0) - ((b.film && b.film.year) || b.firstYear || 0),
  length: (a, b) => (a.durationSeconds || 0) - (b.durationSeconds || 0),
};

/** The orders the device offers its saved titles, offline, where there is no API to list them. */
const SAVED_SORTS = {
  film: ["added", "first-added", "name", "name-desc", "rating", "lowest", "newest", "oldest", "shortest", "longest"],
  show: ["added", "first-added", "name", "name-desc", "rating", "lowest", "newest", "oldest"],
};

/** The words of the states a film can stand in, by the API's names for them. */
const FILM_STATES = { ready: "Ready", preparing: "Preparing", failed: "Could not be prepared" };

// What each shelf calls itself and its titles: the general shelves by kind, and Anime's by kind under it.
const SHELVES = {
  film: { title: "Movies", unit: ["movie", "movies"], empty: "No movies yet", icon: "clapperboard" },
  show: { title: "TV Shows", unit: ["show", "shows"], empty: "No shows yet", icon: "tv" },
  "anime:film": { title: "Anime", unit: ["film", "films"], empty: "No anime films yet", icon: "swords" },
  "anime:show": { title: "Anime", unit: ["series", "series"], empty: "No anime series yet", icon: "swords" },
};

const shelfOf = (kind, category) => SHELVES[(category === ANIME ? "anime:" : "") + kind] || SHELVES.film;

/**
 * Whether a listed title belongs on a shelf of `category`: Anime holds what the API shelves as anime,
 * and the general shelves hold everything else, a title nothing describes yet among them.
 */
const onShelf = (title, category) => isAnime(title) === (category === ANIME);

/**
 * Every title of one kind and category on the shelf, searched by name, narrowed by the filter drawer
 * (genre, decade, length, rating and language, a film's state, and a title's download) and ordered the
 * way somebody asks; with one genre picked, the last tile leads on to Discover's catalogue of that
 * genre. The API searches, narrows and orders the shelf (`/api/library/<scope>`) and says which orders
 * it is served in; opened with no connection, the page is the device's saved titles, which it searches,
 * narrows and orders itself, there being nothing to ask.
 */
export function Library({ kind = "film", category = null, fromDevice = false }) {
  return fromDevice
    ? <SavedShelf key={kind + category} kind={kind} category={category} />
    : <ListedShelf key={kind + category} kind={kind} category={category} />;
}

/** A row of options, one of them on, each replacing the page with its own rather than stacking on it. */
function Segmented({ label, options, on, className }) {
  return (
    <RadioGroup variant="segmented" fill label={label} className={className} value={on}
      options={options.map((o) => ({ value: o.id, label: o.label }))}
      onChange={(id) => navigate(options.find((o) => o.id === id).path, { replace: true })} />
  );
}

/**
 * Movies, TV shows or Anime, on a phone. Its Library tab is the one way into any of them, where a
 * wide screen has a sidebar entry for each.
 */
function ShelfSwitch({ kind, category }) {
  const options = [
    { id: "film", label: "Movies", path: libraryPath("film") },
    { id: "show", label: "TV shows", path: libraryPath("show") },
    { id: ANIME, label: "Anime", path: libraryPath("show", ANIME) },
  ];
  return <Segmented label="Library" options={options} on={category === ANIME ? ANIME : kind} className="kc-kind-switch" />;
}

/** Anime's series or its films, on every screen: the one shelf holds both kinds. */
function AnimeKindSwitch({ kind }) {
  const options = [
    { id: "show", label: "Series", path: libraryPath("show", ANIME) },
    { id: "film", label: "Films", path: libraryPath("film", ANIME) },
  ];
  return <Segmented label="Anime" options={options} on={kind} className="kc-anime-switch" />;
}

/** The switches above a shelf: the shelves on a phone, and under Anime, its two kinds. */
function Switches({ kind, category }) {
  return (
    <>
      <ShelfSwitch kind={kind} category={category} />
      {category === ANIME && <AnimeKindSwitch kind={kind} />}
    </>
  );
}

/** Whether the device holds a title of a shelf of `kind`: a film downloaded whole, a show with an episode downloaded. */
const holding = (kind, marks) => (kind === "show"
  ? (s) => marks.episodes(s.imdbId) > 0
  : (t) => !!marks.film(t.id) && marks.film(t.id).kind === "done");

/** What the device holds of a kind: the title ids of films downloaded whole, the IMDb ids of shows with an episode downloaded. */
function heldIds(kind, downloads) {
  const done = [...downloads.entries()].filter(([, d]) => d.kind === "done");
  return kind === "show"
    ? [...new Set(done.map(([, d]) => d.save.meta.show && d.save.meta.show.imdbId).filter(Boolean))]
    : done.filter(([, d]) => !d.save.meta.show).map(([id]) => id);
}

/** What the API is told the device holds when it holds nothing, which no title is. */
const NOTHING_HELD = "-";

/** The shelf as the API lists it. */
function ListedShelf({ kind, category }) {
  const offered = useOffersSaving();
  const marks = useDownloadMarks();
  const downloads = useDownloads();
  const held = holding(kind, marks);
  const scope = kind === "show" ? "shows" : "films";
  const filter = useListedFilter(
    (params) => api.libraryPage(scope, { ...params, shelf: category === ANIME ? "anime" : "general" })
      .then((page) => ({ titles: page.items.map((i) => i.film || i.show), total: page.total, facets: page.facets })),
    {
      surface: kind === "show" ? SORTED.shows : SORTED.films, keys: KEYS, stateWords: FILM_STATES,
      device: offered ? [downloadedSection] : [], deps: [scope, category],
      // The API keeps the titles this device holds, which it is told: a film by its title id, a show by its IMDb id.
      deviceAsk: (v) => (v.downloaded ? { held: heldIds(kind, downloads).join(",") || NOTHING_HELD } : {}),
    });
  // Download is offered while the device holds any of the shelf's titles.
  const holds = !!filter.titles && filter.titles.some(held);
  const field = holds ? filter.field : { ...filter.field, sections: filter.field.sections.filter((s) => s.id !== downloadedSection.id) };
  return <Shelf kind={kind} category={category} filter={{ ...filter, field }} marks={marks} />;
}

/** The device's saved titles of the shelf, offline, searched, narrowed and ordered here. */
function SavedShelf({ kind, category }) {
  const saved = useLibrary(kind, true);
  const marks = useDownloadMarks();
  const held = holding(kind, marks);
  const [order, pick] = useSort(kind === "show" ? SORTED.shows : SORTED.films, KEYS);
  const all = (saved.data || []).filter((t) => (kind === "show" || t.kind !== "show") && onShelf(t, category));
  const own = all.some(held) ? [downloadedSection] : [];
  const filter = useTitleFilter(all, { own, passes: (t, v) => !v.downloaded || held(t), named: (t) => (kind === "show" ? t.title : filmName(t)) });
  const compare = COMPARED[order.key] || COMPARED.added;
  const titles = [...filter.shown].sort((a, b) => (order.dir === "asc" ? 1 : -1) * compare(a, b));
  const field = { ...filter.field, ...withOrder(filter.field, SAVED_SORTS[kind].map(orderOf), order, pick) };
  return (
    <Shelf kind={kind} category={category} marks={marks}
      filter={{ titles: saved.data ? titles : null, total: saved.data ? all.length : null, error: saved.error, reload: saved.reload, picks: filter.picks, field }} />
  );
}

/** A shelf's page: its heading over how many it shows, the field, and the titles. */
function Shelf({ kind, category, filter, marks }) {
  const words = shelfOf(kind, category);
  const online = useOnline();
  const { titles, total } = filter;
  const genre = filter.picks.genre.length === 1 ? filter.picks.genre[0] : null;

  return (
    <div className="kc-page">
      <PageHead title={words.title} sub={titles ? shownOf(titles.length, total, words.unit) : null} />
      <Switches kind={kind} category={category} />

      {total > 0 && <FilterField placeholder={`Search ${words.unit[1]}`} {...filter.field} />}

      <Failed error={titles ? null : filter.error} onRetry={filter.reload} />
      {total === 0 && (
        <Empty icon={words.icon} title={words.empty}>{kind === "show" ? "Search for a show to fetch its episodes." : null}</Empty>
      )}
      {total > 0 && titles.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {titles && (titles.length > 0 || genre) && (
        <div className="kc-grid">
          {kind === "show"
            ? titles.map((s) => <ShowCard key={s.imdbId || s.firstTitleId} show={s} downloaded={marks.episodes(s.imdbId)} />)
            : titles.map((t) => <FilmCard key={t.id} title={t} download={marks.film(t.id)} />)}
          {online && genre && <MoreTile label="Discover more" to={discoverGenrePath(kind, genre, category)} />}
        </div>
      )}
    </div>
  );
}
