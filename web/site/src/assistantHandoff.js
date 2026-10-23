// Words handed to the assistant as the first message of a new conversation. A page asks with
// `askAssistant`; the shell opens the assistant (the dock, or its page on a phone) and the chat that
// opens sends them once (`CinemaAssistant.jsx`). A request nobody took within `HANDOFF_MS` is dropped,
// so an assistant opened later never sends words from long before.

import React from "react";

/** How long a request waits for an assistant to be there to take it. */
export const HANDOFF_MS = 15000;

let pending = null;
const listeners = new Set();

const change = (next) => {
  pending = next;
  listeners.forEach((l) => l());
};

/** Hands `text` to the assistant as the first message of a new conversation. */
export function askAssistant(text) {
  change({ text, at: Date.now() });
}

/** Marks a request as taken, so no other chat sends it. */
export function settleHandoff(request) {
  if (pending === request) change(null);
}

const subscribe = (listener) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

/** The request waiting to be taken, or null. */
export function useHandoff() {
  return React.useSyncExternalStore(subscribe, () => pending);
}
