import { createSpatialFocus } from "@thekrystalship/krystal/lib/spatialFocus";
import { Rank, back, menu, onBack, onMenu } from "./tv.js";

/**
 * Moving focus with a remote's arrows is the package's navigator (`createSpatialFocus`); this is the
 * part that is about Cinema's pages. Back and Menu come from the wrapper app (`tv.js`): Back closes an
 * open overlay, then comes out of a card Menu went into; Menu goes into a card that holds controls of
 * its own. A page that is shown puts focus on its first control outside navigation (or the one marked
 * `data-tv-initial`), or, when the page was come back to, on the control it was left by.
 */
const focus = createSpatialFocus({ onBack: back, onMenu: menu });

/** Whether the next page shown is one that history went back to. */
let returning = false;

/** A page has been shown in `main`: focus its first control, or the one it was left on. */
export function pageShown(main) {
  if (!main) return;
  const back = returning;
  returning = false;
  focus.follow(() => (back && focus.recall(main)) || focus.initialIn(main));
}

/** Moves focus to the current page's item in the navigation, or answers false when it is already there. */
export const focusNavigation = () => focus.focusNavigation();

/** Starts taking the remote's keys for the whole document. Called once, in TV mode only. */
export function startNavigator() {
  focus.start();
  window.addEventListener("popstate", () => { returning = true; });
  onBack(() => focus.closeOverlay() || focus.leaveCard(), Rank.overlay);
  onMenu(() => focus.enterCard(), Rank.page);
}
