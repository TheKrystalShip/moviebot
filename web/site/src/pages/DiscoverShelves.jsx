import React from "react";
import {
  BriefCard, CollectionTile, Icon, Shelf, Skeleton, useStoredFlag,
} from "@thekrystalship/krystal";
import { catalogue, libraryApi, narrowed, pagesOf, queryParams } from "../api/index.js";
import { POSTERS, TILES } from "../railDensity.js";
import { useLoad, usePaged } from "../data.js";
import { discoverBrowsePath, discoverFranchisePath, discoverListPath } from "../paths.js";
import { apiPicks, pickCount } from "../filters.js";
import { navigate, linkProps } from "../router.js";
import { SORTED, useSort } from "../sorts.js";
import { orderOf, sortOf } from "../orders.js";
import { Empty, Failed } from "../components/Film.jsx";
import { CardsSkeleton, FoundCard } from "../components/TitleCards.jsx";
import { ViewAll } from "../components/ViewAll.jsx";
import { counted } from "../counts.js";

// Discover with nothing typed: its rows of shelves, the moods and the franchises as tiles, and the
// browse categories; a category's shelves; and every list a shelf, a genre or the filter names. Every
// shelf is a list query over the catalogue (`/api/catalogue/discover` names them), and what is shown is
// in the address, the filter drawer's picks with it (`filters.js`).

/** How many of a shelf's titles its rail shows; its own page shows the rest. */
const RAIL_TITLES = 20;

/** What the list query is asked for to leave what the person watched off an answer. */
export const UNWATCHED = { unwatched: "true" };

/** The orders a catalogue list is offered in; a list with its own order (a ranking, a franchise, what is like a title) offers it first. */
export const LIST_ORDERS = ["popular", "rating", "lowest", "newest", "oldest"];
export const OWN_ORDERS = ["order", ...LIST_ORDERS];

/** Whether a list query has an order of its own. */
export const hasOwnOrder = (query) => ["ranking", "franchise", "similarTo", "here"].some((k) => query[k] != null);

/**
 * Whether the person leaves what they watched off a list, kept on the device; the API leaves them off
 * when asked with `UNWATCHED`.
 */
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

/** Where a shelf's head leads: a franchise to its own page, any other shelf to the list its query names. */
export function shelfPath(head) {
  const franchise = queryParams(head.query).franchise;
  return franchise ? discoverFranchisePath(franchise[0]) : discoverListPath(head.query, head.title);
}

/** A shelf's tile: a mood's posters fanned, a series' films in order with how far the person got. */
function ShelfTile({ head, series, size = "md" }) {
  const link = linkProps(shelfPath(head));
  if (series) {
    return (
      <CollectionTile variant="strip" size={size} name={head.title} posters={head.posters || []}
        total={head.total} watched={head.watched || 0} next={head.next ? "Next: " + head.next.title : null}
        caption={`${head.total} ${head.total === 1 ? "title" : "titles"}`} {...link} />
    );
  }
  return (
    <CollectionTile variant="fan" size={size} name={head.title} icon={head.icon} posters={head.posters || []}
      caption={head.total ? `${head.total} titles` : null} {...link} />
  );
}

/** The page's rows, in the order `/api/catalogue/discover` gives them. */
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
      <p className="kc-attribution-note">This product uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.</p>
    </>
  );
}

/** A category's shelves as a row of tiles: the moods, the franchises. */
function TileRail({ section }) {
  if (section.shelves.length === 0) return null;
  return (
    <Shelf perView={TILES} title={section.label} sub={counted(section.count, "shelf", "shelves")}
      action={<ViewAll to={discoverBrowsePath(section.id)}>See all</ViewAll>}>
      {section.shelves.map((h) => <ShelfTile key={h.id} head={h} series={section.series} />)}
    </Shelf>
  );
}

/** A category's posters for its tile: the first poster of each of its first shelves. */
const sectionPosters = (section) => section.shelves.slice(0, 3).map((h) => (h.posters || [])[0]).filter(Boolean);

/** Every browse category as a tile, each leading to its shelves. */
function BrowseGrid({ sections }) {
  const narrow = useNarrow();
  return (
    <BriefCard icon="compass" title="Browse">
      <div className="kc-browse-grid">
        {sections.map((s) => (
          <CollectionTile key={s.id} variant="fan" size={narrow ? "sm" : "md"} name={s.label} icon={s.icon}
            caption={narrow ? null : counted(s.count, "shelf", "shelves")} posters={sectionPosters(s)} {...linkProps(discoverBrowsePath(s.id))} />
        ))}
      </div>
    </BriefCard>
  );
}

/**
 * One shelf's rail, asked for once it nears the screen, so a page of many shelves asks only for what is
 * looked at. A shelf with nothing on it, or that could not be answered, is left out. `picks` are the
 * filter's, which the shelf is asked with.
 */
export function LazyShelf({ head, where, skip, least = 1, picks }) {
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
      {near ? <ShelfRail head={head} where={where} skip={skip} least={least} picks={picks} /> : <RailSkeleton />}
    </div>
  );
}

/**
 * A shelf's rail: its first titles as cards, with how many it holds and See all. `skip` leaves one title
 * off it (the page the rail is on), and a shelf with fewer than `least` titles is left out.
 */
function ShelfRail({ head, where, skip, least = 1, picks }) {
  const [hideWatched] = useHideWatched();
  const query = { ...queryParams(head.query), ...(picks ? apiPicks(picks) : {}), ...(picks && hideWatched ? UNWATCHED : {}), size: RAIL_TITLES };
  const shelf = useLoad(() => catalogue.titles(query), [JSON.stringify(query)]);
  if (!shelf.data) return shelf.error ? null : <RailSkeleton />;
  const titles = skip ? shelf.data.titles.filter((t) => t.id !== skip) : shelf.data.titles;
  if (titles.length === 0 || titles.length < least) return null;
  const total = shelf.data.total - (shelf.data.titles.length - titles.length);
  return (
    <Shelf perView={POSTERS} title={head.title} sub={counted(total, "title")}
      action={<ViewAll to={shelfPath(head)}>See all</ViewAll>}>
      {titles.map((t) => <FoundCard key={t.kind + t.id} found={t} {...where(t)} />)}
    </Shelf>
  );
}

/** A heading over a category or a list: what it is in, its name, and how many. */
export function BrowseHead({ eyebrow, icon, title, count, face }) {
  return (
    <div className="kc-browse-head">
      {face}
      <div className="kc-browse-head__text">
        {eyebrow && <span className="kc-eyebrow">{eyebrow}</span>}
        <h2>{icon && <span className="kc-browse-head__mark"><Icon name={icon} size={18} /></span>}{title}</h2>
      </div>
      {count != null && <span className="kc-count">{count}</span>}
    </div>
  );
}

/** One browse category: the franchises' tiles in a grid a page at a time, any other's shelves as rails, each asked with the filter's `picks`. */
export function CategoryPage({ id, index, where, picks }) {
  if (index.error && !index.data) return <Failed error={index.error} onRetry={index.reload} />;
  if (!index.data) return <BrowseSkeleton />;
  const section = index.data.sections.find((s) => s.id === id);
  if (!section) return <Empty icon="compass" title="Nothing by that name" />;
  return (
    <section className="kc-shelf">
      <BrowseHead eyebrow="Discover" icon={section.icon} title={section.label} count={section.count} />
      {section.series
        ? <SeriesGrid />
        : section.shelves.map((h) => <LazyShelf key={h.id} head={h} where={where} picks={picks} />)}
    </section>
  );
}

/** Every franchise as a tile in a grid, in the person's order, a page at a time as its end nears. */
function SeriesGrid() {
  const narrow = useNarrow();
  const series = usePaged((page) => catalogue.franchises(page).then((p) => ({ titles: p.franchises, next: p.next })), []);
  if (series.error && !series.data) return <Failed error={series.error} onRetry={series.reload} />;
  if (!series.titles) return <CardsSkeleton count={8} />;
  return (
    <>
      <div className="kc-browse-grid">
        {series.titles.map((h) => <ShelfTile key={h.id} head={h} series size={narrow ? "sm" : "md"} />)}
      </div>
      <MoreCards paged={series} />
    </>
  );
}

/** The filter's sort keys as `useSort` keeps them, each in the direction it opens in. */
const FILTER_SORTS = [
  { value: "popular", defaultDir: "desc" },
  { value: "rating", defaultDir: "desc" },
  { value: "year", defaultDir: "desc" },
];

/**
 * The order of a list Discover shows and the orders the drawer offers for it (`sorts`, the list's own
 * first). A list's order is the address's (`sort`), the list's own when it names none, so it holds
 * through a reload and Back and is no other list's; the filter's is the address's, else the one the
 * person last picked there, and a pick is kept as theirs. `{sort, orders, order, pick(key, dir)}`, `sort`
 * the order to ask for (null for a list's own), or null on a page with no order.
 */
export function useDiscoverOrder(query, sorts, { listed, filtering }) {
  const [picked, keep] = useSort(SORTED.filter, FILTER_SORTS);
  if ((!listed && !filtering) || sorts.length === 0) return null;
  const own = listed ? sorts[0] : null;
  const named = query.get("sort");
  const sort = named && sorts.includes(named) ? named : (listed ? own : sortOf(picked, sorts) || "popular");
  const orders = sorts.map(orderOf);
  const pick = (key, dir) => {
    const next = orders.find((o) => o.key === key && o.dir === dir);
    if (!next) return;
    if (filtering) keep(key, dir);
    const address = new URLSearchParams(query);
    if (next.sort === own) address.delete("sort"); else address.set("sort", next.sort);
    navigate("/discover?" + address.toString(), { replace: true });
  };
  return { sort: sort === own ? null : sort, orders, order: orderOf(sort), pick };
}

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
 * Every title one list query names, a page at a time as the grid reaches its end, under a heading:
 * `query` is the list's own (a shelf's, a genre's, a person's, a franchise's), and the drawer's `picks`,
 * the order `sort` and whether the person hides what they watched narrow it. `head` is the heading,
 * `empty` what an empty list says.
 */
export function ListPage({ query, sort, picks, where, head, empty = "Nothing here", icon = "compass" }) {
  const [hideWatched] = useHideWatched();
  const asked = { ...narrowed(query, apiPicks(picks)), ...(sort ? { sort } : {}), ...(hideWatched ? UNWATCHED : {}) };
  const key = JSON.stringify(asked);
  const list = usePaged(pagesOf(asked), [key]);
  const filtered = pickCount(picks) > 0 || hideWatched;
  return (
    <section className="kc-shelf">
      {head(list.data ? list.data.total : null)}
      {list.error && !list.data && <Failed error={list.error} onRetry={list.reload} />}
      {!list.titles && !list.error && <CardsSkeleton count={12} />}
      {list.titles && (list.titles.length > 0
        ? <div className="kc-grid">{list.titles.map((t) => <FoundCard key={t.kind + t.id} found={t} {...where(t)} />)}</div>
        : <Empty icon={icon} title={filtered ? "Nothing matches" : empty} />)}
      <MoreCards paged={list} />
    </section>
  );
}

/** A shelf while its titles are asked for, at the shelf's own height so the page does not jump. */
function RailSkeleton() {
  return (
    <div className="kc-rail-skeleton" aria-hidden="true">
      <div className="kc-rail-skeleton__head">
        <Skeleton w={200} h={22} />
        <Skeleton w={80} h={15} />
      </div>
      <CardsSkeleton count={8} row />
    </div>
  );
}

/** The page's frame while `/api/catalogue/discover` is asked. */
export function FrontSkeleton() {
  return (
    <>
      <Skeleton w="100%" h={380} r={6} />
      <RailSkeleton />
      <RailSkeleton />
    </>
  );
}

export function BrowseSkeleton({ portrait = false }) {
  return (
    <section className="kc-shelf">
      <div className="kc-browse-head">
        {portrait && <Skeleton w={72} h={72} r={999} />}
        <div className="kc-browse-head__text">
          <Skeleton w={60} h={12} />
          <Skeleton w={220} h={26} />
        </div>
      </div>
      <CardsSkeleton count={12} />
    </section>
  );
}

/**
 * Which of the library's titles a card is, for where it leads and whether it plays from the card: the
 * film here by its IMDb id (with its status), or whether an episode of the show is held.
 */
export function useWhere() {
  const titles = useLoad(() => libraryApi.titles().catch(() => []), []);
  const shows = useLoad(() => libraryApi.shows().catch(() => []), []);
  const filmsHere = new Map((titles.data || []).filter((t) => t.kind !== "show" && t.film).map((t) => [t.film.imdbId, t]));
  const heldShows = new Set((shows.data || []).map((x) => x.imdbId));
  return (t) => ({ here: t.kind === "film" ? filmsHere.get(t.id) : null, held: t.kind === "show" && heldShows.has(t.id) });
}

/** The fewest titles a person's shelf on a title's page has; one with fewer is left out. */
const PERSON_LEAST = 4;

/**
 * The shelves under a title's page (`page`, its catalogue page): what is like it, then more from its
 * first director and its first two stars, each a list query without the title itself. A person with too
 * little other work has no shelf.
 */
export function TitleShelves({ page }) {
  const where = useWhere();
  return (
    <div className="kc-title-shelves">
      {titleShelfHeads(page).map((h) => <LazyShelf key={h.id} head={h} where={where} skip={page.id} least={h.least} />)}
    </div>
  );
}

/** The shelves a title's page has, each a head: a name, a list query, and the fewest titles it is shown with. */
export function titleShelfHeads(page) {
  const director = (page.crew || []).find((c) => c.job === "Director" || c.job === "Creator");
  return [
    { id: `like-${page.id}`, title: "More like this", icon: "sparkles", query: `similarTo=${page.id}`, least: 1 },
    ...(director ? [{ id: `directed-${director.personId}`, title: `More from ${director.name}`, icon: "megaphone",
      query: `person=${director.personId}&role=${director.job === "Creator" ? "writing" : "directing"}`, least: PERSON_LEAST }] : []),
    ...(page.cast || []).slice(0, 2).map((p) => ({ id: `starring-${p.personId}`, title: `More from ${p.name}`, icon: "user",
      query: `person=${p.personId}&role=cast`, least: PERSON_LEAST })),
  ];
}
