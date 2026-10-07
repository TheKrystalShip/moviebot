// The saver's writing half: a dedicated worker, because sync access handles exist only in one, and a
// film is gigabytes written a chunk at a time. It runs one save at a time, as `saver.js` hands them
// over, and writes it in the store's fixed order (`CLAUDE.md`): `meta.json` listing every file, then
// each file under its listed name, then `meta.json` again with `completedAt` set.
//
// The page holds the session, so the worker asks it for the bearer token at the start of every
// request (`token`), and once more, renewed, when a request is refused. A track resumes from the
// length already on disk, by `Range`, with `If-Range` naming the validator the bytes on disk arrived
// under (the file's `etag`, else the package's): a track whose files changed since is answered whole,
// the bytes on disk are of another encoding and are dropped, and the save is reported stale so the
// page asks for a new package. A `429` waits for its `Retry-After`.
//
// A package marked `growing` is a title the server is still making: each track is fetched as far as the
// package says it has got, and the run ends `growing` rather than `done`, for the page to ask for the
// package again later and carry on from what is on disk. The server answers a validator of a stretch
// its track still begins with as that track, so the bytes held stay good as the track grows.
//
// While a save is written its per-title lock (`cinema-offline-save:<sub>/<titleId>`) is held, so a
// second tab of this origin never writes the same save, and removing a save waits for the writer to
// let go of it.
//
// Messages in: `run` `{ sub, titleId, meta? }` (a `meta` is a new package to write; without one the
// save on disk is resumed), `stop` `{ sub, titleId }`, `token` `{ n, token }`. Out: `token`
// `{ n, refused }`, `progress` `{ titleId, received, bytes }`, and one of `done`, `growing`, `stopped`,
// `elsewhere`, `unreached`, `stale`, `failed` `{ titleId, status, message }` to end each run. A write
// a reader should see is said on the store's `cinema-offline` channel, which reaches every page.

import { META, isMeta, saveDir } from "./layout.js";

const CHANNEL = "cinema-offline";
const FLUSH_EVERY = 8 * 1024 * 1024;
const PROGRESS_MS = 250;
const RETRIES = [2000, 5000, 15000];
const DEFAULT_RETRY_AFTER_S = 10;

const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel(CHANNEL) : null;
const tokens = new Map();
let asked = 0;
let current = null;

class Ended extends Error {
  constructor(outcome, detail = {}) {
    super(outcome);
    this.outcome = outcome;
    this.detail = detail;
  }
}

self.onmessage = (event) => {
  const message = event.data || {};
  if (message.type === "token") {
    const resolve = tokens.get(message.n);
    tokens.delete(message.n);
    if (resolve) resolve(message.token || null);
  } else if (message.type === "stop") {
    if (current && current.sub === message.sub && current.titleId === message.titleId) current.abort.abort();
  } else if (message.type === "run") {
    void run(message);
  }
};

/** The page's bearer token, renewed first when the last one was refused. */
function token(refused = false) {
  asked += 1;
  const n = asked;
  return new Promise((resolve) => {
    tokens.set(n, resolve);
    self.postMessage({ type: "token", n, refused });
  });
}

/** Tells every page of this origin, the one that started the save among them, to read the saves again. */
function changed() {
  if (channel) channel.postMessage("changed");
}

async function run({ sub, titleId, meta }) {
  const abort = new AbortController();
  current = { sub, titleId, abort };
  try {
    const held = await navigator.locks.request(`cinema-offline-save:${sub}/${titleId}`, { ifAvailable: true }, async (lock) => {
      if (!lock) return false;
      await save(sub, titleId, meta, abort.signal);
      return true;
    });
    self.postMessage({ type: held ? "done" : "elsewhere", titleId });
  } catch (error) {
    if (abort.signal.aborted) self.postMessage({ type: "stopped", titleId });
    else if (error instanceof Ended) self.postMessage({ type: error.outcome, titleId, ...error.detail });
    else self.postMessage({ type: "failed", titleId, message: String((error && error.message) || error) });
  } finally {
    current = null;
  }
}

async function save(sub, titleId, fresh, signal) {
  const dir = await saveDir(sub, titleId, { create: true });
  let meta;
  if (fresh) {
    const before = await readMeta(dir);
    meta = await begin(dir, fresh, before, signal);
  } else {
    meta = await readMeta(dir);
    if (!meta) throw new Ended("failed", { message: "This download is not on the device." });
  }
  if (meta.completedAt) return;

  const progress = reporter(titleId, meta);
  for (const file of meta.files) {
    if (file.role === "video" || file.role === "audio") await track(dir, meta, file, progress, signal);
    else await small(dir, meta, file, progress, signal);
  }
  if (meta.growing) throw new Ended("growing");

  meta.completedAt = new Date().toISOString();
  await writeJson(dir, META, meta);
  changed();
}

/**
 * A new package: its subtitles and artwork are fetched first, which are small, so `meta.json` lists
 * every file with its exact length; then `meta.json` is written, and the files a former package left
 * that this one does not list are removed. A small file a former package of the same title holds
 * whole from the same address is kept as it is. A track kept from a former package keeps the
 * validator its bytes arrived under, and the server says whether its track still begins with them.
 */
async function begin(dir, meta, before, signal) {
  const held = new Map();
  const files = [];
  const formerly = new Map(((before && before.files) || []).map((f) => [f.role === "subtitle" ? "subtitle:" + f.id : f.role, f]));
  const formerTracks = new Map(((before && before.package && before.package.tracks) || []).map((t) => [t.rendition, t]));
  for (const file of meta.files) {
    if (file.role === "video" || file.role === "audio") {
      const former = (before && before.files.find((f) => f.name === file.name)) || null;
      const formerTrack = formerTracks.get(file.rendition);
      const kept = former && (former.etag || (formerTrack && formerTrack.etag));
      const holding = kept && (await sizeOf(dir, file.name)) > 0;
      if (!holding) await truncate(dir, file.name);
      files.push({ ...file, etag: holding ? kept : (trackOf(meta, file) || {}).etag || null });
      continue;
    }
    const former = formerly.get(file.role === "subtitle" ? "subtitle:" + file.id : file.role);
    if (former && former.url === file.url && former.bytes > 0 && (await sizeOf(dir, former.name)) === former.bytes) {
      files.push(former);
      continue;
    }
    const body = await fetchSmall(file.url, file.auth, signal);
    if (!body) {
      if (file.role === "subtitle") throw new Ended("failed", { message: `The subtitle ${file.label || file.id} could not be fetched.` });
      continue;
    }
    const entry = { ...file, name: file.role === "subtitle" ? file.name : file.role + extension(body.type), type: body.type || undefined, bytes: body.bytes.byteLength };
    held.set(entry.name, body.bytes);
    files.push(entry);
  }
  const written = { ...meta, files, bytes: files.reduce((sum, f) => sum + f.bytes, 0) };

  const listed = new Set([META, ...written.files.map((f) => f.name)]);
  const left = [];
  for await (const [name] of dir.entries()) if (!listed.has(name)) left.push(name);
  for (const name of left) await dir.removeEntry(name).catch(() => {});

  await writeJson(dir, META, written);
  changed();
  for (const [name, bytes] of held) await writeWhole(dir, name, bytes);
  if (held.size) changed();
  return written;
}

/** A small file whose length on disk is short of its listing: fetched whole and written again. */
async function small(dir, meta, file, progress, signal) {
  if ((await sizeOf(dir, file.name)) === file.bytes) {
    progress.add(file.bytes);
    return;
  }
  const body = await fetchSmall(file.url, file.auth, signal);
  if (!body) throw new Ended("unreached");
  if (body.bytes.byteLength !== file.bytes) {
    file.bytes = body.bytes.byteLength;
    meta.bytes = meta.files.reduce((sum, f) => sum + f.bytes, 0);
    await writeJson(dir, META, meta);
  }
  await writeWhole(dir, file.name, body.bytes);
  progress.add(file.bytes);
  changed();
}

/**
 * One track, from the length on disk to its end, or for a growing package to as far as the package
 * says the server has got. Each answer's `ETag` is what the bytes on disk arrived under from then on.
 */
async function track(dir, meta, file, progress, signal) {
  const source = trackOf(meta, file);
  if (!source) throw new Ended("failed", { message: "The download lists a track its package does not hold." });
  const growing = !!meta.growing;
  const handle = await (await dir.getFileHandle(file.name, { create: true })).createSyncAccessHandle();
  let validated = false;
  try {
    let at = handle.getSize();
    if (at > file.bytes) {
      handle.truncate(0);
      at = 0;
    }
    progress.add(at);
    let attempt = 0;
    let refused = false;
    while (at < file.bytes) {
      const headers = { Authorization: "Bearer " + ((await token(refused)) || "") };
      // A growing track is asked for only as far as the package names, which its segment index covers.
      if (growing) headers.Range = `bytes=${at}-${file.bytes - 1}`;
      else if (at > 0) headers.Range = `bytes=${at}-`;
      if (at > 0) headers["If-Range"] = file.etag || source.etag;
      let response;
      try {
        response = await fetch(source.url, { headers, cache: "no-store", signal });
      } catch (error) {
        if (signal.aborted) throw error;
        if (attempt >= RETRIES.length) throw new Ended("unreached");
        await pause(RETRIES[attempt++], signal);
        continue;
      }
      if (response.status === 401) {
        if (refused) throw new Ended("failed", { status: 401, message: "Sign in to carry on saving." });
        refused = true;
        continue;
      }
      refused = false;
      if (response.status === 429) {
        const wait = Number(response.headers.get("Retry-After")) || DEFAULT_RETRY_AFTER_S;
        await pause(wait * 1000, signal);
        continue;
      }
      if (response.status === 409) throw new Ended("stale", { status: 409 });
      if (response.status === 200 && at > 0) {
        // What is on disk is of another encoding: it goes, and the package is asked for again.
        handle.truncate(0);
        throw new Ended("stale", { status: 200 });
      }
      if (response.status === 200 && (growing ? Number(response.headers.get("Content-Length")) < file.bytes
        : Number(response.headers.get("Content-Length")) !== file.bytes)) {
        throw new Ended("stale", { status: 200 });
      }
      if (response.status === 206 && !String(response.headers.get("Content-Range") || "").startsWith(`bytes ${at}-`)) {
        throw new Ended("stale", { status: 206 });
      }
      if (response.status !== 200 && response.status !== 206) {
        const answer = await response.json().catch(() => null);
        throw new Ended("failed", { status: response.status, message: (answer && answer.error) || `MovieBot answered ${response.status}.` });
      }

      const etag = response.headers.get("ETag");
      if (etag && etag !== file.etag) {
        file.etag = etag;
        validated = true;
      }
      const reader = response.body.getReader();
      let unflushed = 0;
      try {
        for (;;) {
          const { done, value } = await reader.read();
          if (done) break;
          const room = file.bytes - at;
          const chunk = value.byteLength > room ? value.subarray(0, room) : value;
          const written = handle.write(chunk, { at });
          at += written;
          unflushed += written;
          progress.add(written);
          if (unflushed >= FLUSH_EVERY) {
            handle.flush();
            unflushed = 0;
          }
          if (at >= file.bytes) break;
        }
        attempt = 0;
      } catch (error) {
        if (signal.aborted) throw error;
        // The connection broke mid-track: what arrived is kept and the rest is asked for again.
        if (attempt >= RETRIES.length) throw new Ended("unreached");
        await pause(RETRIES[attempt++], signal);
      } finally {
        handle.flush();
        reader.cancel().catch(() => {});
      }
    }
  } finally {
    handle.close();
  }
  // The validator the bytes arrived under is kept, so the next package resumes them.
  if (validated) await writeJson(dir, META, meta);
  changed();
}

/** The package's track a listed file is. */
function trackOf(meta, file) {
  return ((meta.package && meta.package.tracks) || []).find((t) => t.rendition === file.rendition) || null;
}

/** A small file whole, in memory: `{ bytes, type }`, or null for one that could not be fetched. */
async function fetchSmall(url, auth, signal) {
  if (!url) return null;
  for (let attempt = 0; ; attempt++) {
    try {
      const headers = auth ? { Authorization: "Bearer " + ((await token()) || "") } : {};
      const response = await fetch(url, { headers, signal });
      if (!response.ok) return null;
      return { bytes: new Uint8Array(await response.arrayBuffer()), type: (response.headers.get("Content-Type") || "").split(";")[0] };
    } catch (error) {
      if (signal.aborted) throw error;
      if (attempt >= RETRIES.length) return null;
      await pause(RETRIES[attempt], signal);
    }
  }
}

const EXTENSIONS = { "image/jpeg": ".jpg", "image/png": ".png", "image/webp": ".webp", "image/avif": ".avif", "image/gif": ".gif" };
const extension = (type) => EXTENSIONS[type] || ".jpg";

async function readMeta(dir) {
  try {
    const meta = JSON.parse(await (await (await dir.getFileHandle(META)).getFile()).text());
    return isMeta(meta) ? meta : null;
  } catch {
    return null;
  }
}

async function sizeOf(dir, name) {
  try {
    return (await (await dir.getFileHandle(name)).getFile()).size;
  } catch {
    return -1;
  }
}

async function truncate(dir, name) {
  try {
    const handle = await (await dir.getFileHandle(name)).createSyncAccessHandle();
    handle.truncate(0);
    handle.close();
  } catch {
    // Not there: nothing to start again.
  }
}

async function writeWhole(dir, name, bytes) {
  const handle = await (await dir.getFileHandle(name, { create: true })).createSyncAccessHandle();
  try {
    handle.truncate(0);
    handle.write(bytes, { at: 0 });
    handle.flush();
  } finally {
    handle.close();
  }
}

const writeJson = (dir, name, value) => writeWhole(dir, name, new TextEncoder().encode(JSON.stringify(value)));

/** Counts what is on disk of a save as it is written, and tells the page a few times a second. */
function reporter(titleId, meta) {
  let received = 0;
  let last = 0;
  return {
    add(n) {
      received += n;
      const now = Date.now();
      if (now - last >= PROGRESS_MS || received >= meta.bytes) {
        last = now;
        self.postMessage({ type: "progress", titleId, received, bytes: meta.bytes });
      }
    },
  };
}

/** Waits, ending at once when the save is stopped. */
function pause(ms, signal) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener("abort", () => {
      clearTimeout(timer);
      reject(signal.reason);
    }, { once: true });
  });
}
