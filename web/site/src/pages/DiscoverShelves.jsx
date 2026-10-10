import React from "react";
import { Button, Icon, Rail, Select, Skeleton, SubTabs, Toggle, useStoredFlag } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { FILTER_PARAMS, discoverFilterPath, discoverShelfPath, discoverTabPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { Empty, Failed } from "../components/Film.jsx";
import { CardsSkeleton, FoundCard, foundPage } from "../components/TitleCards.jsx";

// Discover with nothing typed: its shelves, by tab, each a rail of posters that opens every title on
// it; the moods as chips; Surprise me; and the filter. What is shown is in the address.

/** How many of a shelf's titles its rail shows; See all shows the rest. */
const RAIL_TITLES = 20;

/** The tabs Surprise me picks a shelf from: the ones the same for everybody. */
const SURPRISE_TABS = ["moods", "movies", "genres", "themes", "world", "makers"];

/** Whether titles the person watched are left off every shelf, kept on the device. */
export function useHideWatched() {
  return useStoredFlag("cinema:discover:hide-watched");
}

/** A shelf's titles as the person wants them: without what they watched, when they asked for that. */
const shown = (titles, hideWatched) => (hideWatched ? titles.filter((t) => !t.watched) : titles);

/**
 * The tabs and their shelves. `tab` is the tab the address names; the first is shown without one.
 * `where` says where the library holds a title, which decides where its card leads.
 */
export function DiscoverFront({ tab, where, onAsk }) {
  const index = useLoad(() => api.discover(), []);
  const [hideWatched, setHideWatched] = useHideWatched();
  const sections = index.data ? index.data.sections : [];
  const active = sections.find((s) => s.id === tab) || sections[0];
  const moods = sections.find((s) => s.id === "moods");
  const pool = sections.filter((s) => SURPRISE_TABS.includes(s.id)).flatMap((s) => s.shelves.map((h) => h.id));

  if (index.error && !index.data) return <Failed error={index.error} onRetry={index.reload} />;
  if (!index.data) return <FrontSkeleton />;

  return (
    <>
      {moods && (
        <nav className="kc-chips" aria-label={moods.label} data-hswipe="">
          {moods.shelves.map((s) => (
            <a key={s.id} className="kc-season-chip" {...linkProps(discoverShelfPath(s.id))}>
              <Icon name={s.icon} size={14} />{s.title}
            </a>
          ))}
        </nav>
      )}
      <div className="kc-discover-actions">
        <SurpriseButton shelves={pool} where={where} />
        <Button icon="sliders-horizontal" onClick={() => navigate(discoverFilterPath())}>Filter</Button>
        {onAsk && <Button icon="message-circle" onClick={onAsk}>Ask the assistant</Button>}
        <HideWatched on={hideWatched} onChange={setHideWatched} />
      </div>

      <SubTabs
        tabs={sections.map((s) => ({ id: s.id, icon: s.icon, label: s.label }))}
        active={active.id}
        onChange={(id) => navigate(discoverTabPath(id === sections[0].id ? null : id))} />

      {active.shelves.map((head) => (
        <LazyShelf key={head.id} head={head} where={where} hideWatched={hideWatched} />
      ))}
      <p className="kc-attribution-note">Titles and artwork from TMDB.</p>
    </>
  );
}

function HideWatched({ on, onChange }) {
  return (
    <label className="kc-hide-watched">
      <Toggle on={on} onChange={onChange} label="Hide watched" />
      <span>Hide watched</span>
    </label>
  );
}

/**
 * One shelf's rail, asked for once it nears the screen, so a tab of many shelves asks only for what
 * is looked at. A shelf with nothing on it, or that could not be answered, is left out.
 */
function LazyShelf({ head, where, hideWatched }) {
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
      {near ? <ShelfRail head={head} where={where} hideWatched={hideWatched} /> : <RailSkeleton />}
    </div>
  );
}

function ShelfRail({ head, where, hideWatched }) {
  const shelf = useLoad(() => api.discoverShelf(head.id), [head.id]);
  if (!shelf.data) return shelf.error ? null : <RailSkeleton />;
  const titles = shown(shelf.data.titles, hideWatched);
  if (titles.length === 0) return null;
  return (
    <Rail
      variant="posters" icon={head.icon} title={head.title} count={titles.length}
      items={titles.slice(0, RAIL_TITLES)} itemKey={(t) => t.kind + t.imdbId}
      renderItem={(t) => <FoundCard found={t} {...where(t)} />}
      onViewAll={() => navigate(discoverShelfPath(head.id))} viewAllLabel="See all" />
  );
}

/** Every title on one shelf, under its tab's name. */
export function ShelfPage({ id, where }) {
  const shelf = useLoad(() => api.discoverShelf(id), [id]);
  const [hideWatched, setHideWatched] = useHideWatched();
  const s = shelf.data;
  if (shelf.error && !s) return <Failed error={shelf.error} onRetry={shelf.reload} />;
  if (!s) return <BrowseSkeleton />;
  const titles = shown(s.titles, hideWatched);
  return (
    <section className="kc-shelf">
      <div className="kc-browse-head">
        <div className="kc-browse-head__text">
          <span className="kc-eyebrow">{s.section}</span>
          <h2><Icon name={s.icon} size={20} /> {s.title}</h2>
        </div>
        <span className="kc-count">{titles.length}</span>
      </div>
      <div className="kc-discover-actions">
        <SurpriseButton titles={titles} where={where} />
        <HideWatched on={hideWatched} onChange={setHideWatched} />
      </div>
      {titles.length > 0
        ? <div className="kc-grid">{titles.map((t) => <FoundCard key={t.kind + t.imdbId} found={t} {...where(t)} />)}</div>
        : <Empty icon="compass" title="Nothing on this shelf" />}
      <p className="kc-attribution-note">Titles and artwork from TMDB.</p>
    </section>
  );
}

/**
 * Opens one title picked at random: from `titles`, or from one of `shelves` picked at random first.
 * What the person watched is never picked.
 */
function SurpriseButton({ titles, shelves, where }) {
  const [busy, setBusy] = React.useState(false);
  const empty = titles ? !titles.some((t) => !t.watched) : !shelves || shelves.length === 0;

  const pick = async () => {
    setBusy(true);
    try {
      let from = titles;
      for (let tries = 0; !from && tries < 3; tries++) {
        const shelf = await api.discoverShelf(shelves[Math.floor(Math.random() * shelves.length)]).catch(() => null);
        if (shelf && shelf.titles.some((t) => !t.watched)) from = shelf.titles;
      }
      const choices = (from || []).filter((t) => !t.watched);
      if (choices.length === 0) return;
      const t = choices[Math.floor(Math.random() * choices.length)];
      const { here, held } = where(t);
      navigate(foundPage(t, here, held));
    } finally {
      setBusy(false);
    }
  };

  return <Button variant="accent" icon="dices" busy={busy} disabled={empty} onClick={pick}>Surprise me</Button>;
}

/** The decades the filter offers, newest first, by their first year. */
const DECADES = [2020, 2010, 2000, 1990, 1980, 1970, 1960, 1950, 1940];

/** A film's length as the filter words it, in minutes either side. */
const LENGTHS = {
  short: { label: "Under 90 min", max: 89 },
  standard: { label: "90 to 150 min", min: 90, max: 150 },
  long: { label: "Over 150 min", min: 151 },
};

const RATINGS = ["8", "7", "6"];

const SORTS = [
  { value: "popular", label: "Most popular" },
  { value: "rating", label: "Highest rated" },
  { value: "newest", label: "Newest" },
  { value: "oldest", label: "Oldest" },
];

/** The filter's choices as the address holds them; anything it does not offer is any. */
function readFilter(query) {
  const kind = query.get("filter") === "show" ? "show" : "film";
  const decade = Number(query.get("decade"));
  const length = query.get("length");
  const rating = query.get("rating");
  const sort = query.get("sort");
  return {
    kind,
    genre: (query.get(FILTER_PARAMS.genre) || "").trim(),
    decade: DECADES.includes(decade) ? String(decade) : "",
    length: kind === "film" && LENGTHS[length] ? length : "",
    rating: RATINGS.includes(rating) ? rating : "",
    lang: /^[a-z]{2}$/.test(query.get("lang") || "") ? query.get("lang") : "",
    sort: SORTS.some((s) => s.value === sort) ? sort : "",
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
  if (f.sort) q.sort = f.sort;
  return q;
}

/** Discover's filter: a kind, a genre, a decade, a length, a least rating, a language and an order. */
export function FilterPage({ query, where }) {
  const index = useLoad(() => api.discover(), []);
  const [hideWatched, setHideWatched] = useHideWatched();
  const f = readFilter(query);
  const question = filterQuestion(f);
  const key = JSON.stringify(question);
  const found = useLoad(() => api.catalogueDiscover(question), [key]);
  const set = (change) => navigate(discoverFilterPath({ ...f, ...change }), { replace: true });

  const genres = index.data ? (f.kind === "show" ? index.data.showGenres : index.data.filmGenres) : [];
  const languages = index.data ? index.data.languages : [];
  const titles = found.data
    ? shown(found.data.titles.filter((t) => t.imdbId).map((t) => ({ ...t, starring: t.starring.join(", ") })), hideWatched)
    : null;

  return (
    <section className="kc-shelf">
      <div className="kc-browse-head">
        <div className="kc-browse-head__text">
          <span className="kc-eyebrow">Discover</span>
          <h2><Icon name="sliders-horizontal" size={20} /> Filter</h2>
        </div>
        {titles && <span className="kc-count">{titles.length}</span>}
      </div>
      <div className="kc-filters">
        <Select variant="inline" size="md" aria-label="Kind" value={f.kind}
          onChange={(e) => set({ kind: e.target.value, genre: "", length: "" })}
          options={[{ value: "film", label: "Movies" }, { value: "show", label: "TV shows" }]} />
        <Select variant="inline" size="md" aria-label="Genre" value={f.genre} onChange={(e) => set({ genre: e.target.value })}
          options={[{ value: "", label: "Any genre" }, ...genres.map((g) => ({ value: g, label: g }))]} />
        <Select variant="inline" size="md" aria-label="Decade" value={f.decade} onChange={(e) => set({ decade: e.target.value })}
          options={[{ value: "", label: "Any decade" }, ...DECADES.map((d) => ({ value: String(d), label: d + "s" }))]} />
        {f.kind === "film" && (
          <Select variant="inline" size="md" aria-label="Length" value={f.length} onChange={(e) => set({ length: e.target.value })}
            options={[{ value: "", label: "Any length" }, ...Object.entries(LENGTHS).map(([value, l]) => ({ value, label: l.label }))]} />
        )}
        <Select variant="inline" size="md" aria-label="Rating" value={f.rating} onChange={(e) => set({ rating: e.target.value })}
          options={[{ value: "", label: "Any rating" }, ...RATINGS.map((r) => ({ value: r, label: r + "+" }))]} />
        <Select variant="inline" size="md" aria-label="Language" value={f.lang} onChange={(e) => set({ lang: e.target.value })}
          options={[{ value: "", label: "Any language" }, ...languages.map((l) => ({ value: l.code, label: l.name }))]} />
        <Select variant="inline" size="md" aria-label="Order" value={f.sort} onChange={(e) => set({ sort: e.target.value })}
          options={SORTS.map((s, i) => ({ value: i === 0 ? "" : s.value, label: s.label }))} />
      </div>
      <div className="kc-discover-actions">
        <SurpriseButton titles={titles || []} where={where} />
        <HideWatched on={hideWatched} onChange={setHideWatched} />
      </div>
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

function FrontSkeleton() {
  return (
    <>
      <div className="kc-chips" aria-hidden="true">
        {Array.from({ length: 6 }, (_, i) => <Skeleton key={i} w={120} h={34} r={999} />)}
      </div>
      <Skeleton w="100%" h={40} />
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
