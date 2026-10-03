// Reading the API for a page: what came back, whether it is still coming, and what went wrong.
//
// Nothing is cached across pages: the library, the downloads and the stays are read when a page asks
// for them, so what a page shows is what the API says now. A page that shows something moving, a
// download, reads it again on an interval while it is open.

import React from "react";
import { daysLeft } from "./time.js";

export function useLoad(load, deps, { every } = {}) {
  const [state, setState] = React.useState({ data: null, error: null, loading: true });
  const [tick, setTick] = React.useState(0);

  React.useEffect(() => {
    let live = true;
    load().then(
      (data) => live && setState({ data, error: null, loading: false }),
      (error) => live && setState((prev) => ({ data: prev.data, error, loading: false })));
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);

  React.useEffect(() => {
    if (!every) return undefined;
    const id = setInterval(() => setTick((t) => t + 1), every);
    return () => clearInterval(id);
  }, [every]);

  return { ...state, reload: () => setTick((t) => t + 1) };
}

/** The library's rule in words, from the stay the API answered, which carries the rule itself. */
export function leaveRule(stay) {
  const days = stay && stay.leaveAfterDays;
  if (!days) return "Leaves after a while without a play";
  return `Leaves after ${days} ${days === 1 ? "day" : "days"} without a play`;
}

/** Whoever kept a film, as the person asking reads it: "you" for their own keep. */
export function keeperOf(stay, me) {
  if (!stay || !stay.keeperId) return null;
  return me && stay.keeperId === me.id ? "you" : stay.keeperName || "someone";
}

/** How long a film stays, in a few words: "kept by Jo", "3 days left", or nothing before it has settled. */
export function stayWords(stay, me) {
  if (!stay || !stay.settled) return null;
  if (stay.keeperId) return `kept by ${keeperOf(stay, me)}`;
  return stay.daysLeft == null ? null : daysLeft(stay.daysLeft);
}
