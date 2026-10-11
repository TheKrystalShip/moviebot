// The walks: what a person does with the remote, as keys from a page loaded fresh. They name nothing a
// library holds, so the same walk runs on the scratch site and on a real television's own site.
//
// A step is the keys pressed before it, untimed, then one key that is timed; `to` is the path the step
// lands on, checked so a walk that wanders off measures nothing under a wrong name.

const Bar = ["Up", "Up", "Up"];

export const WALKS = {
  home: {
    start: "/",
    steps: [
      { keys: ["Down"] }, { keys: ["Down"] }, { keys: ["Down"] },
      { keys: ["Up"] }, { keys: ["Up"] }, { keys: ["Up"] },
    ],
  },
  bar: {
    start: "/",
    steps: [
      { name: "discover", keys: [...Bar, "Right", "Enter"], to: "/discover" },
      { name: "movies", keys: [...Bar, "Right", "Enter"], to: "/movies" },
      { name: "shows", keys: [...Bar, "Right", "Enter"], to: "/tv" },
      { name: "anime", keys: [...Bar, "Right", "Enter"], to: "/anime" },
      { name: "saved", keys: [...Bar, "Right", "Enter"], to: "/saved" },
      { name: "home", keys: [...Bar, "Left", "Left", "Left", "Left", "Left", "Enter"], to: "/" },
    ],
  },
  row: {
    start: "/movies",
    steps: [
      { keys: ["Down"] }, { keys: ["Right"] }, { keys: ["Right"] }, { keys: ["Right"] }, { keys: ["Right"] },
      { keys: ["Down"] }, { keys: ["Down"] }, { keys: ["Up"] },
    ],
  },
  film: {
    start: "/movies",
    steps: [
      { name: "open", keys: ["Down", "Enter"], to: "/movies/" },
      { keys: ["Down"] },
      { name: "back", keys: ["Escape"], to: "/movies" },
      { name: "open next", keys: ["Right", "Enter"], to: "/movies/" },
      { name: "back", keys: ["Escape"], to: "/movies" },
    ],
  },
  show: {
    start: "/tv",
    steps: [
      { name: "open", keys: ["Down", "Enter"], to: "/tv/" },
      { keys: ["Down"] }, { keys: ["Down"] },
      { name: "back", keys: ["Escape"], to: "/tv" },
    ],
  },
  discover: {
    start: "/discover",
    steps: [{ keys: ["Down"] }, { keys: ["Down"] }, { keys: ["Down"] }, { keys: ["Right"] }, { keys: ["Up"] }],
  },
};

export const Keys = { Up: "ArrowUp", Down: "ArrowDown", Left: "ArrowLeft", Right: "ArrowRight" };
