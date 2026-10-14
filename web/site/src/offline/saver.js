// Download's transfer, from the page: starting a save, the queue of saves under way, pausing,
// resuming and removing one, and what each is doing for the controls that draw it.
//
// The bytes are written by `saver.worker.js`; this side holds what the worker cannot: the session
// (each request's bearer token is asked for here, so a transfer that outlives a token renews it), the
// connection (a save that cannot reach the server waits for it to come back), the Wake Lock that keeps
// a phone's screen on while the page saves, and the person's pauses, kept per account in localStorage
// so a reload resumes every save but the paused ones. One save runs at a time, one track after
// another; the rest wait their turn. A save of a title the server is still making is written a
// stretch at a time: what has been made, then the package asked for again a little later, until the
// package says the title is whole; the other saves run in between.
//
// A save is the worker's to write only while it holds the title's lock, so two tabs never write one
// save: a tab that finds it held waits for the lock and takes the save over when the other tab lets go
// of it. Each tab says what its saves are doing on `cinema-offline-saving`, so every tab draws the
// same progress, and a pause or a removal in one stops the transfer in whichever runs it.

import { createStore, toast, useStore } from "@thekrystalship/krystal";
import { api } from "../api.js";
import { isOnline, onReconnect, probe, unreached } from "../connectivity.js";
import { freshToken, sessionStore } from "../session.js";
import { deviceName } from "./choose.js";
import { FORMAT } from "./layout.js";
import { keepShow } from "./shows.js";
import { forgotten, listSaves, readSave, removeAccount, removeSave, space } from "./store.js";

const PAUSED_KEY = "cinema.offline.paused.";
const CHANNEL = "cinema-offline-saving";
/** How long a save of a title still being made waits between one stretch of it and the next. */
const GROW_MS = 15000;

/** A save refused before a byte moved because the device has no room for it. */
export class NoRoom extends Error {
  constructor(need, free) {
    super("Not enough room on this device.");
    this.need = need;
    this.free = free;
  }
}

/**
 * What each save under way is doing, by title id: `{ state, received, bytes, message }`. `state` is
 * `queued`, `saving`, `paused`, `waiting` (for a connection), `elsewhere` (another tab writes it) or
 * `failed`. A save not listed is finished, or not under way in any tab.
 */
const saving = createStore({});

const lockName = (sub, titleId) => `cinema-offline-save:${sub}/${titleId}`;
const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel(CHANNEL) : null;

let worker = null;
/**
 * The saves waiting their turn: `{ sub, titleId, meta?, reposted? }`, a `meta` being a new package to
 * write, and `reposted` one asked for again after the first was refused.
 */
let queue = [];
/** The save the worker is writing: a job from the queue, and why it is being stopped (`ending`). */
let running = null;
let wakeLock = null;
/** The saves of titles still being made waiting to ask for their next stretch, by title id. */
const growing = new Map();

/** What one title's save is doing, or null. */
export function useSaving(titleId) {
  return useStore(saving, (s) => s[titleId] || null);
}

/** What every save under way is doing, by title id. */
export function useSavingAll() {
  return useStore(saving);
}

/** What every save under way is doing, as it stands now, by title id. */
export const savingNow = () => saving.getState();

/** Calls `fn` whenever what a save under way is doing changes; answers the call that stops it. */
export const onSaving = (fn) => saving.subscribe(fn);

/**
 * How much of a title the server has made, for a save of one it is still making: the shortest of its
 * tracks' seconds against the film's length. 1 for a title already whole.
 */
export function madeOf(meta) {
  if (!meta || !meta.growing || !meta.durationSeconds) return 1;
  const tracks = (meta.package && meta.package.tracks) || [];
  if (!tracks.length) return 0;
  const seconds = Math.min(...tracks.map((t) => (t.segmentSeconds || []).reduce((n, s) => n + s, 0)));
  return Math.max(0, Math.min(1, seconds / meta.durationSeconds));
}

/** How far a save has got, in whole percent: what is here of what there is, scaled by how much of it there is yet. */
export const percentOf = (meta, received, bytes) => (bytes > 0 ? Math.floor((100 * madeOf(meta) * Math.min(received, bytes)) / bytes) : 0);

/**
 * What a save under way that no tab is writing is doing: paused by the person, else waiting its turn,
 * or waiting for a connection when there is none.
 */
export function restingState(titleId, online) {
  const sub = currentSub();
  if (sub && pausedOf(sub).includes(titleId)) return "paused";
  return online ? "queued" : "waiting";
}

function set(titleId, entry, { tell = true } = {}) {
  saving.setState((s) => {
    const next = { ...s };
    if (entry) next[titleId] = { ...(s[titleId] || {}), ...entry };
    else delete next[titleId];
    return next;
  });
  const sub = currentSub();
  if (tell && channel && sub) channel.postMessage({ type: "entry", sub, titleId, entry: saving.getState()[titleId] || null });
}

const currentSub = () => {
  const session = sessionStore.getState();
  return session ? session.user.id : null;
};

function pausedOf(sub) {
  try {
    const ids = JSON.parse(localStorage.getItem(PAUSED_KEY + sub) || "[]");
    return Array.isArray(ids) ? ids : [];
  } catch {
    return [];
  }
}

function setPaused(sub, titleId, paused) {
  const ids = pausedOf(sub).filter((id) => id !== titleId);
  if (paused) ids.push(titleId);
  try {
    if (ids.length) localStorage.setItem(PAUSED_KEY + sub, JSON.stringify(ids));
    else localStorage.removeItem(PAUSED_KEY + sub);
  } catch {
    // Storage refused: the pause holds until the page is reloaded.
  }
}

function workerOf() {
  if (!worker) {
    worker = new Worker(new URL("./saver.worker.js", import.meta.url), { type: "module" });
    worker.onmessage = (event) => { void heard(event.data || {}); };
  }
  return worker;
}

/**
 * Saves a title onto this device: room is asked for and checked first, the package is asked for, and
 * the save joins the queue. `rung` is one of the offer's rungs, `audio` and `subtitles` its audio and
 * subtitle entries picked; with `subtitleLanguage` the save also takes every text subtitle in that
 * language the title carries whenever its package is asked for, and the server looks for one outside
 * the title when it has none. Throws `NoRoom` before anything is fetched when the device cannot hold it.
 */
export async function startSave({ titleId, rung, audio, subtitles, subtitleLanguage = null }) {
  const sub = currentSub();
  if (!sub) throw new Error("Sign in to download films.");
  // Asked first, while the press that asked for the save is still the page's to show a prompt for.
  const persisted = navigator.storage && navigator.storage.persist ? navigator.storage.persist().catch(() => false) : null;
  const need = rung.bytes + audio.reduce((sum, a) => sum + a.bytes, 0) + subtitles.reduce((sum, s) => sum + s.bytes, 0);
  const [room, held] = await Promise.all([space(), readSave(sub, titleId), persisted]);
  const have = held ? held.receivedBytes : 0;
  if (room && need - have > room.free) throw new NoRoom(need - have, room.free);

  const pkg = await api.saveOffline({
    titleId, rung: rung.rendition, audio: audio.map((a) => a.rendition), subtitles: subtitles.map((s) => s.id), device: deviceName(),
    subtitleLanguage,
  });
  setPaused(sub, titleId, false);
  const meta = metaOf(sub, pkg);
  enqueue({ sub, titleId, meta });
  // An episode's show is kept beside it, so its page draws whole with no connection (`shows.js`).
  if (meta.show) void keepShow(sub, meta.show.imdbId);
}

/** Asks for the package of a save again, as it was chosen, and writes it as a new one. */
async function repost(job) {
  const save = job.meta ? job.meta.package.save : (await readSave(job.sub, job.titleId))?.meta.package.save;
  if (!save) throw new Error("This download is not on the device.");
  const pkg = await api.saveOffline({
    titleId: job.titleId, rung: save.rung, audio: save.audio, subtitles: save.subtitles, device: deviceName(),
    subtitleLanguage: save.subtitleLanguage || null,
  });
  return metaOf(job.sub, pkg);
}

/** The `meta.json` a package is written as (`CLAUDE.md` beside this file). */
function metaOf(sub, pkg) {
  const m = pkg.manifest;
  const film = m.film || {};
  const d = pkg.details || {};
  const episode = m.episode ? { season: m.episode.season, number: m.episode.number, name: (pkg.episode && pkg.episode.name) || null } : null;
  const rung = m.video.renditions[0] || {};
  const rendition = (uri) => String(uri).split("/")[0];
  const tracks = pkg.tracks.map((t) => {
    const audio = t.kind === "audio" ? m.audio.find((a) => rendition(a.uri) === t.rendition) : null;
    return {
      name: t.rendition + ".mp4", role: t.kind, rendition: t.rendition, bytes: t.bytes,
      ...(audio ? { language: audio.language, label: audio.label } : {}),
    };
  });
  const subtitles = pkg.subtitles.map((s) => ({
    name: s.id + ".vtt", role: "subtitle", id: s.id, language: s.language, label: s.label,
    forced: s.forced, hearingImpaired: s.hearingImpaired, bytes: s.bytes, url: s.url, auth: true,
  }));
  // Artwork is named once its type is known; the preview sheet is the one image a token opens.
  const artwork = ["poster", "backdrop", "still", "thumbnails"].filter((role) => pkg.artwork[role])
    .map((role) => ({ name: role, role, url: pkg.artwork[role], auth: role === "thumbnails", bytes: 0 }));
  const files = [...tracks, ...subtitles, ...artwork];
  return {
    format: FORMAT,
    titleId: m.id,
    account: sub,
    kind: episode ? "episode" : "film",
    name: film.name || m.title,
    year: film.year || null,
    imdbId: film.imdbId || null,
    show: episode && film.imdbId ? { imdbId: film.imdbId, name: film.name || m.title } : null,
    episode,
    overview: (pkg.episode && pkg.episode.overview) || d.overview || null,
    genres: d.genres || [],
    rating: d.rating ?? null,
    category: pkg.category || null,
    durationSeconds: m.durationSeconds,
    quality: { rendition: rendition(rung.uri), width: rung.width || null, height: rung.height || null, label: rung.name || null },
    files,
    bytes: files.reduce((sum, f) => sum + f.bytes, 0),
    saveId: pkg.save.id,
    startedAt: new Date().toISOString(),
    completedAt: null,
    growing: !!pkg.growing,
    package: pkg,
  };
}

function enqueue(job) {
  if (running && running.sub === job.sub && running.titleId === job.titleId) {
    // A new package for the save being written replaces it once the worker lets go.
    if (job.meta) {
      queue = [job, ...queue.filter((j) => j.titleId !== job.titleId)];
      stopRunning();
    }
    return;
  }
  queue = [...queue.filter((j) => !(j.sub === job.sub && j.titleId === job.titleId)), job];
  set(job.titleId, { state: isOnline() ? "queued" : "waiting", message: null });
  next();
}

function next() {
  if (running) return;
  const sub = currentSub();
  queue = queue.filter((j) => j.sub === sub);
  if (!queue.length || !isOnline()) {
    for (const job of queue) set(job.titleId, { state: "waiting" });
    holdAwake(false);
    return;
  }
  const job = queue.shift();
  running = { ...job, reposted: !!job.reposted, ending: null };
  set(job.titleId, { state: "saving", message: null });
  holdAwake(true);
  workerOf().postMessage({ type: "run", sub: job.sub, titleId: job.titleId, meta: job.meta || null });
}

function stopRunning(ending) {
  if (!running) return;
  if (ending) running.ending = ending;
  workerOf().postMessage({ type: "stop", sub: running.sub, titleId: running.titleId });
}

/** What the worker says. */
async function heard(message) {
  if (message.type === "token") {
    const token = await freshToken({ refused: !!message.refused }).catch(() => null);
    workerOf().postMessage({ type: "token", n: message.n, token });
    return;
  }
  if (message.type === "progress") {
    set(message.titleId, { state: "saving", received: message.received, bytes: message.bytes });
    return;
  }

  const job = running;
  running = null;
  if (!job || job.titleId !== message.titleId) {
    next();
    return;
  }
  switch (message.type) {
    case "done": {
      set(job.titleId, null);
      const save = await readSave(job.sub, job.titleId).catch(() => null);
      if (save && save.meta.saveId) api.completeSave(save.meta.saveId).catch(() => {});
      if (save) toast.info(`Downloaded ${saveTitle(save.meta)}.`);
      break;
    }
    case "growing":
      // The server is still making the title: what it has made is here, and the rest is asked for later.
      set(job.titleId, { state: "saving" });
      later(job.sub, job.titleId);
      break;
    case "stopped":
      if (job.ending === "paused") {
        // The last progress said lags the bytes the worker wrote before it let go.
        const save = await readSave(job.sub, job.titleId).catch(() => null);
        set(job.titleId, { state: "paused", ...(save ? { received: save.receivedBytes } : {}) });
      } else if (job.ending === "removed" || job.ending === "signed-out") set(job.titleId, null);
      break;
    case "elsewhere":
      set(job.titleId, { state: "elsewhere" }, { tell: false });
      // The tab writing it lets go when it finishes, is paused or closes; this one carries on then.
      void navigator.locks.request(lockName(job.sub, job.titleId), () => {}).then(() => {
        const entry = saving.getState()[job.titleId];
        if (currentSub() === job.sub && entry && entry.state !== "paused" && !pausedOf(job.sub).includes(job.titleId)) {
          enqueue({ sub: job.sub, titleId: job.titleId });
        }
      });
      break;
    case "unreached":
      queue = [{ sub: job.sub, titleId: job.titleId }, ...queue];
      unreached();
      void probe();
      break;
    case "stale":
      // The API no longer holds the record (`409`), or the track's files changed: the save is asked
      // for again as it was chosen, once, and written as the new package says.
      if (job.reposted) {
        failed(job.titleId, "This film changed on the server while it was downloaded. Remove it and download it again.");
        break;
      }
      try {
        queue = [{ sub: job.sub, titleId: job.titleId, meta: await repost(job), reposted: true }, ...queue];
      } catch (error) {
        failed(job.titleId, error.message || "This film could not be downloaded.");
      }
      break;
    default:
      failed(job.titleId, message.message || "This film could not be downloaded.");
  }
  next();
}

function failed(titleId, message) {
  set(titleId, { state: "failed", message });
}

/**
 * Asks for the next stretch of a title the server is still making once it has had time to make more,
 * and queues it. With no connection the save waits for one, as any save does; a title gone from the
 * library, or one that could not be made, fails the save.
 */
function later(sub, titleId) {
  clearTimeout(growing.get(titleId));
  growing.set(titleId, setTimeout(async () => {
    growing.delete(titleId);
    if (currentSub() !== sub || pausedOf(sub).includes(titleId)) return;
    if (!isOnline()) {
      enqueue({ sub, titleId });
      return;
    }
    try {
      enqueue({ sub, titleId, meta: await repost({ sub, titleId }) });
    } catch (error) {
      if (error && (error.status === 404 || error.status === 409)) failed(titleId, error.message || "This film could not be downloaded.");
      else later(sub, titleId);
    }
  }, GROW_MS));
}

function stopGrowing(titleId) {
  clearTimeout(growing.get(titleId));
  growing.delete(titleId);
}

/** What a save is called: a film's name, or an episode's show and place. */
export const saveTitle = (meta) => (meta.episode && meta.show ? `${meta.show.name} S${meta.episode.season} · E${meta.episode.number}` : meta.name);

/** Pauses a save; it stays paused across reloads until the person resumes it. */
export function pauseSave(titleId) {
  const sub = currentSub();
  if (!sub) return;
  setPaused(sub, titleId, true);
  queue = queue.filter((j) => j.titleId !== titleId);
  stopGrowing(titleId);
  if (running && running.titleId === titleId) stopRunning("paused");
  else set(titleId, { state: "paused" });
  if (channel) channel.postMessage({ type: "pause", sub, titleId });
}

/** Resumes a paused, waiting or failed save from what is on the device. */
export function resumeSave(titleId) {
  const sub = currentSub();
  if (!sub) return;
  setPaused(sub, titleId, false);
  enqueue({ sub, titleId });
}

/**
 * Removes a save from the device, stopping its transfer first wherever it runs, and queues its
 * record at the API to be forgotten. `forget: false` leaves the API's record alone, for one it no
 * longer holds.
 */
export async function removeFromDevice(sub, titleId, { forget = true } = {}) {
  queue = queue.filter((j) => !(j.sub === sub && j.titleId === titleId));
  stopGrowing(titleId);
  if (running && running.sub === sub && running.titleId === titleId) stopRunning("removed");
  if (channel) channel.postMessage({ type: "stop", sub, titleId });
  setPaused(sub, titleId, false);
  const save = await readSave(sub, titleId).catch(() => null);
  await navigator.locks.request(lockName(sub, titleId), () => removeSave(sub, titleId));
  if (!forget && save && save.meta.saveId) forgotten(sub, save.meta.saveId);
  set(titleId, null);
}

/** Removes every save of an account from the device, stopping any transfer of one first. */
export async function removeAllFromDevice(sub) {
  stopAccount(sub, "removed");
  const saves = await listSaves(sub).catch(() => []);
  for (const save of saves) {
    if (channel) channel.postMessage({ type: "stop", sub, titleId: save.titleId });
    await navigator.locks.request(lockName(sub, save.titleId), () => {});
  }
  await removeAccount(sub);
}

/** Stops every transfer of an account in this tab, leaving what is on the device. */
export function stopAccount(sub, ending = "signed-out") {
  queue = queue.filter((j) => j.sub !== sub);
  for (const titleId of [...growing.keys()]) stopGrowing(titleId);
  if (running && running.sub === sub) stopRunning(ending);
  saving.setState({});
}

/**
 * Queues every save of the account that is under way on the device and not paused, and marks the
 * paused ones: what a page does when it loads, and when the connection comes back.
 */
export async function resumeUnfinished(sub) {
  const paused = pausedOf(sub);
  for (const save of await listSaves(sub).catch(() => [])) {
    if (save.complete || !save.meta.saveId || !(save.meta.package && Array.isArray(save.meta.package.tracks))) continue;
    if (paused.includes(save.titleId)) {
      if (!saving.getState()[save.titleId]) set(save.titleId, { state: "paused", received: save.receivedBytes, bytes: save.meta.bytes }, { tell: false });
      continue;
    }
    const entry = saving.getState()[save.titleId];
    if ((running && running.titleId === save.titleId) || growing.has(save.titleId)) continue;
    if (entry && entry.state === "failed") continue;
    if (!entry) set(save.titleId, { received: save.receivedBytes, bytes: save.meta.bytes }, { tell: false });
    enqueue({ sub, titleId: save.titleId });
  }
}

// The connection back: the saves that waited for it carry on.
onReconnect(() => {
  next();
});

// Another tab's saves: drawn here as that tab says, and stopped here when that tab pauses or removes one.
if (channel) {
  channel.onmessage = (event) => {
    const message = event.data || {};
    if (message.sub !== currentSub()) return;
    const mine = running && running.titleId === message.titleId;
    if (message.type === "entry" && !mine && !queue.some((j) => j.titleId === message.titleId)) {
      set(message.titleId, message.entry, { tell: false });
    } else if (message.type === "pause") {
      queue = queue.filter((j) => j.titleId !== message.titleId);
      if (mine) stopRunning("paused");
      else set(message.titleId, { state: "paused" }, { tell: false });
    } else if (message.type === "stop") {
      queue = queue.filter((j) => j.titleId !== message.titleId);
      if (mine) stopRunning("removed");
      else set(message.titleId, null, { tell: false });
    }
  };
}

/** Keeps the screen on while a save is written and the page is in view; a hidden page cannot hold it. */
function holdAwake(on) {
  if (!("wakeLock" in navigator)) return;
  if (!on) {
    if (wakeLock) wakeLock.release().catch(() => {});
    wakeLock = null;
    return;
  }
  if (wakeLock || document.visibilityState !== "visible") return;
  navigator.wakeLock.request("screen").then((lock) => {
    if (!running) {
      lock.release().catch(() => {});
      return;
    }
    wakeLock = lock;
    lock.addEventListener("release", () => { if (wakeLock === lock) wakeLock = null; });
  }, () => {});
}

if (typeof document !== "undefined") {
  document.addEventListener("visibilitychange", () => holdAwake(!!running));
}
