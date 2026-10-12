// What the focus walk presses, and where focus should go. A walk opens `start`, checks the focus it
// opened on against `ready`, then presses each step's `key` and checks where focus came to rest against
// its `expect`: an element's name as focus.mjs gives it (`bar/Movies`, `row/Continue watching/0`,
// `hero/*`), with `*` for anything, a list of names any of which will do, or `=<n>` for wherever focus
// came to rest after the walk's step n. A step that opens a page
// says `open`, and may name the page it should land on as `path`. In the player a step may also expect
// the film: `paused`, `shown` (the controls up), `seeking` (the scrub bar moving the film) and `moved`
// (the film jumped). A step that is only `wait` presses nothing and lets that many ms pass. A step whose flags (focus moving twice
// for one press, ending against the arrow) are expected says `allow`. A walk marked `optional` runs
// only when --only names it. `start` may be a function, called for each run; `stepMs` is how long
// each step of the walk is watched at the least.
//
// The rows are production's, and change under a walk (Watching now comes and goes with the rooms), so
// a walk names rows by `*` and by `=<n>` rather than by title.

const right = (n) => Array.from({ length: n }, () => ({ key: "Right" }));

export const WALKS = {
  "home-down": {
    about: "Home: from the slides down through the rows and back up into the bar",
    start: "/",
    ready: "hero/*",
    steps: [
      { key: "Down", expect: "row/*/0" }, { key: "Down", expect: "row/*/0" },
      { key: "Down", expect: "row/*/0" }, { key: "Down", expect: "row/*/0" },
      { key: "Up", expect: "=3" }, { key: "Up", expect: "=2" },
      { key: "Up", expect: "=1" }, { key: "Up", expect: "hero/*" },
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
      { key: "OK", open: true, path: "/profile", expect: "side/*History" },
      { key: "Right", expect: "main/*" },
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
      { key: "OK", open: true, path: "/profile", expect: "side/*History" },
      { key: "Back", open: true, path: "/", expect: "bar/Profile" },
      { key: "Down", expect: "hero/*" },
    ],
  },
  "bar-down": {
    about: "Down out of the bar from items far from the page's first control, and from Back deep in the rows",
    start: "/",
    steps: [
      { key: "Up", expect: "bar/Home" }, ...right(3), { key: "Down", expect: "hero/*", note: "from TV Shows" },
      { key: "Up", expect: "bar/Home" }, ...right(7), { key: "Down", expect: "hero/*", note: "from Profile" },
      { key: "Down", expect: "row/*/0" }, { key: "Down" }, { key: "Down", expect: "row/*/0" },
      { key: "Back", expect: "bar/Home" },
      { key: "Down", expect: "=17", note: "back to where the page was left" },
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
  player: {
    about: "The player on the scratch site (--scratch): OK, the controls, and the scrub bar",
    optional: true,
    start: () => `/r/verify-focus-${Date.now().toString(36)}?title=coherence-2013`,
    settle: 15000,
    stepMs: 2200,
    ready: "player/Play",
    steps: [
      { key: "OK", paused: false, expect: "player/Pause", note: "play" },
      { wait: 8000 },
      { key: "OK", paused: true, shown: true, expect: "player/Play", note: "OK pauses while the controls are away" },
      { key: "OK", paused: false, expect: "player/Pause" },
      { wait: 8000 },
      { key: "Right", moved: true, shown: true, seeking: true, expect: "player/Seek", note: "a skip opens the bar seeking" },
      { key: "Right", moved: true, seeking: true, expect: "player/Seek", note: "and goes on skipping" },
      { key: "OK", moved: false, seeking: false, expect: "player/Seek", note: "OK ends seeking" },
      { key: "Right", moved: false, expect: "player/Seek", note: "the bar alone does not move the film" },
      { key: "Left", moved: false, expect: "player/Seek" },
      { key: "Down", moved: false, expect: "player/Pause" },
      { key: "Right", moved: false, expect: "player/Back 10 seconds" },
      { key: "Up", moved: false, expect: "player/Seek" },
      { key: "Down", moved: false, expect: "player/Back 10 seconds", note: "back to where it came up from" },
      { key: "Right", moved: false, expect: "player/Forward 10 seconds" },
      { key: "Right", moved: false },
      { key: "Up", moved: false, expect: "player/Seek" },
      { key: "OK", moved: false, seeking: true, expect: "player/Seek", note: "OK on the bar seeks" },
      { key: "Left", moved: true, seeking: true, expect: "player/Seek" },
      { key: "Back", moved: false, seeking: false, shown: true, expect: "player/Seek", note: "Back ends seeking" },
      { key: "Back", shown: false, note: "then puts the controls away" },
      { key: "Up", shown: true, moved: false },
    ],
  },
};
