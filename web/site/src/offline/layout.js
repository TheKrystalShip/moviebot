// Where a saved film lives in the Origin Private File System, and what its `meta.json` holds. Plain
// functions over directory handles and no DOM, so the worker that writes a save (sync access handles
// exist only in a worker) and the page that reads it name every file the same way.
//
//   /<account>/<title>/meta.json        what was saved, from the package the API answered
//   /<account>/<title>/<file name>      each track, subtitle and artwork file meta.json lists
//   /<account>/.shows/<show>/show.json  a show any saved episode belongs to, as the API described it
//   /<account>/.shows/<show>/<name>     its poster, backdrop and episode stills, which show.json lists
//
// `<account>` is the provider's `sub`, `<title>` the title's id and `<show>` the show's IMDb id, each
// encoded so any string makes a legal name; an encoded name never holds a bare `.`, so `.shows` is no
// title's. Nothing else is kept at the root.

/** The file every save's directory holds, written last by the saver and read first by the page. */
export const META = "meta.json";

/** The shape of `meta.json` this build writes and reads; a save of another shape is not listed. */
export const FORMAT = 1;

/** The roles a file in a save plays. */
export const ROLES = ["video", "audio", "subtitle", "poster", "backdrop", "still", "thumbnails"];

const name = (s) => encodeURIComponent(String(s)).replace(/\./g, "%2E");
const unname = (s) => {
  try { return decodeURIComponent(s); } catch { return s; }
};

/** The directory name an account's saves are kept under. */
export const accountDirName = name;

/** The directory name a title's save is kept under. */
export const titleDirName = name;

/** The directory name a kept show is kept under. */
export const showDirName = name;

/** The title id a save's directory name stands for, and the IMDb id a kept show's does. */
export const titleIdOf = unname;

/** The origin's private root: the same directory from the page and from a worker. */
export function root() {
  return navigator.storage.getDirectory();
}

/** An account's directory, or null when it has none and `create` is not asked for. */
export async function accountDir(sub, { create = false } = {}) {
  try {
    return await (await root()).getDirectoryHandle(accountDirName(sub), { create });
  } catch (error) {
    if (!create && error && error.name === "NotFoundError") return null;
    throw error;
  }
}

/** A title's save directory under an account's, or null when there is none and `create` is not asked for. */
export async function saveDir(sub, titleId, { create = false } = {}) {
  const account = await accountDir(sub, { create });
  if (!account) return null;
  try {
    return await account.getDirectoryHandle(titleDirName(titleId), { create });
  } catch (error) {
    if (!create && error && error.name === "NotFoundError") return null;
    throw error;
  }
}

/** The file a kept show's directory holds, written after its artwork. */
export const SHOW_META = "show.json";

/** The directory under an account's that holds its kept shows. */
export const SHOWS = ".shows";

/** A kept show's directory under an account's, or null when there is none and `create` is not asked for. */
export async function showDir(sub, imdbId, { create = false } = {}) {
  const account = await accountDir(sub, { create });
  if (!account) return null;
  try {
    const shows = await account.getDirectoryHandle(SHOWS, { create });
    return await shows.getDirectoryHandle(showDirName(imdbId), { create });
  } catch (error) {
    if (!create && error && error.name === "NotFoundError") return null;
    throw error;
  }
}

/** Whether a parsed `meta.json` is one this build can read. */
export function isMeta(meta) {
  return !!meta && meta.format === FORMAT && typeof meta.titleId === "string" && Array.isArray(meta.files);
}
