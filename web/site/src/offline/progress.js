// Where a person got to in films nothing else was recording, kept per account until the API has it.
//
// The player writes an entry as a saved film plays alone, and as a room's film plays while the room
// cannot hear the page (`player/engine/room.ts`), one per title, the
// latest replacing the one before: the API keeps only the newest position of a title, so an older one
// is worth nothing to send. Whenever the server can be reached the log goes up in one
// `POST /api/me/progress`, and an entry is taken off once the API has answered for it, unless it was
// written again meanwhile. Starting a saved film resumes from the newer of this log and what the API
// last said (`resumeAt`). The API's last answer is kept beside the log, so a page drawn from the
// device knows where the person got to (`progressOnDevice`).

import { api } from "../api.js";
import { isOnline } from "../connectivity.js";
import { sessionStore } from "../session.js";

const KEY = "cinema.progress.";
const KEPT_KEY = "cinema.progress.kept.";
/** The most the API takes in one batch. */
const BATCH = 500;
/** A position this far into a film is the film watched, and the next start is its beginning. */
const FINISHED_FRACTION = 0.9;

/** The account's log: `[{ titleId, positionSeconds, watchedAt }]`, the newest first. */
export function loggedProgress(sub) {
  if (!sub) return [];
  try {
    const entries = JSON.parse(localStorage.getItem(KEY + sub) || "[]");
    return Array.isArray(entries) ? entries.filter((e) => e && typeof e.titleId === "string") : [];
  } catch {
    return [];
  }
}

function write(sub, entries) {
  try {
    if (entries.length) localStorage.setItem(KEY + sub, JSON.stringify(entries));
    else localStorage.removeItem(KEY + sub);
  } catch {
    // Storage refused: the position is lost, and the film starts from what the API last said.
  }
}

/**
 * Keeps an account's `GET /api/me/progress` answer on the device (`cinema.progress.kept.<sub>`), which
 * is what Home's Continue watching reads with no connection beside the log.
 */
export function keepProgress(sub, entries) {
  if (!sub || !Array.isArray(entries)) return;
  try { localStorage.setItem(KEPT_KEY + sub, JSON.stringify(entries)); } catch { /* the next answer is kept instead */ }
}

/** An account's `GET /api/me/progress` answer as last kept on this device, or an empty list. */
export function keptProgress(sub) {
  if (!sub) return [];
  try {
    const entries = JSON.parse(localStorage.getItem(KEPT_KEY + sub) || "[]");
    return Array.isArray(entries) ? entries.filter((e) => e && typeof e.titleId === "string") : [];
  } catch {
    return [];
  }
}

/** Forgets an account's kept `GET /api/me/progress` answer on this device. */
export function forgetKeptProgress(sub) {
  try { localStorage.removeItem(KEPT_KEY + sub); } catch { /* nothing was kept */ }
}

/**
 * Where the account got to in each title, as this device knows it: the newer of the log and the kept
 * answer, by title id, `{ positionSeconds, watchedAt }`, a title watched past most of its length left
 * out. `durationOf(titleId)` is a title's length in seconds, or null when it is not known.
 */
export function progressOnDevice(sub, durationOf) {
  const newest = new Map();
  const consider = (titleId, positionSeconds, watchedAt) => {
    const held = newest.get(titleId);
    if (!held || Date.parse(watchedAt || 0) > Date.parse(held.watchedAt || 0)) newest.set(titleId, { positionSeconds, watchedAt });
  };
  for (const e of keptProgress(sub)) consider(e.titleId, e.positionSeconds, e.lastWatchedAt);
  for (const e of loggedProgress(sub)) consider(e.titleId, e.positionSeconds, e.watchedAt);
  for (const [titleId, p] of newest) {
    const duration = durationOf(titleId);
    if (!(p.positionSeconds > 0) || (duration > 0 && p.positionSeconds >= duration * FINISHED_FRACTION)) newest.delete(titleId);
  }
  return newest;
}

/** Writes down where the account got to in a title, now. */
export function logProgress(sub, titleId, positionSeconds) {
  if (!sub || !titleId || !Number.isFinite(positionSeconds) || positionSeconds < 0) return;
  const entry = { titleId, positionSeconds: Math.round(positionSeconds * 10) / 10, watchedAt: new Date().toISOString() };
  write(sub, [entry, ...loggedProgress(sub).filter((e) => e.titleId !== titleId)]);
}

let sending = null;

/**
 * Sends the signed-in account's log to the API and takes off what it answered for. One send runs at a
 * time; a send that fails leaves the log for the next.
 */
export function sendProgress() {
  sending ??= (async () => {
    const session = sessionStore.getState();
    const sub = session && session.user.id;
    const entries = loggedProgress(sub).slice(0, BATCH);
    if (!sub || entries.length === 0 || !isOnline()) return;
    await api.sendProgress(entries.map(({ titleId, positionSeconds, watchedAt }) => ({ titleId, positionSeconds, watchedAt })));
    const answered = new Map(entries.map((e) => [e.titleId, e.watchedAt]));
    write(sub, loggedProgress(sub).filter((e) => answered.get(e.titleId) !== e.watchedAt));
  })().catch(() => {}).finally(() => { sending = null; });
  return sending;
}

/**
 * Where a saved film starts when nothing names a position: the newer of the device's log and the
 * API's record (`GET /api/me/progress`, asked only while the server can be reached), in seconds, or
 * null for the beginning. A film watched past most of its length starts again.
 */
export async function resumeAt(sub, titleId, durationSeconds) {
  const local = loggedProgress(sub).find((e) => e.titleId === titleId) || null;
  let remote = null;
  if (isOnline()) {
    const listed = await api.progress().catch(() => null);
    const entry = Array.isArray(listed) ? listed.find((p) => p.titleId === titleId) : null;
    if (entry) remote = { positionSeconds: entry.positionSeconds, watchedAt: entry.lastWatchedAt };
  }
  const newer = [local, remote].filter(Boolean)
    .sort((a, b) => Date.parse(b.watchedAt) - Date.parse(a.watchedAt))[0];
  if (!newer || !(newer.positionSeconds > 0)) return null;
  if (durationSeconds > 0 && newer.positionSeconds >= durationSeconds * FINISHED_FRACTION) return null;
  return newer.positionSeconds;
}
