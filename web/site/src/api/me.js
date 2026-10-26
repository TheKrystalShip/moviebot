// The signed-in person's own records: where they got to and what they watched, how they watch, what
// they want, the plans they are in, the shows they follow, what they have of a show, their saves on a
// device and their paired screens. Every route answers for whoever the token names.

import { keepPreferences } from "../offline/preferences.js";
import { keepProgress } from "../offline/progress.js";
import { sessionStore } from "../session.js";
import { ApiError, call, enc, withQuery } from "./http.js";

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

export const me = {
  // Where they got to, kept on the device for the account, which Home reads with no connection
  // (`offline/progress.js`); and where films played from the device got to, sent once there is one.
  progress: () => call("GET", "/api/me/progress").then(keptPosition),
  sendProgress: (entries) => call("POST", "/api/me/progress", entries),
  history: () => call("GET", "/api/me/history"),
  historyPage: (params) => call("GET", withQuery("/api/me/history/page", params)),
  forgetHistory: (id) => call("DELETE", `/api/me/history/${enc(id)}`),
  // What they say they watched, here or anywhere, or take back: a film, or a show, a season of it or
  // an episode of it, by IMDb id (`{ imdbId, kind, season?, episode?, watched }`).
  setWatched: (change) => call("POST", "/api/me/watched", change),
  // Each answer is kept on the device for the account, which offline pages and the player read with
  // no connection (`offline/preferences.js`).
  preferences: () => call("GET", "/api/me/preferences").then(kept),
  setPreferences: (preferences) => call("PUT", "/api/me/preferences", preferences).then(kept),
  // The subtitle and audio track they watch a film with, the same on every device.
  tracks: (titleId) => call("GET", `/api/me/tracks/${enc(titleId)}`),
  setTracks: (titleId, choice) => call("PUT", `/api/me/tracks/${enc(titleId)}`, choice),
  // The order picked on each page that sorts, by the page's name (`sorts.js`).
  sorts: () => call("GET", "/api/me/sorts"),
  setSort: (surface, order) => call("PUT", `/api/me/sorts/${enc(surface)}`, order),

  // What they have of a show: its episodes as the releases number them, which the library holds and how
  // far they got, where they pick up, their follow and the episode expected next.
  show: (imdbId) => call("GET", `/api/me/shows/${enc(imdbId)}`),

  // Saves on a device: saving a title (which answers the package the device fetches), every save on
  // any device, saying one holds every byte, and forgetting one; and a title not here yet, fetched onto
  // the server and then downloaded here (`offline/wants.js`).
  saveOffline: (request) => call("POST", "/api/me/offline", request),
  offlineSaves: () => call("GET", "/api/me/offline"),
  completeSave: (id) => call("POST", `/api/me/offline/${enc(id)}/complete`),
  forgetSave: (id) => call("DELETE", `/api/me/offline/${enc(id)}`),
  wantOffline: (request) => call("POST", "/api/me/offline/wants", request),
  offlineWants: () => call("GET", "/api/me/offline/wants"),
  unwantOffline: (id) => call("DELETE", `/api/me/offline/wants/${enc(id)}`),

  // What they want to watch, film or show, here or not: the one signal the server acts on. Making a
  // want answers what the checks found; removing one already gone is no failure. Who else wants a
  // title is `wanters`, which names only the people with a visible want.
  wants: () => call("GET", "/api/me/wants"),
  wantsPage: (params) => call("GET", withQuery("/api/me/wants/page", params)),
  want: (imdbId, kind, visible) => call("PUT", `/api/me/wants/${enc(imdbId)}`, visible === undefined ? { kind } : { kind, visible }),
  unwant: (imdbId) => call("DELETE", `/api/me/wants/${enc(imdbId)}`).catch((error) => {
    if (!(error instanceof ApiError) || error.status !== 404) throw error;
    return null;
  }),
  // The titles they want that others visibly want too, ready ones first, each with those others.
  together: () => call("GET", "/api/me/together"),
  wanters: (imdbId) => call("GET", `/api/titles/${enc(imdbId)}/wanters`),

  // Plans to watch something together: their own, one by its id (a 404 for anybody not in it), making
  // one, anybody in it inviting one more person or suggesting a time, answering a time (`at` as the
  // plan names it), the maker picking or moving the time, and the maker cancelling.
  plans: () => call("GET", "/api/me/plans"),
  plan: (id) => call("GET", `/api/plans/${enc(id)}`),
  makePlan: (request) => call("POST", "/api/plans", request),
  invitePlan: (id, userId) => call("POST", `/api/plans/${enc(id)}/people`, { userId }),
  suggestPlanTime: (id, at) => call("POST", `/api/plans/${enc(id)}/times`, { at }),
  answerPlan: (id, at, answer) => call("PUT", `/api/plans/${enc(id)}/answers/${enc(at)}`, { answer }),
  fixPlan: (id, at) => call("POST", `/api/plans/${enc(id)}/fix`, { at }),
  cancelPlan: (id) => call("DELETE", `/api/plans/${enc(id)}`),

  // The television screens this session is paired with, pairing with the one whose code it scanned,
  // and unpairing. A pairing is this browser's, or this installed app's.
  screens: () => call("GET", "/api/me/screens"),
  pairScreen: (code) => call("POST", "/api/me/screens", { code }),
  unpairScreen: (id) => call("DELETE", `/api/me/screens/${enc(id)}`),

  // The shows they follow, and whether each is fetched when a new episode airs.
  follows: () => call("GET", "/api/me/follows"),
  follow: (imdbId, autoDownload) => call("PUT", `/api/me/follows/${enc(imdbId)}`, { autoDownload }),
  unfollow: (imdbId) => call("DELETE", `/api/me/follows/${enc(imdbId)}`),

  // A call under /api/me, for the notification routes the design system's client makes.
  request: (method, path, body) => call(method, "/api/me" + path, body),
};
