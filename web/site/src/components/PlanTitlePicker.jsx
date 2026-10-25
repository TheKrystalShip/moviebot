import React from "react";
import { PosterCard, SearchField, useDebouncedValue } from "@thekrystalship/krystal";
import { catalogue, posterArt } from "../api/index.js";
import { newPlanPath } from "../paths.js";
import { navigate } from "../router.js";
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
    catalogue.titles({ q, size: 20 }).then((page) => page.titles, () => []).then((list) => live && setFound({ q, list }));
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
            <PosterCard key={t.id}
              name={t.title}
              art={posterArt(t)}
              onPick={() => navigate(newPlanPath(t.id, t.kind))}
              facts={[{ value: t.year || null, label: "Year" }]} />
          ))}
        </div>
      )}
    </div>
  );
}
