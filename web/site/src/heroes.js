// The slides at the top of Home and of Discover, on every surface. A page names what it would put
// there, each as a candidate; `useSlides` describes each one and hands back those ready to be shown.
//
// A candidate is `{ key, eyebrow, icon, kind, imdbId }` and one of: `title`, a library film (with
// `room` when people are watching it now); `show`, a library show; or `found`, a catalogue card, with
// `here` the library's film by that id and `held` whether the library holds an episode of a show.
// `upcoming` marks a film that is not out yet.

import React from "react";
import { catalogue, queryParams } from "./api/index.js";
import { LoadTracker, useLoad } from "./data.js";
import { since } from "./time.js";
import { cardSpot, showSpot, titleSpot } from "./spots.js";
import { waitersWords } from "./components/TitleCards.jsx";

/** The most slides a page's top turns through. */
export const MOST_SLIDES = 6;

/**
 * The candidates described, each with its `spot` (`spots.js`), in the order given, and `ready` once its
 * artwork has arrived. A candidate the catalogue does not list is left out. The slides only grow:
 * one is handed back once every candidate before it is described or left out, so the slide on screen
 * never moves, and every one described is laid out at once, so a top as tall as its tallest slide has
 * its height before it is shown. Only the ready ones are turned to (`readySlides`), so a slide never
 * turns to a blank. Until the first is ready, or all are known with none to show, there is nothing to
 * hand back (`null`).
 *
 * The first slide is one of the page's first reads (`LoadTracker`): a page prepared out of sight, and
 * the first page under the splash, is shown with its top in place, and the slides after it arrive
 * while it is up. They are described from the commit that names them, before it is drawn, so the
 * page's first reads are not a frame apart from the next.
 */
export function useSlides(candidates) {
  const [described, setDescribed] = React.useState({});
  const [arrived, setArrived] = React.useState({});
  const ids = candidates ? candidates.map((c) => c.key).join("|") : "";
  const tracker = React.useContext(LoadTracker);
  const answered = React.useRef(null);
  const slides = [];
  for (const c of candidates || []) {
    if (!(c.key in described)) break;
    if (described[c.key]) slides.push({ ...c, spot: described[c.key], ready: !!arrived[c.key] });
  }
  const settled = !!candidates && candidates.every((c) => c.key in described && (!described[c.key] || arrived[c.key]));
  const shown = !!candidates && (readySlides(slides).length > 0 || settled);

  React.useEffect(() => {
    if (!tracker) return undefined;
    tracker.begin();
    let open = true;
    answered.current = () => { if (open) { open = false; tracker.end(); } };
    return answered.current;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  React.useEffect(() => {
    if (shown && answered.current) answered.current();
  }, [shown]);

  React.useLayoutEffect(() => {
    if (!candidates) return undefined;
    let live = true;
    candidates.forEach((c, i) => {
      const arrive = () => { if (live) setArrived((prev) => ({ ...prev, [c.key]: true })); };
      describe(c).then((spot) => {
        if (!live) return;
        setDescribed((prev) => ({ ...prev, [c.key]: spot }));
        if (spot && spot.art) artArrives(spot.art, i === 0).then(arrive);
        else arrive();
      }, () => { if (live) setDescribed((prev) => ({ ...prev, [c.key]: null })); });
    });
    return () => { live = false; };
    // The candidates are fixed once given; their keys say when they are not.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ids]);

  return shown ? slides : null;
}

/** The slides that can be turned to: those from the first whose artwork has arrived, up to one that has not. */
export function readySlides(slides) {
  if (!slides) return [];
  const i = slides.findIndex((s) => !s.ready);
  return i < 0 ? slides : slides.slice(0, i);
}

/**
 * A candidate's spot, from the title's catalogue page, which carries its credits; a page that cannot
 * be had describes a library title from what the library holds.
 */
function describe(c) {
  const page = c.imdbId ? catalogue.title(c.imdbId).catch(() => null) : Promise.resolve(null);
  if (c.title) return page.then((p) => titleSpot(c.title, c.eyebrow, p));
  if (c.show) return page.then((p) => showSpot(c.show, c.eyebrow, p));
  return page.then((p) => (p ? cardSpot(p, c.eyebrow, p) : c.found ? cardSpot(c.found, c.eyebrow) : null));
}

/** The artwork decoded; a slide not shown first fetches it behind what is on screen. */
function artArrives(src, first) {
  const image = new Image();
  if (!first) image.fetchPriority = "low";
  image.src = src;
  return image.decode().catch(() => undefined);
}

/**
 * What the front page turns through: the film people are watching together now, the newest film on
 * the shelf, the title most watched here lately, the film and the show trending this week, and the
 * film most waited on here, or else the next one out. Each one is left out
 * when there is none, and a title is shown once. They are chosen once everything they are chosen
 * from has answered, and kept while the page is open.
 *
 * `library` is the library's films and episodes, `shows` its shows, `rooms` the rooms open now, and
 * `waitedOn` the films waited on here (`/api/anticipated`), `null` until it answers.
 */
export function useHomeSlides({ library, shows, rooms, waitedOn }) {
  const few = { size: 12 };
  const popularHere = useLoad(() => catalogue.titles({ here: "watched", ...few }).catch(() => null), []);
  const films = useLoad(() => catalogue.titles({ ranking: "trending-films", ...few }).catch(() => null), []);
  const showsWeek = useLoad(() => catalogue.titles({ ranking: "trending-shows", ...few }).catch(() => null), []);
  const soon = useLoad(() => catalogue.titles({ ranking: "upcoming", ...few }).catch(() => null), []);
  const chosen = React.useRef(null);

  const settled = library && shows && rooms && waitedOn
    && !popularHere.loading && !films.loading && !showsWeek.loading && !soon.loading;
  if (settled && !chosen.current) {
    chosen.current = homeCandidates({
      library, shows, rooms, waitedOn,
      popularHere: popularHere.data, films: films.data, showsWeek: showsWeek.data, soon: soon.data,
    });
  }

  return useSlides(chosen.current);
}

function homeCandidates({ library, shows, rooms, waitedOn, popularHere, films, showsWeek, soon }) {
  const byId = new Map(library.map((t) => [t.id, t]));
  const filmsHere = new Map(library.filter((t) => t.kind !== "show" && t.film && t.film.imdbId).map((t) => [t.film.imdbId, t]));
  const heldShows = new Set(shows.map((s) => s.imdbId).filter(Boolean));
  const out = [];
  const seen = new Set();
  const add = (c) => {
    if (!c || (c.imdbId && seen.has(c.imdbId)) || out.length >= MOST_SLIDES) return;
    if (c.imdbId) seen.add(c.imdbId);
    out.push(c);
  };
  const fromShelf = (shelf, key, eyebrow, icon, extra) => {
    const titles = shelf ? shelf.titles : [];
    const watched = (x) => !!(x.you && x.you.watched);
    const t = titles.find((x) => !watched(x) && !seen.has(x.id)) || titles.find((x) => !seen.has(x.id));
    if (!t) return null;
    return {
      key, eyebrow, icon, kind: t.kind, imdbId: t.id, found: t,
      here: t.kind === "film" ? filmsHere.get(t.id) : null, held: t.kind === "show" && heldShows.has(t.id), ...extra,
    };
  };

  const room = rooms
    .filter((r) => r.titleId && r.participants > 0 && byId.has(r.titleId))
    .sort((a, b) => b.participants - a.participants)[0];
  if (room) {
    const t = byId.get(room.titleId);
    add({ key: "room", eyebrow: `Watching now · ${room.participants} watching`, icon: "users", kind: "film", imdbId: t.film && t.film.imdbId, title: t, room });
  }

  const newest = newestFilm(library.filter((t) => t.kind !== "show"));
  if (newest) {
    add({
      key: "newest", eyebrow: newest.addedAt ? `Added ${since(newest.addedAt).toLowerCase()}` : "From the library", icon: "sparkles",
      kind: "film", imdbId: newest.film && newest.film.imdbId, title: newest,
    });
  }

  add(fromShelf(popularHere, "popular-here", "Most watched here", "users"));
  add(fromShelf(films, "popular-film", "Most popular movie this week", "flame"));
  add(fromShelf(showsWeek, "popular-show", "Most popular show this week", "flame"));

  const awaited = waitedOn.find((f) => !seen.has(f.imdbId));
  if (awaited) {
    add({
      key: "anticipated", eyebrow: `Most anticipated · ${waitersWords(awaited.waiters, awaited.waiting)}`, icon: "bell",
      kind: "film", imdbId: awaited.imdbId, found: { id: awaited.imdbId, kind: "film", title: awaited.title, year: awaited.year, poster: awaited.posterUrl, genres: [], madeBy: [], starring: [], category: "general" }, upcoming: true,
    });
  } else {
    add(fromShelf(soon, "coming-soon", "Coming soon", "calendar-clock", { upcoming: true }));
  }
  return out;
}

/** The newest ready film: what arrived last is what is worth putting at the top. */
export function newestFilm(films) {
  const ready = films.filter((t) => t.status === "ready");
  const pool = ready.length > 0 ? ready : films;
  return pool.reduce((best, t) => (!best || (t.addedAt || "") > (best.addedAt || "") ? t : best), null);
}

/** How many films Discover's spotlight turns through. */
const DISCOVER_SLIDES = 4;

/**
 * Discover's spotlight: the first few films on the shelf the page names (`index.spotlight`: the
 * season's, else the week's trending films) that the person has not watched. A different one leads
 * each day. `where` is the page's join with the library.
 */
export function useDiscoverSlides(index, where) {
  const head = index.rows.map((r) => r.shelf).find((h) => h && h.id === index.spotlight);
  const query = head ? { ...queryParams(head.query), kind: "film", unwatched: "true", size: 12 } : null;
  const shelf = useLoad(() => (query ? catalogue.titles(query).catch(() => null) : Promise.resolve(null)), [index.spotlight]);
  const candidates = React.useMemo(() => {
    if (shelf.loading) return null;
    if (!shelf.data) return [];
    const picks = shelf.data.titles.slice(0, DISCOVER_SLIDES);
    const lead = picks.length ? new Date().getDate() % picks.length : 0;
    return [...picks.slice(lead), ...picks.slice(0, lead)].map((t) => ({
      key: "spot:" + t.id, eyebrow: head.title, icon: head.icon, kind: "film", imdbId: t.id, found: t, here: where(t).here,
    }));
    // `where` is joined once, when the shelf answers.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shelf.loading, shelf.data]);
  return useSlides(candidates);
}
