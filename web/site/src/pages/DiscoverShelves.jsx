import React from "react";
import {
  BriefCard, CollectionTile, Icon, Rail, Skeleton, Toolbar, ToolbarButton, ToolbarCount, ToolbarFilters, ToolbarSort,
  ToolbarSpacer, useStoredFlag,
} from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { FILTER_PARAMS, discoverBrowsePath, discoverFilterPath, discoverShelfPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { SORTED, useSort } from "../sorts.js";
import { Empty, Failed } from "../components/Film.jsx";
import { CardsSkeleton, FoundCard, foundPage } from "../components/TitleCards.jsx";

// Discover with nothing typed: its rows of shelves, the moods and the franchises as tiles, and the
// browse categories; a category's shelves; one shelf whole; and the filter. Every shelf comes from
// `/api/discover`, and what is shown is in the address.

/** How many of a shelf's titles its rail shows; its own page shows the rest. */
const RAIL_TITLES = 20;

/** Whether the person leaves what they watched off a shelf and the filter's answers, kept on the device. */
export function useHideWatched() {
  return useStoredFlag("cinema:discover:hide-watched");
}

/** Whether the page is a phone's width, where tiles take the shape of a two-column grid. */
function useNarrow() {
  const query = "(max-width: 600px)";
  const [narrow, setNarrow] = React.useState(() => typeof window !== "undefined" && !!window.matchMedia && window.matchMedia(query).matches);
  React.useEffect(() => {
    if (!window.matchMedia) return undefined;
    const list = window.matchMedia(query);
    const change = () => setNarrow(list.matches);
    list.addEventListener("change", change);
    return () => list.removeEventListener("change", change);
  }, []);
  return narrow;
}

/**
 * Surprise me: opens one title picked at random, never one the person watched, from `titles` or from
 * one of `shelves` picked at random first. Answers whether it is picking, and the way to pick.
 */
export function useSurprise(where) {
  const [busy, setBusy] = React.useState(false);
  const pick = async ({ titles, shelves }) => {
    setBusy(true);
    try {
      let from = titles;
      for (let tries = 0; !from && shelves && shelves.length && tries < 3; tries++) {
        const shelf = await api.discoverShelf(shelves[Math.floor(Math.random() * shelves.length)]).catch(() => null);
        if (shelf && shelf.titles.some((t) => !t.watched)) from = shelf.titles;
      }
      const choices = (from || []).filter((t) => !t.watched && t.imdbId);
      if (choices.length === 0) return;
      const t = choices[Math.floor(Math.random() * choices.length)];
      const { here, held } = where(t);
      navigate(foundPage(t, here, held));
    } finally {
      setBusy(false);
    }
  };
  return [busy, pick];
}

/** The shelves Surprise me picks from on the page and in a category: every one the same for everybody. */
export const surprisePool = (index, section) => (index ? index.sections : [])
  .filter((s) => !section || s.id === section)
  .flatMap((s) => s.shelves.map((h) => h.id));

/** A shelf's tile: a mood's posters fanned, a series' films in order with how far the person got. */
function ShelfTile({ head, series, size = "md" }) {
  const link = linkProps(discoverShelfPath(head.id));
  if (series) {
    return (
      <CollectionTile variant="strip" size={size} name={head.title} posters={head.posters || []}
        total={head.total} watched={head.watched || 0} next={head.next ? "Next: " + head.next : null}
        caption={head.total != null ? `${head.total} ${head.total === 1 ? "film" : "films"}` : null} {...link} />
    );
  }
  return (
    <CollectionTile variant="fan" size={size} name={head.title} icon={head.icon} posters={head.posters || []}
      caption={head.total ? `${head.total} titles` : null} {...link} />
  );
}

/** The page's rows, in the order `/api/discover` gives them. */
export function DiscoverRows({ index, where }) {
  const sections = new Map(index.sections.map((s) => [s.id, s]));
  return (
    <>
      {index.rows.map((row, i) => {
        if (row.kind === "shelf") return <LazyShelf key={row.shelf.id} head={row.shelf} where={where} />;
        if (row.kind === "browse") return <BrowseGrid key="browse" sections={index.sections} />;
        const section = sections.get(row.kind);
        return section ? <TileRail key={row.kind} section={section} /> : <React.Fragment key={i} />;
      })}
      <p className="kc-attribution-note">Titles and artwork from TMDB.</p>
    </>
  );
}

/** A category's shelves as a rail of tiles: the moods, the franchises. */
function TileRail({ section }) {
  const shelves = section.shelves.filter((h) => h.total !== 0);
  if (shelves.length === 0) return null;
  return (
    <Rail
      variant="tiles" icon={section.icon} title={section.label} count={shelves.length}
      items={shelves} itemKey={(h) => h.id}
      renderItem={(h) => <ShelfTile head={h} series={section.series} />}
      onViewAll={() => navigate(discoverBrowsePath(section.id))} viewAllLabel="See all" />
  );
}

/** Every browse category as a tile, each leading to its shelves. */
function BrowseGrid({ sections }) {
  const narrow = useNarrow();
  return (
    <BriefCard icon="compass" title="Browse">
      <div className="kc-browse-grid">
        {sections.map((s) => (
          <CollectionTile key={s.id} variant="fan" size={narrow ? "sm" : "md"} name={s.label} icon={s.icon}
            caption={narrow ? null : s.caption} posters={s.posters} {...linkProps(discoverBrowsePath(s.id))} />
        ))}
      </div>
    </BriefCard>
  );
}

/**
 * One shelf's rail, asked for once it nears the screen, so a page of many shelves asks only for what is
 * looked at. A shelf with nothing on it, or that could not be answered, is left out.
 */
function LazyShelf({ head, where }) {
  const ref = React.useRef(null);
  const [near, setNear] = React.useState(false);
  React.useEffect(() => {
    const el = ref.current;
    if (!el || near) return undefined;
    if (typeof IntersectionObserver !== "function") {
      setNear(true);
      return undefined;
    }
    const watcher = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting)) setNear(true);
    }, { rootMargin: "600px 0px" });
    watcher.observe(el);
    return () => watcher.disconnect();
  }, [near]);

  return (
    <div ref={ref} className="kc-lazy-shelf">
      {near ? <ShelfRail head={head} where={where} /> : <RailSkeleton />}
    </div>
  );
}

function ShelfRail({ head, where }) {
  const shelf = useLoad(() => api.discoverShelf(head.id), [head.id]);
  if (!shelf.data) return shelf.error ? null : <RailSkeleton />;
  const titles = shelf.data.titles;
  if (titles.length === 0) return null;
  return (
    <Rail
      variant="posters" icon={head.icon} title={head.title} count={titles.length}
      items={titles.slice(0, RAIL_TITLES)} itemKey={(t) => t.kind + t.imdbId}
      renderItem={(t) => <FoundCard found={t} {...where(t)} />}
      onViewAll={() => navigate(discoverShelfPath(head.id))} viewAllLabel="See all" />
  );
}

/** A heading over a category, a shelf or the filter: what it is in, its name, and how many. */
function BrowseHead({ eyebrow, icon, title, count }) {
  return (
    <div className="kc-browse-head">
      <div className="kc-browse-head__text">
        {eyebrow && <span className="kc-eyebrow">{eyebrow}</span>}
        <h2>{icon && <span className="kc-browse-head__mark"><Icon name={icon} size={18} /></span>}{title}</h2>
      </div>
      {count != null && <span className="kc-count">{count}</span>}
    </div>
  );
}

/** One browse category: a series' tiles in a grid, any other's shelves as rails. */
export function CategoryPage({ id, index, where }) {
  const [busy, surprise] = useSurprise(where);
  const narrow = useNarrow();
  if (index.error && !index.data) return <Failed error={index.error} onRetry={index.reload} />;
  if (!index.data) return <BrowseSkeleton />;
  const section = index.data.sections.find((s) => s.id === id);
  if (!section) return <Empty icon="compass" title="Nothing by that name" />;
  return (
    <section className="kc-shelf">
      <BrowseHead eyebrow="Discover" icon={section.icon} title={section.label} count={section.shelves.length} />
      <Toolbar>
        <ToolbarSpacer />
        <ToolbarButton icon="dices" spinning={busy} disabled={busy}
          onClick={() => surprise({ shelves: surprisePool(index.data, section.id) })}>Surprise me</ToolbarButton>
      </Toolbar>
      {section.series
        ? (
          <div className="kc-browse-grid">
            {section.shelves.filter((h) => h.total !== 0).map((h) => <ShelfTile key={h.id} head={h} series size={narrow ? "sm" : "md"} />)}
          </div>
        )
        : section.shelves.map((h) => <LazyShelf key={h.id} head={h} where={where} />)}
      <p className="kc-attribution-note">Titles and artwork from TMDB.</p>
    </section>
  );
}

/** The decades a filter offers, newest first, by their first year. */
const DECADES = [2020, 2010, 2000, 1990, 1980, 1970, 1960, 1950, 1940];

const decadeOf = (year) => (year ? Math.floor(year / 10) * 10 : null);

/** The filter field that leaves off what the person watched, which the device keeps for them. */
const watchedField = (hide, setHide) => ({
  id: "watched", label: "Watched", value: hide ? "hide" : "show", default: "show", onChange: (v) => setHide(v === "hide"),
  options: [{ value: "show", label: "Show everything" }, { value: "hide", label: "Hide what I watched" }],
});

const SHELF_SORTS = [
  { value: "order", label: "Shelf order", defaultDir: "asc" },
  { value: "rating", label: "Rating", defaultDir: "desc" },
  { value: "year", label: "Release date", defaultDir: "desc" },
];

/** A shelf's titles in an order the person picked; `order` is the shelf's own. */
function sorted(titles, { key, dir }) {
  const by = key === "rating" ? (t) => t.rating || 0 : key === "year" ? (t) => t.year || 0 : null;
  const ordered = by ? [...titles].sort((a, b) => by(a) - by(b)) : [...titles];
  return dir === "asc" ? ordered : ordered.reverse();
}

/**
 * Every title on one shelf, up to 40, with krystal-ui's Toolbar as on the library's pages: what to
 * leave off, an order, how many are shown, and Surprise me among them.
 */
export function ShelfPage({ id, where }) {
  const shelf = useLoad(() => api.discoverShelf(id), [id]);
  const [hideWatched, setHideWatched] = useHideWatched();
  const [busy, surprise] = useSurprise(where);
  const [kind, setKind] = React.useState("all");
  const [decade, setDecade] = React.useState("any");
  const [rating, setRating] = React.useState("any");
  const [sort, setSort] = useSort(SORTED.shelf, SHELF_SORTS);
  const s = shelf.data;
  if (shelf.error && !s) return <Failed error={shelf.error} onRetry={shelf.reload} />;
  if (!s) return <BrowseSkeleton />;

  const all = s.titles;
  const mixed = all.some((t) => t.kind === "show") && all.some((t) => t.kind === "film");
  const decades = DECADES.filter((d) => all.some((t) => decadeOf(t.year) === d));
  const shown = sorted(all
    .filter((t) => !hideWatched || !t.watched)
    .filter((t) => kind === "all" || t.kind === kind)
    .filter((t) => decade === "any" || decadeOf(t.year) === Number(decade))
    .filter((t) => rating === "any" || (t.rating || 0) >= Number(rating)), sort);

  const fields = [
    watchedField(hideWatched, setHideWatched),
    {
      id: "kind", label: "Kind", value: kind, default: "all", onChange: setKind, hidden: !mixed,
      options: [{ value: "all", label: "Movies and shows" }, { value: "film", label: "Movies" }, { value: "show", label: "TV shows" }],
    },
    {
      id: "decade", label: "Decade", value: decade, default: "any", onChange: setDecade, hidden: decades.length < 2,
      options: [{ value: "any", label: "Any decade" }, ...decades.map((d) => ({ value: String(d), label: d + "s", count: all.filter((t) => decadeOf(t.year) === d).length }))],
    },
    {
      id: "rating", label: "Rating", value: rating, default: "any", onChange: setRating,
      options: [{ value: "any", label: "Any rating" }, { value: "7", label: "7 and up" }, { value: "8", label: "8 and up" }],
    },
  ];

  return (
    <section className="kc-shelf">
      <BrowseHead eyebrow={s.section} icon={s.icon} title={s.title} />
      <Toolbar>
        <ToolbarFilters fields={fields} />
        <ToolbarSort value={sort.key} dir={sort.dir} onChange={setSort} options={SHELF_SORTS} />
        <ToolbarSpacer />
        <ToolbarCount shown={shown.length} total={all.length} unit="titles" />
        <ToolbarButton icon="dices" spinning={busy} disabled={busy || !shown.some((t) => !t.watched)}
          onClick={() => surprise({ titles: shown })}>Surprise me</ToolbarButton>
      </Toolbar>
      {shown.length > 0
        ? <div className="kc-grid">{shown.map((t) => <FoundCard key={t.kind + t.imdbId} found={t} {...where(t)} />)}</div>
        : <Empty icon="compass" title={all.length ? "Nothing matches" : "Nothing on this shelf"} />}
      <p className="kc-attribution-note">Titles and artwork from TMDB.</p>
    </section>
  );
}

/** A film's length as the filter words it, in minutes either side. */
const LENGTHS = {
  short: { label: "Under 90 min", max: 89 },
  standard: { label: "90 to 150 min", min: 90, max: 150 },
  long: { label: "Over 150 min", min: 151 },
};

const RATINGS = ["8", "7", "6"];

const FILTER_SORTS = [
  { value: "popular", label: "Popularity", defaultDir: "desc" },
  { value: "rating", label: "Rating", defaultDir: "desc" },
  { value: "year", label: "Release date", defaultDir: "desc" },
];

/** The filter's order as the address and the catalogue name it, from the toolbar's key and direction. */
const sortParam = ({ key, dir }) => (key === "year" ? (dir === "asc" ? "oldest" : "newest") : key);

/** The toolbar's key and direction for the order the address names. */
const sortOrder = (param) => (param === "newest" || param === "oldest"
  ? { key: "year", dir: param === "oldest" ? "asc" : "desc" }
  : { key: param, dir: "desc" });

const SORT_PARAMS = ["popular", "rating", "newest", "oldest"];

/** The filter's choices as the address holds them; anything it does not offer is any. */
export function readFilter(query) {
  const kind = query.get("filter") === "show" ? "show" : "film";
  const get = (key) => query.get(FILTER_PARAMS[key]) || "";
  const decade = Number(get("decade"));
  return {
    kind,
    genre: get("genre").trim(),
    decade: DECADES.includes(decade) ? String(decade) : "",
    length: kind === "film" && LENGTHS[get("length")] ? get("length") : "",
    rating: RATINGS.includes(get("rating")) ? get("rating") : "",
    lang: /^[a-z]{2}$/.test(get("lang")) ? get("lang") : "",
    sort: SORT_PARAMS.includes(get("sort")) ? get("sort") : "",
  };
}

/** What the catalogue is asked for a filter. Any rating still needs a hundred votes, so nobody's home video is listed. */
function filterQuestion(f) {
  const q = { kind: f.kind, limit: "20" };
  if (f.genre) q.genre = f.genre;
  if (f.decade) {
    q.from = f.decade;
    q.to = String(Number(f.decade) + 9);
  }
  if (f.length) {
    if (LENGTHS[f.length].min) q.minRuntime = String(LENGTHS[f.length].min);
    if (LENGTHS[f.length].max) q.maxRuntime = String(LENGTHS[f.length].max);
  }
  if (f.rating) {
    q.minRating = f.rating;
    q.minVotes = "200";
  } else {
    q.goodOnly = "false";
    q.minVotes = "100";
  }
  if (f.lang) q.language = f.lang;
  if (f.sort && f.sort !== "popular") q.sort = f.sort;
  return q;
}

/** The filter's fields, as krystal-ui's ToolbarFilters draws them, each changing `f` through `set`. */
function filterFields(f, set, index, hideWatched, setHideWatched) {
  const genres = index ? (f.kind === "show" ? index.showGenres : index.filmGenres) : [];
  const languages = index ? index.languages : [];
  return [
    {
      id: "kind", label: "Kind", value: f.kind, default: "film", onChange: (v) => set({ kind: v, genre: "", length: "" }),
      options: [{ value: "film", label: "Movies" }, { value: "show", label: "TV shows" }],
    },
    {
      id: "genre", label: "Genre", value: f.genre, default: "", onChange: (v) => set({ genre: v }),
      options: [{ value: "", label: "Any genre" }, ...genres.map((g) => ({ value: g, label: g }))],
    },
    {
      id: "decade", label: "Decade", value: f.decade, default: "", onChange: (v) => set({ decade: v }),
      options: [{ value: "", label: "Any decade" }, ...DECADES.map((d) => ({ value: String(d), label: d + "s" }))],
    },
    {
      id: "length", label: "Length", value: f.length, default: "", onChange: (v) => set({ length: v }), hidden: f.kind !== "film",
      options: [{ value: "", label: "Any length" }, ...Object.entries(LENGTHS).map(([value, l]) => ({ value, label: l.label }))],
    },
    {
      id: "rating", label: "Rating", value: f.rating, default: "", onChange: (v) => set({ rating: v }),
      options: [{ value: "", label: "Any rating" }, ...RATINGS.map((r) => ({ value: r, label: r + " and up" }))],
    },
    {
      id: "lang", label: "Language", value: f.lang, default: "", onChange: (v) => set({ lang: v }),
      options: [{ value: "", label: "Any language" }, ...languages.map((l) => ({ value: l.code, label: l.name }))],
    },
    watchedField(hideWatched, setHideWatched),
  ];
}

/** The filter's control beside Discover's field: picking anything opens the filter at that choice. */
export function LandingFilters({ index }) {
  const [hideWatched, setHideWatched] = useHideWatched();
  const f = readFilter(new URLSearchParams());
  const open = (change) => navigate(discoverFilterPath({ ...f, ...change }));
  const fields = filterFields(f, open, index, hideWatched, (hide) => {
    setHideWatched(hide);
    open({});
  });
  return <div className="kc-discover-filters"><ToolbarFilters fields={fields} /></div>;
}

/**
 * Discover's filter: a kind, a genre, a decade, a length, a least rating, a language and an order. An
 * address that names no order is in the order the person last picked here.
 */
export function FilterPage({ query, index, where }) {
  const [hideWatched, setHideWatched] = useHideWatched();
  const [busy, surprise] = useSurprise(where);
  const [picked, pick] = useSort(SORTED.filter, FILTER_SORTS);
  const named = readFilter(query);
  const f = { ...named, sort: named.sort || sortParam(picked) };
  const order = sortOrder(f.sort);
  const question = filterQuestion(f);
  const key = JSON.stringify(question);
  const found = useLoad(() => api.catalogueDiscover(question), [key]);
  const set = (change) => navigate(discoverFilterPath({ ...named, ...change }), { replace: true });

  const all = found.data ? found.data.titles.filter((t) => t.imdbId).map((t) => ({ ...t, starring: t.starring.join(", ") })) : null;
  const titles = all && (hideWatched ? all.filter((t) => !t.watched) : all);

  return (
    <section className="kc-shelf">
      <BrowseHead eyebrow="Discover" icon="sliders-horizontal" title="Filter" />
      <Toolbar>
        <ToolbarFilters fields={filterFields(f, set, index.data, hideWatched, setHideWatched)} />
        <ToolbarSort value={order.key} dir={order.dir}
          onChange={(key, dir) => {
            pick(key, dir);
            set({ sort: sortParam({ key, dir }) });
          }}
          options={FILTER_SORTS} />
        <ToolbarSpacer />
        {titles && <ToolbarCount shown={titles.length} total={all.length} unit="titles" />}
        <ToolbarButton icon="dices" spinning={busy} disabled={busy || !titles || !titles.some((t) => !t.watched)}
          onClick={() => surprise({ titles })}>Surprise me</ToolbarButton>
      </Toolbar>
      {found.error && <Failed error={found.error} onRetry={found.reload} />}
      {!titles && !found.error && <CardsSkeleton count={12} />}
      {titles && (titles.length > 0
        ? <div className="kc-grid">{titles.map((t) => <FoundCard key={t.kind + t.imdbId} found={t} {...where(t)} />)}</div>
        : <Empty icon="sliders-horizontal" title="Nothing matches" />)}
      <p className="kc-attribution-note">Titles and artwork from TMDB.</p>
    </section>
  );
}

/** A rail while its shelf is asked for, at the rail's own height so the page does not jump. */
function RailSkeleton() {
  return (
    <div className="kc-rail-skeleton" aria-hidden="true">
      <Skeleton w={200} h={16} />
      <CardsSkeleton count={8} row />
    </div>
  );
}

/** The page's frame while `/api/discover` is asked. */
export function FrontSkeleton() {
  return (
    <>
      <Skeleton w="100%" h={380} r={6} />
      <RailSkeleton />
      <RailSkeleton />
    </>
  );
}

function BrowseSkeleton() {
  return (
    <section className="kc-shelf">
      <div className="kc-browse-head">
        <div className="kc-browse-head__text">
          <Skeleton w={60} h={12} />
          <Skeleton w={220} h={26} />
        </div>
      </div>
      <CardsSkeleton count={12} />
    </section>
  );
}
