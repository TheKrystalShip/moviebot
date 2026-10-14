// This person's notifications, as the bell shows them: read from the API while a page is open, every
// minute and whenever the page comes back into view, so a push that arrived while the app was
// closed is in the bell the moment it is opened.

import React from "react";
import { createStore, useStore } from "@thekrystalship/krystal";
import { api } from "./api.js";

const EVERY_MS = 60 * 1000;

const store = createStore({ unread: 0, items: null, error: null });
let watchers = 0;
let timer = null;

async function refresh() {
  try {
    const reply = await api.notifications();
    store.setState({ unread: reply.unread, items: reply.items, error: null });
  } catch (error) {
    store.setState({ error });
  }
}

const onVisible = () => { if (document.visibilityState === "visible") refresh(); };

function watch() {
  if (watchers++ > 0) return;
  refresh();
  timer = setInterval(refresh, EVERY_MS);
  document.addEventListener("visibilitychange", onVisible);
}

function unwatch() {
  if (--watchers > 0) return;
  clearInterval(timer);
  document.removeEventListener("visibilitychange", onVisible);
}

/** The bell's state, kept fresh while any component reading it is mounted. */
export function useNotifications() {
  React.useEffect(() => { watch(); return unwatch; }, []);
  return useStore(store);
}

/** Marks the named notifications read, or every one when none are named, and shows it at once. */
export async function markRead(ids) {
  store.setState((s) => {
    const items = (s.items || []).map((n) => (!ids || ids.includes(n.id) ? { ...n, read: true } : n));
    return { ...s, items, unread: items.filter((n) => !n.read).length };
  });
  try {
    await api.markRead(ids);
  } finally {
    refresh();
  }
}

/** How each kind is drawn: its icon and the tone of the square it sits in. */
export const KIND = {
  available: { icon: "bell-ring", tone: "teal" },
  ready: { icon: "circle-check", tone: "success" },
  invite: { icon: "user-plus", tone: "teal" },
  episode: { icon: "tv", tone: "teal" },
  shared: { icon: "bookmark", tone: "teal" },
};
