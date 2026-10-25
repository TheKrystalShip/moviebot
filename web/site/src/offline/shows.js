// The shows the saved episodes belong to, kept on the device so a show's page draws in full with no
// connection: its catalogue page, what the person has of it (its seasons, and every episode's name,
// number and still), and their artwork. A show is
// kept when one of its episodes starts saving, written again whenever its page is read from the API
// while it is kept, and goes with the last of its episodes (`removeSave` in `store.js`).

import { catalogue, meApi } from "../api/index.js";
import { listSaves, readShow, savesChanged, writeShow } from "./store.js";

/** Writes of one show run one after another, so two episodes started together write it once each in turn. */
const writing = new Map();

/** The artwork a show's answers name: its poster, its backdrop and each episode's still, by file name. */
function artworkOf({ page, mine }) {
  const art = [];
  if (page.poster) art.push({ name: "poster", url: page.poster });
  if (page.backdrop) art.push({ name: "backdrop", url: page.backdrop });
  for (const season of mine.seasons || []) {
    for (const e of season.episodes || []) {
      if (e.still) art.push({ name: `s${e.season}e${e.number}`, url: e.still });
    }
  }
  return art;
}

/** A show's two answers, `{ page, mine }`: its catalogue page and what the person has of it. */
export async function showAnswers(imdbId) {
  const [page, mine] = await Promise.all([catalogue.title(imdbId), meApi.show(imdbId)]);
  return { page, mine };
}

/**
 * Keeps a show on the device for an account: `answered` is its answers (`showAnswers`) when the page
 * already holds them, else they are asked for. Artwork already kept from the same address is not
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
  const show = answered || await showAnswers(imdbId);
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

/** Writes a show again from the answers a page read (`{ page, mine }`), when the account keeps it. */
export async function refreshShow(sub, show) {
  const imdbId = show && show.page && show.page.id;
  if (sub && imdbId && show.mine && await readShow(sub, imdbId)) await keepShow(sub, imdbId, show);
}
