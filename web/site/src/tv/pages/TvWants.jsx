import React from "react";
import { Button, EmptyState, TvCard, TvRow } from "@thekrystalship/krystal";
import { discoverPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { clock } from "../../time.js";
import { SORTED, useSort } from "../../sorts.js";
import { useWantList } from "../../wants.js";
import { Failed } from "../../components/Film.jsx";
import { WANT_SORTS, stateLine, wantHref } from "../../pages/Wants.jsx";

/** The rows, in the order a person cares about them: what can be watched, what is arriving, what waits. */
const ROWS = [
  { id: "ready", icon: "circle-check", title: "Ready", test: (w) => w.state === "ready" },
  { id: "fetching", icon: "loader", title: "Fetching", test: (w) => w.state === "fetching" },
  { id: "waiting", icon: "calendar-clock", title: "Waiting", test: (w) => w.state === "waiting" },
  { id: "rest", icon: "bookmark", title: "Wanted", test: (w) => !["ready", "fetching", "waiting"].includes(w.state) },
];

/** A wanted title's name, and under it its year, length, where it stands and how many others want it. */
function WantLine({ want: w }) {
  if (!w) return <div className="kc-tvsv__line" />;
  const facts = [
    w.year, w.runtimeMinutes ? clock(w.runtimeMinutes * 60) : null, stateLine(w).label,
    w.others > 0 ? `${w.others} ${w.others === 1 ? "other" : "others"}` : null,
  ].filter(Boolean);
  return (
    <div className="kc-tvsv__line">
      <span className="kc-tvsv__name">{w.title}</span>
      <span className="kc-tvsv__facts">{facts.join(" · ")}</span>
    </div>
  );
}

/** What the page holds when it holds none: Discover, focused, for the remote to land on. */
function Nothing() {
  return (
    <div className="kc-tvsv__empty">
      <EmptyState icon="bookmark" title="Nothing yet"
        actions={<Button variant="primary" size="lg" icon="compass" data-tv-initial="" {...linkProps(discoverPath())}>Discover</Button>} />
    </div>
  );
}

/**
 * What the person wants to watch, on a television: a row each for what is ready, arriving and waiting for
 * a release, then the rest, in the order last picked on a Wants page, with the focused title named under
 * its row. Wants are made and changed from a title's page.
 */
export function TvWants() {
  const { list, error, reload } = useWantList();
  const [sort] = useSort(SORTED.wants, WANT_SORTS);
  const [focused, setFocused] = React.useState(null);

  if (error && !list) return <div className="kc-tv-page kc-tv-pad"><Failed error={error} onRetry={reload} /></div>;
  if (!list) return <div className="kc-tv-page" />;
  if (list.length === 0) return <div className="kc-tv-page kc-tvsv kc-tvsv--none"><Nothing /></div>;

  const order = WANT_SORTS.find((s) => s.value === sort.key) || WANT_SORTS[0];
  const ordered = [...list].sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));

  return (
    <div className="kc-tv-page kc-tvsv">
      {ROWS.map((r) => {
        const rows = ordered.filter(r.test);
        if (rows.length === 0) return null;
        return (
          <section key={r.id}>
            <TvRow icon={r.icon} title={r.title} count={rows.length}>
              {rows.map((w) => (
                <TvCard key={w.imdbId} art={w.posterUrl} label={w.title}
                  progress={w.state === "fetching" ? w.progress || 0 : undefined}
                  {...linkProps(wantHref(w))} onFocus={() => setFocused(w)} />
              ))}
            </TvRow>
            <WantLine want={focused && rows.includes(focused) ? focused : null} />
          </section>
        );
      })}
    </div>
  );
}
