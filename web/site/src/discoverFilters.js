// Discover's filter: the picks the address holds, what the API is asked with them, and the sections the
// filter drawer (krystal's FilterDrawer, in the search field) offers. Every Discover answer filters the
// same way: Discover's catalogue, a question, a person's titles, a genre's, a shelf and a browse
// category. The sections, their ids and their option values are the API's (`docs/api-contract.md`,
// Filters and facets); the options of each section are the ones the answer's facets report, so a
// page offers only what its answer holds and leaves out a section with fewer than two.

import React from "react";
import { onTv } from "./tv/tv.js";

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

/** The address with `picks` in place of the picks it held, every other parameter kept. */
export function withPicks(query, picks) {
  const next = new URLSearchParams(query);
  for (const param of PICK_PARAMS) next.delete(param);
  if (picks.kind) next.set(ADDRESS.kind, picks.kind);
  for (const id of LISTS) for (const v of picks[id]) next.append(ADDRESS[id], v);
  if (picks.rating) next.set(ADDRESS.rating, picks.rating);
  return "/discover?" + next.toString();
}

/** A section's options from the facets, with every pick kept in so it can be taken off. */
function optionsOf(facet, chosen, label) {
  const values = facet ? facet.options.map((o) => o.value) : [];
  const labels = new Map(facet ? facet.options.map((o) => [o.value, o.label]) : []);
  for (const v of chosen) if (!values.includes(v)) values.push(v);
  return values.map((value) => ({ value, label: label(value, labels.get(value)) }));
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
  add({ id: "language", label: "Language", kind: "many", options: optionsOf(by.get("language"), chosen("language"), (v, l) => l || v) }, 2);
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
 * Whether the drawer in the search field whose input is `inputRef` is open, read from its button. On a
 * television the page's answers under the open drawer are made inert, so the remote's arrows stay in
 * the drawer.
 */
export function useDrawerOpen(inputRef) {
  const [open, setOpen] = React.useState(false);
  React.useEffect(() => {
    const field = inputRef.current && inputRef.current.closest("form");
    if (!onTv || !field || typeof MutationObserver !== "function") return undefined;
    const read = () => {
      const trigger = field.querySelector(".k-filters__trigger");
      setOpen(!!trigger && trigger.getAttribute("aria-expanded") === "true");
    };
    read();
    const watcher = new MutationObserver(read);
    watcher.observe(field, { attributes: true, subtree: true, childList: true, attributeFilter: ["aria-expanded"] });
    return () => watcher.disconnect();
  }, [inputRef]);
  return open;
}
