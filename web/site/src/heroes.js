// The slides at the top of Home and of Discover, on every surface. A page names what it would put
// there, each as a candidate; `useSlides` describes each one and hands back those ready to be shown.
//
// A candidate is `{ key, eyebrow, icon, kind, imdbId }` and one of: `title`, a library film (with
// `room` when people are watching it now); `show`, a library show; or `found`, a title from a shelf of
// the film database's, with `here` the library's film by that id and `held` whether the library holds an
// episode of a show. `upcoming` marks a film that is not out yet.

import React from "react";
import { api } from "./api.js";
import { useLoad } from "./data.js";
import { since } from "./time.js";
import { filmSpot, showSpot, showViewSpot, titleSpot } from "./spots.js";
import { waitersWords } from "./components/TitleCards.jsx";

/** The most slides a page's top turns through. */
export const MOST_SLIDES = 6;

/**
 * The candidates described, each with its `spot` (`spots.js`), in the order given. A candidate the film
 * database cannot describe is left out, and one is shown only once its artwork has arrived, so a slide
 * never turns to a blank. The slides only grow: one is handed back once every candidate before it is
 * ready or left out, so the slide on screen never moves.
 */
export function useSlides(candidates) {
  const [done, setDone] = React.useState({});
  const ids = candidates ? candidates.map((c) => c.key).join("|") : "";

  React.useEffect(() => {
    if (!candidates) return undefined;
    let live = true;
    candidates.forEach((c) => {
      const settle = (spot) => { if (live) setDone((prev) => ({ ...prev, [c.key]: spot })); };
      describe(c)
        .then((spot) => (spot && spot.art ? artArrives(spot.art).then(() => spot) : spot))
        .then(settle, () => settle(null));
    });
    return () => { live = false; };
    // The candidates are fixed once given; their keys say when they are not.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ids]);

  if (!candidates) return null;
  const slides = [];
  for (const c of candidates) {
    if (!(c.key in done)) break;
    if (done[c.key]) slides.push({ ...c, spot: done[c.key] });
  }
  return slides;
}

function describe(c) {
  if (c.title) return Promise.resolve(titleSpot(c.title, c.eyebrow));
  if (c.show) return Promise.resolve(showSpot(c.show, c.eyebrow));
  if (c.kind === "show") return api.show(c.imdbId).then((s) => showViewSpot(s, c.eyebrow));
  if (c.film) return Promise.resolve(filmSpot(c.film, c.eyebrow));
  return api.film(c.imdbId).then((f) => filmSpot(f, c.eyebrow));
}

function artArrives(src) {
  const image = new Image();
  image.src = src;
  return image.decode().catch(() => undefined);
}

/**
 * What the front page turns through: the film people are watching together now, the newest film on
 * the shelf, the title most watched here lately, the film and the show the film database counts most
 * popular this week, and the film most waited on here, or else the next one out. Each one is left out
 * when there is none, and a title is shown once. They are chosen once everything they are chosen
 * from has answered, and kept while the page is open.
 *
 * `library` is the library's films and episodes, `shows` its shows, `rooms` the rooms open now, and
 * `waitedOn` the films waited on here (`/api/anticipated`), `null` until it answers.
 */
export function useHomeSlides({ library, shows, rooms, waitedOn }) {
  const popularHere = useLoad(() => api.discoverShelf("popular-here").catch(() => null), []);
  const films = useLoad(() => api.discoverShelf("trending-films").catch(() => null), []);
  const showsWeek = useLoad(() => api.discoverShelf("trending-shows").catch(() => null), []);
  const soon = useLoad(() => api.discoverShelf("coming-soon").catch(() => null), []);
  const [candidates, setCandidates] = React.useState(null);

  const settled = library && shows && rooms && waitedOn
    && !popularHere.loading && !films.loading && !showsWeek.loading && !soon.loading;

  React.useEffect(() => {
    if (!settled || candidates) return;
    setCandidates(homeCandidates({
      library, shows, rooms, waitedOn,
      popularHere: popularHere.data, films: films.data, showsWeek: showsWeek.data, soon: soon.data,
    }));
  }, [settled, candidates, library, shows, rooms, waitedOn, popularHere.data, films.data, showsWeek.data, soon.data]);

  return useSlides(candidates);
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
    const t = titles.find((x) => !x.watched && !seen.has(x.imdbId)) || titles.find((x) => !seen.has(x.imdbId));
    if (!t) return null;
    return {
      key, eyebrow, icon, kind: t.kind, imdbId: t.imdbId, found: t,
      here: t.kind === "film" ? filmsHere.get(t.imdbId) : null, held: t.kind === "show" && heldShows.has(t.imdbId), ...extra,
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
      kind: "film", imdbId: awaited.imdbId, found: { ...awaited, kind: "film" }, upcoming: true,
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
 * Discover's spotlight: the first few films on the season's shelf, or the week's popular films out of
 * season (`index.spotlight`), that the person has not watched. A different one leads each day.
 * `where` is the page's join with the library.
 */
export function useDiscoverSlides(index, where) {
  const kept = (index.shelves || []).find((s) => s.id === index.spotlight);
  const shelf = useLoad(() => api.discoverShelf(index.spotlight).catch(() => null), [index.spotlight], { known: kept });
  const candidates = React.useMemo(() => {
    if (shelf.loading) return null;
    if (!shelf.data) return [];
    const picks = shelf.data.titles.filter((t) => t.kind === "film" && !t.watched).slice(0, DISCOVER_SLIDES);
    const lead = picks.length ? new Date().getDate() % picks.length : 0;
    return [...picks.slice(lead), ...picks.slice(0, lead)].map((t) => {
      const w = where(t);
      const film = (index.films || []).find((f) => f.imdbId === t.imdbId);
      return { key: "spot:" + t.imdbId, eyebrow: shelf.data.title, icon: shelf.data.icon, kind: "film", imdbId: t.imdbId, found: t, here: w.here, film };
    });
    // `where` is joined once, when the shelf answers.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shelf.loading, shelf.data]);
  return useSlides(candidates);
}
