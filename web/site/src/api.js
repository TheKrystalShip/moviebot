// MovieBot.Api, as the site reaches it: same origin, the session's access token as the bearer.
//
// This is the one module that attaches the token. A 401 means the token has lapsed or was never
// good: the session is renewed once and the call made again, and a second 401 is a session that has
// ended, which the page answers by saying so and offering the way back rather than showing a library
// that failed to load. A 403 is the person's account not letting them do what they asked.
//
// Every call tells `connectivity.js` whether the server answered: a call that fails on the network,
// or is answered by a gateway or a captive portal rather than by MovieBot, is the page going offline,
// and anything the server answers, an error among it, is the page online.

import { answeredByServer, reached, unreached } from "./connectivity.js";
import { keepPreferences } from "./offline/preferences.js";
import { keepProgress } from "./offline/progress.js";
import { forget, freshToken, sessionStore } from "./session.js";

export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

const UNANSWERED = "MovieBot is not answering. Try again in a moment.";

async function send(method, path, body, token) {
  const headers = { Accept: "application/json" };
  if (token) headers.Authorization = "Bearer " + token;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  let response;
  try {
    response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
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
  let response = await send(method, path, body, await freshToken());
  if (response.status === 401) response = await send(method, path, body, await freshToken({ refused: true }));

  if (response.status === 401) {
    await forget();
    throw new ApiError(401, "Sign in to carry on.");
  }
  if (response.status === 204) return null;

  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new ApiError(response.status, (payload && payload.error) || `MovieBot answered ${response.status}.`);
  return payload;
}

const enc = encodeURIComponent;

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
  cataloguePerson: (personId) => call("GET", `/api/catalogue/people/${enc(personId)}`),
  // Films or shows found by what they are like: kind, genre, years, runtime, rating, language and order.
  catalogueDiscover: (params) => call("GET", "/api/catalogue/discover?" + new URLSearchParams(params)),
  // Discover's tabs and the shelves on each for the signed-in person, with what its filter picks from;
  // and one shelf's titles.
  discover: () => call("GET", "/api/discover"),
  discoverShelf: (id) => call("GET", `/api/discover/shelves/${enc(id)}`),
  catalogueGenre: (kind, genre, category) => call("GET", `/api/catalogue/genre?kind=${enc(kind)}&name=${enc(genre)}`
    + (category ? `&category=${enc(category)}` : "")),

  // Shows: the library's, one by its IMDb id (in the library or not), and what the tracker offers of one.
  shows: () => call("GET", "/api/shows"),
  show: (imdbId) => call("GET", `/api/shows/${enc(imdbId)}`),
  showReleases: (imdbId) => call("GET", `/api/shows/${enc(imdbId)}/releases`),
  rooms: () => call("GET", "/api/sessions"),
  // One room as it stands, which lets the person in when it admits them; a 403 is a room private to them.
  room: (id) => call("GET", `/api/sessions/${enc(id)}`),

  // The catalogue's matches for a name, films and shows together or films alone, each with its
  // poster and cast; and whether a film can be watched and how.
  findTitles: (q) => call("GET", `/api/titles/find?q=${enc(q)}`),
  findFilms: (q) => call("GET", `/api/films/find?q=${enc(q)}`),
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
  forgetHistory: (id) => call("DELETE", `/api/me/history/${enc(id)}`),
  // Each answer is kept on the device for the account, which is what offline pages and the player
  // read with no connection (`offline/preferences.js`).
  preferences: () => call("GET", "/api/me/preferences").then(kept),
  setPreferences: (preferences) => call("PUT", "/api/me/preferences", preferences).then(kept),
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
  unwant: (id) => call("DELETE", `/api/me/offline/wants/${enc(id)}`),

  // Lists of films and shows to watch later. `later` names the signed-in person's own Watch later;
  // asked about a title, each list says whether it holds it.
  lists: (holding) => call("GET", "/api/me/lists" + (holding ? `?holding=${enc(holding)}` : "")),
  saved: () => call("GET", "/api/me/saved"),
  list: (id) => call("GET", `/api/lists/${enc(id)}`),
  createList: (name) => call("POST", "/api/lists", { name }),
  renameList: (id, name) => call("PATCH", `/api/lists/${enc(id)}`, { name }),
  deleteList: (id) => call("DELETE", `/api/lists/${enc(id)}`),
  saveToList: (id, imdbId, kind, note) => call("PUT", `/api/lists/${enc(id)}/entries/${enc(imdbId)}`, { kind, note }),
  removeFromList: (id, imdbId) => call("DELETE", `/api/lists/${enc(id)}/entries/${enc(imdbId)}`),
  setListMember: (id, userId, role) => call("PUT", `/api/lists/${enc(id)}/members/${enc(userId)}`, { role }),
  removeListMember: (id, userId) => call("DELETE", `/api/lists/${enc(id)}/members/${enc(userId)}`),

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

  // This person's notifications, the pushes their browsers take, and which kinds they are pushed.
  notifications: () => call("GET", "/api/me/notifications"),
  markRead: (ids) => call("POST", "/api/me/notifications/read", ids ? { ids } : {}),
  notificationPreferences: () => call("GET", "/api/me/notifications/preferences"),
  setNotificationPreferences: (preferences) => call("PUT", "/api/me/notifications/preferences", preferences),
  pushKey: () => call("GET", "/api/push/key"),
  pushStatus: (endpoint) => call("GET", "/api/me/push" + (endpoint ? `?endpoint=${enc(endpoint)}` : "")),
  subscribePush: (subscription) => call("POST", "/api/me/push", subscription),
  unsubscribePush: (endpoint) => call("DELETE", `/api/me/push?endpoint=${enc(endpoint)}`),

  // Who can be asked into a room, and asking them.
  people: () => call("GET", "/api/people"),
  invite: (room, userId) => call("POST", `/api/rooms/${enc(room)}/invite`, { userId }),

  wishes: () => call("GET", "/api/wishes"),
  anticipated: () => call("GET", "/api/anticipated"),
  wish: (chosen) => call("POST", "/api/wishes", { chosen }),
  cancelWish: (imdbId) => call("DELETE", `/api/wishes/${enc(imdbId)}`),
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
