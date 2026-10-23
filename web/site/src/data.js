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

/**
 * A list read a page at a time. `load(page)` answers `{ titles, next }` for the page asked, `next`
 * being the page that follows and absent on the last. The first page is read as `useLoad` reads, and
 * `loadMore` reads the page after the last one held and appends its titles; new `deps` start over.
 * `titles` is every title held, null before the first page, and `next` is the page `loadMore` reads.
 */
export function usePaged(load, deps, options) {
  const first = useLoad(() => load(1), deps, options);
  const [more, setMore] = React.useState({ base: null, titles: [], next: undefined, loading: false, error: null });
  const base = first.data;
  const mine = more.base === base ? more : null;
  const next = base ? (mine ? mine.next : base.next) : undefined;

  const loadMore = () => {
    if (!base || !next || (mine && mine.loading)) return;
    setMore((cur) => ({ ...(cur.base === base ? cur : { base, titles: [], next: base.next }), loading: true, error: null }));
    load(next).then(
      (answer) => setMore((cur) => (cur.base !== base ? cur : { ...cur, titles: [...cur.titles, ...answer.titles], next: answer.next, loading: false })),
      (error) => setMore((cur) => (cur.base !== base ? cur : { ...cur, loading: false, error })));
  };

  return {
    ...first,
    titles: base ? [...base.titles, ...(mine ? mine.titles : [])] : null,
    next,
    loadingMore: !!mine && mine.loading,
    moreError: mine ? mine.error : null,
    loadMore,
  };
}

/**
 * One read for a page: `load` is asked when the page mounts and again when `deps` change, every
 * `every` milliseconds when given, and on `reload`. `known` is an answer the page already holds, from
 * a read that carried it: the read starts answered with it and asks nothing until a reload or new deps.
 */
export function useLoad(load, deps, { every, known } = {}) {
  const [state, setState] = React.useState(() => known != null
    ? { data: known, error: null, loading: false }
    : { data: null, error: null, loading: true });
  const [tick, setTick] = React.useState(0);
  const tracker = React.useContext(LoadTracker);
  const first = React.useRef(true);

  React.useEffect(() => {
    // What the page already holds is the first answer; a reload or other deps ask again.
    if (first.current && known != null) {
      first.current = false;
      return undefined;
    }
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
