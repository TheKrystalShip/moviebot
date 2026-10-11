/**
 * The film a television holds, as the phone's remote shows it: the title's manifest (its length, markers,
 * chapters and episode), the episode either side of it, and the sheet of preview frames its scrub bar
 * points at. It follows the room the remote is in (`remoteRoom.js`): when the room holds another title
 * this reads it again, and what it holds until it has is the previous film's, which `useRemoteFilm` never
 * hands out for a title the room no longer holds.
 *
 * The player reads the same things through its room (`player/engine/room.ts`); the remote has no player,
 * so it asks the API for them itself.
 */

import { createStore, useStore } from "@thekrystalship/krystal-ui";
import { api } from "./api.js";
import { currentRemoteRoom, subscribeRemoteRoom } from "./remoteRoom.js";
import { followFilm } from "./remoteSettings.js";
import { freshToken } from "./session.js";

const nothing = { titleId: null, sessionId: null, manifest: null, around: null, thumbnails: null };

const filmStore = createStore(nothing);

let reading = 0;
let sheetUrl = null;

const pad = (n) => String(n).padStart(2, "0");

/**
 * The picture that stands for what is playing: an episode's still, a film's backdrop. The API keeps them
 * beside the descriptions (`docs/api-contract.md`); a title with no description has none, and the list
 * names the next place to look.
 */
export function stillSources(manifest) {
  const imdbId = manifest && manifest.film && manifest.film.imdbId;
  if (!imdbId) return manifest && manifest.poster ? [`/media/${encodeURIComponent(manifest.id)}/${encodeURIComponent(manifest.poster)}`] : [];
  const id = encodeURIComponent(imdbId);
  return manifest.episode
    ? [`/shows/${id}/s${pad(manifest.episode.season)}e${pad(manifest.episode.number)}.jpg`, `/shows/${id}/backdrop.jpg`]
    : [`/films/${id}/backdrop.jpg`, `/films/${id}/poster.jpg`];
}

async function sheetOf(manifest) {
  if (!manifest.thumbnails) return null;
  try {
    const token = await freshToken();
    const response = await fetch(`/media/${encodeURIComponent(manifest.id)}/${manifest.thumbnails.uri}`, {
      headers: token ? { Authorization: "Bearer " + token } : {},
    });
    if (!response.ok) return null;
    return { strip: manifest.thumbnails, url: URL.createObjectURL(await response.blob()) };
  } catch {
    // A bar that previews nothing is worth more than one that previews a grey box.
    return null;
  }
}

function release() {
  if (sheetUrl) URL.revokeObjectURL(sheetUrl);
  sheetUrl = null;
}

async function read(titleId, sessionId) {
  const mine = ++reading;
  try {
    const [manifest, around] = await Promise.all([api.title(titleId), api.around(sessionId).catch(() => null)]);
    if (mine !== reading) return;
    filmStore.setState({ titleId, sessionId, manifest, around, thumbnails: null });
    const thumbnails = await sheetOf(manifest);
    if (mine !== reading) {
      if (thumbnails) URL.revokeObjectURL(thumbnails.url);
      return;
    }
    release();
    sheetUrl = thumbnails && thumbnails.url;
    filmStore.setState({ thumbnails });
  } catch {
    // The remote's controls that need the film stay as they were; the room's own state still drives play and pause.
  }
}

/**
 * Reads the held film's manifest again, for what it gained since: a subtitle track fetched from outside
 * lands beside the manifest, and the lists the remote offers come from here. Only the manifest is replaced.
 */
export async function refreshRemoteFilm() {
  const { titleId } = filmStore.getState();
  if (!titleId) return;
  const manifest = await api.title(titleId);
  if (filmStore.getState().titleId === titleId) filmStore.setState({ manifest });
}

let lastHeld = null;

function follow() {
  const { state } = currentRemoteRoom();
  const titleId = (state && state.titleId) || null;
  const held = titleId && `${titleId}@${state.sessionId}`;
  if (held === lastHeld) return;
  lastHeld = held;
  reading += 1;
  followFilm(titleId);
  if (!titleId) {
    release();
    filmStore.setState(nothing);
    return;
  }
  void read(titleId, state.sessionId);
}

subscribeRemoteRoom(follow);

/** What the room holds, once read, or null for the title it held before or none. */
export function currentFilm() {
  const { state } = currentRemoteRoom();
  const film = filmStore.getState();
  return state && state.titleId && film.titleId === state.titleId ? film : null;
}

/** The same, for a component: `{manifest, around, thumbnails}` for the title the room holds, else nulls. */
export function useRemoteFilm() {
  const film = useStore(filmStore, (f) => f);
  const state = useStore(roomOnly, (room) => room.state);
  return state && state.titleId && film.titleId === state.titleId ? film : nothing;
}

const roomOnly = {
  getState: currentRemoteRoom,
  subscribe: subscribeRemoteRoom,
};
