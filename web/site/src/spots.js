// What a spotlight says about one title: the artwork behind it, the line above its name, its name,
// whether it is a film or a show and on which shelf, its facts, its overview and who made it. Built from
// the title's catalogue card, and its page where one is held, so a spotlight reads the same on every
// page and every surface. `credit` is the one line a television shows; `credits` is the title's page,
// which the credits table reads (`Credits`).

import { certificateOf, posterUrl, runtimeText } from "./api/index.js";
import { clock } from "./time.js";

/** The facts line of a card: its rating, year, certificate where the page has one, `length` and genres. */
function factsOf(card, page, length) {
  const rating = card && card.rating;
  return {
    rating: rating && rating.value,
    votes: rating && rating.votes,
    ratedBy: rating && rating.by,
    year: card && card.year,
    cert: certificateOf(page),
    length,
    genres: (card && card.genres) || [],
  };
}

/** The one credit line a television shows: who directed a film, who created a show. */
function creditOf(card) {
  if (!card || !card.madeBy || card.madeBy.length === 0) return null;
  return { label: card.kind === "show" ? "Created by" : "Directed by", names: card.madeBy.join(", ") };
}

/** How long a show runs, by its seasons. */
const seasonsText = (count) => (count > 0 ? `${count} ${count === 1 ? "season" : "seasons"}` : null);

/** A title from the catalogue, by its card, and its page when the page is held. */
export function cardSpot(card, eyebrow, page = null) {
  const film = card.kind !== "show";
  return {
    key: (film ? "film:" : "show:") + card.id,
    art: card.backdrop || card.poster,
    eyebrow,
    kind: film ? "film" : "show",
    category: card.category,
    title: card.title,
    facts: factsOf(card, page, film ? runtimeText(card.runtime) : seasonsText(page ? page.seasons.length : 0)),
    overview: (page && page.overview) || card.plot,
    credit: creditOf(card),
    credits: page,
  };
}

/** A film or an episode in the library: its card's, with the library's own length and poster. */
export function titleSpot(t, eyebrow, page = null) {
  const card = t.card;
  if (card) return { ...cardSpot(card, eyebrow, page), key: "title:" + t.id, art: card.backdrop || posterUrl(t),
    facts: factsOf(card, page, t.durationSeconds ? clock(t.durationSeconds) : runtimeText(card.runtime)) };
  const film = t.film || {};
  return {
    key: "title:" + t.id, art: posterUrl(t), eyebrow, kind: "film", category: null, title: film.name || t.title,
    facts: factsOf(null, null, t.durationSeconds ? clock(t.durationSeconds) : null), overview: null, credit: null, credits: null,
  };
}

/** A show in the library: its card's, with the seasons the library holds. */
export function showSpot(s, eyebrow, page = null) {
  if (s.card) return { ...cardSpot(s.card, eyebrow, page), key: "show:" + (s.imdbId || s.firstTitleId),
    facts: factsOf(s.card, page, seasonsText(s.seasons.length)) };
  return {
    key: "show:" + (s.imdbId || s.firstTitleId), art: null, eyebrow, kind: "show", category: null, title: s.title,
    facts: factsOf(null, null, seasonsText(s.seasons.length)), overview: null, credit: null, credits: null,
  };
}

/** A saved entry, filled from the library's own title where the library holds it, else from its card. */
export function entrySpot(e, held, eyebrow) {
  if (held) return { ...titleSpot(held, eyebrow), key: "entry:" + e.imdbId };
  if (e.card) return { ...cardSpot(e.card, eyebrow), key: "entry:" + e.imdbId };
  return {
    key: "entry:" + e.imdbId, art: null, eyebrow, kind: e.kind === "show" ? "show" : "film", category: null, title: e.title,
    facts: factsOf(null, null, null),
  };
}
