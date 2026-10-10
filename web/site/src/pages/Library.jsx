import {
  Toolbar, ToolbarCount, ToolbarFilters, ToolbarSearch, ToolbarSort, ToolbarSpacer, useFilters,
} from "@thekrystalship/krystal-ui";
import { filmName } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLibrary } from "../offline/hooks.js";
import { ANIME, discoverGenrePath, isAnime, libraryPath } from "../paths.js";
import { navigate } from "../router.js";
import { SORTED, useSort } from "../sorts.js";
import { Empty, Failed, FilmCard, PageHead, ShowCard } from "../components/Film.jsx";
import { MoreTile } from "../components/TitleCards.jsx";
import { useDownloadMarks, useOffersSaving } from "../components/DownloadButton.jsx";

/**
 * The Filters popover's Download field, where this browser downloads: every title, or only those
 * downloaded to this device (`held`).
 */
function downloadField(f, offered, all, held) {
  return {
    id: "download", label: "Download", value: f.values.download, onChange: (v) => f.set("download", v), default: "all", hidden: !offered,
    options: [{ value: "all", label: "All" }, { value: "downloaded", label: "Downloaded", count: all.filter(held).length }],
  };
}

const rating = (t) => (t.details && t.details.rating) || 0;
const year = (t) => (t.film && t.film.year) || 0;

// Each comparator orders ascending; the toolbar's direction reverses it.
const SORTS = [
  { value: "added", label: "Recently added", defaultDir: "desc", compare: (a, b) => (a.addedAt || "").localeCompare(b.addedAt || "") },
  { value: "name", label: "Name", defaultDir: "asc", compare: (a, b) => filmName(a).localeCompare(filmName(b)) },
  { value: "rating", label: "Rating", defaultDir: "desc", compare: (a, b) => rating(a) - rating(b) },
  { value: "year", label: "Year", defaultDir: "desc", compare: (a, b) => year(a) - year(b) },
  { value: "length", label: "Length", defaultDir: "asc", compare: (a, b) => a.durationSeconds - b.durationSeconds },
];

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
 * Every title of one kind and category on the shelf, searched by name, narrowed by state, genre and
 * download, and ordered the way somebody asks; with a genre picked, the last tile leads on to
 * Discover's catalogue of that genre. The kind and the category are the API's words for what a title
 * is and where it is shelved.
 */
export function Library({ kind = "film", category = null, fromDevice = false }) {
  return kind === "show"
    ? <ShowLibrary category={category} fromDevice={fromDevice} />
    : <FilmLibrary category={category} fromDevice={fromDevice} />;
}

/** A row of options, one of them on, each replacing the page with its own rather than stacking on it. */
function Segmented({ label, options, on, className }) {
  return (
    <div className={"kc-segmented kc-segmented--fill " + className} role="radiogroup" aria-label={label}>
      {options.map((o) => (
        <button key={o.id} type="button" role="radio" aria-checked={o.id === on}
          className={"kc-segmented__option" + (o.id === on ? " is-on" : "")}
          onClick={() => { if (o.id !== on) navigate(o.path, { replace: true }); }}>
          {o.label}
        </button>
      ))}
    </div>
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

const SHOW_SORTS = [
  { value: "added", label: "Recently added", defaultDir: "desc", compare: (a, b) => (a.addedAt || "").localeCompare(b.addedAt || "") },
  { value: "name", label: "Name", defaultDir: "asc", compare: (a, b) => a.title.localeCompare(b.title) },
  { value: "rating", label: "Rating", defaultDir: "desc", compare: (a, b) => rating(a) - rating(b) },
  { value: "year", label: "First aired", defaultDir: "desc", compare: (a, b) => (a.firstYear || 0) - (b.firstYear || 0) },
];

/**
 * Every show the library holds, one card each however many of its episodes are here, searched by
 * name, narrowed by genre, and ordered the way somebody asks.
 */
function ShowLibrary({ category, fromDevice }) {
  const shows = useLibrary("show", fromDevice);
  const words = shelfOf("show", category);
  const online = useOnline();
  const f = useFilters({ search: "", genre: "all", download: "all" });
  const offered = useOffersSaving();
  const marks = useDownloadMarks();
  const held = (s) => marks.episodes(s.imdbId) > 0;
  const [sort, setSort] = useSort(SORTED.shows, SHOW_SORTS);

  const all = (shows.data || []).filter((s) => onShelf(s, category));
  const genreCounts = new Map();
  for (const s of all) for (const g of (s.details && s.details.genres) || []) genreCounts.set(g, (genreCounts.get(g) || 0) + 1);
  const genres = [...genreCounts.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));

  const inGenre = (s) => f.values.genre === "all" || !!(s.details && s.details.genres.includes(f.values.genre));
  const query = f.debouncedQuery.trim().toLowerCase();
  const order = SHOW_SORTS.find((s) => s.value === sort.key) || SHOW_SORTS[0];
  const shown = all
    .filter((s) => (!query || s.title.toLowerCase().includes(query)) && inGenre(s) && (f.values.download === "all" || held(s)))
    .sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));

  const fields = [
    {
      id: "genre", label: "Genre", value: f.values.genre, onChange: (v) => f.set("genre", v), default: "all", hidden: genres.length === 0,
      options: [{ value: "all", label: "Every genre" }, ...genres.map(([g, n]) => ({ value: g, label: g, count: n }))],
    },
    downloadField(f, offered, all, held),
  ];

  return (
    <div className="kc-page">
      <PageHead title={words.title} sub={shows.data ? `${all.length} ${all.length === 1 ? words.unit[0] : words.unit[1]}` : null} />
      <Switches kind="show" category={category} />

      {all.length > 0 && (
        <Toolbar>
          <ToolbarSearch value={f.query} onChange={f.setQuery} pending={f.searchPending} placeholder={`Search ${words.unit[1]}…`} />
          <ToolbarFilters fields={fields} onReset={() => f.setQuery("")} />
          <ToolbarSort value={sort.key} dir={sort.dir} onChange={setSort} options={SHOW_SORTS} />
          <ToolbarSpacer />
          <ToolbarCount shown={shown.length} total={all.length} unit={words.unit[1]} />
        </Toolbar>
      )}

      <Failed error={shows.error} onRetry={shows.reload} />
      {shows.data && all.length === 0 && <Empty icon={words.icon} title={words.empty}>Search for a show to fetch its episodes.</Empty>}
      {all.length > 0 && shown.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {(shown.length > 0 || f.values.genre !== "all") && (
        <div className="kc-grid">
          {shown.map((s) => <ShowCard key={s.imdbId || s.firstTitleId} show={s} downloaded={marks.episodes(s.imdbId)} />)}
          {online && f.values.genre !== "all" && <MoreTile label="Discover more" to={discoverGenrePath("show", f.values.genre, category)} />}
        </div>
      )}
    </div>
  );
}

function FilmLibrary({ category, fromDevice }) {
  const kind = "film";
  const titles = useLibrary("film", fromDevice);
  const online = useOnline();
  const f = useFilters({ search: "", state: "all", genre: "all", download: "all" });
  const offered = useOffersSaving();
  const marks = useDownloadMarks();
  const held = (t) => !!marks.film(t.id) && marks.film(t.id).kind === "done";
  const [sort, setSort] = useSort(SORTED.films, SORTS);
  const words = shelfOf(kind, category);

  const all = (titles.data || []).filter((t) => t.kind !== "show" && onShelf(t, category));

  const states = [
    { value: "all", label: "All", test: () => true },
    { value: "ready", label: "Ready", test: (t) => t.status === "ready" },
    { value: "preparing", label: "Preparing", test: (t) => t.status === "transcoding" },
    { value: "failed", label: "Could not be prepared", test: (t) => t.status === "failed" },
  ];
  // The genres the shelf actually holds, most common first: a genre nothing here is in is no filter.
  const genreCounts = new Map();
  for (const t of all) for (const g of (t.details && t.details.genres) || []) genreCounts.set(g, (genreCounts.get(g) || 0) + 1);
  const genres = [...genreCounts.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));

  const state = states.find((s) => s.value === f.values.state) || states[0];
  const inGenre = (t) => f.values.genre === "all" || !!(t.details && t.details.genres.includes(f.values.genre));
  const query = f.debouncedQuery.trim().toLowerCase();
  const matches = (t) => !query || filmName(t).toLowerCase().includes(query);
  const order = SORTS.find((s) => s.value === sort.key) || SORTS[0];
  const shown = all
    .filter((t) => matches(t) && state.test(t) && inGenre(t) && (f.values.download === "all" || held(t)))
    .sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));

  const option = (o) => ({ value: o.value, label: o.label, count: all.filter(o.test).length });
  const fields = [
    { id: "state", label: "State", value: f.values.state, onChange: (v) => f.set("state", v), default: "all", options: states.map(option) },
    {
      id: "genre", label: "Genre", value: f.values.genre, onChange: (v) => f.set("genre", v), default: "all", hidden: genres.length === 0,
      options: [{ value: "all", label: "Every genre" }, ...genres.map(([g, n]) => ({ value: g, label: g, count: n }))],
    },
    downloadField(f, offered, all, held),
  ];
  const unit = all.length === 1 ? words.unit[0] : words.unit[1];

  return (
    <div className="kc-page">
      <PageHead title={words.title} sub={titles.data ? `${all.length} ${unit}` : null} />
      <Switches kind={kind} category={category} />

      {all.length > 0 && (
        <Toolbar>
          <ToolbarSearch value={f.query} onChange={f.setQuery} pending={f.searchPending} placeholder={`Search ${words.unit[1]}…`} />
          <ToolbarFilters fields={fields} onReset={() => f.setQuery("")} />
          <ToolbarSort value={sort.key} dir={sort.dir} onChange={setSort} options={SORTS} />
          <ToolbarSpacer />
          <ToolbarCount shown={shown.length} total={all.length} unit={words.unit[1]} />
        </Toolbar>
      )}

      <Failed error={titles.error} onRetry={titles.reload} />

      {titles.data && all.length === 0 && <Empty icon={words.icon} title={words.empty} />}
      {all.length > 0 && shown.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {(shown.length > 0 || f.values.genre !== "all") && (
        <div className="kc-grid">
          {shown.map((t) => <FilmCard key={t.id} title={t} download={marks.film(t.id)} />)}
          {online && f.values.genre !== "all" && <MoreTile label="Discover more" to={discoverGenrePath(kind, f.values.genre, category)} />}
        </div>
      )}
    </div>
  );
}
