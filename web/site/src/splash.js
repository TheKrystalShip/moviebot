// The splash: the mark on the canvas, drawn by index.html with the document's first bytes, held over
// everything the site does before its first screen is ready, then faded away.
//
// What is on screen decides how long it stays. A screen that is ready the moment it is drawn (a
// sign-in code, a problem, a player) holds nothing, and the splash leaves once it is committed. A
// screen that is still coming holds it (`holdSplash`): the dark screen while a session is restored,
// and the first page while its reads are answered and its artwork decodes (`PageStage`). It is gone at
// `SPLASH_CAP_MS` after the document started whatever is still coming, and the page is shown as it
// stands, waiting the way it always does.
//
// It matches the screens a device draws before the site runs: the iOS launch images
// (`scripts/make-icons.mjs`), the colours Android builds an installed app's launch screen from (the
// manifest), and the television app's WebView background. So it takes over from them unseen.

/** The longest the splash stays, from the moment the document started. */
export const SPLASH_CAP_MS = 3000;
/** How long it takes to fade; `index.html` holds the same in the splash's transition. */
const FADE_MS = 280;

let holds = 0;
let gone = !document.getElementById("kc-splash");
let checking = false;

/**
 * Keeps the splash up until the function handed back is called. A hold let go is checked a frame
 * later, so a screen that replaces another in one commit takes the hold over before it can lapse.
 */
export function holdSplash() {
  holds += 1;
  let held = true;
  return () => {
    if (!held) return;
    held = false;
    holds -= 1;
    settleSplash();
  };
}

/** Lets the splash go if nothing on screen holds it, a frame from now. */
export function settleSplash() {
  if (gone || checking) return;
  checking = true;
  requestAnimationFrame(() => {
    checking = false;
    if (holds === 0) leave();
  });
}

function leave() {
  if (gone) return;
  gone = true;
  const splash = document.getElementById("kc-splash");
  if (!splash) return;
  splash.classList.add("is-leaving");
  const still = window.matchMedia && matchMedia("(prefers-reduced-motion: reduce)").matches;
  setTimeout(() => splash.remove(), still ? 0 : FADE_MS);
}

if (!gone) setTimeout(leave, Math.max(0, SPLASH_CAP_MS - performance.now()));
