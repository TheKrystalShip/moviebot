// Download of what the library does not hold yet: a film or a show's episodes the server fetches,
// and this device downloads once the library holds them.
//
// The API keeps the wants (`/api/me/offline/wants`) and says where each stands: `finding`,
// `downloading` (with how much has arrived), `preparing`, `ready` (with the library's `titleId`) or
// `failed`. This side keeps this device's as the API last answered them, asks again while any is
// waiting, and starts the download of each one ready, at the height it was asked at and with the
// audio and subtitles Download picks by default (`choose.js`). A title still being made is downloaded
// as it is made (`saver.js`). Saving the title ends the want at the API, and this side lets go of it.

import { createStore, useStore } from "@thekrystalship/krystal";
import { api } from "../api.js";
import { isOnline } from "../connectivity.js";
import { sessionStore } from "../session.js";
import { defaultAudio, defaultRung, defaultSubtitles, deviceName, rungFor } from "./choose.js";
import { keptPreferences } from "./preferences.js";
import { NoRoom, startSave } from "./saver.js";
import { readSave } from "./store.js";

/** How often the wants are asked after while one is waiting on the server. */
const TEND_MS = 5000;

/**
 * This device's wants: `list` as the API last answered them (null before it was asked), and `refused`,
 * the ones this device could not start, by want id, with why (`{ problem, message }`).
 */
const wanted = createStore({ sub: null, list: null, refused: {} });

let timer = null;
let tending = null;
const starting = new Set();

const currentSub = () => {
  const session = sessionStore.getState();
  return session ? session.user.id : null;
};

/** The signed-in account's wants on this device, each with where it stands. */
export function useWants() {
  return useStore(wanted, (s) => (s.sub === currentSub() ? s.list : null)) || NONE;
}
const NONE = [];

/** This device's wants as they stand now, and what it could not start: `{ list, refused }`. */
export function wantsNow() {
  const s = wanted.getState();
  return { list: (s.sub === currentSub() && s.list) || NONE, refused: s.refused };
}

/** Calls `fn` whenever this device's wants change; answers the call that stops it. */
export const onWants = (fn) => wanted.subscribe(fn);

const STAGE_WORDS = {
  finding: "Finding a release",
  downloading: "Arriving on the server",
  preparing: "Being prepared",
  ready: "Starting",
};

/** What a want's stage on the server is, in words. */
export const stageWords = (want) => STAGE_WORDS[want.stage] || STAGE_WORDS.finding;

/** What a want is called: the film's name, or the episode's show and place; null when the server has no name for it. */
export function wantTitle(want) {
  if (!want.name) return null;
  return want.episode ? `${want.name} S${want.episode.season} · E${want.episode.episode}` : want.name;
}

/** What this device could not start, by want id. */
export function useRefused() {
  return useStore(wanted, (s) => s.refused);
}

/** The want of a film, or with `season` and `number` an episode of a show, on this device, or null. */
export function wantOf(list, imdbId, season = null, number = null) {
  if (!imdbId) return null;
  return list.find((w) => w.imdbId.toLowerCase() === imdbId.toLowerCase()
    && (season == null ? !w.episode : !!w.episode && w.episode.season === season && w.episode.episode === number)) || null;
}

/**
 * Asks for a film, or the episodes of a show named in `episodes` (`[{ season, episode }]`), to be
 * downloaded here, at the picture `height` (null for this device's own pick), with audio in
 * `audioLanguage` where it has it (null for the title's own) and subtitles in `subtitleLanguage`
 * (null for none).
 */
export async function askDownload({ imdbId, episodes = null, height = null, audioLanguage = null, subtitleLanguage = null }) {
  const answered = await api.wantOffline({ imdbId, episodes, height, audioLanguage, subtitleLanguage, device: deviceName() });
  const ids = new Set(answered.map((w) => w.id));
  wanted.setState((s) => ({
    sub: currentSub(),
    list: [...(s.sub === currentSub() && s.list ? s.list.filter((w) => !ids.has(w.id)) : []), ...answered],
    refused: Object.fromEntries(Object.entries(s.refused).filter(([id]) => !ids.has(id))),
  }));
  void tendWants();
}

/** Stops waiting on a want; its download on the server is the library's to keep or let go. */
export async function letGo(want) {
  await api.unwant(want.id);
  forget(want.id);
}

function forget(id) {
  wanted.setState((s) => ({
    ...s,
    list: s.list ? s.list.filter((w) => w.id !== id) : s.list,
    refused: Object.fromEntries(Object.entries(s.refused).filter(([key]) => key !== id)),
  }));
}

/**
 * Downloads a title the library holds at the picture `height`, or this device's own pick, with audio
 * in `audioLanguage` where the title has it, else the title's own, and the subtitles in
 * `subtitleLanguage` (none for null) with the forced ones of the audio, as Download picks them. The
 * server adds the subtitles in that language the title gains later (`startSave`). Throws `NoRoom`
 * when the device cannot hold what there is of it.
 */
export async function downloadAt(titleId, height, { audioLanguage = null, subtitleLanguage = null } = {}) {
  const sub = currentSub();
  const [offer, preferences] = await Promise.all([
    api.offlineOffer(titleId),
    api.preferences().catch(() => keptPreferences(sub)),
  ]);
  const rung = height ? rungFor(offer.rungs, height) : defaultRung(offer.rungs, preferences);
  if (!rung) throw new Error("Nothing of this title can be downloaded yet.");
  const asked = { ...preferences, audioLanguage, subtitleLanguage };
  const audioIds = defaultAudio(offer.audio, asked);
  const audio = offer.audio.filter((a) => audioIds.includes(a.rendition));
  const subtitleIds = defaultSubtitles(offer.subtitles, audio, asked);
  await startSave({ titleId, rung, audio, subtitles: offer.subtitles.filter((s) => subtitleIds.includes(s.id)), subtitleLanguage });
}

/**
 * Asks after this device's wants and starts the download of each one ready, once at a time; asks
 * again a few seconds later while any is still waiting on the server. What `sync.js` runs when a page
 * loads and the connection comes back.
 */
export function tendWants() {
  tending ??= tend().finally(() => { tending = null; });
  return tending;
}

async function tend() {
  clearTimeout(timer);
  const sub = currentSub();
  if (!sub || !isOnline()) return;
  let list;
  try {
    const device = deviceName();
    list = (await api.offlineWants()).filter((w) => w.device === device);
  } catch {
    timer = setTimeout(() => void tendWants(), TEND_MS * 3);
    return;
  }
  if (currentSub() !== sub) return;
  wanted.setState((s) => ({ sub, list, refused: s.sub === sub ? s.refused : {} }));

  for (const want of list) {
    if (want.stage !== "ready" || !want.titleId || starting.has(want.id) || wanted.getState().refused[want.id]) continue;
    starting.add(want.id);
    try {
      // A title already here is not downloaded twice; the want is let go of.
      if (await readSave(sub, want.titleId)) await api.unwant(want.id);
      else await downloadAt(want.titleId, want.height, { audioLanguage: want.audioLanguage, subtitleLanguage: want.subtitleLanguage });
      forget(want.id);
    } catch (error) {
      if (error instanceof NoRoom) refuse(want.id, "noRoom", error.message);
      else if (error && (error.status === 404 || error.status === 409)) refuse(want.id, "unavailable", error.message);
    } finally {
      starting.delete(want.id);
    }
  }

  const waiting = (wanted.getState().list || []).some((w) => w.stage !== "failed" && !wanted.getState().refused[w.id]);
  if (waiting) timer = setTimeout(() => void tendWants(), TEND_MS);
}

function refuse(id, problem, message) {
  wanted.setState((s) => ({ ...s, refused: { ...s.refused, [id]: { problem, message } } }));
}

/** Stops asking after the wants, for an account signing out. */
export function stopWants() {
  clearTimeout(timer);
  wanted.setState({ sub: null, list: null, refused: {} });
}
