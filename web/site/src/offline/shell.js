// Downloads in the phone app: inside the TKS shell (`phone/README.md`), a save's tracks are
// downloaded by the shell rather than the page, because the shell keeps fetching with the screen off
// and the page out of view and a browser's page does not. The page hands the tracks over as one of
// the shell's downloads, with the save's grant as the header every request carries and the save's
// `complete` route as where the shell says it is whole; it reads back how far each track has got, and
// plays each one from the address the shell answers it at, once whole. Everything else of a save, its
// `meta.json`, subtitles and artwork, is the store's as ever.

const bridge = typeof window !== "undefined" && window.TksShell ? window.TksShell : null;

/** Whether this page runs inside the shell, which then downloads the tracks of every save it can. */
export const inApp = () => !!bridge;

/** Whether a listed file is a track the shell holds rather than the store. */
export const heldByApp = (file) => !!file && file.store === "app";

/** Whether a save's tracks are the shell's. */
export const savedByApp = (meta) => !!meta && Array.isArray(meta.files) && meta.files.some(heldByApp);

function parsed(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

/**
 * One save as the shell has it: `{ id, state, received, bytes, reason?, files: [{ name, received,
 * bytes }] }`, a file named by its track's rendition, `state` one of `queued`, `downloading`,
 * `waiting`, `paused`, `failed`, `done`, and `reason` why one failed: `credential` (the grant ended),
 * `gone` (the save or the title is no longer on the server), `http-<status>`. Null when the shell
 * holds none by that id, or this is no shell.
 */
export function appStatus(saveId) {
  return bridge && saveId ? parsed(bridge.status(saveId)) : null;
}

/** Hands a save's tracks to the shell as one download, with its grant: what the shell fetches with no page running. */
export function appDownload(meta) {
  const pkg = meta.package;
  const files = meta.files.filter(heldByApp).map((file) => {
    const track = pkg.tracks.find((t) => t.rendition === file.rendition);
    return { name: file.rendition, url: track.url, bytes: track.bytes, type: "video/mp4", validator: track.etag };
  });
  const answer = bridge.download(JSON.stringify({
    id: meta.saveId,
    title: appName(meta),
    headers: { [pkg.grant.header]: pkg.grant.value },
    files,
    done: { url: `/api/me/offline/${encodeURIComponent(meta.saveId)}/complete` },
  }));
  if (answer !== "ok") throw new Error(answer || "The app could not take this download.");
}

export const appPause = (saveId) => bridge && bridge.pause(saveId);
export const appResume = (saveId) => bridge && bridge.resume(saveId);
export const appRemove = (saveId) => bridge && saveId && bridge.remove(saveId);

/** How many bytes of a listed track the shell holds, from a status read once for the save. */
export function appReceived(status, file) {
  const held = status && Array.isArray(status.files) ? status.files.find((f) => f.name === file.rendition) : null;
  return held ? Math.min(held.received, file.bytes) : 0;
}

/** Whether the shell holds a listed track whole. */
export function appHoldsWhole(status, file) {
  return appReceived(status, file) === file.bytes;
}

/**
 * A whole track the shell holds, as the player reads a file: its `size`, and `slice(start, end)` whose
 * `arrayBuffer()` is that byte range (end exclusive), asked of the address the shell answers it at.
 */
export function appFile(saveId, rendition, size) {
  const url = bridge.fileUrl(saveId, rendition);
  const slice = (start = 0, end = size) => ({
    size: end - start,
    arrayBuffer: async () => {
      // The range is named in the query: a WebView applies a Range header to the shell's answer itself.
      const response = await fetch(`${url}?start=${start}&end=${end}`, { cache: "no-store" });
      if (response.status !== 200) throw new Error(`The app answered ${response.status} for ${rendition}.`);
      return response.arrayBuffer();
    },
  });
  return { size, slice };
}

/** What a failed download says to the person, from the shell's reason. */
export function appFailure(reason) {
  if (reason === "gone") return "This film is no longer on the server. Remove it and download it again.";
  return "This film could not be downloaded.";
}

/** What the shell's notification calls a save: a film's name, or an episode's show and place. */
function appName(meta) {
  return meta.episode && meta.show ? `${meta.show.name} S${meta.episode.season} · E${meta.episode.number}` : meta.name;
}
