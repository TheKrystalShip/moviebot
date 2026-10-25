// The library: the titles this server holds and can play, what the tracker offers to fetch, and the
// things about one held title (its subtitles, what it can be saved as). A title here is the library's,
// named by its library id; what it is, is the catalogue's card it carries (`card`).

import { call, enc, withQuery } from "./http.js";

export const library = {
  titles: () => call("GET", "/api/titles"),
  title: (id) => call("GET", `/api/titles/${enc(id)}`),
  // The episode a room holds and the ones either side, which a remote's previous and next name.
  around: (sessionId) => call("GET", `/api/sessions/${enc(sessionId)}/around`),
  // The shows the library holds an episode of, one entry each.
  shows: () => call("GET", "/api/shows"),
  // A shelf's page, searched, narrowed and ordered: `scope` is films, shows or all.
  page: (scope, params) => call("GET", withQuery(`/api/library/${scope}`, params)),
  // What the tracker offers of a show, season by season, and whether a film can be watched and how.
  showReleases: (imdbId) => call("GET", `/api/shows/${enc(imdbId)}/releases`),
  availability: (imdbId) => call("GET", `/api/films/${enc(imdbId)}/availability`),
  // Watch now: fetch it and play it in a room the moment it can be opened. Answers with the room at
  // once; a 409 is every slot for watching while it arrives in use.
  watchNow: (request) => call("POST", "/api/watch", request).catch((error) => {
    if (error.status === 409) return { outcome: "full" };
    throw error;
  }),
  // What OpenSubtitles offers for a film (it costs no allowance), and adding one for the room (it spends one).
  searchSubtitles: (titleId, languages) =>
    call("GET", `/api/titles/${enc(titleId)}/subtitles/search?language=${enc([...new Set(languages.map((l) => l.toLowerCase()))].sort().join(","))}`),
  addSubtitle: (titleId, fileId) => call("POST", `/api/titles/${enc(titleId)}/subtitles`, { fileId }),
  // What a title can be saved on a device as, and how big a title not here yet would be.
  offlineOffer: (id) => call("GET", `/api/titles/${enc(id)}/offline`),
  offlineEstimate: (imdbId, season = null, episodes = null) => call("GET", `/api/offline/estimate?imdbId=${enc(imdbId)}`
    + (season != null ? `&season=${season}&episodes=${episodes.join(",")}` : "")),
  // The films people wait on that nothing has yet, as counts.
  anticipated: () => call("GET", "/api/anticipated"),
};

/**
 * Where a library title's poster is, always on this server: `/media/<id>/poster.jpg`, the catalogue's
 * poster for it served by the library, versioned so the browser keeps it; else its card's.
 */
export function posterUrl(title) {
  if (title && title.poster) {
    const version = title.posterVersion ? `?v=${enc(title.posterVersion)}` : "";
    return `/media/${enc(title.id)}/${enc(title.poster)}${version}`;
  }
  return (title && title.card && title.card.poster) || null;
}

/**
 * What to call a library title: a film by the catalogue's name and year where there is one, an episode
 * by its show's name and its place in it.
 */
export function filmName(title) {
  const film = title && title.film;
  const card = title && title.card;
  const name = (card && card.title) || (film && film.name);
  if (title && title.episode && name) return `${name} · ${episodeCode(title.episode)}`;
  const year = (card && card.year) || (film && film.year);
  if (name) return year ? `${name} (${year})` : name;
  return (title && title.title) || "";
}

/** An episode's place as the design writes it: "S2 · E4". */
export function episodeCode(episode) {
  return episode ? `S${episode.season} · E${episode.number}` : "";
}
