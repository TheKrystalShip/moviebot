// The films saved on this device, as the page reads them: listing an account's saves, reading one,
// its artwork, the room left on the device, and removing a save or every save of an account.
// `CLAUDE.md` beside this file is the contract the saver and the player build against.

import { accountDir, accountDirName, isMeta, META, root, saveDir, titleDirName, titleIdOf } from "./layout.js";

const CHANNEL = "cinema-offline";
const FORGET_KEY = "cinema.offline.forget.";

/** Whether this browser can keep saved films: the Origin Private File System is there. */
export function offlineSupported() {
  return typeof navigator !== "undefined" && !!navigator.storage && typeof navigator.storage.getDirectory === "function";
}

/**
 * One save: `{ titleId, meta, receivedBytes, complete }`. `receivedBytes` is what is on the device of
 * the files `meta.files` lists, and `complete` that the saver finished (`meta.completedAt`).
 */
async function readFrom(dir, titleId) {
  let meta;
  try {
    meta = JSON.parse(await (await (await dir.getFileHandle(META)).getFile()).text());
  } catch {
    return null;
  }
  if (!isMeta(meta) || meta.titleId !== titleId) return null;
  let receivedBytes = 0;
  for (const file of meta.files) {
    try {
      receivedBytes += Math.min(file.bytes || Infinity, (await (await dir.getFileHandle(file.name)).getFile()).size);
    } catch {
      // Not started yet: nothing of it is here.
    }
  }
  return { titleId, meta, receivedBytes, complete: !!meta.completedAt };
}

/** Every save of an account on this device, finished and under way, the newest started first. */
export async function listSaves(sub) {
  if (!sub || !offlineSupported()) return [];
  const account = await accountDir(sub);
  if (!account) return [];
  const saves = [];
  for await (const [name, handle] of account.entries()) {
    if (handle.kind !== "directory") continue;
    const save = await readFrom(handle, titleIdOf(name));
    if (save) saves.push(save);
  }
  return saves.sort((a, b) => String(b.meta.startedAt || "").localeCompare(String(a.meta.startedAt || "")));
}

/** One title's save for an account, or null when this device holds none. */
export async function readSave(sub, titleId) {
  if (!sub || !titleId || !offlineSupported()) return null;
  const dir = await saveDir(sub, titleId);
  return dir ? readFrom(dir, titleId) : null;
}

/** A file of a save as a `File`, or null when it is not on the device. */
export async function saveFile(sub, titleId, name) {
  const dir = await saveDir(sub, titleId);
  if (!dir) return null;
  try {
    return await (await dir.getFileHandle(name)).getFile();
  } catch {
    return null;
  }
}

/**
 * An object URL for a save's artwork, `poster` or `backdrop`, or null when it has none on the device.
 * The caller revokes it (`useArtwork` does).
 */
export async function artworkUrl(save, role) {
  const entry = save && save.meta.files.find((f) => f.role === role);
  if (!entry) return null;
  const file = await saveFile(save.meta.account, save.titleId, entry.name);
  return file && file.size > 0 ? URL.createObjectURL(file) : null;
}

/** What the browser lets this origin keep: `{ usage, quota, free }` in bytes, or null when it will not say. */
export async function space() {
  if (typeof navigator === "undefined" || !navigator.storage || !navigator.storage.estimate) return null;
  try {
    const { usage = 0, quota = 0 } = await navigator.storage.estimate();
    return { usage, quota, free: Math.max(0, quota - usage) };
  } catch {
    return null;
  }
}

/**
 * Removes a save from the device. A save the API holds a record of (`meta.saveId`) is queued to be
 * forgotten there, which is sent once there is a connection (`pendingForgets`).
 */
export async function removeSave(sub, titleId) {
  const save = await readSave(sub, titleId);
  const account = await accountDir(sub);
  if (account) {
    await account.removeEntry(titleDirName(titleId), { recursive: true }).catch((error) => {
      if (!error || error.name !== "NotFoundError") throw error;
    });
  }
  if (save && save.meta.saveId) queueForget(sub, save.meta.saveId);
  savesChanged();
}

/** Removes every save an account has on this device. */
export async function removeAccount(sub) {
  if (!sub || !offlineSupported()) return;
  const saves = await listSaves(sub);
  await (await root()).removeEntry(accountDirName(sub), { recursive: true }).catch((error) => {
    if (!error || error.name !== "NotFoundError") throw error;
  });
  for (const save of saves) if (save.meta.saveId) queueForget(sub, save.meta.saveId);
  savesChanged();
}

/** The API's records of saves removed from this device, still to be forgotten there. */
export function pendingForgets(sub) {
  try {
    const ids = JSON.parse(localStorage.getItem(FORGET_KEY + sub) || "[]");
    return Array.isArray(ids) ? ids : [];
  } catch {
    return [];
  }
}

/** Takes a record off the queue once the API has forgotten it. */
export function forgotten(sub, saveId) {
  writeForgets(sub, pendingForgets(sub).filter((id) => id !== saveId));
}

function queueForget(sub, saveId) {
  const ids = pendingForgets(sub);
  if (!ids.includes(saveId)) writeForgets(sub, [...ids, saveId]);
}

function writeForgets(sub, ids) {
  try {
    if (ids.length) localStorage.setItem(FORGET_KEY + sub, JSON.stringify(ids));
    else localStorage.removeItem(FORGET_KEY + sub);
  } catch {
    // Storage refused: the record outlives the save on the API until the device reconciles.
  }
}

const listeners = new Set();
const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel(CHANNEL) : null;
if (channel) channel.onmessage = () => { for (const fn of listeners) fn(); };

/**
 * Says the saves on this device changed: every page reading them here and in this origin's other
 * tabs reads them again. The saver calls it whenever it writes `meta.json` or finishes a file.
 */
export function savesChanged() {
  for (const fn of listeners) fn();
  if (channel) channel.postMessage("changed");
}

/** Calls `fn` whenever the saves change; answers the way to stop. */
export function onSavesChanged(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}
