import React from "react";
import {
  RadioGroup, ShowcaseCard, Toolbar, ToolbarCount, ToolbarSearch, ToolbarSort, ToolbarSpacer, useFilters,
} from "@thekrystalship/krystal";
import { episodeCode } from "../api.js";
import { discoverFilmPath, moviePath, showPath } from "../paths.js";
import { navigate } from "../router.js";
import { SORTED, useSort } from "../sorts.js";
import { ago, clock } from "../time.js";
import { refreshWants, useWantList } from "../wants.js";
import { Empty, Failed, PageHead, kindLine, watchHref } from "../components/Film.jsx";
import { CardsSkeleton } from "../components/TitleCards.jsx";
import { useWantRibbon } from "../components/WantButton.jsx";

/** How often a title being fetched is asked after, so its progress moves on the page. */
const FETCHING_EVERY_MS = 10000;

/** Where a want leads: the library's own page for a film it holds, otherwise the catalogue's. */
export function wantHref(w) {
  if (w.kind === "show") return showPath(w.imdbId, { held: w.inLibrary, category: w.category });
  return w.libraryId ? moviePath(w.libraryId, w.category) : discoverFilmPath(w.imdbId);
}

/** What a want's state looks like on a card's foot. */
export function stateLine(w) {
  switch (w.state) {
    case "ready": return { tone: "success", icon: "circle-check", label: "Ready" };
    case "fetching": return { tone: "notice", icon: "loader", label: `Fetching · ${Math.floor((w.progress || 0) * 100)}%` };
    case "waiting": return { tone: "notice", icon: "calendar-clock", label: "Waiting for a release" };
    case "available": return { tone: "active", icon: "download", label: "Available" };
    default: return { icon: "bookmark", label: `Wanted ${ago(w.since)}`, title: new Date(w.since).toLocaleString() };
  }
}

/**
 * A wanted title as a card: its poster named a film or a series, its year, a film's length or a show's
 * next episode, how many others want it, where it stands (ready, fetching with its progress, waiting for
 * a release, available), and Just me when nobody else is shown it. Its ribbon changes or removes the
 * want, and Watch opens a film the library holds; `action` puts another in its place.
 */
export function WantCard({ want: w, action }) {
  const [ribbon, menu] = useWantRibbon(w.imdbId, w.kind, w.title);
  const next = w.nextEpisode;
  return (
    <>
      <ShowcaseCard compactFacts
        name={w.title}
        eyebrow={kindLine(w.kind, w)}
        art={w.posterUrl ? `url("${w.posterUrl}")` : null}
        onPick={() => navigate(wantHref(w))}
        badge={w.visible ? null : { tone: "info", label: "Just me" }}
        progress={w.state === "fetching" ? w.progress || 0 : undefined}
        specs={[
          { icon: "calendar", value: w.year || null, label: "Year" },
          w.kind === "show"
            ? { icon: "calendar-clock", value: next ? episodeCode({ season: next.season, number: next.episode }) : null, label: "Next" }
            : { icon: "clock", value: w.runtimeMinutes ? clock(w.runtimeMinutes * 60) : null, label: "Length" },
          { icon: "users", value: w.others > 0 ? w.others : null, label: "Others" },
        ]}
        status={stateLine(w)}
        action={action || (w.libraryId && w.kind !== "show"
          ? { label: "Watch", onClick: () => navigate(watchHref(w.libraryId)) }
          : { label: "View" })}
        ribbon={ribbon} />
      {menu}
    </>
  );
}

// Each comparator orders ascending; the toolbar's direction reverses it.
export const WANT_SORTS = [
  { value: "since", label: "Date added", defaultDir: "desc", compare: (a, b) => a.since.localeCompare(b.since) },
  { value: "name", label: "Name", defaultDir: "asc", compare: (a, b) => a.title.localeCompare(b.title) },
  { value: "year", label: "Year", defaultDir: "desc", compare: (a, b) => (a.year || 0) - (b.year || 0) },
  { value: "rating", label: "Rating", defaultDir: "desc", compare: (a, b) => (a.rating || 0) - (b.rating || 0) },
  { value: "others", label: "Others", defaultDir: "desc", compare: (a, b) => a.others - b.others },
];

const STATES = [
  { value: "all", label: "All", test: () => true },
  { value: "ready", label: "Ready", test: (w) => w.state === "ready" },
  { value: "fetching", label: "Fetching", test: (w) => w.state === "fetching" },
  { value: "waiting", label: "Waiting", test: (w) => w.state === "waiting" },
];

/**
 * What the person wants to watch: every want searched, narrowed by where it stands and ordered the way
 * they last picked. A title being fetched is asked after while the page is open.
 */
export function Wants() {
  const { list, error, reload } = useWantList();
  const f = useFilters({ search: "", state: "all" });
  const [sort, setSort] = useSort(SORTED.wants, WANT_SORTS);

  const fetching = !!list && list.some((w) => w.state === "fetching");
  React.useEffect(() => {
    if (!fetching) return undefined;
    const id = setInterval(refreshWants, FETCHING_EVERY_MS);
    return () => clearInterval(id);
  }, [fetching]);

  const all = list || [];
  const state = STATES.find((s) => s.value === f.values.state) || STATES[0];
  const query = f.debouncedQuery.trim().toLowerCase();
  const order = WANT_SORTS.find((s) => s.value === sort.key) || WANT_SORTS[0];
  const shown = all
    .filter((w) => (!query || w.title.toLowerCase().includes(query)) && state.test(w))
    .sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));

  return (
    <div className="kc-page">
      <PageHead title="Want to watch" />

      {all.length > 0 && (
        <>
          <RadioGroup variant="segmented" fill label="State" className="kc-wants-switch" value={f.values.state}
            options={STATES.map((s) => ({ value: s.value, label: s.label }))} onChange={(v) => f.set("state", v)} />
          <Toolbar>
            <ToolbarSearch value={f.query} onChange={f.setQuery} pending={f.searchPending} placeholder="Search…" />
            <ToolbarSort value={sort.key} dir={sort.dir} onChange={setSort} options={WANT_SORTS} />
            <ToolbarSpacer />
            <ToolbarCount shown={shown.length} total={all.length} unit="titles" />
          </Toolbar>
        </>
      )}

      <Failed error={list ? null : error} onRetry={reload} />
      {!list && !error && <CardsSkeleton count={8} />}
      {list && all.length === 0 && <Empty icon="bookmark" title="Nothing yet" />}
      {all.length > 0 && shown.length === 0 && <Empty title="Nothing here" />}
      {shown.length > 0 && (
        <div className="kc-grid">
          {shown.map((w) => <WantCard key={w.imdbId} want={w} />)}
        </div>
      )}
    </div>
  );
}
