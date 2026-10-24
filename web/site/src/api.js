// MovieBot.Api, as the site reaches it: same origin, the session's access token as the bearer.
//
// This is the one module that attaches the token, through the session's `authorizedFetch`, which
// renews and replays a refused call once. A 401 that survives that is a session the API will not
// honour: it is ended, which the page answers by saying so and offering the way back rather than
// showing a library that failed to load. A 403 is the person's account not letting them do what they
// asked.
//
// Every call tells `connectivity.js` whether the server answered: a call that fails on the network,
// or is answered by a gateway or a captive portal rather than by MovieBot, is the page going offline,
// and anything the server answers, an error among it, is the page online.

import { answeredByServer, reached, unreached } from "./connectivity.js";
import { keepPreferences } from "./offline/preferences.js";
import { keepProgress } from "./offline/progress.js";
import { queryOf } from "./query.js";
import { session, sessionStore } from "./session.js";

export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

const UNANSWERED = "MovieBot is not answering. Try again in a moment.";
const SIGN_IN = "Sign in to carry on.";

/** The request with the bearer when somebody is signed in, and without it when nobody is. */
function authorized(path, init) {
  return session.authorizedFetch(path, init).catch((error) => {
    if (error && error.code === "none") return fetch(path, init);
    throw error;
  });
}

async function send(method, path, body) {
  const headers = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  let response;
  try {
    response = await authorized(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch (error) {
    if (error && error.code === "ended") throw new ApiError(401, SIGN_IN);
    unreached();
    throw new ApiError(0, UNANSWERED);
  }
  if (!answeredByServer(response)) {
    unreached();
    throw new ApiError(0, UNANSWERED);
  }
  reached();
  return response;
}

async function call(method, path, body) {
  const response = await send(method, path, body);

  if (response.status === 401) {
    if (session.getSnapshot().status === "live") session.end("refused");
    throw new ApiError(401, SIGN_IN);
  }
  if (response.status === 204) return null;

  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new ApiError(response.status, (payload && payload.error) || `MovieBot answered ${response.status}.`);
  return payload;
}

const enc = encodeURIComponent;

const withQuery = (path, params) => {
  const query = queryOf(params);
  return query ? `${path}?${query}` : path;
};

/** Keeps the signed-in person's preferences as the API answered them, and answers them. */
function kept(preferences) {
  const session = sessionStore.getState();
  if (session) keepPreferences(session.user.id, preferences);
  return preferences;
}

/** Keeps where the signed-in person got to as the API answered it, and answers it. */
function keptPosition(entries) {
  const session = sessionStore.getState();
  if (session) keepProgress(session.user.id, entries);
  return entries;
}

export const api = {
  titles: () => call("GET", "/api/titles"),
  title: (id) => call("GET", `/api/titles/${enc(id)}`),
  // The episode a room holds and the ones either side, which a remote's previous and next name.
  around: (sessionId) => call("GET", `/api/sessions/${enc(sessionId)}/around`),
  film: (imdbId) => call("GET", `/api/films/${enc(imdbId)}`),
  // The films and shows the film database counts as trending this week, whether here or not.
  popular: () => call("GET", "/api/popular"),
  // The film database browsed: everything a person is credited on, by its id for them, and the most
  // popular films or shows in a genre, by the genre's name.
  // `picks` are a filter's, as on catalogueDiscover; each Facets call is what each section of that answer holds.
  cataloguePerson: (personId, picks = {}) => call("GET", withQuery(`/api/catalogue/people/${enc(personId)}`, picks)),
  cataloguePersonFacets: (personId, picks = {}) => call("GET", withQuery(`/api/catalogue/people/${enc(personId)}/facets`, picks)),
  // Films or shows found by what they are like: kind, genre, years, runtime, rating, language and order.
  // A list is several values of one section, any of them (`anyGenre`, `decade`, `length`, `language`).
  catalogueDiscover: (params, page = 1) => call("GET", withQuery("/api/catalogue/discover", page > 1 ? { ...params, page } : params)),
  // Which options each section of that discovery's answers holds, each without its own section's picks.
  catalogueDiscoverFacets: (params) => call("GET", withQuery("/api/catalogue/discover/facets", params)),
  // Discover's tabs and the shelves on each for the signed-in person, with what its filter picks from;
  // and one page of a shelf's titles, with the page that follows it.
  discover: () => call("GET", "/api/discover"),
  discoverShelf: (id, page = 1, sort = null, picks = {}) =>
    call("GET", withQuery(`/api/discover/shelves/${enc(id)}`, { page: page > 1 ? page : null, sort, ...picks })),
  discoverShelfFacets: (id, picks = {}) => call("GET", withQuery(`/api/discover/shelves/${enc(id)}/facets`, picks)),
  // What a browse category's shelves hold together; each shelf takes the picks through discoverShelf.
  discoverSectionFacets: (id, picks = {}) => call("GET", withQuery(`/api/discover/sections/${enc(id)}/facets`, picks)),
  catalogueGenre: (kind, genre, category, picks = {}) => call("GET", withQuery("/api/catalogue/genre", { kind, name: genre, category, ...picks })),
  catalogueGenreFacets: (kind, genre, category, picks = {}) =>
    call("GET", withQuery("/api/catalogue/genre/facets", { kind, name: genre, category, ...picks })),

  // Shows: the library's, one by its IMDb id (in the library or not), and what the tracker offers of one.
  shows: () => call("GET", "/api/shows"),
  // A library shelf's page, searched, narrowed and ordered: `scope` is films, shows or all.
  libraryPage: (scope, params) => call("GET", withQuery(`/api/library/${scope}`, params)),
  show: (imdbId) => call("GET", `/api/shows/${enc(imdbId)}`),
  showReleases: (imdbId) => call("GET", `/api/shows/${enc(imdbId)}/releases`),
  rooms: () => call("GET", "/api/sessions"),
  // One room as it stands, which lets the person in when it admits them; a 403 is a room private to them.
  room: (id) => call("GET", `/api/sessions/${enc(id)}`),

  // The catalogue's matches for a name, films and shows together or films alone, each with its
  // poster and cast; and whether a film can be watched and how.
  findTitles: (q) => call("GET", `/api/titles/find?q=${enc(q)}`),
  findFilms: (q) => call("GET", `/api/films/find?q=${enc(q)}`),
  // The words as a question of the catalogue (top 10 horror from 2000): whether they are one, the
  // parts they were read as, and a page of the titles that answer them, with the page that follows,
  // or why none does.
  // `picks` are a filter's on the question, as on catalogueDiscover; askFacets is what each section holds.
  ask: (q, page = 1, picks = {}) => call("GET", withQuery("/api/catalogue/ask", { q, ...picks, page: page > 1 ? page : null })),
  askFacets: (q, picks = {}) => call("GET", withQuery("/api/catalogue/ask/facets", { q, ...picks })),
  availability: (imdbId) => call("GET", `/api/films/${enc(imdbId)}/availability`),
  // Watch now: fetch it and play it in a room the moment it can be opened. Answers with the room at
  // once; a 409 is every slot for watching while it arrives in use, and carries them.
  watchNow: (request) => call("POST", "/api/watch", request).catch((error) => {
    if (error.status === 409) return { outcome: "full" };
    throw error;
  }),

  // What the signed-in person has watched, and how they watch.
  // Kept on the device for the account, which Home reads with no connection (`offline/progress.js`).
  progress: () => call("GET", "/api/me/progress").then(keptPosition),
  // Where films played from the device got to, sent once there is a connection (`offline/progress.js`).
  sendProgress: (entries) => call("POST", "/api/me/progress", entries),
  history: () => call("GET", "/api/me/history"),
  historyPage: (params) => call("GET", withQuery("/api/me/history/page", params)),
  forgetHistory: (id) => call("DELETE", `/api/me/history/${enc(id)}`),
  // Each answer is kept on the device for the account, which is what offline pages and the player
  // read with no connection (`offline/preferences.js`).
  preferences: () => call("GET", "/api/me/preferences").then(kept),
  setPreferences: (preferences) => call("PUT", "/api/me/preferences", preferences).then(kept),

  // The subtitle and audio track the signed-in person watches a film with, the same on every device.
  tracks: (titleId) => call("GET", `/api/me/tracks/${enc(titleId)}`),
  setTracks: (titleId, choice) => call("PUT", `/api/me/tracks/${enc(titleId)}`, choice),

  // What OpenSubtitles offers for a film (it costs no allowance), and adding one for the room (it spends one).
  searchSubtitles: (titleId, languages) =>
    call("GET", `/api/titles/${enc(titleId)}/subtitles/search?language=${enc([...new Set(languages.map((l) => l.toLowerCase()))].sort().join(","))}`),
  addSubtitle: (titleId, fileId) => call("POST", `/api/titles/${enc(titleId)}/subtitles`, { fileId }),
  // The order picked on each page that sorts, by the page's name (`sorts.js`).
  sorts: () => call("GET", "/api/me/sorts"),
  setSort: (surface, order) => call("PUT", `/api/me/sorts/${enc(surface)}`, order),

  // Films saved on a device: what a title can be saved as, saving it (which answers the package the
  // device fetches), every save the person has on any device, saying one holds every byte, and
  // forgetting one this device no longer holds.
  offlineOffer: (id) => call("GET", `/api/titles/${enc(id)}/offline`),
  saveOffline: (request) => call("POST", "/api/me/offline", request),
  offlineSaves: () => call("GET", "/api/me/offline"),
  completeSave: (id) => call("POST", `/api/me/offline/${enc(id)}/complete`),
  forgetSave: (id) => call("DELETE", `/api/me/offline/${enc(id)}`),
  // Download of what the library does not hold yet: fetched onto the server, then downloaded here
  // (`offline/wants.js`).
  wantOffline: (request) => call("POST", "/api/me/offline/wants", request),
  offlineEstimate: (imdbId, season = null, episodes = null) => call("GET", `/api/offline/estimate?imdbId=${enc(imdbId)}`
    + (season != null ? `&season=${season}&episodes=${episodes.join(",")}` : "")),
  offlineWants: () => call("GET", "/api/me/offline/wants"),
  unwantOffline: (id) => call("DELETE", `/api/me/offline/wants/${enc(id)}`),

  // What the signed-in person wants to watch, film or show, here or not: the one signal the server acts
  // on. Making a want answers what the checks found; removing one that is already gone is no failure.
  // Who else wants a title is read from `wanters`, which names only the people with a visible want.
  wants: () => call("GET", "/api/me/wants"),
  wantsPage: (params) => call("GET", withQuery("/api/me/wants/page", params)),
  want: (imdbId, kind, visible) => call("PUT", `/api/me/wants/${enc(imdbId)}`, visible === undefined ? { kind } : { kind, visible }),
  unwant: (imdbId) => call("DELETE", `/api/me/wants/${enc(imdbId)}`).catch((error) => {
    if (!(error instanceof ApiError) || error.status !== 404) throw error;
    return null;
  }),
  // The titles the person wants that others visibly want too, ready ones first, each with those others.
  together: () => call("GET", "/api/me/together"),
  wanters: (imdbId) => call("GET", `/api/titles/${enc(imdbId)}/wanters`),

  // Plans to watch something together: the person's own, one by its id (a 404 for anybody not in it), making
  // one, anybody in it inviting one more person (the reply's `delivery`, as for a room) or suggesting a
  // time, answering a time (`at` as the plan names it), the maker picking a time or moving the pick, and
  // the maker cancelling.
  plans: () => call("GET", "/api/me/plans"),
  plan: (id) => call("GET", `/api/plans/${enc(id)}`),
  makePlan: (request) => call("POST", "/api/plans", request),
  invitePlan: (id, userId) => call("POST", `/api/plans/${enc(id)}/people`, { userId }),
  suggestPlanTime: (id, at) => call("POST", `/api/plans/${enc(id)}/times`, { at }),
  answerPlan: (id, at, answer) => call("PUT", `/api/plans/${enc(id)}/answers/${enc(at)}`, { answer }),
  fixPlan: (id, at) => call("POST", `/api/plans/${enc(id)}/fix`, { at }),
  cancelPlan: (id) => call("DELETE", `/api/plans/${enc(id)}`),

  // Shelving a film or a show as anime (`anime`) or with its kind (`general`), against the API's rule.

  // The television screens this session is paired with, pairing with the one whose code it scanned,
  // and unpairing. The session is the token's: a pairing is this browser's, or this installed app's.
  screens: () => call("GET", "/api/me/screens"),
  pairScreen: (code) => call("POST", "/api/me/screens", { code }),
  unpairScreen: (id) => call("DELETE", `/api/me/screens/${enc(id)}`),

  // The shows this person follows, and whether each is fetched when a new episode airs.
  follows: () => call("GET", "/api/me/follows"),
  follow: (imdbId, autoDownload) => call("PUT", `/api/me/follows/${enc(imdbId)}`, { autoDownload }),
  unfollow: (imdbId) => call("DELETE", `/api/me/follows/${enc(imdbId)}`),

  // A call under /api/me, for the notification routes the design system's client makes.
  request: (method, path, body) => call(method, "/api/me" + path, body),

  // Who can be asked into a room, and asking them.
  people: (q = null) => call("GET", withQuery("/api/people", { q })),
  invite: (room, userId) => call("POST", `/api/rooms/${enc(room)}/invite`, { userId }),

  anticipated: () => call("GET", "/api/anticipated"),
};

/**
 * Where a title's poster is, always on this server. `/media/<id>/poster.jpg` is the one poster the
 * API shows the title by everywhere, the same one its search result and its page show; the
 * description's copy covers a title the listing names none for. The page never reaches the database
 * or the title index itself. A listed title carries its poster's version, and asking with it lets the
 * browser keep the poster rather than check it on every page.
 */
export function posterUrl(title) {
  if (title && title.poster) {
    const version = title.posterVersion ? `?v=${enc(title.posterVersion)}` : "";
    return `/media/${enc(title.id)}/${enc(title.poster)}${version}`;
  }
  return (title && title.details && title.details.posterUrl) || null;
}

/**
 * What to call a title: a film by the catalogue's name and year where there is one, an episode by its
 * show's name and its place in it.
 */
export function filmName(title) {
  const film = title && title.film;
  if (title && title.episode && film && film.name) return `${film.name} · ${episodeCode(title.episode)}`;
  if (film && film.name) return film.year ? `${film.name} (${film.year})` : film.name;
  return (title && title.title) || "";
}

/** An episode's place as the design writes it: "S2 · E4". */
export function episodeCode(episode) {
  return episode ? `S${episode.season} · E${episode.number}` : "";
}
