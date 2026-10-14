// What the signed-in person has saved, as the IMDb ids on every list they can save to: what marks a
// card's ribbon. It is read once for every card on every page, while anything reading it is mounted,
// and again whenever the page comes back into view, the connection comes back or somebody else signs
// in, so a title saved on another device or by somebody sharing a list is marked the next time this
// page is looked at.
//
// Every save and unsave made here goes through it: a ribbon or the Save button changes the set at
// once, and a read answered while one is under way is set aside and asked again once it is done, so
// an answer from before the change never undoes it. Until the first read is answered nothing is
// known, and a card draws no ribbon rather than one that says unsaved.

import React from "react";
import { createStore, useStore } from "@thekrystalship/krystal";
import { api } from "./api.js";
import { onReconnect } from "./connectivity.js";
import { LoadTracker } from "./data.js";
import { sessionStore } from "./session.js";

const store = createStore({ ids: null });
let watchers = 0;
let reading = null;
let changing = 0;
let changed = false;
let stopReconnect = null;
let stopSession = null;
let userId = null;

function refresh() {
  if (reading) return reading;
  if (!(sessionStore.getState() || {}).user) {
    store.setState({ ids: null });
    return Promise.resolve();
  }
  changed = false;
  reading = api.saved().then(
    (ids) => { if (!changed && changing === 0) store.setState({ ids: new Set(ids) }); },
    () => {},
  ).finally(() => {
    reading = null;
    if (changed && changing === 0) refresh();
  });
  return reading;
}

const onVisible = () => { if (document.visibilityState === "visible") refresh(); };

function onSession() {
  const id = ((sessionStore.getState() || {}).user || {}).id || null;
  if (id === userId) return;
  userId = id;
  store.setState({ ids: null });
  refresh();
}

function watch() {
  if (watchers++ > 0) return;
  userId = ((sessionStore.getState() || {}).user || {}).id || null;
  refresh();
  document.addEventListener("visibilitychange", onVisible);
  stopReconnect = onReconnect(refresh);
  stopSession = sessionStore.subscribe(onSession);
}

function unwatch() {
  if (--watchers > 0) return;
  document.removeEventListener("visibilitychange", onVisible);
  stopReconnect();
  stopSession();
}

/**
 * Whether the person saved a title: true or false, or null while it is not known. A page being
 * prepared out of sight waits for the first read, so its cards are shown with their ribbons.
 */
export function useSaved(imdbId) {
  const tracker = React.useContext(LoadTracker);
  React.useEffect(() => {
    watch();
    if (tracker && reading && !store.getState().ids) {
      tracker.begin();
      reading.finally(() => tracker.end());
    }
    return unwatch;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return useStore(store, (s) => (s.ids && imdbId ? s.ids.has(imdbId) : null));
}

/** Marks a title saved or not at once, as a change made here says it is. */
export function markSaved(imdbId, on) {
  const { ids } = store.getState();
  if (!ids || ids.has(imdbId) === on) return;
  const next = new Set(ids);
  if (on) next.add(imdbId); else next.delete(imdbId);
  store.setState({ ids: next });
}

/**
 * Runs a change to what the person saved: `before` is the set as it shows the change at once, kept if
 * `work` succeeds and taken back if it fails. A read answered meanwhile is set aside and asked again.
 */
export async function changeSaved(work, before) {
  changing += 1;
  changed = true;
  const undo = store.getState().ids;
  if (before) before();
  try {
    return await work();
  } catch (error) {
    if (undo) store.setState({ ids: undo });
    throw error;
  } finally {
    changing -= 1;
    if (changing === 0 && watchers > 0) refresh();
  }
}

/** Asks again which titles are saved, after a change to whole lists: one deleted, joined or left. */
export function refreshSaved() {
  changed = true;
  if (!reading) refresh();
}
