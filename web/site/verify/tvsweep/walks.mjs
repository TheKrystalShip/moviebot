// What the sweep presses. A walk opens `start`, presses `setup` untimed, then times each step's `key`
// alone, after its `pre` keys untimed. A step is watched for `window` ms (1500 unless given; longer for
// one that opens a page). A walk marked `optional` runs only when --only names it.
const OPEN = 3500;

export const WALKS = {
  "home-rows": {
    start: "/",
    steps: [
      { key: "Down", label: "hero->row1" }, { key: "Down", label: "row1->row2" }, { key: "Down", label: "row2->row3" },
      { key: "Right", label: "card->card" }, { key: "Right", label: "card->card" }, { key: "Right", label: "card->card" },
      { key: "Up", label: "row3->row2" }, { key: "Up", label: "row2->row1" }, { key: "Up", label: "row1->hero" },
      { key: "Up", label: "hero->bar" }, { key: "Down", label: "bar->hero" },
    ],
  },
  bar: {
    start: "/",
    setup: ["Up"],
    steps: [
      { key: "Right", label: "bar->Discover" }, { key: "Right", label: "bar->Movies" }, { key: "Right", label: "bar->TV" },
      { key: "Right", label: "bar->Anime" }, { key: "Left", label: "bar<-Movies" }, { key: "Down", label: "bar->page" },
    ],
  },
  "deep-bar": {
    start: "/movies",
    setup: ["Down", "Down", "Down", "Down"],
    steps: [
      { key: "Back", label: "deep->bar(Back)", window: 2000 }, { key: "Down", label: "bar->page", window: 2000 },
      { key: "Down", label: "row1->row2" }, { key: "Down", label: "row2->row3" }, { key: "Down", label: "row3->row4" },
      { key: "Up", label: "row4->row3" }, { key: "Up", label: "row3->row2" }, { key: "Up", label: "row2->row1" },
      { key: "Up", label: "row1->bar(Up)", window: 2000 },
    ],
  },
  "row-scroll": {
    start: "/movies",
    setup: ["Down"],
    steps: [
      { key: "Right" }, { key: "Right" }, { key: "Right" }, { key: "Right" }, { key: "Right" }, { key: "Right" },
      { key: "Left" }, { key: "Left" },
    ],
  },
  discover: {
    start: "/discover",
    // The first Down is untimed: a cold scratch site answers Discover's first reads slowly.
    setup: ["Down", "Up"],
    steps: [
      { key: "Down", label: "v1" }, { key: "Down", label: "v2" }, { key: "Down", label: "v3" }, { key: "Down", label: "v4" },
      { key: "Down", label: "v5" }, { key: "Down", label: "v6" }, { key: "Down", label: "v7" }, { key: "Down", label: "v8" },
      { key: "Right", label: "h1" }, { key: "Right", label: "h2" }, { key: "Right", label: "h3" },
      { key: "Up", label: "^1" }, { key: "Up", label: "^2" }, { key: "Up", label: "^3" }, { key: "Up", label: "^4" },
      { key: "Up", label: "^5" }, { key: "Up", label: "^6" }, { key: "Up", label: "^7" }, { key: "Up", label: "^8" },
    ],
  },
  "discover-open": {
    start: "/",
    setup: ["Up"],
    steps: [
      { pre: ["Right"], key: "OK", label: "open Discover", window: OPEN },
      { pre: ["Back", "Left"], key: "OK", label: "open Home", window: OPEN },
      { pre: ["Right"], key: "OK", label: "open Discover again", window: OPEN },
    ],
  },
  switch: {
    start: "/",
    setup: ["Up"],
    steps: [
      { pre: ["Right"], key: "OK", label: "open Discover", window: OPEN },
      { pre: ["Back"], key: "Right", label: "bar->Movies" },
      { key: "OK", label: "open Movies", window: OPEN },
      { pre: ["Back", "Right"], key: "OK", label: "open TV", window: OPEN },
      { pre: ["Back", "Left", "Left", "Left"], key: "OK", label: "open Home", window: OPEN },
    ],
  },
  film: {
    start: "/movies",
    setup: ["Down"],
    steps: [
      { key: "OK", label: "open film", window: OPEN }, { key: "Down", label: "film down" }, { key: "Up", label: "film up" },
      { key: "Back", label: "back to Movies", window: OPEN },
    ],
  },
  show: {
    start: "/tv",
    setup: ["Down"],
    optional: true,
    steps: [
      { key: "OK", label: "open show", window: OPEN }, { key: "Down", label: "show down" }, { key: "Down", label: "show down" },
      { key: "Right", label: "episode->episode" }, { key: "Back", label: "back to TV", window: OPEN },
    ],
  },
};
