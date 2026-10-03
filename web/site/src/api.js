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
  retention: (id) => call("GET", `/api/titles/${enc(id)}/retention`),
  rooms: () => call("GET", "/api/sessions"),
  downloads: () => call("GET", "/api/downloads"),
  keeps: () => call("GET", "/api/keeps"),
  keep: (hash) => call("PUT", `/api/keeps/${enc(hash)}`, { requestedBy: { userId: "", displayName: "" } }),
  release: (hash) => call("POST", `/api/keeps/${enc(hash)}/release`, { requestedBy: { userId: "", displayName: "" } }),
  releases: (q) => call("GET", `/api/releases?q=${enc(q)}`),
  releasesFor: (imdb) => call("GET", `/api/releases?imdb=${enc(imdb)}`),
  download: (torrentId) => call("POST", "/api/downloads", { torrentId, requestedBy: { userId: "", displayName: "" } }),
  wishes: () => call("GET", "/api/wishes"),
  wish: (chosen) => call("POST", "/api/wishes", { chosen, requestedBy: { userId: "", displayName: "" } }),
  cancelWish: (imdbId) => call("DELETE", `/api/wishes/${enc(imdbId)}`),
};

/**
 * Where a film's poster is. The poster beside the film is served to anyone, because Discord's own
 * servers fetch it for a viewer's presence; the catalogue's is the fallback for a film the ingest
 * found no artwork for.
 */
export function posterUrl(title) {
  if (title && title.poster) return `/media/${enc(title.id)}/${enc(title.poster)}`;
  return (title && title.film && title.film.posterUrl) || null;
}

/** What to call a film: the catalogue's name and year where there is one. */
export function filmName(title) {
  const film = title && title.film;
  if (film && film.name) return film.year ? `${film.name} (${film.year})` : film.name;
  return (title && title.title) || "";
}

/** A length or a position the way a person reads one. */
export function clock(seconds) {
  const s = Math.max(0, Math.round(seconds || 0));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h > 0 ? `${h}h ${String(m).padStart(2, "0")}m` : `${m}m`;
}
