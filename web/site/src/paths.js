// Every address the site has, built in one place. An address is hierarchical: each page's path begins
// with the path of the page it belongs under, so a path read segment by segment is a trail from Home.
// `router.js` parses what these build.

const seg = encodeURIComponent;

/** The tabs of the profile, in the order they are shown; the first is the page's own address. */
export const PROFILE_TABS = ["history", "following", "taste", "notifications", "playback", "remote", "subtitles", "appearance", "account"];

export const homePath = "/";
export const wantsPath = "/wants";
/** The person's plans to watch something together. */
export const plansPath = "/plans";
/** The form that plans watching a title together, prefilled with the title and its kind (`film` or `show`). */
export const newPlanPath = (imdbId, kind) => "/plans/new" + (imdbId ? `?title=${seg(imdbId)}` + (kind === "show" ? "&kind=show" : "") : "");
/** One plan, by its id. */
export const planPath = (id) => `/plans/${seg(id)}`;
export const aboutPath = "/about";
/** The assistant's page. Not `/assistant`, which is the assistant's own service on the same origin. */
export const chatPath = "/chat";
/** Where the provider sends the browser back with a code. */
export const signedInPath = "/signed-in";

/** The API's category for a title shelved as anime, whatever its kind. */
export const ANIME = "anime";

/** Whether a title is shelved as anime: a catalogue card, or a library title or show by the card it carries. */
export const isAnime = (title) => !!title && (title.card || title).category === ANIME;

/**
 * The shelf of one kind, as the API words it (`film` or `show`), in a category: `anime`, or anything
 * else for the general shelves. Anime's own shelf lists its series, and its films one level below.
 */
export const libraryPath = (kind, category) => (category === ANIME
  ? (kind === "film" ? "/anime/films" : "/anime")
  : (kind === "show" ? "/tv" : "/movies"));

/** One film in the library, under the shelf its category puts it on. */
export const moviePath = (titleId, category) => libraryPath("film", category) + "/" + seg(titleId);

const seasonSegment = (season) => (season == null ? "" : "/season/" + season);

/** A show the library holds at least one episode of, at a season or at its default one, under its category's shelf. */
export const tvPath = (imdbId, season, category) => libraryPath("show", category) + "/" + seg(imdbId) + seasonSegment(season);

/** Finding anything to watch, here or not; `q` is what was searched for. */
export const discoverPath = (q) => "/discover" + (q ? "?q=" + seg(q) : "");

/** One of Discover's browse categories: its shelves, by the category's id. */
export const discoverBrowsePath = (id) => "/discover?browse=" + seg(id);

/**
 * Every title one list query names, under `name`: a shelf, a genre, a keyword, a studio, an award, a
 * place. `query` is the list query as its string (a shelf's own) or as its parameters; the address
 * holds it beside `list`, which is the heading, so it can be shared and reloaded.
 */
export function discoverListPath(query, name) {
  const params = new URLSearchParams(typeof query === "string" ? query : undefined);
  if (typeof query !== "string") {
    for (const [key, value] of Object.entries(query || {})) {
      for (const one of Array.isArray(value) ? value : [value]) if (one != null && one !== "") params.append(key, String(one));
    }
  }
  params.set("list", name || "");
  return "/discover?" + params.toString();
}

/**
 * Discover's filter: the catalogue's films and shows at the filter drawer's picks, which the address
 * holds beside `filter` (`filters.js`).
 */
export const discoverFilterPath = () => "/discover?filter=1";

/** The films and shows one person is credited on, by the catalogue's id for them. */
export const discoverPersonPath = (personId) => "/discover?person=" + seg(personId);

/** A franchise's titles in its own order, by its id (`c<collection>` or a Wikidata item). */
export const discoverFranchisePath = (id) => "/discover?franchise=" + seg(id);

/** The films (`film`) or shows (`show`) in one genre, by its name; its anime alone with `anime`. */
export const discoverGenrePath = (kind, genre, category) => discoverListPath(
  { kind: kind === "show" ? "show" : "film", genre, category: category === ANIME ? ANIME : null }, genre);

/** A film the library does not hold. */
export const discoverFilmPath = (imdbId) => "/discover/film/" + seg(imdbId);

/** A show the library holds no episode of, at a season or at its default one. */
export const discoverShowPath = (imdbId, season) => "/discover/show/" + seg(imdbId) + seasonSegment(season);

/**
 * A show's page. Where the library holds it and which shelf it is on are the page's to find out, so a
 * link that does not know says nothing and lands on the general shelf's address, which forwards when
 * it is wrong.
 */
export const showPath = (imdbId, { held = true, season, category } = {}) => (held ? tvPath(imdbId, season, category) : discoverShowPath(imdbId, season));

/** The profile at a tab; the first tab has no segment. */
export const profilePath = (tab) => (tab && tab !== PROFILE_TABS[0] ? "/profile/" + seg(tab) : "/profile");

/** A room. */
export const roomPath = (room) => "/r/" + seg(room);

/** A room's guest link, which the API names by its code. */
export const guestPath = (code) => "/g/" + seg(code);

/** A television's pairing code, which a phone opens by scanning it: `/p/<code>`, as the API names it. */
export const pairPath = (code) => "/p/" + seg(code);

/** The query flag that plays a room's title from this device, alone, rather than in the room. */
export const OFFLINE_FLAG = "offline";

/**
 * A film saved on this device, played from it: a room address of its own that the player opens with
 * no hub, its timeline local. `at` is where to start, in seconds.
 */
export const playSavedPath = (titleId, at) => roomPath(crypto.randomUUID().replaceAll("-", "").slice(0, 8))
  + "?title=" + seg(titleId) + "&" + OFFLINE_FLAG + "=1" + (at > 0 ? "&at=" + Math.floor(at) : "");
