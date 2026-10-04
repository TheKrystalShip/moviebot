// MovieBot.Api, as the site reaches it: same origin, the session's token as the bearer.
//
// This is the one module that attaches the token. A 401 means the token has lapsed or was never
// good, so the browser is signed out and the page asks for a sign-in rather than showing a
// library that failed to load.

import { currentToken, signOut } from "./session.js";

export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

async function call(method, path, body) {
  const token = currentToken();
  const headers = { Accept: "application/json" };
  if (token) headers.Authorization = "Bearer " + token;
  if (body !== undefined) headers["Content-Type"] = "application/json";

  let response;
  try {
    response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    throw new ApiError(0, "MovieBot is not answering. Try again in a moment.");
  }

  if (response.status === 401) {
    signOut();
    throw new ApiError(401, "Sign in to carry on.");
  }
  if (response.status === 204) return null;

  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new ApiError(response.status, (payload && payload.error) || `MovieBot answered ${response.status}.`);
  return payload;
}

const enc = encodeURIComponent;

export const api = {
  titles: () => call("GET", "/api/titles"),
  title: (id) => call("GET", `/api/titles/${enc(id)}`),
  film: (imdbId) => call("GET", `/api/films/${enc(imdbId)}`),
  // The films and shows the film database counts as trending this week, whether here or not.
  popular: () => call("GET", "/api/popular"),

  // Shows: the library's, one by its IMDb id (in the library or not), and the episodes either side
  // of one, which the player moves the room to.
  shows: () => call("GET", "/api/shows"),
  show: (imdbId) => call("GET", `/api/shows/${enc(imdbId)}`),
  findShows: (q) => call("GET", `/api/shows/find?q=${enc(q)}`),
  showReleases: (imdbId) => call("GET", `/api/shows/${enc(imdbId)}/releases`),
  neighbours: (id) => call("GET", `/api/titles/${enc(id)}/neighbours`),
  rooms: () => call("GET", "/api/sessions"),

  // A film by its IMDb id: the catalogue's matches for a name, and whether one can be watched and how.
  findFilms: (q) => call("GET", `/api/films/find?q=${enc(q)}`),
  availability: (imdbId) => call("GET", `/api/films/${enc(imdbId)}/availability`),
  // Watch now: fetch it and play it in a room the moment it can be opened. Answers with the room at
  // once; a 409 is every slot for watching while it arrives in use, and carries them.
  watchNow: (request) => call("POST", "/api/watch", request).catch((error) => {
    if (error.status === 409) return { outcome: "full" };
    throw error;
  }),
  downloads: () => call("GET", "/api/downloads"),

  // How long a settled film stays. A keep is the signed-in person's own: the token names who.
  stays: () => call("GET", "/api/stays"),
  stay: (id) => call("GET", `/api/titles/${enc(id)}/stay`),
  keep: (id) => call("PUT", `/api/titles/${enc(id)}/keep`),
  unkeep: (id) => call("DELETE", `/api/titles/${enc(id)}/keep`),

  // What the signed-in person has watched, and how they watch.
  progress: () => call("GET", "/api/me/progress"),
  history: () => call("GET", "/api/me/history"),
  forgetHistory: (id) => call("DELETE", `/api/me/history/${enc(id)}`),
  preferences: () => call("GET", "/api/me/preferences"),
  setPreferences: (preferences) => call("PUT", "/api/me/preferences", preferences),

  // Lists of films and shows to watch later. `later` names the signed-in person's own Watch later;
  // asked about a title, each list says whether it holds it.
  lists: (holding) => call("GET", "/api/me/lists" + (holding ? `?holding=${enc(holding)}` : "")),
  list: (id) => call("GET", `/api/lists/${enc(id)}`),
  createList: (name) => call("POST", "/api/lists", { name }),
  renameList: (id, name) => call("PATCH", `/api/lists/${enc(id)}`, { name }),
  deleteList: (id) => call("DELETE", `/api/lists/${enc(id)}`),
  saveToList: (id, imdbId, kind, note) => call("PUT", `/api/lists/${enc(id)}/entries/${enc(imdbId)}`, { kind, note }),
  removeFromList: (id, imdbId) => call("DELETE", `/api/lists/${enc(id)}/entries/${enc(imdbId)}`),
  setListMember: (id, userId, role) => call("PUT", `/api/lists/${enc(id)}/members/${enc(userId)}`, { role }),
  removeListMember: (id, userId) => call("DELETE", `/api/lists/${enc(id)}/members/${enc(userId)}`),

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

  // The devices that hold one of this person's tokens.
  sessions: () => call("GET", "/api/me/sessions"),
  signOutDevice: (id) => call("DELETE", `/api/me/sessions/${enc(id)}`),
  signOutOtherDevices: () => call("DELETE", "/api/me/sessions"),

  releases: (q) => call("GET", `/api/releases?q=${enc(q)}`),
  releasesFor: (imdb) => call("GET", `/api/releases?imdb=${enc(imdb)}`),
  // `episodes` ([{season, episode}]) fetches only those out of a show's release.
  download: (torrentId, episodes) =>
    call("POST", "/api/downloads", { torrentId, requestedBy: { userId: "", displayName: "" }, ...(episodes ? { episodes } : {}) }),
  wishes: () => call("GET", "/api/wishes"),
  wish: (chosen) => call("POST", "/api/wishes", { chosen, requestedBy: { userId: "", displayName: "" } }),
  cancelWish: (imdbId) => call("DELETE", `/api/wishes/${enc(imdbId)}`),
};

/**
 * Where a film's poster is, always on this server. The poster beside the film comes first; the copy
 * the API keeps from the film database is the fallback for a film the ingest found no artwork for.
 * The page never reaches the database or the title index itself.
 */
export function posterUrl(title) {
  if (title && title.poster) return `/media/${enc(title.id)}/${enc(title.poster)}`;
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
