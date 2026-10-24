// What a spotlight says about one title: the artwork behind it, the line above its name, its name,
// whether it is a film or a show and on which shelf, its facts, its overview and who made it. Built from
// whatever a page holds for the title — a library title, a show, a saved entry, the film database's
// description — so a spotlight reads the same on every page and every surface, whichever of them it
// came from. `credit` is the one line a television shows; `credits` is the title page's table
// (`Credits`), from the film database's description where the page has it.

import { posterUrl } from "./api.js";
import { clock } from "./time.js";

/** The credits table's people from a film's or a show's description, or nothing without one. */
function creditsOf(view) {
  if (!view) return null;
  return { creators: view.creators, directors: view.directors, writers: view.writers, personIds: view.personIds, cast: view.cast || [] };
}

/** A film or an episode in the library; `view` is its description from the film database, when asked. */
export function titleSpot(t, eyebrow, view = null) {
  const d = t.details;
  const film = t.film || {};
  return {
    key: "title:" + t.id,
    art: (d && d.backdropUrl) || posterUrl(t),
    eyebrow,
    kind: "film",
    category: t.category,
    title: film.name || t.title,
    facts: {
      rating: d && d.rating,
      votes: d && d.votes,
      ratedBy: d && d.ratedBy,
      year: film.year,
      cert: d && d.certification,
      length: t.durationSeconds ? clock(t.durationSeconds) : null,
      genres: d ? d.genres : [],
    },
    overview: (d && d.overview) || (view && view.overview),
    credit: d && d.directors && d.directors.length > 0 ? { label: "Directed by", names: d.directors.join(", ") } : null,
    credits: creditsOf(view),
  };
}

/** A show in the library; `view` is its description from the film database, when asked. */
export function showSpot(s, eyebrow, view = null) {
  const d = s.details;
  const seasons = s.seasons.length;
  return {
    key: "show:" + (s.imdbId || s.firstTitleId),
    art: d && (d.backdropUrl || d.posterUrl),
    eyebrow,
    kind: "show",
    category: s.category,
    title: s.title,
    facts: {
      rating: d && d.rating,
      votes: d && d.votes,
      ratedBy: d && d.ratedBy,
      year: s.firstYear,
      cert: d && d.certification,
      length: seasons > 0 ? `${seasons} ${seasons === 1 ? "season" : "seasons"}` : null,
      genres: d ? d.genres : [],
    },
    overview: (d && d.overview) || (view && view.overview),
    credit: d && d.directors && d.directors.length > 0 ? { label: "Created by", names: d.directors.join(", ") } : null,
    credits: creditsOf(view),
  };
}

/** A saved entry, filled from the library's own title where the library holds it. */
export function entrySpot(e, held, eyebrow) {
  if (held) return { ...titleSpot(held, eyebrow), key: "entry:" + e.imdbId };
  return {
    key: "entry:" + e.imdbId,
    art: e.posterUrl,
    eyebrow,
    kind: e.kind === "show" ? "show" : "film",
    category: e.category,
    title: e.title,
    facts: {
      rating: e.rating,
      year: e.year,
      length: e.runtimeMinutes ? clock(e.runtimeMinutes * 60) : null,
      genres: e.genres || [],
    },
  };
}

/** A film as the film database describes it (`/api/films/{imdbId}`). */
export function filmSpot(f, eyebrow) {
  return {
    key: "film:" + f.imdbId,
    art: f.backdropUrl || f.posterUrl,
    eyebrow,
    kind: "film",
    category: f.category,
    title: f.title,
    facts: {
      rating: f.rating,
      votes: f.votes,
      ratedBy: f.ratedBy,
      year: f.year,
      cert: f.certification,
      length: f.runtimeMinutes ? `${Math.floor(f.runtimeMinutes / 60)}h ${String(f.runtimeMinutes % 60).padStart(2, "0")}m` : null,
      genres: f.genres || [],
    },
    overview: f.overview,
    credit: f.directors && f.directors.length > 0 ? { label: "Directed by", names: f.directors.join(", ") } : null,
    credits: creditsOf(f),
  };
}

/** A show as the film database describes it (`/api/shows/{imdbId}`). */
export function showViewSpot(s, eyebrow) {
  const seasons = s.seasons.length;
  return {
    key: "show:" + s.imdbId,
    art: s.backdropUrl || s.posterUrl,
    eyebrow,
    kind: "show",
    category: s.category,
    title: s.title,
    facts: {
      rating: s.rating,
      votes: s.votes,
      ratedBy: s.ratedBy,
      year: s.firstYear,
      cert: s.certification,
      length: seasons > 0 ? `${seasons} ${seasons === 1 ? "season" : "seasons"}` : null,
      genres: s.genres || [],
    },
    overview: s.overview,
    credit: s.creators && s.creators.length > 0 ? { label: "Created by", names: s.creators.join(", ") } : null,
    credits: creditsOf(s),
  };
}
