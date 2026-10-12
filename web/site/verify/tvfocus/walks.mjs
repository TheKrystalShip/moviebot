// What the focus walk presses, and where focus should go. A walk opens `start`, checks the focus it
// opened on against `ready`, then presses each step's `key` and checks where focus came to rest against
// its `expect`: an element's name as focus.mjs gives it (`bar/Movies`, `row/Continue watching/0`,
// `hero/*`), with `*` for anything, or a list of names any of which will do. A step that opens a page
// says `open`, and may name the page it should land on as `path`. A step whose flags (focus moving twice
// for one press, ending against the arrow) are expected says `allow`. A walk marked `optional` runs
// only when --only names it.
//
// Row names and counts are production's library, so a walk names the first rows it relies on with `*`
// where the library decides.

const right = (n) => Array.from({ length: n }, () => ({ key: "Right" }));

export const WALKS = {
  "home-down": {
    about: "Home: from the slides down through the rows and back up into the bar",
    start: "/",
    ready: "hero/*",
    steps: [
      { key: "Down", expect: "row/Continue watching/0" }, { key: "Down", expect: "row/Watch later/0" },
      { key: "Down", expect: "row/Recently added/0" }, { key: "Down", expect: "row/Anticipated/0" },
      { key: "Up", expect: "row/Recently added/0" }, { key: "Up", expect: "row/Watch later/0" },
      { key: "Up", expect: "row/Continue watching/0" }, { key: "Up", expect: "hero/*" },
      { key: "Up", expect: "bar/Home" }, { key: "Down", expect: "hero/*" },
    ],
  },
  "profile-back": {
    about: "Home's bar to the profile, about in it, and Back to where it was opened from",
    start: "/",
    steps: [
      { key: "Up", expect: "bar/Home" },
      ...right(6), { key: "Right", expect: "bar/Profile" },
      { key: "OK", open: true, path: "/profile", expect: "side/*History", note: "lands in the section list" },
      { key: "Down", expect: "side/*Requests" }, { key: "Down", expect: "side/*Following" },
      { key: "Back", open: true, path: "/", expect: "bar/Profile", note: "back where the profile was opened" },
      { key: "Down", expect: "hero/*", note: "out of the bar into the page" },
    ],
  },
  "profile-panel-back": {
    about: "The profile's panel and back out of it to Home",
    start: "/",
    steps: [
      { key: "Up" }, ...right(7),
      { key: "OK", open: true, path: "/profile" },
      { key: "Left", expect: "side/*History" },
      { key: "Right", expect: "main/*" },
      { key: "Back", open: true, path: "/", expect: "bar/Profile" },
    ],
  },
  "profile-from-rows": {
    about: "Into the profile from deep in Home's rows, and Back",
    start: "/",
    steps: [
      { key: "Down" }, { key: "Down" }, { key: "Up" }, { key: "Up" }, { key: "Up", expect: "bar/Home" }, ...right(7),
      { key: "OK", open: true, path: "/profile" },
      { key: "Back", open: true, path: "/", expect: "bar/Profile" },
      { key: "Down", expect: "hero/*" },
    ],
  },
  "bar-to-pages": {
    about: "Down from the bar into each page's content, and Up back into it",
    start: "/",
    steps: [
      { key: "Up", expect: "bar/Home" }, { key: "Right", expect: "bar/Discover" },
      { key: "OK", open: true, path: "/discover" },
      { key: "Up", expect: "bar/Discover" }, { key: "Down" }, { key: "Down" }, { key: "Up" }, { key: "Up", expect: "bar/Discover" },
      { key: "Right", expect: "bar/Movies" },
      { key: "OK", open: true, path: "/movies", expect: "row/*/0" },
      { key: "Up", expect: "bar/Movies" }, { key: "Down", expect: "row/*/0" },
      { key: "Down", expect: "row/*/0" }, { key: "Down", expect: "row/*/0" },
      { key: "Up", expect: "row/*/0" }, { key: "Up", expect: "row/*/0" }, { key: "Up", expect: "bar/Movies" },
      { key: "Right", expect: "bar/TV Shows" },
      { key: "OK", open: true, path: "/tv", expect: "row/*/0" },
      { key: "Up", expect: "bar/TV Shows" },
      { key: "Down", expect: "row/*/0", note: "back to the card it left" },
      { key: "Down", expect: "row/*/0" }, { key: "Up", expect: "row/*/0" }, { key: "Up", expect: "bar/TV Shows" },
      { key: "Right" }, { key: "Right", expect: "bar/Saved" },
      { key: "OK", open: true, path: "/saved" },
      { key: "Up", expect: "bar/Saved" }, { key: "Down" }, { key: "Down" }, { key: "Up" }, { key: "Up", expect: "bar/Saved" },
    ],
  },
  "movies-deep": {
    about: "Movies: deep into the rows, sideways, then back up to the bar",
    start: "/movies",
    ready: "row/*/0",
    steps: [
      { key: "Down" }, { key: "Down" }, { key: "Down" }, { key: "Down" }, { key: "Down", expect: "row/*/0" },
      { key: "Right" }, { key: "Right" }, { key: "Right", expect: "row/*/3" },
      { key: "Down", expect: "row/*/0" }, { key: "Up", expect: "row/*/3", note: "back to the card it left" },
      { key: "Up" }, { key: "Up" }, { key: "Up" }, { key: "Up" }, { key: "Up" },
      { key: "Up", expect: "bar/Movies" },
    ],
  },
};
