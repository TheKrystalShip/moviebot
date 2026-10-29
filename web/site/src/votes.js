// What the signed-in person likes and dislikes, as the API keeps it (`GET /api/me/votes`): what lights a
// Like or a Dislike wherever one is drawn, on a page, on a card and in a card's sheet, and what the taste
// page lists. A vote is on a subject, `{ kind, id }`: a title by its IMDb id, a person by the catalogue's
// person id, a franchise by its id (`c<collection>` or a Wikidata item), a company by its id. It is read
// once for every button on every page, while anything reading it is mounted, and again whenever the page
// comes back into view, the connection comes back or somebody else signs in, so a vote cast on another
// device lights here the next time this page is looked at.
//
// Every vote cast, moved or cleared here goes through it: a button changes the set at once, and a read
// answered while one is under way is set aside and asked again once it is done, so an answer from before
// the change never undoes it. Until the first read is answered nothing is known, and a button draws
// neither lit.

import React from "react";
import { createStore, useStore } from "@thekrystalship/krystal";
import { meApi } from "./api/index.js";
import { onReconnect } from "./connectivity.js";
import { LoadTracker } from "./data.js";
import { sessionStore } from "./session.js";

/** The kinds of subject a vote is on, as the API names them. */
export const VOTE_KINDS = ["title", "person", "franchise", "company"];

const keyOf = (kind, id) => `${kind}:${id}`;

/** The votes as the API answered them, newest first, and by subject. Null until the first read. */
const store = createStore({ list: null, byKey: null, error: null });
let watchers = 0;
let reading = null;
let changing = 0;
let changed = false;
let stopReconnect = null;
let stopSession = null;
let userId = null;

function keep(list) {
  store.setState({ list, byKey: list ? new Map(list.map((v) => [keyOf(v.kind, v.id), v])) : null, error: null });
}

function refresh() {
  if (reading) return reading;
  if (!(sessionStore.getState() || {}).user) {
    keep(null);
    return Promise.resolve();
  }
  changed = false;
  reading = meApi.votes().then(
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
 * The person's vote on a subject: `1` liked, `-1` disliked, `0` neither, or `undefined` while it is not
 * known. A page being prepared out of sight waits for the first read, so its buttons are drawn lit.
 */
export function useVote(kind, id) {
  useWatching();
  return useStore(store, (s) => {
    if (!s.byKey || !kind || id === null || id === undefined) return undefined;
    const held = s.byKey.get(keyOf(kind, String(id)));
    return held ? held.vote : 0;
  });
}

/**
 * Every vote the person has, newest first, as `{ list, error, reload }`: `list` is null while it is not
 * known, and `error` is why the last read failed.
 */
export function useVoteList() {
  useWatching();
  const { list, error } = useStore(store);
  return { list, error, reload: refreshVotes };
}

/** Asks again what the person voted. */
export function refreshVotes() {
  changed = true;
  if (!reading) refresh();
}

/**
 * Casts, moves or clears the person's vote on a subject: `vote` is `1`, `-1`, or `0` to clear it. The
 * list shows it at once, keeps it if the API agrees and takes it back if it refuses, and the refusal is
 * thrown for the caller to say. `name` is what a vote not yet in the list is called until the next read.
 */
export async function setVote(kind, id, vote, { name } = {}) {
  const subject = String(id);
  const key = keyOf(kind, subject);
  changing += 1;
  changed = true;
  const { list: undo } = store.getState();
  if (undo) {
    const rest = undo.filter((v) => keyOf(v.kind, v.id) !== key);
    const held = undo.find((v) => keyOf(v.kind, v.id) === key);
    keep(vote === 0 ? rest : [{ ...(held || { kind, id: subject, name: name || "" }), vote, votedAt: new Date().toISOString() }, ...rest]);
  }
  try {
    return vote === 0 ? await meApi.unvote(kind, subject) : await meApi.vote(kind, subject, vote);
  } catch (error) {
    if (undo) keep(undo);
    throw error;
  } finally {
    changing -= 1;
    if (changing === 0 && watchers > 0) refresh();
  }
}
