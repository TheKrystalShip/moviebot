// The site's addresses. A page is a path, so a film's page can be linked to and reloaded; the API
// answers any path that is not its own with the site's index.

import { createStore, useStore } from "@thekrystalship/krystal-ui";
import {
  ANIME, PROFILE_TABS, aboutPath, discoverFilmPath, discoverPath, discoverShowPath, guestPath, homePath, libraryPath,
  listPath, moviePath, notifyPath, pairPath, profilePath, roomPath, savedPath, signInPath, tvPath,
} from "./paths.js";

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
 *             discover-show, saved, list, notify, profile, about, signin, room, guest, pair, embed, notfound
 *   category  `anime` for a shelf or a title's page under Anime; null everywhere else
 *   arg       the id the path carries: a title, a show, a film, a list, a room or a code
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
  switch (head) {
    case "":
      route = shaped("home", homePath);
      break;
    case "movies":
      route = rest[0] ? shaped("movie", moviePath(rest[0]), { arg: rest[0] }) : shaped("movies", libraryPath("film"));
      break;
    case "tv": {
      const season = seasonOf(rest.slice(1));
      route = rest[0] ? show(rest[0], season, tvPath(rest[0], season), "show") : shaped("tv", libraryPath("show"));
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
        route = shaped("anime", libraryPath("show", ANIME), { category: ANIME });
      }
      break;
    }
    case "discover": {
      const [kind, id, ...more] = rest;
      const season = seasonOf(more);
      if (kind === "film" && id) route = shaped("discover-film", discoverFilmPath(id), { arg: id });
      else if (kind === "show" && id) route = show(id, season, discoverShowPath(id, season), "discover-show");
      else route = shaped("discover", discoverPath());
      break;
    }
    case "saved":
      route = rest[0] ? shaped("list", listPath(rest[0]), { arg: rest[0] }) : shaped("saved", savedPath);
      break;
    case "profile": {
      const tab = PROFILE_TABS.includes(rest[0]) && rest[0] !== PROFILE_TABS[0] ? rest[0] : null;
      route = shaped("profile", profilePath(tab), { tab, key: profilePath() });
      break;
    }
    case "notify": route = shaped("notify", notifyPath); break;
    case "about": route = shaped("about", aboutPath); break;
    case "signin": route = shaped("signin", signInPath); break;
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
      return { ...shaped("notfound", url.pathname), query: url.searchParams, href: url.pathname + url.search, segments };
  }
  return { ...route, query: new URLSearchParams(search), href: route.path + search, segments: route.path.split("/").filter(Boolean).map(decode) };
}

/** The canonical spelling of an address, or of the one the browser is at. */
const canonical = (href) => parse(href).href;

const here = () => window.location.pathname + window.location.search;

/** The browser's address, brought to its canonical spelling without adding a history entry. */
function settle() {
  const href = canonical(here());
  if (href !== here()) window.history.replaceState(null, "", href);
  return href;
}

const routeStore = createStore(settle());

window.addEventListener("popstate", () => routeStore.setState(settle));

/** How many entries this page pushed, so going back never leaves the site. */
let depth = 0;

window.addEventListener("popstate", () => { depth = Math.max(0, depth - 1); });

export function navigate(to, { replace = false } = {}) {
  const next = canonical(to);
  if (next === here()) return;
  if (replace) window.history.replaceState(null, "", next);
  else {
    window.history.pushState(null, "", next);
    depth += 1;
  }
  routeStore.setState(() => next);
}

/** Back to the page this one was opened from, or to a page of the site's own when it was opened directly. */
export function goBack(fallback = homePath) {
  if (depth > 0) window.history.back();
  else navigate(fallback, { replace: true });
}

let last = null;

/** The route the address names; see `parse`. */
export function useRoute() {
  const href = useStore(routeStore);
  if (!last || last.href !== href) last = parse(href);
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
