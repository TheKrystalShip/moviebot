// Reading the API for a page: what came back, whether it is still coming, and what went wrong.
//
// Nothing is cached across pages: the library, the rooms and the wishes are read when a page asks for
// them, so what a page shows is what the API says now. A page that shows something moving, the rooms,
// reads it again on an interval while it is open. A page navigated to is prepared out of sight until
// its first reads are answered (`LoadTracker`), so the read never shows as an empty page.

import React from "react";

/**
 * What a page being prepared out of sight waits on (`PageStage`): every read a page makes on mounting
 * reports when it starts and when it is answered, so the page is shown with its data rather than
 * empty. Later reads, a poll or a reload, are the page's own business and report nothing.
 */
export const LoadTracker = React.createContext(null);

export function useLoad(load, deps, { every } = {}) {
  const [state, setState] = React.useState({ data: null, error: null, loading: true });
  const [tick, setTick] = React.useState(0);
  const tracker = React.useContext(LoadTracker);
  const first = React.useRef(true);

  React.useEffect(() => {
    let live = true;
    let pending = first.current && tracker ? tracker : null;
    first.current = false;
    if (pending) pending.begin();
    const settled = () => {
      if (pending) pending.end();
      pending = null;
    };
    load().then(
      (data) => { if (live) setState({ data, error: null, loading: false }); settled(); },
      (error) => { if (live) setState((prev) => ({ data: prev.data, error, loading: false })); settled(); });
    return () => { live = false; settled(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);

  React.useEffect(() => {
    if (!every) return undefined;
    const id = setInterval(() => setTick((t) => t + 1), every);
    return () => clearInterval(id);
  }, [every]);

  return { ...state, reload: () => setTick((t) => t + 1) };
}
