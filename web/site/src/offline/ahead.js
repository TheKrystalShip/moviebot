// What the player keeps ahead of the playhead for an account, in the Origin Private File System beside
// its saves (`player/engine/media/ahead.ts`):
//
//   /<account>/.ahead/<title>/<directory>/<file>   one file of a title's media, by its path under it
//
// The path a file has under a title's media (`v0/seg00012.m4s`) is its directory and its name, each
// encoded as `layout.js` encodes a title's, so none holds a bare `.` and `.ahead` is no title's. Nothing
// here is a save: no `meta.json`, nothing the API records, nothing the library lists. A file is written
// whole through a writable stream, which the browser commits on close, so a reader never meets one half
// written.

import { accountDir, titleDirName, titleIdOf } from "./layout.js";

/** The directory under an account's that holds what the player keeps ahead. */
export const AHEAD = ".ahead";

const ignoreMissing = (error) => {
  if (!error || error.name !== "NotFoundError") throw error;
};

async function aheadDir(sub, create) {
  const account = await accountDir(sub, { create });
  if (!account) return null;
  try {
    return await account.getDirectoryHandle(AHEAD, { create });
  } catch (error) {
    ignoreMissing(error);
    return null;
  }
}

async function titleDir(sub, titleId, create) {
  const ahead = await aheadDir(sub, create);
  if (!ahead) return null;
  try {
    return await ahead.getDirectoryHandle(titleDirName(titleId), { create });
  } catch (error) {
    ignoreMissing(error);
    return null;
  }
}

/** A path under a title's media as its directory and file names on the device. */
function split(relative) {
  const at = relative.lastIndexOf("/");
  return at < 0 ? ["", titleDirName(relative)] : [titleDirName(relative.slice(0, at)), titleDirName(relative.slice(at + 1))];
}

async function folder(dir, name, create) {
  if (name === "") return dir;
  try {
    return await dir.getDirectoryHandle(name, { create });
  } catch (error) {
    ignoreMissing(error);
    return null;
  }
}

/** The account's store of what the player keeps ahead (`AheadStore`). */
export function aheadStore(sub) {
  return {
    async list(titleId) {
      const dir = await titleDir(sub, titleId, false);
      if (!dir) return [];
      const files = [];
      for await (const [name, handle] of dir.entries()) {
        if (handle.kind === "file") {
          files.push(titleIdOf(name));
          continue;
        }
        for await (const [file, entry] of handle.entries()) {
          if (entry.kind === "file") files.push(`${titleIdOf(name)}/${titleIdOf(file)}`);
        }
      }
      return files;
    },

    async read(titleId, relative) {
      const dir = await titleDir(sub, titleId, false);
      const [directory, name] = split(relative);
      const parent = dir && await folder(dir, directory, false);
      if (!parent) return null;
      try {
        return await (await (await parent.getFileHandle(name)).getFile()).arrayBuffer();
      } catch (error) {
        ignoreMissing(error);
        return null;
      }
    },

    async write(titleId, relative, bytes) {
      const dir = await titleDir(sub, titleId, true);
      const [directory, name] = split(relative);
      const parent = await folder(dir, directory, true);
      const writable = await (await parent.getFileHandle(name, { create: true })).createWritable();
      await writable.write(bytes);
      await writable.close();
    },

    async remove(titleId, relatives) {
      const dir = await titleDir(sub, titleId, false);
      if (!dir) return;
      for (const relative of relatives) {
        const [directory, name] = split(relative);
        const parent = await folder(dir, directory, false);
        if (parent) await parent.removeEntry(name).catch(ignoreMissing);
      }
    },

    async keepOnly(titleIds) {
      const ahead = await aheadDir(sub, false);
      if (!ahead) return;
      const keep = new Set(titleIds.map(titleDirName));
      const drop = [];
      for await (const [name] of ahead.entries()) if (!keep.has(name)) drop.push(name);
      for (const name of drop) await ahead.removeEntry(name, { recursive: true }).catch(ignoreMissing);
    },

    async spareBytes() {
      try {
        const { quota, usage } = await navigator.storage.estimate();
        return typeof quota === "number" && typeof usage === "number" ? quota - usage : null;
      } catch {
        return null;
      }
    },
  };
}
