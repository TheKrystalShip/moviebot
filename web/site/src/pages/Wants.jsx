import React from "react";
import { PosterCard } from "@thekrystalship/krystal";
import { episodeCode } from "../api.js";
import { discoverFilmPath, moviePath, showPath } from "../paths.js";
import { navigate } from "../router.js";
import { SORTED, useSort } from "../sorts.js";
import { shownOf, withOrder } from "../orders.js";
import { ago, clock } from "../time.js";
import { refreshWants, useWantList } from "../wants.js";
import { Empty, Failed, PageHead, watchHref } from "../components/Film.jsx";
import { FilterField, useTitleFilter } from "../components/FilterField.jsx";
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
 * A wanted title as a card: its poster, its year, a film's length or a show's
 * next episode, how many others want it, where it stands (ready, fetching with its progress, waiting for
 * a release, available), and Just me when nobody else is shown it. Its ribbon changes or removes the
 * want, and Watch opens a film the library holds; `action` puts another in its place.
 */
export function WantCard({ want: w, action }) {
  const [ribbon, menu] = useWantRibbon(w.imdbId, w.kind, w.title);
  const next = w.nextEpisode;
  return (
    <>
      <PosterCard
        name={w.title}
        art={w.posterUrl ? `url("${w.posterUrl}")` : null}
        onPick={() => navigate(wantHref(w))}
        badge={w.visible ? null : { tone: "info", label: "Just me" }}
        progress={w.state === "fetching" ? w.progress || 0 : undefined}
        facts={[
          { value: w.year || null, label: "Year" },
          w.kind === "show"
            ? { value: next ? "Next " + episodeCode({ season: next.season, number: next.episode }) : null, label: "Next episode" }
            : { value: w.runtimeMinutes ? clock(w.runtimeMinutes * 60) : null, label: "Length" },
          { icon: "users", value: w.others > 0 ? w.others : null, label: "Others who want it" },
        ]}
        status={stateLine(w)}
        action={action || (w.libraryId && w.kind !== "show"
          ? { label: "Watch", onClick: () => navigate(watchHref(w.libraryId)) }
          : undefined)}
        ribbon={ribbon} />
      {menu}
    </>
  );
}

// Each comparator orders ascending; an order's direction reverses it.
export const WANT_SORTS = [
  { value: "since", defaultDir: "desc", compare: (a, b) => a.since.localeCompare(b.since) },
  { value: "name", defaultDir: "asc", compare: (a, b) => a.title.localeCompare(b.title) },
  { value: "year", defaultDir: "desc", compare: (a, b) => (a.year || 0) - (b.year || 0) },
  { value: "rating", defaultDir: "desc", compare: (a, b) => (a.rating || 0) - (b.rating || 0) },
  { value: "others", defaultDir: "desc", compare: (a, b) => a.others - b.others },
];

/** The orders the drawer offers the wants; "wanted" counts the other people who want a title too. */
export const WANT_ORDERS = [
  { key: "since", dir: "desc", label: "Recently added" },
  { key: "since", dir: "asc", label: "First added" },
  { key: "name", dir: "asc", label: "Name A–Z" },
  { key: "name", dir: "desc", label: "Name Z–A" },
  { key: "rating", dir: "desc", label: "Highest rated" },
  { key: "rating", dir: "asc", label: "Lowest rated" },
  { key: "year", dir: "desc", label: "Newest first" },
  { key: "year", dir: "asc", label: "Oldest first" },
  { key: "others", dir: "desc", label: "Most wanted" },
  { key: "others", dir: "asc", label: "Least wanted" },
];

/** A want's order as `useSort` keeps it, applied to the wants a filter shows. */
export const ordered = (wants, sort) => {
  const order = WANT_SORTS.find((s) => s.value === sort.key) || WANT_SORTS[0];
  return [...wants].sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));
};

/** Where a want can stand, by the words the State section and its address use. */
export const WANT_STATES = [
  { value: "ready", label: "Ready", test: (w) => w.state === "ready" },
  { value: "fetching", label: "Fetching", test: (w) => w.state === "fetching" },
  { value: "waiting", label: "Waiting", test: (w) => w.state === "waiting" },
];

/**
 * The filter of the wants: the drawer's sections over them (kind, genre, decade, length, rating and
 * language), and where a want stands when the wants stand in more than one place.
 */
export function useWantFilter(all) {
  const held = WANT_STATES.filter((s) => all.some(s.test));
  const own = held.length >= 2 ? [{ id: "state", label: "State", kind: "optional", options: held.map(({ value, label }) => ({ value, label })) }] : [];
  const passes = (w, v) => !v.state || !!WANT_STATES.find((s) => s.value === v.state && s.test(w));
  return useTitleFilter(all, { fixed: [], own, passes, named: (w) => w.title });
}

/**
 * What the person wants to watch: every want searched, narrowed by the filter drawer and ordered the
 * way they last picked. A title being fetched is asked after while the page is open.
 */
export function Wants() {
  const { list, error, reload } = useWantList();
  const [sort, setSort] = useSort(SORTED.wants, WANT_SORTS);

  const fetching = !!list && list.some((w) => w.state === "fetching");
  React.useEffect(() => {
    if (!fetching) return undefined;
    const id = setInterval(refreshWants, FETCHING_EVERY_MS);
    return () => clearInterval(id);
  }, [fetching]);

  const all = list || [];
  const filter = useWantFilter(all);
  const shown = ordered(filter.shown, sort);

  return (
    <div className="kc-page">
      <PageHead title="Want to watch" sub={list ? shownOf(shown.length, all.length, ["title", "titles"]) : null} />

      {all.length > 0 && (
        <FilterField placeholder="Search what you want to watch" {...filter.field} {...withOrder(filter.field, WANT_ORDERS, sort, setSort)} />
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
