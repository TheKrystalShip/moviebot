// What a spotlight says about one title: the artwork behind it, the line above its name, its name, its
// facts, its overview and who made it. Built from whatever a page holds for the title — a library
// title, a show, a saved entry, the film database's description — so a spotlight reads the same on
// every page and every surface, whichever of them it came from.

import { posterUrl } from "./api.js";
import { clock } from "./time.js";

/** A film or an episode in the library. */
export function titleSpot(t, eyebrow) {
  const d = t.details;
  const film = t.film || {};
  return {
    key: "title:" + t.id,
    art: (d && d.backdropUrl) || posterUrl(t),
    eyebrow,
    title: film.name || t.title,
    facts: {
      rating: d && d.rating,
      votes: d && d.votes,
      year: film.year,
      cert: d && d.certification,
      length: t.durationSeconds ? clock(t.durationSeconds) : null,
      genres: d ? d.genres : [],
    },
    overview: d && d.overview,
    credit: d && d.directors && d.directors.length > 0 ? { label: "Directed by", names: d.directors.join(", ") } : null,
  };
}

/** A show in the library. */
export function showSpot(s, eyebrow) {
  const d = s.details;
  const seasons = s.seasons.length;
  return {
    key: "show:" + (s.imdbId || s.firstTitleId),
    art: d && (d.backdropUrl || d.posterUrl),
    eyebrow,
    title: s.title,
    facts: {
      rating: d && d.rating,
      votes: d && d.votes,
      year: s.firstYear,
      cert: d && d.certification,
      length: seasons > 0 ? `${seasons} ${seasons === 1 ? "season" : "seasons"}` : null,
      genres: d ? d.genres : [],
    },
    overview: d && d.overview,
    credit: d && d.directors && d.directors.length > 0 ? { label: "Created by", names: d.directors.join(", ") } : null,
  };
}

/** A saved entry, filled from the library's own title where the library holds it. */
export function entrySpot(e, held, eyebrow) {
  if (held) return { ...titleSpot(held, eyebrow), key: "entry:" + e.imdbId };
  return {
    key: "entry:" + e.imdbId,
    art: e.posterUrl,
    eyebrow,
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
    title: f.title,
    facts: {
      rating: f.rating,
      votes: f.votes,
      year: f.year,
      cert: f.certification,
      length: f.runtimeMinutes ? `${Math.floor(f.runtimeMinutes / 60)}h ${String(f.runtimeMinutes % 60).padStart(2, "0")}m` : null,
      genres: f.genres || [],
    },
    overview: f.overview,
    credit: f.directors && f.directors.length > 0 ? { label: "Directed by", names: f.directors.join(", ") } : null,
  };
}

/** A show as the film database describes it (`/api/shows/{imdbId}`). */
export function showViewSpot(s, eyebrow) {
  const seasons = s.seasons.length;
  return {
    key: "show:" + s.imdbId,
    art: s.backdropUrl || s.posterUrl,
    eyebrow,
    title: s.title,
    facts: {
      rating: s.rating,
      votes: s.votes,
      year: s.firstYear,
      cert: s.certification,
      length: seasons > 0 ? `${seasons} ${seasons === 1 ? "season" : "seasons"}` : null,
      genres: s.genres || [],
    },
    overview: s.overview,
    credit: s.creators && s.creators.length > 0 ? { label: "Created by", names: s.creators.join(", ") } : null,
  };
}
