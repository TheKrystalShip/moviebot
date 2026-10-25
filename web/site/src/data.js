// Reading the API for a page: what came back, whether it is still coming, and what went wrong.
//
// Nothing is cached across pages: the library, the rooms and the wishes are read when a page asks for
// them, so what a page shows is what the API says now. A page that shows something moving, the rooms,
// reads it again on an interval while it is open. A page navigated to is prepared out of sight until
// its first reads are answered (`LoadTracker`), so the read never shows as an empty page.
//
// A list read a page at a time is what Back and Forward return to as it was left: every page of it the
// visit held, in the order it held them, so the column opens where it was (`scrollMemory.js`) on the
// title it was left by. A reload reads the same number of pages again.

import React from "react";
import { sessionRecord } from "./visits.js";

/**
 * What a page being prepared out of sight waits on (`PageStage`): every read a page makes on mounting
 * reports when it starts and when it is answered, so the page is shown with its data rather than
 * empty. Later reads, a poll or a reload, are the page's own business and report nothing.
 */
export const LoadTracker = React.createContext(null);

/** The route a page is drawn for, with its visit's `entry` and `move` (`router.js`); `PageStage` provides it. */
export const PageRoute = React.createContext(null);

/** Each paged list a visit held, by entry and list: `{ base, titles, next, pages }`, as `usePaged` keeps it. */
const held = new Map();
const HELD_CAP = 30;

/** How many pages each paged list held, by entry and list, so a reload reads as many again. */
const pagesHeld = sessionRecord("cinema:paged", 60);

/** Which list each first page answered is for, so a list is never kept under another's key. */
const listOf = new WeakMap();

/** What tells one entry of a paged list from another: a title's kind and IMDb id, a shelf's id, else the object itself. */
const titleKey = (t) => (t && t.imdbId ? (t.kind || "") + t.imdbId : t && t.id ? "shelf:" + t.id : t);

/**
 * A list read a page at a time. `load(page)` answers `{ titles, next }` for the page asked, `next`
 * being the page that follows and absent on the last. The first page is read as `useLoad` reads, and
 * `loadMore` reads the page after the last one held and appends its titles, leaving out any it
 * already holds (a list ordered by how much a title is looked at can move one onto the next page
 * between two reads); new `deps` start over. `titles` is every title held, null before the first
 * page, and `next` is the page `loadMore` reads.
 *
 * Arrived at by Back or Forward, the list is the one the visit held when it was left, and is read
 * nothing; arrived at by a reload, it reads as many pages as it held, at once. A `reload` reads the
 * first page again.
 */
export function usePaged(load, deps, options) {
  const route = React.useContext(PageRoute);
  const list = route && route.entry ? route.entry + " " + JSON.stringify(deps) : null;
  const returned = route && (route.move === "pop" || route.move === "load");
  const lastList = React.useRef(null);
  const [more, setMore] = React.useState({ base: null, titles: [], next: undefined, pages: 1, loading: false, error: null });
  const first = useLoad(() => {
    const arriving = lastList.current !== list;
    lastList.current = list;
    const answered = (answer) => {
      if (answer && typeof answer === "object") listOf.set(answer, list);
      return answer;
    };
    const kept = arriving && returned && list ? held.get(list) : null;
    if (kept) {
      setMore({ base: kept.base, titles: kept.titles, next: kept.next, pages: kept.pages, loading: false, error: null });
      return Promise.resolve(kept.base);
    }
    const pages = arriving && returned && list ? pagesHeld.get(list) || 1 : 1;
    if (pages === 1) return load(1).then(answered);
    return readPages(load, pages).then((read) => {
      setMore({ ...read, loading: false, error: null });
      return answered(read.base);
    }, () => load(1).then(answered));
  }, deps, options);
  const base = first.data;
  const mine = more.base === base ? more : null;
  const next = base ? (mine ? mine.next : base.next) : undefined;

  React.useEffect(() => {
    if (!list || !base || listOf.get(base) !== list) return;
    const kept = { base, titles: mine ? mine.titles : [], next, pages: mine ? mine.pages : 1 };
    held.delete(list);
    held.set(list, kept);
    while (held.size > HELD_CAP) held.delete(held.keys().next().value);
    pagesHeld.set(list, kept.pages);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [list, base, more]);

  const loadMore = () => {
    if (!base || !next || (mine && mine.loading)) return;
    setMore((cur) => ({ ...(cur.base === base ? cur : { base, titles: [], next: base.next, pages: 1 }), loading: true, error: null }));
    load(next).then(
      (answer) => setMore((cur) => {
        if (cur.base !== base) return cur;
        const held = new Set([...base.titles, ...cur.titles].map(titleKey));
        const fresh = answer.titles.filter((t) => !held.has(titleKey(t)));
        return { ...cur, titles: [...cur.titles, ...fresh], next: answer.next, pages: cur.pages + 1, loading: false };
      }),
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
 * Pages 1 to `pages` of a list, read at once and held as `usePaged` holds them: the first as `base`,
 * the titles of the rest after it, leaving out any already held, and the page after the last.
 */
function readPages(load, pages) {
  return Promise.all(Array.from({ length: pages }, (_, i) => load(i + 1))).then(([base, ...rest]) => {
    const seen = new Set(base.titles.map(titleKey));
    const titles = [];
    for (const answer of rest) {
      for (const t of answer.titles) {
        const key = titleKey(t);
        if (!seen.has(key)) {
          seen.add(key);
          titles.push(t);
        }
      }
    }
    return { base, titles, next: rest.length ? rest[rest.length - 1].next : base.next, pages };
  });
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
