// The phone app's downloads (`phone/README.md`): inside Krystal Cinema for Android, a save's tracks
// are fetched by the app rather than the page, because the app keeps fetching with the screen off and
// the page out of view and a browser's page does not. The page hands the tracks over with the save's
// grant, reads back how far each has got, and plays each one from the address the app answers it at,
// once whole. Everything else of a save, its `meta.json`, subtitles and artwork, is the store's as
// ever.

const bridge = typeof window !== "undefined" && window.KrystalCinemaApp ? window.KrystalCinemaApp : null;

/** Whether this page runs inside the app, which then downloads the tracks of every save it can. */
export const inApp = () => !!bridge;

/** Whether a listed file is a track the app holds rather than the store. */
export const heldByApp = (file) => !!file && file.store === "app";

/** Whether a save's tracks are the app's. */
export const savedByApp = (meta) => !!meta && Array.isArray(meta.files) && meta.files.some(heldByApp);

function parsed(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

/**
 * One save as the app has it: `{ saveId, state, received, bytes, message?, tracks: [{ rendition,
 * received, bytes }] }`, `state` one of `queued`, `downloading`, `waiting`, `paused`, `failed`,
 * `done`; null when the app holds none by that id, or this is no app.
 */
export function appStatus(saveId) {
  return bridge && saveId ? parsed(bridge.status(saveId)) : null;
}

/** Hands a save's tracks to the app, with its grant: what the app fetches with no page running. */
export function appDownload(meta) {
  const pkg = meta.package;
  const tracks = meta.files.filter(heldByApp).map((file) => {
    const track = pkg.tracks.find((t) => t.rendition === file.rendition);
    return { rendition: file.rendition, url: track.url, bytes: track.bytes, etag: track.etag };
  });
  const answer = bridge.download(JSON.stringify({ saveId: meta.saveId, titleId: meta.titleId, name: appName(meta), grant: pkg.grant, tracks }));
  if (answer !== "ok") throw new Error(answer || "The app could not take this download.");
}

export const appPause = (saveId) => bridge && bridge.pause(saveId);
export const appResume = (saveId) => bridge && bridge.resume(saveId);
export const appRemove = (saveId) => bridge && saveId && bridge.remove(saveId);

/** How many bytes of a listed track the app holds, from a status read once for the save. */
export function appReceived(status, file) {
  const track = status && Array.isArray(status.tracks) ? status.tracks.find((t) => t.rendition === file.rendition) : null;
  return track ? Math.min(track.received, file.bytes) : 0;
}

/**
 * A whole track the app holds, as the player reads a file: its `size`, and `slice(start, end)` whose
 * `arrayBuffer()` is that byte range (end exclusive), asked of the address the app answers it at.
 */
export function appFile(saveId, rendition, size) {
  const url = bridge.trackUrl(saveId, rendition);
  const slice = (start = 0, end = size) => ({
    size: end - start,
    arrayBuffer: async () => {
      // The range is named in the query: a WebView applies a Range header to the app's answer itself.
      const response = await fetch(`${url}?start=${start}&end=${end}`, { cache: "no-store" });
      if (response.status !== 200) throw new Error(`The app answered ${response.status} for ${rendition}.`);
      return response.arrayBuffer();
    },
  });
  return { size, slice };
}

/** What the app's notification calls a save: a film's name, or an episode's show and place. */
function appName(meta) {
  return meta.episode && meta.show ? `${meta.show.name} S${meta.episode.season} · E${meta.episode.number}` : meta.name;
}
