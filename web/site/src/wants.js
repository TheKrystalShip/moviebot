// What the signed-in person wants to watch, as the API keeps it (`GET /api/me/wants`): what marks a
// card's ribbon and fills the Wants page. It is read once for every card on every page, while anything
// reading it is mounted, and again whenever the page comes back into view, the connection comes back
// or somebody else signs in, so a want made on another device is marked the next time this page is
// looked at.
//
// Every want made, changed or removed here goes through it: a ribbon or the Want button changes the set
// at once, and a read answered while one is under way is set aside and asked again once it is done, so
// an answer from before the change never undoes it. Until the first read is answered nothing is known,
// and a card draws no ribbon rather than one that says unwanted.

import React from "react";
import { createStore, useStore } from "@thekrystalship/krystal";
import { api } from "./api.js";
import { onReconnect } from "./connectivity.js";
import { LoadTracker } from "./data.js";
import { sessionStore } from "./session.js";

/** The wants as the API answered them, newest first, and by IMDb id. Null until the first read. */
const store = createStore({ list: null, byId: null, error: null });
let watchers = 0;
let reading = null;
let changing = 0;
let changed = false;
let stopReconnect = null;
let stopSession = null;
let userId = null;

function keep(list) {
  store.setState({ list, byId: list ? new Map(list.map((w) => [w.imdbId, w])) : null, error: null });
}

function refresh() {
  if (reading) return reading;
  if (!(sessionStore.getState() || {}).user) {
    keep(null);
    return Promise.resolve();
  }
  changed = false;
  reading = api.wants().then(
    (list) => { if (!changed && changing === 0) keep(list); },
    (error) => { store.setState({ error }); },
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
  keep(null);
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

/** Keeps the store read while the component is mounted, and holds a page being prepared until the first read is answered. */
function useWatching() {
  const tracker = React.useContext(LoadTracker);
  React.useEffect(() => {
    watch();
    if (tracker && reading && !store.getState().list) {
      tracker.begin();
      reading.finally(() => tracker.end());
    }
    return unwatch;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
}

/**
 * The person's want on a title, as `GET /api/me/wants` describes it: the want, `null` when they have
 * none, or `undefined` while it is not known. A page being prepared out of sight waits for the first
 * read, so its cards are shown with their ribbons.
 */
export function useWant(imdbId) {
  useWatching();
  return useStore(store, (s) => (s.byId && imdbId ? s.byId.get(imdbId) || null : undefined));
}

/**
 * Every want the person has, newest first, as `{ list, error, reload }`: `list` is null while it is not
 * known, and `error` is why the last read failed.
 */
export function useWantList() {
  useWatching();
  const { list, error } = useStore(store);
  return { list, error, reload: refreshWants };
}

/** Asks again what the person wants: the Wants page does while a title is still being fetched. */
export function refreshWants() {
  changed = true;
  if (!reading) refresh();
}

/**
 * Runs a change to what the person wants: `before` is the list as it shows the change at once, kept if
 * `work` succeeds and taken back if it fails. A read answered meanwhile is set aside and asked again.
 */
async function change(work, before) {
  changing += 1;
  changed = true;
  const { list: undo } = store.getState();
  if (undo) keep(before(undo));
  try {
    return await work();
  } catch (error) {
    if (undo) keep(undo);
    throw error;
  } finally {
    changing -= 1;
    if (changing === 0 && watchers > 0) refresh();
  }
}

/**
 * Makes or changes the person's want on a title. A title not yet wanted is visible to everybody unless
 * `visible` is false; a title already wanted keeps its visibility unless `visible` says otherwise.
 * Answers the API's `SetWantReply`.
 */
export function setWant(imdbId, kind, { visible, title } = {}) {
  return change(() => api.want(imdbId, kind, visible), (list) => {
    const held = list.find((w) => w.imdbId === imdbId);
    if (held) return list.map((w) => (w === held ? { ...held, visible: visible === undefined ? held.visible : visible } : w));
    const placeholder = {
      imdbId, kind, title: title || "", genres: [], visible: visible !== false, since: new Date().toISOString(),
      inLibrary: false, watched: false, others: 0,
    };
    return [placeholder, ...list];
  });
}

/** Removes the person's want on a title. */
export function removeWant(imdbId) {
  return change(() => api.unwant(imdbId), (list) => list.filter((w) => w.imdbId !== imdbId));
}

/** What a made want found, in a sentence for the toast that says so; null when it found nothing to add to the button's change. */
export function wantWords(reply, name) {
  switch (reply && reply.outcome) {
    case "waiting": return `You will be told when ${name} is out.`;
    case "available": return `${name} is out and can be fetched.`;
    case "inLibrary": return `${(reply && reply.libraryName) || name} can be watched now.`;
    case "downloading": return `${name} is on its way.`;
    case "alreadyWanted": return `You already want ${name}.`;
    default: return null;
  }
}

/** How many other people want the title with a want that names them, as a card says it. */
export function othersWords(others) {
  return others > 0 ? `${others} ${others === 1 ? "other wants" : "others want"} it` : null;
}
