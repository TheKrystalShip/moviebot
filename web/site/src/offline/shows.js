// The shows the saved episodes belong to, kept on the device so a show's page draws in full with no
// connection: what the show is, its seasons, and every episode's name, number and still. A show is
// kept when one of its episodes starts saving, written again whenever its page is read from the API
// while it is kept, and goes with the last of its episodes (`removeSave` in `store.js`).

import { api } from "../api.js";
import { listSaves, readShow, savesChanged, writeShow } from "./store.js";

/** Writes of one show run one after another, so two episodes started together write it once each in turn. */
const writing = new Map();

/** The artwork a show answer names: its poster, its backdrop and each episode's still, by file name. */
function artworkOf(show) {
  const art = [];
  if (show.posterUrl) art.push({ name: "poster", url: show.posterUrl });
  if (show.backdropUrl) art.push({ name: "backdrop", url: show.backdropUrl });
  for (const season of show.seasons || []) {
    for (const e of season.episodes || []) {
      if (e.stillUrl) art.push({ name: `s${e.season}e${e.number}`, url: e.stillUrl });
    }
  }
  return art;
}

/**
 * Keeps a show on the device for an account: `answered` is its `GET /api/shows/{imdbId}` answer when
 * the page already holds one, else it is asked for. Artwork already kept from the same address is not
 * fetched again; one that cannot be fetched is left out.
 */
export function keepShow(sub, imdbId, answered) {
  if (!sub || !imdbId) return Promise.resolve();
  const key = sub + "/" + imdbId;
  const next = (writing.get(key) || Promise.resolve())
    .then(() => write(sub, imdbId, answered))
    .catch(() => {})
    .finally(() => { if (writing.get(key) === next) writing.delete(key); });
  writing.set(key, next);
  return next;
}

async function write(sub, imdbId, answered) {
  const show = answered || await api.show(imdbId);
  const before = await readShow(sub, imdbId);
  const had = new Map((before ? before.files : []).map((f) => [f.name, f]));
  const files = [];
  const artwork = new Map();
  for (const { name, url } of artworkOf(show)) {
    const old = had.get(name);
    if (old && old.url === url) {
      files.push(old);
      continue;
    }
    const response = await fetch(url).catch(() => null);
    if (!response || !response.ok) continue;
    const blob = await response.blob();
    files.push({ name, url, type: blob.type || response.headers.get("content-type") || "" });
    artwork.set(name, blob);
  }
  if (before && artwork.size === 0 && files.length === before.files.length && JSON.stringify(before.show) === JSON.stringify(show)) return;
  const kept = { imdbId, keptAt: new Date().toISOString(), show, files };
  if (await writeShow(sub, kept, artwork)) savesChanged();
}

/** Keeps every show an account has a saved episode of and no kept show for. */
export async function keepMissingShows(sub) {
  const shows = new Set((await listSaves(sub)).map((s) => s.meta.show && s.meta.show.imdbId).filter(Boolean));
  for (const imdbId of shows) {
    if (!(await readShow(sub, imdbId))) await keepShow(sub, imdbId);
  }
}

/** Writes a show again from an answer a page read, when the account keeps it. */
export async function refreshShow(sub, show) {
  if (sub && show && show.imdbId && await readShow(sub, show.imdbId)) await keepShow(sub, show.imdbId, show);
}
