import { RadioGroup } from "@thekrystalship/krystal";
import { filmName } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLibrary } from "../offline/hooks.js";
import { ANIME, discoverGenrePath, isAnime, libraryPath } from "../paths.js";
import { navigate } from "../router.js";
import { SORTED, useSort } from "../sorts.js";
import { shownOf, withOrder } from "../orders.js";
import { Empty, Failed, FilmCard, PageHead, ShowCard } from "../components/Film.jsx";
import { FilterField, useTitleFilter } from "../components/FilterField.jsx";
import { MoreTile } from "../components/TitleCards.jsx";
import { useDownloadMarks, useOffersSaving } from "../components/DownloadButton.jsx";

/** The Download section, where this browser downloads: only the titles downloaded to this device. */
const downloadedSection = { id: "downloaded", label: "Download", kind: "toggle", option: "Downloaded here" };

const rating = (t) => (t.details && t.details.rating) || 0;
const year = (t) => (t.film && t.film.year) || 0;

// Each comparator orders ascending; an order's direction reverses it.
export const SORTS = [
  { value: "added", defaultDir: "desc", compare: (a, b) => (a.addedAt || "").localeCompare(b.addedAt || "") },
  { value: "name", defaultDir: "asc", compare: (a, b) => filmName(a).localeCompare(filmName(b)) },
  { value: "rating", defaultDir: "desc", compare: (a, b) => rating(a) - rating(b) },
  { value: "year", defaultDir: "desc", compare: (a, b) => year(a) - year(b) },
  { value: "length", defaultDir: "asc", compare: (a, b) => a.durationSeconds - b.durationSeconds },
];

/** The orders the drawer offers a shelf of films, and of shows, which have no length. */
export const SHOW_ORDERS = [
  { key: "added", dir: "desc", label: "Recently added" },
  { key: "added", dir: "asc", label: "First added" },
  { key: "name", dir: "asc", label: "Name A–Z" },
  { key: "name", dir: "desc", label: "Name Z–A" },
  { key: "rating", dir: "desc", label: "Highest rated" },
  { key: "rating", dir: "asc", label: "Lowest rated" },
  { key: "year", dir: "desc", label: "Newest first" },
  { key: "year", dir: "asc", label: "Oldest first" },
];
export const FILM_ORDERS = [
  ...SHOW_ORDERS,
  { key: "length", dir: "asc", label: "Shortest first" },
  { key: "length", dir: "desc", label: "Longest first" },
];

// What each shelf calls itself and its titles: the general shelves by kind, and Anime's by kind under it.
const SHELVES = {
  film: { title: "Movies", unit: ["movie", "movies"], empty: "No movies yet", icon: "clapperboard" },
  show: { title: "TV Shows", unit: ["show", "shows"], empty: "No shows yet", icon: "tv" },
  "anime:film": { title: "Anime", unit: ["film", "films"], empty: "No anime films yet", icon: "swords" },
  "anime:show": { title: "Anime", unit: ["series", "series"], empty: "No anime series yet", icon: "swords" },
};

/** The states a film on the shelf can be in, by the words the State section and its address use. */
const FILM_STATES = [
  { value: "ready", label: "Ready", test: (t) => t.status === "ready" },
  { value: "preparing", label: "Preparing", test: (t) => t.status === "transcoding" },
  { value: "failed", label: "Could not be prepared", test: (t) => t.status === "failed" },
];

const shelfOf = (kind, category) => SHELVES[(category === ANIME ? "anime:" : "") + kind] || SHELVES.film;

/**
 * Whether a listed title belongs on a shelf of `category`: Anime holds what the API shelves as anime,
 * and the general shelves hold everything else, a title nothing describes yet among them.
 */
const onShelf = (title, category) => isAnime(title) === (category === ANIME);

/**
 * Every title of one kind and category on the shelf, searched by name, narrowed by the filter drawer
 * (genre, decade, length, rating and language, the shelf's options counted over its titles, and a
 * film's state and download), and ordered the way somebody asks; with one genre picked, the last tile
 * leads on to Discover's catalogue of that genre. The kind and the category are the API's words for
 * what a title is and where it is shelved.
 */
export function Library({ kind = "film", category = null, fromDevice = false }) {
  return kind === "show"
    ? <ShowLibrary category={category} fromDevice={fromDevice} />
    : <FilmLibrary category={category} fromDevice={fromDevice} />;
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

export const SHOW_SORTS = [
  { value: "added", defaultDir: "desc", compare: (a, b) => (a.addedAt || "").localeCompare(b.addedAt || "") },
  { value: "name", defaultDir: "asc", compare: (a, b) => a.title.localeCompare(b.title) },
  { value: "rating", defaultDir: "desc", compare: (a, b) => rating(a) - rating(b) },
  { value: "year", defaultDir: "desc", compare: (a, b) => (a.firstYear || 0) - (b.firstYear || 0) },
];

/**
 * Every show the library holds, one card each however many of its episodes are here, searched by
 * name, narrowed by the filter drawer, and ordered the way somebody asks.
 */
function ShowLibrary({ category, fromDevice }) {
  const shows = useLibrary("show", fromDevice);
  const words = shelfOf("show", category);
  const online = useOnline();
  const offered = useOffersSaving();
  const marks = useDownloadMarks();
  const held = (s) => marks.episodes(s.imdbId) > 0;
  const [sort, setSort] = useSort(SORTED.shows, SHOW_SORTS);

  const all = (shows.data || []).filter((s) => onShelf(s, category));
  const own = offered && all.some(held) ? [downloadedSection] : [];
  const filter = useTitleFilter(all, { own, passes: (s, v) => !v.downloaded || held(s), named: (s) => s.title });
  const order = SHOW_SORTS.find((s) => s.value === sort.key) || SHOW_SORTS[0];
  const shown = [...filter.shown].sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));
  const genre = filter.picks.genre.length === 1 ? filter.picks.genre[0] : null;

  return (
    <div className="kc-page">
      <PageHead title={words.title} sub={shows.data ? shownOf(shown.length, all.length, words.unit) : null} />
      <Switches kind="show" category={category} />

      {all.length > 0 && (
        <FilterField placeholder={`Search ${words.unit[1]}`} {...filter.field} {...withOrder(filter.field, SHOW_ORDERS, sort, setSort)} />
      )}

      <Failed error={shows.error} onRetry={shows.reload} />
      {shows.data && all.length === 0 && <Empty icon={words.icon} title={words.empty}>Search for a show to fetch its episodes.</Empty>}
      {all.length > 0 && shown.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {(shown.length > 0 || genre) && (
        <div className="kc-grid">
          {shown.map((s) => <ShowCard key={s.imdbId || s.firstTitleId} show={s} downloaded={marks.episodes(s.imdbId)} />)}
          {online && genre && <MoreTile label="Discover more" to={discoverGenrePath("show", genre, category)} />}
        </div>
      )}
    </div>
  );
}

function FilmLibrary({ category, fromDevice }) {
  const kind = "film";
  const titles = useLibrary("film", fromDevice);
  const online = useOnline();
  const offered = useOffersSaving();
  const marks = useDownloadMarks();
  const held = (t) => !!marks.film(t.id) && marks.film(t.id).kind === "done";
  const [sort, setSort] = useSort(SORTED.films, SORTS);
  const words = shelfOf(kind, category);

  const all = (titles.data || []).filter((t) => t.kind !== "show" && onShelf(t, category));

  // A film's state is offered when the shelf holds films in more than one; a state nothing is in is no filter.
  const heldStates = FILM_STATES.filter((s) => all.some(s.test));
  const own = [
    ...(heldStates.length >= 2 ? [{ id: "state", label: "State", kind: "optional", options: heldStates.map(({ value, label }) => ({ value, label })) }] : []),
    ...(offered && all.some(held) ? [downloadedSection] : []),
  ];
  const passes = (t, v) => (!v.state || !!FILM_STATES.find((s) => s.value === v.state && s.test(t))) && (!v.downloaded || held(t));
  const filter = useTitleFilter(all, { own, passes, named: filmName });
  const order = SORTS.find((s) => s.value === sort.key) || SORTS[0];
  const shown = [...filter.shown].sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));
  const genre = filter.picks.genre.length === 1 ? filter.picks.genre[0] : null;

  return (
    <div className="kc-page">
      <PageHead title={words.title} sub={titles.data ? shownOf(shown.length, all.length, words.unit) : null} />
      <Switches kind={kind} category={category} />

      {all.length > 0 && (
        <FilterField placeholder={`Search ${words.unit[1]}`} {...filter.field} {...withOrder(filter.field, FILM_ORDERS, sort, setSort)} />
      )}

      <Failed error={titles.error} onRetry={titles.reload} />

      {titles.data && all.length === 0 && <Empty icon={words.icon} title={words.empty} />}
      {all.length > 0 && shown.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {(shown.length > 0 || genre) && (
        <div className="kc-grid">
          {shown.map((t) => <FilmCard key={t.id} title={t} download={marks.film(t.id)} />)}
          {online && genre && <MoreTile label="Discover more" to={discoverGenrePath(kind, genre, category)} />}
        </div>
      )}
    </div>
  );
}
