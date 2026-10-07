// Writes a kept show into the Origin Private File System through sync access handles, which every
// browser with the file system has in a dedicated worker, where a page's `createWritable` is not
// everywhere. `store.js`'s `writeShow` is the one caller: it posts `{ id, sub, imdbId, kept, artwork }`,
// `artwork` a list of `[name, Blob]`, and is answered `{ id, ok, error }`. The artwork is written
// first and `show.json` last, then every file the listing no longer names is removed.

import { SHOW_META, showDir } from "./layout.js";

async function write(dir, name, data) {
  const handle = await dir.getFileHandle(name, { create: true });
  const access = await handle.createSyncAccessHandle();
  try {
    access.truncate(0);
    access.write(data, { at: 0 });
    access.flush();
  } finally {
    access.close();
  }
}

self.onmessage = async ({ data: { id, sub, imdbId, kept, artwork } }) => {
  try {
    const dir = await showDir(sub, imdbId, { create: true });
    for (const [name, blob] of artwork) await write(dir, name, new Uint8Array(await blob.arrayBuffer()));
    await write(dir, SHOW_META, new TextEncoder().encode(JSON.stringify(kept)));
    const named = new Set([SHOW_META, ...kept.files.map((f) => f.name)]);
    const stale = [];
    for await (const [name] of dir.entries()) if (!named.has(name)) stale.push(name);
    for (const name of stale) await dir.removeEntry(name).catch(() => {});
    self.postMessage({ id, ok: true });
  } catch (error) {
    self.postMessage({ id, ok: false, error: String((error && error.message) || error) });
  }
};
