import React from "react";
import { FilterDrawer, SearchField, useDebouncedValue } from "@thekrystalship/krystal";
import { useLoad } from "../data.js";
import { filterWhole } from "../facets.js";
import { apiPicks, drawerValues, picksOfValues, readPicks, sectionsOf, useDrawer, wholeSections, withPicks } from "../filters.js";
import { orderOf, sortOf, withOrder } from "../orders.js";
import { useSort } from "../sorts.js";
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

/** What a page in the API's one order keeps no order under. */
const UNSORTED = "unsorted";
const UNSORTED_KEYS = [{ value: "none", defaultDir: "asc" }];

/** The section a page's states are picked in, as the API's pages name it. */
const STATE = "state";

/**
 * The filter of a page the API searches, narrows and orders whole (`/api/library/<scope>`,
 * `/api/me/wants/page`): the picks and the state picked in the address, the words typed in the field,
 * and the order the person keeps on the page (`useSort` under `surface`, its keys `keys`; a page served in
 * one order names no `surface`). `ask(params)`
 * asks the API and answers `{titles, total, facets}`; its facets give the drawer its sections, the
 * page's states (worded by `stateWords`, offered while it holds two) and its orders. `device` are
 * sections only the device knows the answer to, kept in the address under their ids, and
 * `deviceAsk(values)` what the API is asked for them; `passes(title)` is what a surface leaves off
 * what the API answers. `deps` ask again when what they name changes. Answers the titles as the API
 * orders them, how many the page holds, the picks, and the props of its FilterField.
 */
export function useListedFilter(ask, { surface, keys, fixed = ["kind"], stateWords = {}, device = [], deviceAsk = () => ({}), passes = () => true, deps = [] }) {
  const route = useRoute();
  const read = readPicks(route.query);
  const picks = fixed.includes("kind") ? { ...read, kind: null } : read;
  const state = route.query.get(STATE) || null;
  const deviceValues = Object.fromEntries(device.map((s) => [s.id, s.kind === "toggle" ? route.query.get(s.id) === "1" : route.query.get(s.id)]));
  const [text, setText] = React.useState("");
  const words = useDebouncedValue(text.trim(), 250);
  const [order, pick] = useSort(surface || UNSORTED, keys || UNSORTED_KEYS);
  const sort = surface ? sortOf(order) : null;
  const params = { ...apiPicks(picks), ...(words ? { q: words } : {}), ...(sort ? { sort } : {}), ...(state ? { state } : {}), ...deviceAsk(deviceValues) };
  const answer = useLoad(() => ask(params), [JSON.stringify(params), ...deps]);
  const drawer = useDrawer();

  const page = answer.data;
  const facets = page ? page.facets : null;
  const states = facets ? facets.sections.find((s) => s.id === STATE) : null;
  const stateSection = states && (states.options.length >= 2 || state)
    ? [{ id: STATE, label: "State", kind: "optional", options: states.options.map((o) => ({ value: o.value, label: stateWords[o.value] || o.value })) }]
    : [];
  const filters = sectionsOf(facets ? { sections: facets.sections.filter((s) => s.id !== STATE) } : null, picks, { watched: false });
  const drawn = {
    sections: [...filters, ...stateSection, ...device],
    values: { ...drawerValues(picks, false), [STATE]: state, ...deviceValues },
    onValues: (next) => {
      const extra = { [STATE]: next[STATE] || null, ...Object.fromEntries(device.map((s) => [s.id, next[s.id]])) };
      navigate(withPicks(route.query, picksOfValues(next), route.path, extra), { replace: true });
    },
  };
  const orders = facets ? facets.sorts.map(orderOf) : [];
  const field = orders.length > 0 ? { ...drawn, ...withOrder(drawn, orders, order, pick) } : drawn;
  const titles = page ? page.titles.filter((t) => passes(t, deviceValues)) : null;
  return {
    titles, total: page ? page.total : null, error: answer.error, reload: answer.reload, picks, drawer,
    field: { value: text, onChange: setText, ...field, drawer },
  };
}
