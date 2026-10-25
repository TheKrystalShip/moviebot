// A film saved on this device, as the player plays it (`engine/media/deviceLoader.ts`): read from the
// store (`src/offline/CLAUDE.md`) for one account, and only when the save is whole.

import { appFile, appHoldsWhole, appStatus, heldByApp } from "../offline/shell.js";
import { readSave, saveFile } from "../offline/store.js";

const TRACK_ROLES = new Set(["video", "audio"]);

/**
 * The account's save of a title as the player's `DeviceCopy`, or null when there is none, it is still
 * under way, or a track on the device is not the length the package says. Each track is the file as it
 * stands now, which reads by slices without loading it; the subtitles and the preview sheet are read
 * when asked for, by the manifest-relative path each stands for. A track the phone app holds is read
 * from the app, and only once the app holds it whole. The manifest carries the save's card, which is
 * what the player tells about the title with no connection.
 */
export async function deviceCopy(sub, titleId) {
  const save = await readSave(sub, titleId);
  if (!save || !save.complete) return null;
  const { files, package: saved } = save.meta;
  const manifest = saved && saved.manifest;
  if (!manifest || !Array.isArray(saved.tracks) || saved.tracks.length === 0) return null;

  const tracks = [];
  for (const track of saved.tracks) {
    const entry = files.find((f) => TRACK_ROLES.has(f.role) && f.rendition === track.rendition);
    const file = entry && (heldByApp(entry) ? appTrack(save.meta.saveId, entry) : await saveFile(sub, titleId, entry.name));
    if (!file || file.size !== track.bytes) return null;
    tracks.push({
      uri: track.uri,
      initBytes: track.initBytes,
      segmentSeconds: track.segmentSeconds,
      segmentBytes: track.segmentBytes,
      file,
    });
  }

  // Each file beside the tracks, by the path the manifest names it at.
  const named = new Map();
  for (const subtitle of manifest.subtitles || []) {
    const entry = files.find((f) => f.role === "subtitle" && f.id === subtitle.id);
    if (entry && subtitle.uri) named.set(subtitle.uri, entry.name);
  }
  const sheet = files.find((f) => f.role === "thumbnails");
  if (sheet && manifest.thumbnails) named.set(manifest.thumbnails.uri, sheet.name);

  const posterEntry = files.find((f) => f.role === "poster");
  const poster = posterEntry ? await saveFile(sub, titleId, posterEntry.name) : null;

  return {
    manifest: saved.card ? { ...manifest, card: saved.card } : manifest,
    tracks,
    file: (relative) => (named.has(relative) ? saveFile(sub, titleId, named.get(relative)) : Promise.resolve(null)),
    poster: poster && poster.size > 0 ? poster : null,
  };
}

/** A track the app holds whole, as a file the player reads by slices; null while it is short. */
function appTrack(saveId, entry) {
  return appHoldsWhole(appStatus(saveId), entry) ? appFile(saveId, entry.rendition, entry.bytes) : null;
}
