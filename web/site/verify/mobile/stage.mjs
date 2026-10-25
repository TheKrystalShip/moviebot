// What the scratch library holds before anything is looked at, put there the way a person would put
// it: through the API and the room hub, never by writing a file. The person looking is a member of the
// server, signed in on the site; two others are watching films in rooms of their own.
//
//   - the viewer is partway through three films
//   - Ana is playing a film
//   - Ben has a film paused
//
// The rooms stay open, with their people in them, until `close()` is called.

import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const signalR = require("@microsoft/signalr");

const wait = (ms) => new Promise((r) => setTimeout(r, ms));

export const VIEWER = { id: "verify-viewer", name: "Phone Check" };
/** People a view may act as instead of the viewer, by the name views.mjs gives them. */
export const PEOPLE = { viewer: VIEWER, player: { id: "verify-player", name: "Player Check" }, empty: { id: "verify-empty", name: "Empty Check" } };
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
 * Waits for the catalogue to list every title of the library: until then a title has no card, so no
 * rating and no backdrop, which is not what a phone will show.
 */
async function described(api, token, timeoutMs = 90000) {
  const until = Date.now() + timeoutMs;
  for (;;) {
    const titles = await call(api, token, "GET", "/api/titles");
    if (titles.every((t) => t.card) || Date.now() > until) return titles;
    await wait(2000);
  }
}

/**
 * Three televisions of the viewer's, each a hub connection signed in through the television client: one
 * playing a film part way through, one on an episode, and one in no room. A phone is paired with some of
 * them by `pair`, which asks each television for the code it would show and sends it as the phone's
 * session, and let go of with `unpair`.
 */
export async function televisions(api, issuer, film, episode) {
  const make = async (name, sid) => connect(api, issuer.mint(VIEWER, { client: "cinema-tv", sid, device: name }));
  const tvs = {
    living: { name: "Living room TV", sid: "verify-tv-living", hub: null },
    kitchen: { name: "Kitchen TV", sid: "verify-tv-kitchen", hub: null },
    bedroom: { name: "Bedroom TV", sid: "verify-tv-bedroom", hub: null },
  };
  for (const tv of Object.values(tvs)) tv.hub = await make(tv.name, tv.sid);
  await tvs.living.hub.invoke("Join", "verify-tv-living-room");
  await tvs.living.hub.invoke("LoadTitle", film.id, Math.floor(film.durationSeconds * 0.3));
  await tvs.living.hub.invoke("Play", Math.floor(film.durationSeconds * 0.3));
  await wait(800);
  await tvs.living.hub.invoke("Pause", Math.floor(film.durationSeconds * 0.3) + 1);
  if (episode) {
    await tvs.kitchen.hub.invoke("Join", "verify-tv-kitchen-room");
    await tvs.kitchen.hub.invoke("LoadTitle", episode.id, 120);
    await tvs.kitchen.hub.invoke("Play", 120);
  }
  return {
    tvs,
    /** Pairs the phone session `sid` with the named televisions. */
    async pair(sid, names) {
      for (const name of names) {
        const issued = await tvs[name].hub.invoke("PairingCode");
        await call(api, issuer.mint(VIEWER, { sid }), "POST", "/api/me/screens", { code: issued.path.slice("/p/".length) });
      }
    },
    /** Ends the phone session's pairings with the named televisions. */
    async unpair(sid, names) {
      for (const name of names) await call(api, issuer.mint(VIEWER, { sid }), "DELETE", `/api/me/screens/${tvs[name].sid}`).catch(() => {});
    },
    async close() { await Promise.all(Object.values(tvs).map((tv) => tv.hub.stop())); },
  };
}

export async function stage(api, issuer, { tmdb = true } = {}) {
  const token = memberToken(issuer, VIEWER);
  const titles = tmdb ? await described(api, token) : await call(api, token, "GET", "/api/titles");
  const ready = titles.filter((t) => t.status === "ready");
  if (ready.length < 5) throw new Error(`Staging needs five ready films; the library has ${ready.length}.`);

  // The viewer's progress: partway through, then out of the room, which is what records it.
  for (const [title, fraction] of [[ready[0], 0.6], [ready[1], 0.2], [ready[2], 0.35]]) {
    const hub = await sitIn(api, issuer, VIEWER, `verify-progress-${title.id}`, title.id, Math.floor(title.durationSeconds * fraction), false);
    await hub.stop();
  }

  const rooms = [
    await sitIn(api, issuer, OTHERS[0], "verify-ana", ready[3].id, 1800, true),
    await sitIn(api, issuer, OTHERS[1], "verify-ben", ready[4].id, 900, false),
  ];

  // Wants: three of the viewer's, the second kept to the viewer and the others visible, and Ana and Ben
  // wanting some of the same, so a card counts others and a title page shows faces. Joining a room is
  // what taught the API everybody's name. Wanting asks the film database about a title, so it needs the
  // token that database is read with.
  const named = ready.filter((t) => t.film && t.film.imdbId);
  if (tmdb) {
    const want = (who, title, visible) => call(api, who, "PUT", `/api/me/wants/${encodeURIComponent(title.film.imdbId)}`, { kind: "film", visible });
    const ana = memberToken(issuer, OTHERS[0]);
    const ben = memberToken(issuer, OTHERS[1]);
    for (const [i, title] of named.slice(0, 4).entries()) await want(token, title, i !== 1);
    for (const title of named.slice(0, 3)) await want(ana, title, true);
    await want(ben, named[0], true);
    await want(ben, named[1], false);
  }

  const episode = ready.find((t) => t.episode);
  const screens = await televisions(api, issuer, ready[5], episode);
  return {
    token,
    film: ready[0],
    /** The film the living room television holds, which no other view has played. */
    onTv: ready[5],
    other: ready[7],
    episode,
    screens,
    async close() { await screens.close(); await Promise.all(rooms.map((hub) => hub.stop())); },
  };
}
