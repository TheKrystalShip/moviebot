import React from "react";
import {
  Toolbar, ToolbarCount, ToolbarFilters, ToolbarSearch, ToolbarSort, ToolbarSpacer, useFilters,
} from "@thekrystalship/krystal-ui";
import { api, filmName } from "../api.js";
import { useLoad } from "../data.js";
import { discoverGenrePath, libraryPath } from "../paths.js";
import { navigate } from "../router.js";
import { Empty, Failed, FilmCard, PageHead, ShowCard } from "../components/Film.jsx";
import { MoreTile } from "../components/TitleCards.jsx";

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

const KINDS = {
  film: { title: "Movies", unit: ["movie", "movies"], empty: "No movies yet" },
  show: { title: "TV Shows", unit: ["show", "shows"], empty: "No shows yet" },
};

/**
 * Every title of one kind on the shelf, searched by name, narrowed by state and genre, and
 * ordered the way somebody asks; with a genre picked, the last tile leads on to Discover's catalogue
 * of that genre. The kind is the API's word for what a title is.
 */
export function Library({ kind = "film" }) {
  return kind === "show" ? <ShowLibrary /> : <FilmLibrary />;
}

/**
 * Movies or TV shows, on a phone. Its Library tab is the one way into either, where a wide screen
 * has a sidebar entry for each; picking one replaces the page rather than stacking on it.
 */
function KindSwitch({ kind }) {
  const options = [{ kind: "film", label: "Movies", path: libraryPath("film") }, { kind: "show", label: "TV shows", path: libraryPath("show") }];
  return (
    <div className="kc-segmented kc-segmented--fill kc-kind-switch" role="radiogroup" aria-label="Library">
      {options.map((o) => (
        <button key={o.kind} type="button" role="radio" aria-checked={o.kind === kind}
          className={"kc-segmented__option" + (o.kind === kind ? " is-on" : "")}
          onClick={() => { if (o.kind !== kind) navigate(o.path, { replace: true }); }}>
          {o.label}
        </button>
      ))}
    </div>
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
function ShowLibrary() {
  const shows = useLoad(() => api.shows(), []);
  const f = useFilters({ search: "", genre: "all" });
  const [sort, setSort] = React.useState({ key: "added", dir: "desc" });

  const all = shows.data || [];
  const genreCounts = new Map();
  for (const s of all) for (const g of (s.details && s.details.genres) || []) genreCounts.set(g, (genreCounts.get(g) || 0) + 1);
  const genres = [...genreCounts.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));

  const inGenre = (s) => f.values.genre === "all" || !!(s.details && s.details.genres.includes(f.values.genre));
  const query = f.debouncedQuery.trim().toLowerCase();
  const order = SHOW_SORTS.find((s) => s.value === sort.key) || SHOW_SORTS[0];
  const shown = all
    .filter((s) => (!query || s.title.toLowerCase().includes(query)) && inGenre(s))
    .sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));

  const fields = [
    {
      id: "genre", label: "Genre", value: f.values.genre, onChange: (v) => f.set("genre", v), default: "all", hidden: genres.length === 0,
      options: [{ value: "all", label: "Every genre" }, ...genres.map(([g, n]) => ({ value: g, label: g, count: n }))],
    },
  ];

  return (
    <div className="kc-page">
      <PageHead title="TV Shows" sub={shows.data ? `${all.length} ${all.length === 1 ? "show" : "shows"}` : null} />
      <KindSwitch kind="show" />

      {all.length > 0 && (
        <Toolbar>
          <ToolbarSearch value={f.query} onChange={f.setQuery} pending={f.searchPending} placeholder="Search shows…" />
          <ToolbarFilters fields={fields} onReset={() => f.setQuery("")} />
          <ToolbarSort value={sort.key} dir={sort.dir} onChange={(key, dir) => setSort({ key, dir })} options={SHOW_SORTS} />
          <ToolbarSpacer />
          <ToolbarCount shown={shown.length} total={all.length} unit="shows" />
        </Toolbar>
      )}

      <Failed error={shows.error} onRetry={shows.reload} />
      {shows.data && all.length === 0 && <Empty icon="tv" title="No shows yet">Search for a show to fetch its episodes.</Empty>}
      {all.length > 0 && shown.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {(shown.length > 0 || f.values.genre !== "all") && (
        <div className="kc-grid">
          {shown.map((s) => <ShowCard key={s.imdbId || s.firstTitleId} show={s} />)}
          {f.values.genre !== "all" && <MoreTile label="Discover more" to={discoverGenrePath("show", f.values.genre)} />}
        </div>
      )}
    </div>
  );
}

function FilmLibrary() {
  const kind = "film";
  const titles = useLoad(() => api.titles(), []);
  const f = useFilters({ search: "", state: "all", genre: "all" });
  const [sort, setSort] = React.useState({ key: "added", dir: "desc" });
  const words = KINDS[kind] || KINDS.film;

  const all = (titles.data || []).filter((t) => (kind === "show" ? t.kind === "show" : t.kind !== "show"));

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
    .filter((t) => matches(t) && state.test(t) && inGenre(t))
    .sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));

  const option = (o) => ({ value: o.value, label: o.label, count: all.filter(o.test).length });
  const fields = [
    { id: "state", label: "State", value: f.values.state, onChange: (v) => f.set("state", v), default: "all", options: states.map(option) },
    {
      id: "genre", label: "Genre", value: f.values.genre, onChange: (v) => f.set("genre", v), default: "all", hidden: genres.length === 0,
      options: [{ value: "all", label: "Every genre" }, ...genres.map(([g, n]) => ({ value: g, label: g, count: n }))],
    },
  ];
  const unit = all.length === 1 ? words.unit[0] : words.unit[1];

  return (
    <div className="kc-page">
      <PageHead title={words.title} sub={titles.data ? `${all.length} ${unit}` : null} />
      <KindSwitch kind={kind} />

      {all.length > 0 && (
        <Toolbar>
          <ToolbarSearch value={f.query} onChange={f.setQuery} pending={f.searchPending} placeholder={`Search ${words.unit[1]}…`} />
          <ToolbarFilters fields={fields} onReset={() => f.setQuery("")} />
          <ToolbarSort value={sort.key} dir={sort.dir} onChange={(key, dir) => setSort({ key, dir })} options={SORTS} />
          <ToolbarSpacer />
          <ToolbarCount shown={shown.length} total={all.length} unit={words.unit[1]} />
        </Toolbar>
      )}

      <Failed error={titles.error} onRetry={titles.reload} />

      {titles.data && all.length === 0 && <Empty icon={kind === "show" ? "tv" : "clapperboard"} title={words.empty} />}
      {all.length > 0 && shown.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {(shown.length > 0 || f.values.genre !== "all") && (
        <div className="kc-grid">
          {shown.map((t) => <FilmCard key={t.id} title={t} />)}
          {f.values.genre !== "all" && <MoreTile label="Discover more" to={discoverGenrePath(kind, f.values.genre)} />}
        </div>
      )}
    </div>
  );
}
