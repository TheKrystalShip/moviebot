import React from "react";
import { api, clock, filmName } from "../api.js";
import { useLoad, staysFor } from "../data.js";
import { Empty, Failed, PageHead, PosterCard } from "../components/Film.jsx";

const SORTS = [
  { id: "name", label: "Name", compare: (a, b) => filmName(a).localeCompare(filmName(b)) },
  { id: "year", label: "Year", compare: (a, b) => ((b.film && b.film.year) || 0) - ((a.film && a.film.year) || 0) },
  { id: "length", label: "Length", compare: (a, b) => a.durationSeconds - b.durationSeconds },
];

/** Every film on the shelf, narrowed by what state it is in and ordered the way somebody asks. */
export function Library() {
  const titles = useLoad(() => api.titles(), []);
  const keeps = useLoad(() => api.keeps().catch(() => []), []);
  const [filter, setFilter] = React.useState("all");
  const [sort, setSort] = React.useState("name");

  const keptBy = new Map((keeps.data || []).filter((k) => k.libraryId).map((k) => [k.libraryId, k]));
  const all = titles.data || [];
  const filters = [
    { id: "all", label: "All", test: () => true },
    { id: "ready", label: "Ready", test: (t) => t.status === "ready" },
    { id: "preparing", label: "Preparing", test: (t) => t.status === "transcoding" },
    { id: "kept", label: "Kept", test: (t) => !!(keptBy.get(t.id) && keptBy.get(t.id).keeper) },
    { id: "clock", label: "On the clock", test: (t) => !!(keptBy.get(t.id) && !keptBy.get(t.id).keeper) },
  ];
  const active = filters.find((f) => f.id === filter) || filters[0];
  const order = SORTS.find((s) => s.id === sort) || SORTS[0];
  const shown = all.filter(active.test).sort(order.compare);

  return (
    <div className="kc-page">
      <PageHead title="Library" sub={titles.data ? `${all.length} ${all.length === 1 ? "film" : "films"}` : null} />
      <div className="kc-toolbar">
        <div className="kc-chips">
          {filters.map((f) => (
            <button key={f.id} type="button" className={"kc-chip" + (f.id === filter ? " kc-chip--on" : "")} onClick={() => setFilter(f.id)}>
              {f.label}<span>{all.filter(f.test).length}</span>
            </button>
          ))}
        </div>
        <div className="kc-sort">
          <span>Sort</span>
          {SORTS.map((s) => (
            <button key={s.id} type="button" className={"kc-chip" + (s.id === sort ? " kc-chip--on" : "")} onClick={() => setSort(s.id)}>
              {s.label}
            </button>
          ))}
        </div>
      </div>

      <Failed error={titles.error} onRetry={titles.reload} />

      {titles.data && shown.length === 0
        ? <Empty title="Nothing here">No film matches this filter.</Empty>
        : (
          <div className="kc-grid">
            {shown.map((t) => (
              <PosterCard
                key={t.id}
                title={t}
                note={t.status === "transcoding" ? "Still preparing" : t.status === "failed" ? "Could not be prepared" : staysFor(keptBy.get(t.id)) || clock(t.durationSeconds)}
                tone={t.status === "failed" ? "danger" : keptBy.get(t.id) && !keptBy.get(t.id).keeper ? "warn" : null} />
            ))}
          </div>
        )}
    </div>
  );
}
