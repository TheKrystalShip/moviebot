/**
 * What the phone's remote shows of the television's own settings, and how it changes them: the person's
 * preferences (subtitle style, quality) and the tracks they watch the film with, both kept by the API.
 *
 * The phone and the television read the same records and report nothing to each other. The phone writes
 * through the API, which checks the change against the film and pushes it to the person's connections
 * (`PreferencesChanged`, `TracksChanged`); the remote's connection hears it here and a television playing
 * the film applies it. What a label or a checked row shows is the record as the API holds it, read when the
 * remote connects and when the film changes, and kept current by the push. A change shows at once and is
 * confirmed or taken back by what the API then says.
 */

import { createStore, toast, useStore } from "@thekrystalship/krystal";
import { meApi } from "./api/index.js";
import { Ordered } from "./player/engine/session/ordered.ts";

/** How long a style change waits for the next before it is sent, so a slider dragged across its range is one write. */
const StyleSettleMs = 250;

const nothing = { preferences: null, tracks: null };

/** The person's preferences, and the tracks they watch the film the remote's room holds with (null while neither is read). */
const settingsStore = createStore(nothing);

let titleId = null;
let reading = 0;
let styleSave = null;

/** Reads both records again: the preferences, and the film's tracks when the room holds one. A failed read leaves what is held. */
export async function refreshSettings() {
  const mine = ++reading;
  const asking = titleId;
  try {
    const [preferences, tracks] = await Promise.all([meApi.preferences(), asking ? meApi.tracks(asking) : Promise.resolve(null)]);
    if (mine !== reading) return;
    settingsStore.setState((held) => ({
      preferences: styleSave === null ? preferences : { ...preferences, subtitleStyle: held.preferences && held.preferences.subtitleStyle },
      tracks: asking === titleId ? tracks : held.tracks,
    }));
  } catch {
    // The labels keep what they had; the push brings the next change.
  }
}

/** The room holds this film now (or none): its tracks are read, and the previous film's are dropped. */
export function followFilm(id) {
  if (id === titleId) return;
  titleId = id;
  settingsStore.setState((held) => ({ ...held, tracks: null }));
  if (id) void refreshSettings();
}

/**
 * The pushes, applied in the order the API kept the writes (`session/ordered.ts`): the record replaces the
 * one held, except a style being chosen here, and the tracks are kept when they are the film the room holds.
 */
const preferencesOrder = new Ordered((preferences) => {
  settingsStore.setState((held) => ({
    ...held,
    preferences: styleSave === null || !held.preferences ? preferences : { ...preferences, subtitleStyle: held.preferences.subtitleStyle },
  }));
});
const tracksOrders = new Map();
function tracksOrder(film) {
  if (!tracksOrders.has(film)) {
    tracksOrders.set(film, new Ordered((tracks) => {
      if (tracks.titleId === titleId) settingsStore.setState((held) => ({ ...held, tracks }));
    }));
  }
  return tracksOrders.get(film);
}

/** The API says how the person watches changed. */
export function heardPreferences(preferences) {
  preferencesOrder.heard(preferences);
}

/** The API says the tracks of a film changed. */
export function heardTracks(tracks) {
  tracksOrder(tracks.titleId).heard(tracks);
}

/** A write to the API, held against the pushes until it has answered, and reported to `order`. */
async function writeThrough(order, send) {
  const done = order.writing();
  try {
    const answer = await send();
    done(answer);
    return answer;
  } catch (error) {
    done(null);
    throw error;
  }
}

/** Lets go of what was read, for a remote that ended. */
export function forgetSettings() {
  reading += 1;
  titleId = null;
  if (styleSave !== null) window.clearTimeout(styleSave);
  styleSave = null;
  settingsStore.setState(nothing);
}

/** The language the person's subtitles start in, as the API holds it, or undefined. */
export function subtitleLanguage() {
  const held = settingsStore.getState().preferences;
  return held ? held.subtitleLanguage : undefined;
}

/** The records for the film the room holds. */
export function useRemoteSettings() {
  return useStore(settingsStore, (s) => s);
}

/** The preferences as held, read first when the remote has not yet heard them. */
async function preferencesNow() {
  if (!settingsStore.getState().preferences) await refreshSettings();
  const held = settingsStore.getState().preferences;
  if (!held) throw new Error("Your settings could not be read.");
  return held;
}

/** Writes through, and puts the records back as the API holds them when it refused. */
async function writing(write) {
  try {
    return await write();
  } catch (error) {
    void refreshSettings();
    throw error;
  }
}

/** What a phone changes about the television's own settings. Each shows at once and the API's answer confirms it. */
export const settingsCommands = {
  chooseSubtitle: (film, id) => {
    settingsStore.setState((held) => ({
      ...held,
      tracks: { ...(held.tracks || { titleId: film }), titleId: film, subtitleId: id === null ? undefined : id, subtitleOff: id === null },
    }));
    return writing(() => writeThrough(tracksOrder(film), () => meApi.setTracks(film, id === null ? { subtitleOff: true } : { subtitleId: id })));
  },
  chooseAudio: (film, id) => {
    settingsStore.setState((held) => ({ ...held, tracks: { ...(held.tracks || { titleId: film }), titleId: film, audioId: id } }));
    return writing(() => writeThrough(tracksOrder(film), () => meApi.setTracks(film, { audioId: id })));
  },
  chooseQuality: async (height) => {
    const held = await preferencesNow();
    settingsStore.setState((s) => ({ ...s, preferences: { ...held, qualityHeight: height } }));
    return writing(() => writeThrough(preferencesOrder, () => meApi.setPreferences({ ...held, qualityHeight: height })));
  },
  /**
   * The look of subtitles: shown at once, sent once the choosing pauses. Every send carries the whole
   * stored style, and one that fails says so and puts the record back.
   */
  setStyle: (style) => {
    settingsStore.setState((s) => ({ ...s, preferences: { ...(s.preferences || {}), subtitleStyle: style } }));
    if (styleSave !== null) window.clearTimeout(styleSave);
    styleSave = window.setTimeout(() => {
      styleSave = null;
      const held = settingsStore.getState().preferences;
      if (held) writing(() => writeThrough(preferencesOrder, () => meApi.setPreferences(held))).catch((error) => toast.fromError(error, "The TV did not take that"));
    }, StyleSettleMs);
  },
};
