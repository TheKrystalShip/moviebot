// Reading the API for a page: what came back, whether it is still coming, and what went wrong.
//
// Nothing is cached across pages: the library, the downloads and the keeps are read when a page asks
// for them, so what a page shows is what the API says now. A page that shows something moving, a
// download, reads it again on an interval while it is open.

import React from "react";

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

/** How long a film stays, from the seconds the retention rule gives, in a few words. */
export function staysFor(kept) {
  if (!kept) return null;
  if (kept.keeper) return "Kept";
  const seconds = kept.isFinished ? kept.remainingSeconds : kept.windowSeconds;
  const days = seconds / 86400;
  if (days >= 1) return `${Math.ceil(days)} ${Math.ceil(days) === 1 ? "day" : "days"} left`;
  const hours = Math.max(1, Math.ceil(seconds / 3600));
  return `${hours} ${hours === 1 ? "hour" : "hours"} left`;
}
