// Every address the site has, built in one place. An address is hierarchical: each page's path begins
// with the path of the page it belongs under, so a path read segment by segment is a trail from Home.
// `router.js` parses what these build.

const seg = encodeURIComponent;

/** The tabs of the profile, in the order they are shown; the first is the page's own address. */
export const PROFILE_TABS = ["history", "offline", "requests", "following", "notifications", "playback", "subtitles", "appearance", "account"];

/** The profile's tabs drawn from the device, which are all it shows with no connection. */
export const DEVICE_TABS = ["offline", "playback", "subtitles", "appearance", "account"];

export const homePath = "/";
export const savedPath = "/saved";
export const notifyPath = "/notify";
export const aboutPath = "/about";
/** Where the provider sends the browser back with a code. */
export const signInPath = "/signin";

/** The shelf of one kind, as the API words it: `film` or `show`. */
export const libraryPath = (kind) => (kind === "show" ? "/tv" : "/movies");

/** One film in the library. */
export const moviePath = (titleId) => "/movies/" + seg(titleId);

const seasonSegment = (season) => (season == null ? "" : "/season/" + season);

/** A show the library holds at least one episode of, at a season or at its default one. */
export const tvPath = (imdbId, season) => "/tv/" + seg(imdbId) + seasonSegment(season);

/** Finding anything to watch, here or not; `q` is what was searched for. */
export const discoverPath = (q) => "/discover" + (q ? "?q=" + seg(q) : "");

/** Discover's catalogue of the films and shows one person is credited on, by the film database's id for them. */
export const discoverPersonPath = (personId) => "/discover?person=" + seg(personId);

/** Discover's catalogue of the films (`film`) or shows (`show`) in one genre, by the genre's name. */
export const discoverGenrePath = (kind, genre) => "/discover?genre=" + seg(genre) + "&kind=" + (kind === "show" ? "show" : "film");

/** A film the library does not hold. */
export const discoverFilmPath = (imdbId) => "/discover/film/" + seg(imdbId);

/** A show the library holds no episode of, at a season or at its default one. */
export const discoverShowPath = (imdbId, season) => "/discover/show/" + seg(imdbId) + seasonSegment(season);

/**
 * A show's page. Where the library holds it is the page's to find out, so a link that does not know
 * says nothing and lands on the shelf's address, which forwards when it is wrong.
 */
export const showPath = (imdbId, { held = true, season } = {}) => (held ? tvPath(imdbId, season) : discoverShowPath(imdbId, season));

/** One list; `later` is the person's own Watch later. */
export const listPath = (id) => "/saved/" + seg(id);

/** The profile at a tab; the first tab has no segment. */
export const profilePath = (tab) => (tab && tab !== PROFILE_TABS[0] ? "/profile/" + seg(tab) : "/profile");

/** A room. */
export const roomPath = (room) => "/r/" + seg(room);

/** The query flag that plays a room's title from this device, alone, rather than in the room. */
export const OFFLINE_FLAG = "offline";

/**
 * A film saved on this device, played from it: a room address of its own that the player opens with
 * no hub, its timeline local. `at` is where to start, in seconds.
 */
export const playSavedPath = (titleId, at) => roomPath(crypto.randomUUID().replaceAll("-", "").slice(0, 8))
  + "?title=" + seg(titleId) + "&" + OFFLINE_FLAG + "=1" + (at > 0 ? "&at=" + Math.floor(at) : "");
