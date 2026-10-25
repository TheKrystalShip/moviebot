import React from "react";
import { PosterCard } from "@thekrystalship/krystal";
import { episodeCode, meApi, posterArt, runtimeText } from "../api/index.js";
import { discoverFilmPath, moviePath, showPath } from "../paths.js";
import { navigate } from "../router.js";
import { SORTED } from "../sorts.js";
import { shownOf } from "../orders.js";
import { ago } from "../time.js";
import { refreshWants, useWantList } from "../wants.js";
import { Empty, Failed, PageHead, watchHref } from "../components/Film.jsx";
import { FilterField, useListedFilter } from "../components/FilterField.jsx";
import { CardsSkeleton } from "../components/TitleCards.jsx";
import { useWantRibbon } from "../components/WantButton.jsx";

/** How often a title being fetched is asked after, so its progress moves on the page. */
const FETCHING_EVERY_MS = 10000;

/** Where a want leads: the library's own page for a film it holds, otherwise the catalogue's. */
export function wantHref(w) {
  const category = w.card && w.card.category;
  if (w.kind === "show") return showPath(w.imdbId, { held: w.inLibrary, category });
  return w.libraryId ? moviePath(w.libraryId, category) : discoverFilmPath(w.imdbId);
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
        art={posterArt(w.card)}
        onPick={() => navigate(wantHref(w))}
        badge={w.visible ? null : { tone: "info", label: "Just me" }}
        progress={w.state === "fetching" ? w.progress || 0 : undefined}
        facts={[
          { value: (w.card && w.card.year) || null, label: "Year" },
          w.kind === "show"
            ? { value: next ? "Next " + episodeCode({ season: next.season, number: next.episode }) : null, label: "Next episode" }
            : { value: (w.card && runtimeText(w.card.runtime)) || null, label: "Length" },
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

/** The keys the wants keep their order under (`useSort`), the first the one they open in. */
const WANT_KEYS = [
  { value: "added", defaultDir: "desc" },
  { value: "name", defaultDir: "asc" },
  { value: "rating", defaultDir: "desc" },
  { value: "year", defaultDir: "desc" },
  { value: "others", defaultDir: "desc" },
];

/** The words of the states a want can stand in, by the API's names for them. */
const WANT_STATE_WORDS = { ready: "Ready", fetching: "Fetching", available: "Available", waiting: "Waiting" };

/**
 * The wants as their page shows them: searched, narrowed and ordered by the API (`/api/me/wants/page`),
 * asked again whenever the person's wants change, and each shown as the wants held now say it is, so a
 * want removed or changed shows at once.
 */
export function useWantsPage(list) {
  const changed = (list || []).map((w) => w.imdbId + ":" + w.state).join(",");
  const filter = useListedFilter(
    (params) => meApi.wantsPage(params).then((page) => ({ titles: page.wants, total: page.total, facets: page.facets, sorts: page.sorts })),
    { surface: SORTED.wants, keys: WANT_KEYS, fixed: [], stateWords: WANT_STATE_WORDS, deps: [changed] });
  const held = new Map((list || []).map((w) => [w.imdbId, w]));
  const titles = filter.titles && filter.titles.filter((w) => held.has(w.imdbId)).map((w) => held.get(w.imdbId));
  return { ...filter, titles };
}

/**
 * What the person wants to watch: every want searched, narrowed by the filter drawer and ordered the
 * way they last picked (`useWantsPage`). A title being fetched is asked after while the page is open.
 */
export function Wants() {
  const { list, error, reload } = useWantList();
  const filter = useWantsPage(list);

  const fetching = !!list && list.some((w) => w.state === "fetching");
  React.useEffect(() => {
    if (!fetching) return undefined;
    const id = setInterval(refreshWants, FETCHING_EVERY_MS);
    return () => clearInterval(id);
  }, [fetching]);

  const all = list || [];
  const shown = filter.titles;

  return (
    <div className="kc-page">
      <PageHead title="Want to watch" sub={list && shown ? shownOf(shown.length, all.length, ["title", "titles"]) : null} />

      {all.length > 0 && <FilterField placeholder="Search what you want to watch" {...filter.field} />}

      <Failed error={list ? filter.error && !shown ? filter.error : null : error} onRetry={list ? filter.reload : reload} />
      {(!list || (all.length > 0 && !shown)) && !error && <CardsSkeleton count={8} />}
      {list && all.length === 0 && <Empty icon="bookmark" title="Nothing yet" />}
      {all.length > 0 && shown && shown.length === 0 && <Empty title="Nothing here" />}
      {shown && shown.length > 0 && (
        <div className="kc-grid">
          {shown.map((w) => <WantCard key={w.imdbId} want={w} />)}
        </div>
      )}
    </div>
  );
}
