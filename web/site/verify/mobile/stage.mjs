// What the scratch library holds before anything is looked at, put there the way a person would put
// it: through the API and the room hub, never by writing a file. The person looking is a member of the
// server, signed in on the site; two others are watching films in rooms of their own.
//
//   - the viewer is partway through three films, one of them on the library's clock (so its clock
//     restarts and it has its full stay)
//   - the viewer keeps one film on the clock
//   - Ana is playing a film on the clock, so a room is watching something about to leave
//   - Ben has a film paused
//
// The rooms stay open, with their people in them, until `close()` is called.

import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const signalR = require("@microsoft/signalr");

const wait = (ms) => new Promise((r) => setTimeout(r, ms));

export const VIEWER = { id: "verify-viewer", name: "Phone Check" };
/** People a view may act as instead of the viewer, by the name views.mjs gives them. */
export const PEOPLE = { viewer: VIEWER, player: { id: "verify-player", name: "Player Check" } };
const OTHERS = [{ id: "verify-ana", name: "Ana" }, { id: "verify-ben", name: "Ben" }];

/** A person's access token, minted by the scratch site's identity provider: what signing in on the site holds. */
export function memberToken(issuer, person, hours = 6) {
  return issuer.mint(person, { hours });
}

async function call(api, token, method, path, body) {
  const headers = { Authorization: `Bearer ${token}` };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const response = await fetch(api + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

async function connect(api, token) {
  const hub = new signalR.HubConnectionBuilder()
    .withUrl(api + "/hub/session", { accessTokenFactory: () => token })
    .configureLogging(signalR.LogLevel.Error)
    .build();
  await hub.start();
  return hub;
}

/** A person in a room watching a film from `at`, playing or paused. */
async function sitIn(api, issuer, person, room, titleId, at, playing) {
  const hub = await connect(api, memberToken(issuer, person));
  await hub.invoke("Join", room);
  await hub.invoke("LoadTitle", titleId, at);
  await hub.invoke("Play", at);
  await wait(1200);
  if (!playing) await hub.invoke("Pause", at + 1);
  return hub;
}

/**
 * Waits for the film database to have described the library, when the scratch API has a token for
 * it: until then a card has no rating and a page no backdrop, which is not what a phone will show.
 */
async function described(api, token, timeoutMs = 90000) {
  const until = Date.now() + timeoutMs;
  for (;;) {
    const titles = await call(api, token, "GET", "/api/titles");
    if (titles.every((t) => t.details) || Date.now() > until) return titles;
    await wait(2000);
  }
}

export async function stage(api, issuer, { tmdb = true } = {}) {
  const token = memberToken(issuer, VIEWER);
  const titles = tmdb ? await described(api, token) : await call(api, token, "GET", "/api/titles");
  const stays = await call(api, token, "GET", "/api/stays");
  const ready = titles.filter((t) => t.status === "ready");
  const settled = ready.filter((t) => stays.some((s) => s.titleId === t.id && s.settled));
  const fresh = ready.filter((t) => !settled.includes(t));
  if (settled.length < 3 || fresh.length < 3) {
    throw new Error(`Staging needs three films on the clock and three off it; the library has ${settled.length} and ${fresh.length}.`);
  }

  // The viewer's progress: partway through, then out of the room, which is what records it.
  for (const [title, fraction] of [[settled[0], 0.35], [fresh[0], 0.6], [fresh[1], 0.2]]) {
    const hub = await sitIn(api, issuer, VIEWER, `verify-progress-${title.id}`, title.id, Math.floor(title.durationSeconds * fraction), false);
    await hub.stop();
  }

  await call(api, token, "PUT", `/api/titles/${encodeURIComponent(settled[1].id)}/keep`);

  const rooms = [
    await sitIn(api, issuer, OTHERS[0], "verify-ana", settled[2].id, 1800, true),
    await sitIn(api, issuer, OTHERS[1], "verify-ben", fresh[2].id, 900, false),
  ];

  // Lists: the viewer's Watch later with a note on one, a list of the viewer's shared with Ben, and
  // one of Ana's the viewer may add to. Joining a room is what taught the API everybody's name.
  const save = (who, list, title, note) => call(api, who, "PUT",
    `/api/lists/${encodeURIComponent(list)}/entries/${encodeURIComponent(title.film.imdbId)}`, { kind: "film", note });
  const named = ready.filter((t) => t.film && t.film.imdbId);
  for (const [i, title] of named.slice(0, 4).entries()) await save(token, "later", title, i === 1 ? "The one Ana keeps quoting" : undefined);
  const mine = await call(api, token, "POST", "/api/lists", { name: "Rainy Sunday" });
  for (const title of named.slice(2, 4)) await save(token, mine.list.id, title);
  await call(api, token, "PUT", `/api/lists/${mine.list.id}/members/${OTHERS[1].id}`, { role: "viewer" });
  const ana = memberToken(issuer, OTHERS[0]);
  const theirs = await call(api, ana, "POST", "/api/lists", { name: "Friday night with the group" });
  for (const title of named.slice(4, 7)) await save(ana, theirs.list.id, title);
  await call(api, ana, "PUT", `/api/lists/${theirs.list.id}/members/${VIEWER.id}`, { role: "editor" });
  await save(token, theirs.list.id, named[0]);

  return {
    token,
    list: theirs.list.id,
    film: fresh[0],
    leaving: settled[2],
    kept: settled[1],
    async close() { await Promise.all(rooms.map((hub) => hub.stop())); },
  };
}
