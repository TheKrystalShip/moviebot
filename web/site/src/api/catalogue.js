// The catalogue, as the API serves it (docs/api-contract.md, Catalogue): one card wherever a title is
// listed, one page for a title, and one list query whose filters answer every list. A title is named
// by its IMDb id, and every image by the URL its card carries, which never changes under its name.
//
// A list query is an object of the query's parameters (`{ genre: ["Horror"], from: 2000, sort:
// "rating" }`), a list parameter as an array; a shelf names its query as a string, which `queryParams`
// reads into one. The API joins every card with the asking person (`you`: whether the library holds
// it, its title there, whether they watched it and whether they want it).

import { call, enc, withQuery } from "./http.js";

export const catalogue = {
  // One page of the titles a query names, as `{ titles, total, next }`; `next` is the page after, absent on the last.
  titles: (query = {}, page = 1) => call("GET", withQuery("/api/catalogue/titles", page > 1 ? { ...query, page } : query)),
  // Every section's options over what a query names, each counted with the other sections' picks.
  facets: (query = {}) => call("GET", withQuery("/api/catalogue/facets", query)),
  title: (imdbId) => call("GET", `/api/catalogue/titles/${enc(imdbId)}`),
  season: (imdbId, number) => call("GET", `/api/catalogue/titles/${enc(imdbId)}/seasons/${number}`),
  // Words resolved to what the list query takes: `[{ filter, value, name, detail }]`.
  terms: (q) => call("GET", withQuery("/api/catalogue/terms", { q })),
  // Words read as a question: `{ question, parts, query, message }`; `query` is what the list query is asked.
  ask: (q) => call("GET", withQuery("/api/catalogue/ask", { q })),
  person: (id) => call("GET", `/api/catalogue/people/${enc(id)}`),
  // The franchises in the person's order, a page at a time, as `{ franchises, total, next }`.
  franchises: (page = 1, size = 40) => call("GET", withQuery("/api/catalogue/franchises", { page: page > 1 ? page : null, size })),
  franchise: (id) => call("GET", `/api/catalogue/franchises/${enc(id)}`),
  // The Discover page: its rows and categories for the person, every shelf a name and a query.
  discover: () => call("GET", "/api/catalogue/discover"),
};

/** A query's pages as `usePaged` reads them: `(page) => { titles, next }`. */
export const pagesOf = (query) => (page) => catalogue.titles(query, page);

/** A query string (a shelf's) as the object the list query takes: every parameter a list of its values. */
export function queryParams(text) {
  const query = {};
  for (const [key, value] of new URLSearchParams(text || "")) {
    for (const one of value.split(",")) if (one) (query[key] ||= []).push(one);
  }
  return query;
}

/** Two queries as one: the second's parameters over the first's. */
export const narrowed = (query, more) => ({ ...query, ...more });

/** A card's name with its year, as a list says it. */
export function cardName(card) {
  if (!card) return "";
  return card.year ? `${card.title} (${card.year})` : card.title;
}

/** The rating a card shows, to one decimal; null when it has none (fewer than 25 votes). */
export function cardRating(card) {
  const value = card && card.rating && card.rating.value;
  return value != null ? value.toFixed(1) : null;
}

/** Whose a card's rating is, as a page names it. */
export function ratedBy(card) {
  const by = card && card.rating && card.rating.by;
  return by === "imdb" ? "IMDb" : by === "tmdb" ? "TMDB" : null;
}

/** A card's poster as krystal's art takes it, or null when it has none. */
export const posterArt = (card) => (card && card.poster ? `url("${card.poster}")` : null);

/**
 * The certificate a title's page shows: the one for the viewer's own country, read from the browser's
 * languages (en-GB is GB), since Cinema fixes no country; none when the browser names none or the title
 * has none there.
 */
export function certificateOf(page) {
  const certificates = page && page.certificates;
  if (!certificates) return null;
  const languages = typeof navigator !== "undefined" ? navigator.languages || [navigator.language] : [];
  const region = languages.map((l) => (l || "").split("-")[1]).find((r) => r && r.length === 2);
  return region ? certificates[region.toUpperCase()] || null : null;
}

/** A film's length as a page's facts write it: 1h 46m. */
export function runtimeText(minutes) {
  return minutes ? `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, "0")}m` : null;
}

/** Whether a card is a film, as opposed to a show. */
export const isFilm = (card) => card && card.kind === "film";
