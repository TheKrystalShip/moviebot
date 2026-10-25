// The site's addresses. A page is a path, so a film's page can be linked to and reloaded; the API
// answers any path that is not its own with the site's index.

import { createStore, useStore } from "@thekrystalship/krystal";
import { PROFILE_TAB_FACES } from "./nav.js";
import {
  ANIME, PROFILE_TABS, aboutPath, chatPath, discoverFilmPath, discoverPath, discoverShowPath, guestPath, homePath, libraryPath,
  moviePath, newPlanPath, pairPath, planPath, plansPath, profilePath, roomPath, signedInPath, tvPath, wantsPath,
} from "./paths.js";

/** The groups the places are drawn in: the sidebar's, in its order, then the places only the phone's dock reaches. */
export const SECTIONS = [
  { id: "home", label: "Home", bare: true },
  { id: "library", label: "Library" },
  { id: "yours", label: "Yours" },
  { id: "more", label: "More", dockOnly: true },
];

/** A profile tab as a place of its own: the tab's address, icon and name. */
const tabPlace = (id, icon) => {
  const face = PROFILE_TAB_FACES.find((t) => t.id === id);
  return { path: profilePath(id), icon: icon || face.icon, label: face.label, section: "yours", views: [] };
};

/**
 * The site's landing places, once: the pages a person can open with no argument. The names the sidebar,
 * the footer, the phone's dock and the breadcrumb trail give them, the section each is drawn in (none for
 * a place only the footer reaches), `view` the route it parses to, and `views` every route that lights it.
 * `plain` marks a place whose address takes nothing after it. A place is routed, drawn in the sidebar and
 * drawn in the dock by being here; the table's order is the order of a section's places. `short` is the
 * name the dock's bar gives it where that is shorter.
 */
export const PLACES = {
  home: { path: homePath, icon: "house", label: "Home", section: "home", view: "home", views: ["home"] },
  discover: {
    path: discoverPath(), icon: "compass", label: "Discover", section: "library", view: "discover",
    views: ["discover", "discover-film", "discover-show"],
  },
  movies: { path: libraryPath("film"), icon: "clapperboard", label: "Movies", section: "library", view: "movies", views: ["movies", "movie"] },
  tv: { path: libraryPath("show"), icon: "tv", label: "TV Shows", section: "library", view: "tv", views: ["tv", "show"] },
  anime: {
    path: libraryPath("show", ANIME), icon: "swords", label: "Anime", section: "library", view: "anime", views: ["anime", "anime-films"],
  },
  wants: { path: wantsPath, icon: "bookmark", label: "Want to watch", section: "yours", view: "wants", views: ["wants"], plain: true },
  plans: { path: plansPath, icon: "calendar-clock", label: "Plans", section: "yours", view: "plans", views: ["plans", "plan-new", "plan"] },
  history: tabPlace("history"),
  // Its tab's icon is TV Shows' own, and the two sit in the same sidebar.
  following: tabPlace("following", "list-video"),
  assistant: { path: chatPath, icon: "bot", label: "Assistant", section: "more", view: "chat", views: ["chat"], plain: true },
  profile: { path: profilePath(), icon: "user", label: "Profile", short: "Me", section: "more", view: "profile", views: ["profile"] },
  about: { path: aboutPath, icon: "info", label: "About", view: "about", views: [], plain: true },
};

/** The ids of the places drawn in a section, in the table's order. */
export const placesIn = (section) => Object.keys(PLACES).filter((id) => PLACES[id].section === section);

/** The four pages a phone's dock holds in its free slots until a person chooses others. */
export const DEFAULT_SLOTS = ["movies", "discover", "assistant", "profile"];

const BY_VIEW = new Map(Object.entries(PLACES).flatMap(([id, p]) => p.views.map((view) => [view, id])));
const PLAIN = new Map(Object.entries(PLACES).filter(([, p]) => p.plain).map(([id, p]) => [p.path.slice(1), id]));

/** The route a place parses to, spelt as its table entry gives it. */
const landing = (id) => ({ view: PLACES[id].view, path: PLACES[id].path });

/**
 * The places a route lights, the one the sidebar lights first: a page lights its place, a title or a
 * show under Anime lights Anime, and a profile tab with a place of its own lights that place and the
 * profile. A route that belongs to no place lights none.
 */
export function lights(route) {
  if (route.category === ANIME && route.view !== "discover-show") return ["anime"];
  if (route.view === "profile") {
    const tab = route.tab || PROFILE_TABS[0];
    return PLACES[tab] ? [tab, "profile"] : ["profile"];
  }
  const id = BY_VIEW.get(route.view);
  return id ? [id] : [];
}

const decode = (s) => {
  try { return decodeURIComponent(s); } catch { return s; }
};

/** The season an address names, from the segments after a show's id: `season/<n>`, or none. */
const seasonOf = ([word, n]) => (word === "season" && /^\d+$/.test(n || "") ? Number(n) : null);

/**
 * Links the API sent and people hold name pages by an earlier shape of address; arriving by one
 * resolves to the address the same page has now. Returns the segments and query string to read.
 */
function arrived(segments, search) {
  const [head, ...rest] = segments;
  const query = new URLSearchParams(search);
  switch (head) {
    case "signin": return [[], ""];
    case "title": return [["movies", ...rest], search];
    case "film": return [rest.length ? ["discover", "film", ...rest] : ["discover"], search];
    case "show": {
      const season = query.get("season");
      query.delete("season");
      return [["tv", ...rest.slice(0, 1), ...(rest.length && season ? ["season", season] : [])], query.size ? "?" + query : ""];
    }
    case "profile": {
      if (rest.length > 0 || !query.has("tab")) return [segments, search];
      const tab = query.get("tab");
      query.delete("tab");
      return [["profile", tab], query.size ? "?" + query : ""];
    }
    default: return [segments, search];
  }
}

/**
 * What an address is. Every address has one canonical spelling, built by `paths.js`: an extra
 * segment, a trailing slash, a tab or season that does not exist and an earlier shape of address all
 * parse to the canonical route, whose `href` is what the address becomes. An unknown first segment is
 * `notfound` and keeps the address as typed.
 *
 *   view      which page: home, movies, movie, tv, show, anime, anime-films, discover, discover-film,
 *             discover-show, wants, plans, plan-new, plan, profile, about, chat, signed-in, room, guest, pair, embed,
 *             notfound
 *   category  `anime` for a shelf or a title's page under Anime; null everywhere else
 *   arg       the id the path carries: a title, a show, a film, a room or a code
 *   season    the season a show's path names, or null for the show's own choice
 *   tab       the profile tab the path names, or null for the first
 *   segments  the canonical path, decoded, one entry per level
 *   path      the canonical path, and `href` that with the query string
 *   key       the page this is: a season or a tab of a page is the same page
 */
function parse(href) {
  const url = new URL(href, window.location.origin);
  const [segments, search] = arrived(url.pathname.split("/").filter(Boolean).map(decode), url.search);
  const [head = "", ...rest] = segments;
  const shaped = (view, path, more = {}) => ({ view, path, arg: null, season: null, tab: null, category: null, key: path, ...more });
  const show = (id, season, path, view, category = null) => shaped(view, path, { arg: id, season, category, key: "show:" + id });

  let route;
  const place = (id) => shaped(landing(id).view, landing(id).path);
  switch (head) {
    case "":
      route = place("home");
      break;
    case "movies":
      route = rest[0] ? shaped("movie", moviePath(rest[0]), { arg: rest[0] }) : place("movies");
      break;
    case "tv": {
      const season = seasonOf(rest.slice(1));
      route = rest[0] ? show(rest[0], season, tvPath(rest[0], season), "show") : place("tv");
      break;
    }
    case "anime": {
      const [first, id] = rest;
      if (first === "films") {
        route = id
          ? shaped("movie", moviePath(id, ANIME), { arg: id, category: ANIME })
          : shaped("anime-films", libraryPath("film", ANIME), { category: ANIME });
      } else if (first) {
        const season = seasonOf(rest.slice(1));
        route = show(first, season, tvPath(first, season, ANIME), "show", ANIME);
      } else {
        route = { ...place("anime"), category: ANIME };
      }
      break;
    }
    case "discover": {
      const [kind, id, ...more] = rest;
      const season = seasonOf(more);
      if (kind === "film" && id) route = shaped("discover-film", discoverFilmPath(id), { arg: id });
      else if (kind === "show" && id) route = show(id, season, discoverShowPath(id, season), "discover-show");
      else route = place("discover");
      break;
    }
    case "plans": {
      const [first] = rest;
      route = first === "new" ? shaped("plan-new", newPlanPath())
        : first ? shaped("plan", planPath(first), { arg: first })
          : place("plans");
      break;
    }
    case "profile": {
      const tab = PROFILE_TABS.includes(rest[0]) && rest[0] !== PROFILE_TABS[0] ? rest[0] : null;
      route = shaped("profile", profilePath(tab), { tab, key: landing("profile").path });
      break;
    }
    case "signed-in": route = shaped("signed-in", signedInPath); break;
    case "r":
      route = rest[0] ? shaped("room", roomPath(rest[0]), { arg: rest[0] }) : shaped("home", homePath);
      break;
    case "g":
      route = rest[0] ? shaped("guest", guestPath(rest[0]), { arg: rest[0] }) : shaped("home", homePath);
      break;
    case "p":
      route = rest[0] ? shaped("pair", pairPath(rest[0]), { arg: rest[0] }) : shaped("home", homePath);
      break;
    case "embed":
      return { ...shaped("embed", url.pathname), query: url.searchParams, href: url.pathname + url.search, segments };
    default:
      if (PLAIN.has(head)) { route = place(PLAIN.get(head)); break; }
      return { ...shaped("notfound", url.pathname), query: url.searchParams, href: url.pathname + url.search, segments };
  }
  return { ...route, query: new URLSearchParams(search), href: route.path + search, segments: route.path.split("/").filter(Boolean).map(decode) };
}

/** The canonical spelling of an address, or of the one the browser is at. */
const canonical = (href) => parse(href).href;

const here = () => window.location.pathname + window.location.search;

const freshEntry = () => Math.random().toString(36).slice(2, 10);

/** The history entry the browser is at, by an id it carries in its state, given one the first time it is seen. */
function entryHere() {
  const state = window.history.state;
  if (state && typeof state.entry === "string") return state.entry;
  const entry = freshEntry();
  window.history.replaceState({ ...(state && typeof state === "object" ? state : {}), entry }, "", here());
  return entry;
}

/** The browser's address, brought to its canonical spelling without adding a history entry. */
function settle() {
  const href = canonical(here());
  if (href !== here()) window.history.replaceState(window.history.state, "", href);
  return href;
}

/**
 * Where the browser is and how it got there: `{ href, entry, move }`, `entry` the history entry's id
 * and `move` one of `load` (the page opened or was reloaded on it), `push` (a new entry), `replace`
 * (the same entry at a new address) or `pop` (Back or Forward). Every navigation is a new object, so
 * one address arrived at twice is two visits.
 */
const routeStore = createStore({ href: settle(), entry: entryHere(), move: "load" });

window.addEventListener("popstate", () => {
  const href = settle();
  routeStore.setState(() => ({ href, entry: entryHere(), move: "pop" }));
});

/** How many entries this page pushed, so going back never leaves the site. */
let depth = 0;

window.addEventListener("popstate", () => { depth = Math.max(0, depth - 1); });

export function navigate(to, { replace = false } = {}) {
  const next = canonical(to);
  if (next === here()) return;
  if (replace) {
    const entry = entryHere();
    window.history.replaceState({ ...window.history.state, entry }, "", next);
    routeStore.setState(() => ({ href: next, entry, move: "replace" }));
  } else {
    const entry = freshEntry();
    window.history.pushState({ entry }, "", next);
    depth += 1;
    routeStore.setState(() => ({ href: next, entry, move: "push" }));
  }
}

/** Back to the page this one was opened from, or to a page of the site's own when it was opened directly. */
export function goBack(fallback = homePath) {
  if (depth > 0) window.history.back();
  else navigate(fallback, { replace: true });
}

let last = null;
let lastVisit = null;

/**
 * The route the address names (see `parse`), with the visit it is: `entry`, the history entry's id,
 * and `move`, how it was arrived at (see `routeStore`).
 */
export function useRoute() {
  const visit = useStore(routeStore);
  if (lastVisit !== visit) {
    last = { ...parse(visit.href), entry: visit.entry, move: visit.move };
    lastVisit = visit;
  }
  return last;
}

/** A link that navigates in place. */
export function linkProps(to) {
  return {
    href: to,
    onClick: (e) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      e.preventDefault();
      navigate(to);
    },
  };
}
