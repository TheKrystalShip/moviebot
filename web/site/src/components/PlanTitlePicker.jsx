import React from "react";
import { SearchField, ShowcaseCard, useDebouncedValue } from "@thekrystalship/krystal";
import { api } from "../api.js";
import { newPlanPath } from "../paths.js";
import { navigate } from "../router.js";
import { kindLine } from "./Film.jsx";
import { CardsSkeleton } from "./TitleCards.jsx";

/**
 * Finds the title a plan is for: the catalogue's matches for a name as the person types, films and
 * shows together, each a poster card whose Plan a watch opens the form with it filled in.
 */
export function PlanTitlePicker({ autoFocus = false }) {
  const [draft, setDraft] = React.useState("");
  const q = useDebouncedValue(draft.trim(), 300);
  const [found, setFound] = React.useState({ q: "", list: [] });
  React.useEffect(() => {
    if (!q) return undefined;
    let live = true;
    api.findTitles(q).catch(() => []).then((list) => live && setFound({ q, list }));
    return () => { live = false; };
  }, [q]);

  const settled = found.q === q;
  return (
    <div className="kc-plan-picker">
      <SearchField size="xl" label="Find a film or show" placeholder="Find a film or show" value={draft} autoFocus={autoFocus}
        onChange={setDraft} onSubmit={setDraft} submit={false} />
      {q && !settled && <CardsSkeleton count={4} />}
      {q && settled && found.list.length > 0 && (
        <div className="kc-grid">
          {found.list.map((t) => (
            <ShowcaseCard key={t.imdbId} compactFacts
              name={t.title}
              eyebrow={kindLine(t.kind, t)}
              art={t.posterUrl ? `url("${t.posterUrl}")` : null}
              onPick={() => navigate(newPlanPath(t.imdbId, t.kind))}
              specs={[{ icon: "calendar", value: t.year || null, label: "Year" }]}
              action={{ label: "Plan a watch" }} />
          ))}
        </div>
      )}
    </div>
  );
}
