// The filter of every page that shows titles: the picks the address holds, what the API is asked with
// them, and the sections the filter drawer (krystal's FilterDrawer, in the page's search field) offers.
// Discover's answers (its catalogue, a question, a person's titles, a genre's, a shelf and a browse
// category) take their options from the API's facets; the library's shelves, Want to watch and History
// count theirs over the list they hold (`facets.js`). The sections, their ids and their option values
// are the API's (`docs/api-contract.md`, Filters and facets): a section offers the same options
// whatever is picked, an option nothing is under at the other picks is shown and cannot be picked, and
// a section with fewer than two options is left out, so the drawer holds still while somebody picks.

import React from "react";
import { wholeFacets } from "./facets.js";
import { Rank, onBack, onTv } from "./tv/tv.js";

/** The address parameter each section's picks are kept under. A list repeats its parameter. */
const ADDRESS = { kind: "k", genre: "g", decade: "decade", length: "length", rating: "rating", language: "lang" };

/** The sections that hold several picks. */
const LISTS = ["genre", "decade", "length", "language"];

const KINDS = ["film", "show", "anime"];
const KIND_LABELS = { all: "All", film: "Movies", show: "TV shows", anime: "Anime" };
const LENGTH_LABELS = { short: "Under 90 min", standard: "90 to 150 min", long: "Over 150 min" };

/** Every address parameter a pick is kept under. */
export const PICK_PARAMS = Object.values(ADDRESS);

/** The picks the address holds, by section: `kind` and `rating` one value or null, the rest lists. */
export function readPicks(query) {
  const all = (param) => query.getAll(param).flatMap((v) => v.split(",")).map((v) => v.trim()).filter(Boolean);
  const kind = query.get(ADDRESS.kind);
  const rating = query.get(ADDRESS.rating);
  return {
    kind: KINDS.includes(kind) ? kind : null,
    genre: all(ADDRESS.genre),
    decade: all(ADDRESS.decade).filter((d) => /^\d{4}$/.test(d)),
    length: all(ADDRESS.length).filter((l) => LENGTH_LABELS[l]),
    rating: /^\d+$/.test(rating || "") ? rating : null,
    language: all(ADDRESS.language).filter((l) => /^[a-z]{2}$/.test(l)),
  };
}

/** How many picks there are. */
export const pickCount = (picks) => (picks.kind ? 1 : 0) + (picks.rating ? 1 : 0) + LISTS.reduce((n, id) => n + picks[id].length, 0);

/** The picks as the API's parameters; a kind of All asks nothing of the kind. */
export function apiPicks(picks) {
  const params = {};
  if (picks.kind === "anime") {
    params.kind = "all";
    params.category = "anime";
  } else if (picks.kind) params.kind = picks.kind;
  if (picks.genre.length) params.anyGenre = picks.genre;
  if (picks.decade.length) params.decade = picks.decade;
  if (picks.length.length) params.length = picks.length;
  if (picks.rating) params.minRating = picks.rating;
  if (picks.language.length) params.language = picks.language;
  return params;
}

/**
 * What Discover's catalogue is asked for the filter's picks: films and shows unless a kind is picked.
 * Any rating still needs a hundred votes, so nobody's home video is listed, and the floor of votes
 * stays the same with a rating picked, so the rating's options measure the answer a pick gives.
 */
export function filterQuestion(picks, sort) {
  const q = { kind: "all", ...apiPicks(picks), goodOnly: "false", minVotes: "100" };
  if (sort && sort !== "popular") q.sort = sort;
  return q;
}

/**
 * The address at `path` with `picks` in place of the picks it held, every other parameter kept. `extra`
 * sets a page's own parameters beside them: a value sets one, and null or false takes it off.
 */
export function withPicks(query, picks, path = "/discover", extra = {}) {
  const next = new URLSearchParams(query);
  for (const param of PICK_PARAMS) next.delete(param);
  if (picks.kind) next.set(ADDRESS.kind, picks.kind);
  for (const id of LISTS) for (const v of picks[id]) next.append(ADDRESS[id], v);
  if (picks.rating) next.set(ADDRESS.rating, picks.rating);
  for (const [param, value] of Object.entries(extra)) {
    if (value === null || value === undefined || value === false || value === "") next.delete(param);
    else next.set(param, value === true ? "1" : String(value));
  }
  const rest = next.toString();
  return path + (rest ? "?" + rest : "");
}

const languageNames = typeof Intl !== "undefined" && Intl.DisplayNames ? new Intl.DisplayNames(["en"], { type: "language" }) : null;

/** A language's name in English by its ISO code, or the code where none is known. */
export function languageName(code) {
  try {
    return (languageNames && languageNames.of(code)) || code;
  } catch {
    return code;
  }
}

/**
 * A section's options from the facets, with every pick kept in so it can be taken off; an option the
 * facets report empty cannot be picked.
 */
function optionsOf(facet, chosen, label) {
  const values = facet ? facet.options.map((o) => o.value) : [];
  const by = new Map(facet ? facet.options.map((o) => [o.value, o]) : []);
  for (const v of chosen) if (!values.includes(v)) values.push(v);
  return values.map((value) => {
    const option = by.get(value);
    return { value, label: label(value, option && option.label), disabled: !!(option && option.empty) };
  });
}

/**
 * The drawer's sections for an answer's facets (`{sections: [{id, options}]}`, or null while they are
 * asked for) and the picks. A section is offered when it has two options or a pick to take off; while
 * the facets are asked for, a section shows its picks alone. `watched` adds the switch that leaves off
 * what the person watched.
 */
export function sectionsOf(facets, picks, { watched = true } = {}) {
  const by = new Map((facets ? facets.sections : []).map((s) => [s.id, s]));
  const chosen = (id) => (id === "kind" || id === "rating" ? (picks[id] ? [picks[id]] : []) : picks[id]);
  const sections = [];
  const add = (section, least) => {
    if (section.options.length >= least || chosen(section.id).length > 0) sections.push(section);
  };

  const kinds = optionsOf(by.get("kind"), chosen("kind"), (v) => KIND_LABELS[v] || v);
  add({ id: "kind", label: "Kind", kind: "one", base: "all", options: [{ value: "all", label: KIND_LABELS.all }, ...kinds] }, 3);
  add({ id: "genre", label: "Genre", kind: "many", options: optionsOf(by.get("genre"), chosen("genre"), (v) => v) }, 2);
  add({ id: "decade", label: "Decade", kind: "many", options: optionsOf(by.get("decade"), chosen("decade"), (v) => v + "s") }, 2);
  add({ id: "length", label: "Length", kind: "many", options: optionsOf(by.get("length"), chosen("length"), (v) => LENGTH_LABELS[v] || v) }, 2);
  add({ id: "rating", label: "Rating", kind: "optional", options: optionsOf(by.get("rating"), chosen("rating"), (v) => v + " and up") }, 2);
  add({ id: "language", label: "Language", kind: "many", options: optionsOf(by.get("language"), chosen("language"), (v, l) => l || languageName(v)) }, 2);
  if (watched) sections.push({ id: "watched", label: "Watched", kind: "toggle", option: "Hide watched" });
  return sections;
}

/** The drawer's values for the picks and the watched switch. */
export const drawerValues = (picks, hideWatched) => ({ ...picks, kind: picks.kind || "all", watched: hideWatched });

/** The picks the drawer's next values hold. */
export const picksOfValues = (values) => ({
  kind: KINDS.includes(values.kind) ? values.kind : null,
  genre: values.genre || [],
  decade: values.decade || [],
  length: values.length || [],
  rating: values.rating || null,
  language: values.language || [],
});

/**
 * An answer's facets, asked again whenever `key` changes. The last facets stay while the next are
 * asked for, so a pick never empties the drawer. It reports nothing to the page's preparation: facets
 * can take seconds the first time, and the page is shown without them.
 */
export function useFacets(load, key) {
  const [held, setHeld] = React.useState({ key: null, data: null });
  React.useEffect(() => {
    if (!load) { setHeld({ key, data: null }); return undefined; }
    let live = true;
    load().then((data) => { if (live) setHeld({ key, data }); }, () => { if (live) setHeld((cur) => ({ ...cur, key })); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  return held.data;
}

/**
 * The page's filter drawer: the ref it is handed, whether it is open (its `onOpenChange`), and on a
 * television the remote's Back given to it before the page, so Back leaves a section and then closes
 * the drawer. On a television the page's answers under the open drawer are made inert, so the remote's
 * arrows stay in it.
 */
export function useDrawer() {
  const ref = React.useRef(null);
  const [open, setOpen] = React.useState(false);
  React.useEffect(() => (onTv ? onBack(() => (ref.current ? ref.current.back() : false), Rank.screen) : undefined), []);
  return { ref, open, onOpenChange: setOpen };
}

/**
 * The drawer's sections and picks over a list the page holds whole: the options each section's titles
 * hold (`wholeFacets`), the sections a page's scope fixes left out, and the page's own sections after
 * them.
 */
export function wholeSections(titles, picks, fixed = [], own = []) {
  return [...sectionsOf({ sections: wholeFacets(titles, picks, fixed) }, picks, { watched: false }), ...own];
}
