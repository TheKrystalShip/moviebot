import React from "react";
import {
  BriefCard, CollectionTile, Icon, Rail, Skeleton, Toolbar, ToolbarButton, ToolbarCount, ToolbarSort, ToolbarSpacer, useStoredFlag,
} from "@thekrystalship/krystal";
import { api } from "../api.js";
import { POSTERS, TILES } from "../railDensity.js";
import { useLoad, usePaged } from "../data.js";
import { discoverBrowsePath, discoverShelfPath } from "../paths.js";
import { apiPicks, filterQuestion, pickCount } from "../filters.js";
import { linkProps, navigate } from "../router.js";
import { SORTED, useSort } from "../sorts.js";
import { Empty, Failed } from "../components/Film.jsx";
import { CardsSkeleton, FoundCard, foundPage } from "../components/TitleCards.jsx";

// Discover with nothing typed: its rows of shelves, the moods and the franchises as tiles, and the
// browse categories; a category's shelves; one shelf whole; and the filter's answers. Every shelf comes
// from `/api/discover`, and what is shown is in the address, the filter drawer's picks with it
// (`filters.js`).

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

/**
 * How many titles a list holds, as far as it is known: the count, with a `+` when `more` says the
 * list goes on past it (a page answered with a `next`, a tile whose shelf has `more`).
 */
export const atLeast = (count, more) => (more ? `${count}+` : count);

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
      caption={head.total ? `${atLeast(head.total, head.more)} titles` : null} {...link} />
  );
}

/** The page's rows, in the order `/api/discover` gives them. */
export function DiscoverRows({ index, where }) {
  const sections = new Map(index.sections.map((s) => [s.id, s]));
  const kept = new Map((index.shelves || []).map((s) => [s.id, s]));
  return (
    <>
      {index.rows.map((row, i) => {
        if (row.kind === "shelf") return <LazyShelf key={row.shelf.id} head={row.shelf} kept={kept.get(row.shelf.id)} where={where} />;
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
      perView={TILES} icon={section.icon} title={section.label} count={shelves.length}
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
 * One shelf's rail, drawn at once from what the index carried for it, else asked for once it nears the
 * screen, so a page of many shelves asks only for what is looked at. A shelf with nothing on it, or that
 * could not be answered, is left out. `picks` are the filter's, which the shelf is asked with.
 */
export function LazyShelf({ head, kept, where, skip, least = 1, picks }) {
  const ref = React.useRef(null);
  const [near, setNear] = React.useState(!!kept);
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
      {near ? <ShelfRail head={head} kept={kept} where={where} skip={skip} least={least} picks={picks} /> : <RailSkeleton />}
    </div>
  );
}

/**
 * A shelf's rail: its first titles as cards, with how many it holds and See all. `skip` leaves one title
 * off it (the page the rail is on), and a shelf with fewer than `least` titles is left out. A title
 * carrying a `role` (a person's shelf) is shown with that part in place of its cast.
 */
function ShelfRail({ head, kept, where, skip, least = 1, picks }) {
  const [hideWatched] = useHideWatched();
  const asked = picks ? apiPicks(picks) : {};
  const filtered = !!picks && pickCount(picks) > 0;
  const shelf = useLoad(() => api.discoverShelf(head.id, 1, null, asked), [head.id, JSON.stringify(asked)], { known: filtered ? undefined : kept });
  if (!shelf.data) return shelf.error ? null : <RailSkeleton />;
  const all = skip ? shelf.data.titles.filter((t) => t.imdbId !== skip) : shelf.data.titles;
  const titles = picks && hideWatched ? all.filter((t) => !t.watched) : all;
  if (titles.length === 0 || titles.length < least) return null;
  return (
    <Rail
      perView={POSTERS} icon={head.icon} title={head.title} count={atLeast(titles.length, shelf.data.next)}
      items={titles.slice(0, RAIL_TITLES)} itemKey={(t) => t.kind + t.imdbId}
      renderItem={(t) => <FoundCard found={t} {...where(t)} line={t.role || t.starring} />}
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

/** One browse category: a series' tiles in a grid, any other's shelves as rails, each asked with the filter's `picks`. */
export function CategoryPage({ id, index, where, picks }) {
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
        : section.shelves.map((h) => <LazyShelf key={h.id} head={h} where={where} picks={picks} />)}
      <p className="kc-attribution-note">Titles and artwork from TMDB.</p>
    </section>
  );
}

const SHELF_SORTS = [
  { value: "order", label: "Shelf order", defaultDir: "asc" },
  { value: "rating", label: "Rating", defaultDir: "desc" },
  { value: "year", label: "Release date", defaultDir: "desc" },
];

/**
 * The order the shelf route serves a shelf in, from the toolbar's key and direction: absent for the
 * shelf's own. The API orders the whole shelf, so a page read later continues the grid and moves
 * nothing already in it.
 */
const shelfSort = ({ key, dir }) => (key === "year" ? (dir === "asc" ? "oldest" : "newest") : key === "rating" ? "rating" : null);

/** The toolbar's key and direction for the order a shelf's address names; the shelf's own for any other. */
const shelfOrder = (param) => (param === "rating" ? { key: "rating", dir: "desc" }
  : param === "newest" ? { key: "year", dir: "desc" }
    : param === "oldest" ? { key: "year", dir: "asc" }
      : { key: "order", dir: "asc" });

/**
 * The next page of a grid, read as its end comes near: a plain skeleton of cards while it is read, and
 * the failure with a retry when it cannot be. `paged` is what `usePaged` returned for the grid.
 */
export function MoreCards({ paged }) {
  const { next, loadingMore, moreError, loadMore } = paged;
  const ref = React.useRef(null);
  const latest = React.useRef(loadMore);
  latest.current = loadMore;
  const idle = !!next && !loadingMore && !moreError;
  React.useEffect(() => {
    const el = ref.current;
    if (!el || !idle) return undefined;
    if (typeof IntersectionObserver !== "function") {
      latest.current();
      return undefined;
    }
    const watcher = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting)) latest.current();
    }, { rootMargin: "600px 0px" });
    watcher.observe(el);
    return () => watcher.disconnect();
  }, [idle, next]);

  if (!next) return null;
  return (
    <div ref={ref} style={{ minHeight: 1 }}>
      {loadingMore && <CardsSkeleton count={6} row />}
      {moreError && <Failed error={moreError} onRetry={loadMore} />}
    </div>
  );
}

/**
 * Every title on one shelf, a page at a time as the grid reaches its end, with krystal's Toolbar as on
 * the library's pages: an order, how many are shown, and Surprise me. It opens in the order its rail
 * shows; an order picked here is the address's (`sort`), so it holds through a reload and Back and is no
 * other shelf's. The filter drawer's `picks` are asked of the shelf, and what the person watched is left
 * off when they hide it.
 */
export function ShelfPage({ id, sort: param, where, picks }) {
  const sort = shelfOrder(param);
  const order = shelfSort(sort);
  const setSort = (key, dir) => {
    const next = new URLSearchParams(window.location.search);
    if (shelfSort({ key, dir })) next.set("sort", shelfSort({ key, dir })); else next.delete("sort");
    navigate("/discover?" + next.toString(), { replace: true });
  };
  const asked = apiPicks(picks);
  const shelf = usePaged((page) => api.discoverShelf(id, page, order, asked), [id, order, JSON.stringify(asked)]);
  const [hideWatched] = useHideWatched();
  const [busy, surprise] = useSurprise(where);
  const s = shelf.data;
  if (shelf.error && !s) return <Failed error={shelf.error} onRetry={shelf.reload} />;
  if (!s) return <BrowseSkeleton />;

  const all = shelf.titles;
  const shown = hideWatched ? all.filter((t) => !t.watched) : all;
  const filtered = pickCount(picks) > 0 || hideWatched;

  return (
    <section className="kc-shelf">
      <BrowseHead eyebrow={s.section} icon={s.icon} title={s.title} />
      <Toolbar>
        <ToolbarSort value={sort.key} dir={sort.dir} onChange={setSort} options={SHELF_SORTS} />
        <ToolbarSpacer />
        <ToolbarCount shown={shown.length} total={atLeast(all.length, shelf.next)} unit="titles" />
        <ToolbarButton icon="dices" spinning={busy} disabled={busy || !shown.some((t) => !t.watched)}
          onClick={() => surprise({ titles: shown })}>Surprise me</ToolbarButton>
      </Toolbar>
      {shown.length > 0
        ? <div className="kc-grid">{shown.map((t) => <FoundCard key={t.kind + t.imdbId} found={t} {...where(t)} />)}</div>
        : <Empty icon="compass" title={filtered ? "Nothing matches" : "Nothing on this shelf"} />}
      <MoreCards paged={shelf} />
      <p className="kc-attribution-note">Titles and artwork from TMDB.</p>
    </section>
  );
}

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

/**
 * Discover's filter: the catalogue's films and shows at the drawer's picks, in an order. An address
 * that names no order is in the order the person last picked here.
 */
export function FilterPage({ query, picks, where }) {
  const [hideWatched] = useHideWatched();
  const [busy, surprise] = useSurprise(where);
  const [picked, pick] = useSort(SORTED.filter, FILTER_SORTS);
  const named = SORT_PARAMS.includes(query.get("sort")) ? query.get("sort") : "";
  const sort = named || sortParam(picked);
  const order = sortOrder(sort);
  const question = filterQuestion(picks, sort);
  const key = JSON.stringify(question);
  const found = usePaged((page) => api.catalogueDiscover(question, page), [key]);
  const setSort = (s) => {
    const next = new URLSearchParams(query);
    next.set("sort", s);
    navigate("/discover?" + next.toString(), { replace: true });
  };

  const all = found.titles ? found.titles.filter((t) => t.imdbId).map((t) => ({ ...t, starring: t.starring.join(", ") })) : null;
  const titles = all && (hideWatched ? all.filter((t) => !t.watched) : all);

  return (
    <section className="kc-shelf">
      <BrowseHead eyebrow="Discover" icon="sliders-horizontal" title="Filter" />
      <Toolbar>
        <ToolbarSort value={order.key} dir={order.dir}
          onChange={(k, dir) => {
            pick(k, dir);
            setSort(sortParam({ key: k, dir }));
          }}
          options={FILTER_SORTS} />
        <ToolbarSpacer />
        {titles && <ToolbarCount shown={titles.length} total={atLeast(all.length, found.next)} unit="titles" />}
        <ToolbarButton icon="dices" spinning={busy} disabled={busy || !titles || !titles.some((t) => !t.watched)}
          onClick={() => surprise({ titles })}>Surprise me</ToolbarButton>
      </Toolbar>
      {found.error && <Failed error={found.error} onRetry={found.reload} />}
      {!titles && !found.error && <CardsSkeleton count={12} />}
      {titles && (titles.length > 0
        ? <div className="kc-grid">{titles.map((t) => <FoundCard key={t.kind + t.imdbId} found={t} {...where(t)} />)}</div>
        : <Empty icon="sliders-horizontal" title="Nothing matches" />)}
      <MoreCards paged={found} />
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

/** Which of the library's titles a card is: the film here by its IMDb id, or whether the show is held. */
export function useWhere() {
  const titles = useLoad(() => api.titles().catch(() => []), []);
  const shows = useLoad(() => api.shows().catch(() => []), []);
  const filmsHere = new Map((titles.data || []).filter((t) => t.kind !== "show" && t.film).map((t) => [t.film.imdbId, t]));
  const heldShows = new Set((shows.data || []).map((x) => x.imdbId));
  return (t) => ({ here: t.kind === "film" ? filmsHere.get(t.imdbId) : null, held: t.kind === "show" && heldShows.has(t.imdbId) });
}

/** The fewest titles a person's shelf on a title's page has; one with fewer is left out. */
const PERSON_LEAST = 4;

/**
 * The shelves under a title's page: what is like it, then more from its director and from its first two
 * stars, each a Discover shelf (`like-`, `directed-`, `starring-`) without the title itself. A person
 * with too little other work has no shelf.
 */
export function TitleShelves({ kind, imdbId, directors = [], stars = [] }) {
  const where = useWhere();
  return (
    <div className="kc-title-shelves">
      {titleShelfHeads(kind, imdbId, directors, stars).map((h) => (
        <LazyShelf key={h.id} head={h} where={where} skip={imdbId} least={h.least} />
      ))}
    </div>
  );
}

/**
 * The shelves a title's page has, each `{id, title, icon, least}`: what is like it, then its first
 * director's films and its first two stars' work, for a person the film database has an id for.
 */
export function titleShelfHeads(kind, imdbId, directors = [], stars = []) {
  return [
    { id: `like-${kind}-${imdbId}`, title: "More like this", icon: "sparkles", least: 1 },
    ...directors.filter((p) => p.personId).slice(0, 1)
      .map((p) => ({ id: `directed-${p.personId}`, title: `More from ${p.name}`, icon: "megaphone", least: PERSON_LEAST })),
    ...stars.filter((p) => p.personId).slice(0, 2)
      .map((p) => ({ id: `starring-${p.personId}`, title: `More from ${p.name}`, icon: "user", least: PERSON_LEAST })),
  ];
}
