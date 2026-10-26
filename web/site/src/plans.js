// A plan page is told when a plan changes (`PlanChanged` on the person's own hub connection, which the
// API sends to every person in the plan), so it reads the plan again instead of polling. The connection
// is held only while a page uses it, and a reconnect asks again for whatever was missed.

import React from "react";
import { holdPersonConnection } from "./personConnection.js";

/** How soon a time may be put forward, in minutes, and how far ahead, in days (the API's `PlanLimits`). */
export const SOONEST_MINUTES = 5;
export const FURTHEST_DAYS = 30;

/** The most times a plan holds still ahead. */
export const MAX_TIMES = 6;

/** An instant as a `datetime-local` input holds it: local, to the minute. */
export function localInput(at) {
  const shifted = new Date(at.getTime() - at.getTimezoneOffset() * 60000);
  return shifted.toISOString().slice(0, 16);
}

/** The earliest and latest a `datetime-local` input offers for a plan's time, from `now`. */
export function timeBounds(now = Date.now()) {
  return {
    min: localInput(new Date(now + SOONEST_MINUTES * 60000)),
    max: localInput(new Date(now + FURTHEST_DAYS * 24 * 3600000)),
  };
}

/** Calls `onChange(id)` when a plan the person is in changes, and `onChange(null)` after a reconnect. */
export function usePlanChanges(onChange) {
  const latest = React.useRef(onChange);
  latest.current = onChange;
  React.useEffect(() => {
    let connected = false;
    const held = holdPersonConnection({
      listen: (connection) => connection.on("PlanChanged", (id) => latest.current(id)),
      opened: () => {
        if (connected) latest.current(null);
        connected = true;
      },
    });
    return () => held.stop();
  }, []);
}
