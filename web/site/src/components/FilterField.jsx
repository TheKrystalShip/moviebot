import React from "react";
import { FilterDrawer, SearchField } from "@thekrystalship/krystal";
import { filterWhole } from "../facets.js";
import { drawerValues, picksOfValues, readPicks, useDrawer, wholeSections, withPicks } from "../filters.js";
import { navigate, useRoute } from "../router.js";
import { onTv } from "../tv/tv.js";

/**
 * A page's search field over the titles it holds, with the filter drawer at its start: the words narrow
 * the titles as they are typed, and the drawer's sections (`filters.js`) narrow them by what they are.
 * `drawer` is the page's `useDrawer()`, so the page knows when the drawer is open; with no sections the
 * field is drawn without one. On a television it is the screen's field, at the top of the page.
 */
export function FilterField({ value, onChange, placeholder, sections, values, onValues, drawer }) {
  const filters = sections.length > 0
    ? <FilterDrawer ref={drawer.ref} onOpenChange={drawer.onOpenChange} sections={sections} values={values} onChange={onValues} />
    : undefined;
  return onTv
    ? <SearchField size="xl" className="kc-tv-search" data-tv-top="" value={value} onChange={onChange} onSubmit={() => {}} submit={false}
      placeholder={placeholder} filters={filters} inputProps={{ className: "kc-tv-search__input" }} />
    : <SearchField size="lg" value={value} onChange={onChange} onSubmit={() => {}} submit={false}
      placeholder={placeholder} filters={filters} />;
}

/**
 * The filter of a page that holds its titles whole: the picks in the address, the words typed in the
 * field, and the drawer's sections over the titles (`filters.js`). `fixed` are the sections the page's
 * scope fixes, a shelf of one kind fixing Kind; `own` are the page's own sections after the rest, whose
 * values the address keeps under their ids. Answers the titles that pass the picks, the page's own
 * sections (`passes`) and the words (`named`), and the props of its FilterField.
 */
export function useTitleFilter(all, { fixed = ["kind"], own = [], passes = () => true, named }) {
  const route = useRoute();
  const read = readPicks(route.query);
  const picks = fixed.includes("kind") ? { ...read, kind: null } : read;
  const ownValues = Object.fromEntries(own.map((s) => [s.id, s.kind === "toggle" ? route.query.get(s.id) === "1" : route.query.get(s.id)]));
  const [text, setText] = React.useState("");
  const words = React.useDeferredValue(text).trim().toLowerCase();
  const drawer = useDrawer();
  const sections = wholeSections(all, picks, fixed, own);
  const values = { ...drawerValues(picks, false), ...ownValues };
  const onValues = (next) => {
    const extra = Object.fromEntries(own.map((s) => [s.id, next[s.id]]));
    navigate(withPicks(route.query, picksOfValues(next), route.path, extra), { replace: true });
  };
  const shown = filterWhole(all, picks).filter((t) => passes(t, ownValues) && (!words || named(t).toLowerCase().includes(words)));
  return { picks, shown, drawer, field: { value: text, onChange: setText, sections, values, onValues, drawer } };
}
