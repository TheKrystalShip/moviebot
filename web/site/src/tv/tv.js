/**
 * Whether this page is on a television, driven by a remote, and the two keys a remote has that never
 * reach a page.
 *
 * A television is known by the wrapper app's mark in the user agent, or by `?tv=1` on any address,
 * which holds for the rest of the tab's life (`?tv=0` lets go of it) so the mode can be worked on in
 * a desktop browser. Media queries cannot say it: the reference Fire TV's WebView matches both
 * `(pointer: coarse)` and `(hover: hover)`.
 *
 * Back and Menu reach the wrapper's activity and never the page, so the wrapper calls
 * `window.krystalTv.back()` and `window.krystalTv.menu()` and reads what they answer: whether the page
 * used the key. A Back the page did not use closes the app. In a desktop browser in TV mode, Escape
 * stands for Back and the context-menu key for Menu.
 */

const UserAgentMark = "KrystalCinemaTV";
const StoredFlag = "cinema.tv";

function detect() {
  if (typeof window === "undefined") return false;
  try {
    const asked = new URLSearchParams(window.location.search).get("tv");
    if (asked === "1") sessionStorage.setItem(StoredFlag, "1");
    if (asked === "0") sessionStorage.removeItem(StoredFlag);
    if (sessionStorage.getItem(StoredFlag) === "1") return true;
  } catch {
    // Storage refused: only the user agent can say.
  }
  return navigator.userAgent.includes(UserAgentMark);
}

/** Whether this page is on a television. Decided once, when the site loads. */
export const onTv = detect();

/**
 * Who answers Back and Menu. A handler answers whether it used the key, and the first that does ends
 * it: a higher `rank` first, and among equals the newest. An open overlay outranks everything
 * (`Rank.overlay`, the navigator's), a screen that holds its own state comes next (`Rank.screen`, the
 * player's), and the page last (`Rank.page`, the frame's way back).
 */
export const Rank = { page: 0, screen: 5, overlay: 10 };

const backHandlers = [];
const menuHandlers = [];

function register(list, handler, rank) {
  const entry = { handler, rank };
  const at = list.findIndex((other) => other.rank <= rank);
  list.splice(at < 0 ? list.length : at, 0, entry);
  return () => {
    const index = list.indexOf(entry);
    if (index >= 0) list.splice(index, 1);
  };
}

/** Adds a Back handler at `rank`. Returns its removal. */
export const onBack = (handler, rank = Rank.page) => register(backHandlers, handler, rank);

/** Adds a Menu handler at `rank`. Returns its removal. */
export const onMenu = (handler, rank = Rank.page) => register(menuHandlers, handler, rank);

const run = (list) => list.some((entry) => entry.handler() === true);

/** Back, as the remote's key: whether the page used it. */
export const back = () => run(backHandlers);

/** Menu, as the remote's key: whether the page used it. */
export const menu = () => run(menuHandlers);

/** Marks the document and opens the wrapper's way in. Called once, before the site renders. */
export function startTv() {
  if (!onTv) return;
  document.documentElement.dataset.tv = "";
  window.krystalTv = { back, menu };
}
