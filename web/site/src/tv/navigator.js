import { Rank, back, menu, onBack, onMenu } from "./tv.js";

/**
 * Moving focus with a remote's arrows, from what is on the screen.
 *
 * Nothing on a page says where an arrow goes. On a press the navigator gathers what can take focus and
 * is drawn, scores each candidate in the arrow's direction from the focused element's rectangle — the
 * gap along the arrow first, then how far it sits off to the side — and moves focus to the best. Every
 * arrow is taken in TV mode: the WebView otherwise moves focus by rules of its own, which on these pages
 * skip whole regions and stop on the first floating button they meet.
 *
 * What counts as a candidate:
 * - anything that takes focus by Tab and is drawn, outside anything `aria-hidden` or `inert` (the page
 *   being prepared out of sight is `aria-hidden` until it is shown);
 * - not a container that is focusable only to be scrolled or grouped, such as a row's scrolling track;
 * - not a control inside a card that is itself a control. Menu goes into the focused card's own
 *   controls and Back comes out again.
 *
 * On top of the geometry:
 * - a candidate in the area focus is in, the sidebar or the page's column, comes before any in the
 *   other, so Down at the foot of a short page does not leave it for the sidebar while Left from its
 *   edge still reaches it;
 * - the topmost open dialog or menu holds focus inside it, and when it closes focus goes back to what
 *   was focused when it opened;
 * - a row that scrolls sideways remembers the element last focused in it, and coming back into the row
 *   lands there;
 * - entering the sidebar or a navigation landmark from outside lands on the item marked as the
 *   current page;
 * - focus is never left on nothing: when the focused element goes away, the nearest candidate to where
 *   it was takes it;
 * - a page that is shown puts focus on its first control outside navigation, or, when the page was
 *   come back to, on the control that was focused when it was left; until a key is pressed or focus
 *   goes anywhere else, focus follows that choice as the page's content arrives, since what is first
 *   changes as it does;
 * - a submitted search moves focus off its field to the form's submit button, which closes the
 *   keyboard over the results.
 *
 * Elements a key already means something to keep it: a slider keeps Left and Right, and a text field
 * keeps them until its caret is at the end it points to.
 */

const Focusable = 'a[href], button, input, select, textarea, [tabindex], [contenteditable="true"]';
const Overlay = '[role="dialog"], [role="menu"], [aria-modal="true"]';
/** Elements that act on their own: a card that is one, and anything nested in it, is a control in it. */
const Control = 'a[href], button, [role="link"][tabindex], [role="button"][tabindex]';
const InteractiveRoles = new Set([
  "button", "link", "menuitem", "menuitemradio", "menuitemcheckbox", "option", "tab", "slider",
  "switch", "checkbox", "radio", "combobox", "textbox", "searchbox", "spinbutton",
]);
const NativeControls = new Set(["A", "BUTTON", "INPUT", "SELECT", "TEXTAREA", "SUMMARY"]);
const Directions = { ArrowUp: "up", ArrowDown: "down", ArrowLeft: "left", ArrowRight: "right" };

/** How far two rectangles may overlap along the arrow and still be one ahead of the other. */
const Overlap = 4;
/** What sitting off to the side costs against the gap along the arrow. */
const AsideWeight = 3;

let inside = null;
let opener = null;
let last = null;
let returning = false;
const rowMemory = new WeakMap();
const pageMemory = new Map();

const rectOf = (el) => el.getBoundingClientRect();
const isNative = (el) => NativeControls.has(el.tagName);
const role = (el) => el.getAttribute("role");

function drawn(el) {
  if (el.closest('[aria-hidden="true"], [inert]')) return false;
  const r = rectOf(el);
  if (r.width < 1 || r.height < 1) return false;
  const style = getComputedStyle(el);
  return style.visibility !== "hidden" && style.display !== "none";
}

function usable(el) {
  if (el.tabIndex < 0 || el.disabled || el.getAttribute("aria-disabled") === "true") return false;
  if (el.tagName === "INPUT" && el.type === "hidden") return false;
  return true;
}

/** Focusable only to be scrolled or to group what is inside it. */
function isContainer(el) {
  if (isNative(el) || InteractiveRoles.has(role(el))) return false;
  return el.querySelector(Focusable) !== null;
}

/** Inside a card that is a control of its own, other than the one Menu went into. */
function nested(el, scope) {
  const card = el.parentElement && el.parentElement.closest(Control);
  return card !== null && card !== scope && scope.contains(card);
}

function candidates(scope) {
  const found = [];
  for (const el of scope.querySelectorAll(Focusable)) {
    if (!usable(el) || isContainer(el) || nested(el, scope) || !drawn(el)) continue;
    found.push(el);
  }
  return found;
}

function topOverlay() {
  const open = [...document.querySelectorAll(Overlay)].filter(drawn);
  return open.length > 0 ? open[open.length - 1] : null;
}

function scope() {
  const overlay = topOverlay();
  if (overlay) return overlay;
  if (inside && inside.isConnected && drawn(inside)) return inside;
  inside = null;
  return document.body;
}

/** The candidate's cost in the arrow's direction from `from`, or null when it is not that way. */
function cost(from, to, direction) {
  const horizontal = direction === "left" || direction === "right";
  let gap;
  switch (direction) {
    case "right": if (to.left < from.right - Overlap) return null; gap = to.left - from.right; break;
    case "left": if (to.right > from.left + Overlap) return null; gap = from.left - to.right; break;
    case "down": if (to.top < from.bottom - Overlap) return null; gap = to.top - from.bottom; break;
    default: if (to.bottom > from.top + Overlap) return null; gap = from.top - to.bottom;
  }
  const [a0, a1, b0, b1] = horizontal
    ? [from.top, from.bottom, to.top, to.bottom]
    : [from.left, from.right, to.left, to.right];
  const shared = Math.min(a1, b1) - Math.max(a0, b0);
  const aside = shared > 0 ? 0 : -shared;
  const centre = Math.abs((a0 + a1) / 2 - (b0 + b1) / 2);
  return Math.max(0, gap) + aside * AsideWeight + centre * 0.05;
}

/**
 * The nearest ancestor that scrolls sideways and only sideways: a row. A page's column that also
 * scrolls sideways, because something on it is too wide, is still a column.
 */
function rowOf(el) {
  for (let node = el.parentElement; node && node !== document.body; node = node.parentElement) {
    const style = getComputedStyle(node);
    if (/(auto|scroll)/.test(style.overflowX) && !/(auto|scroll)/.test(style.overflowY)
      && node.scrollWidth > node.clientWidth + 1) return node;
  }
  return null;
}

/** Where focus lands when it moves to `target` from `from`: the row's or the landmark's own place. */
function landing(target, from) {
  const row = rowOf(target);
  if (row && row !== rowOf(from)) {
    const remembered = rowMemory.get(row);
    if (remembered && remembered.isConnected && row.contains(remembered) && drawn(remembered)) return remembered;
  }
  const landmark = target.closest("aside, nav");
  if (landmark && !landmark.contains(from)) {
    const current = landmark.querySelector('[aria-current="page"]');
    if (current && usable(current) && drawn(current)) return current;
  }
  return target;
}

function reduceMotion() {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/** The nearest ancestor that scrolls up and down: the page's column, a menu's list. */
function columnOf(el) {
  for (let node = el.parentElement; node && node !== document.body; node = node.parentElement) {
    const style = getComputedStyle(node);
    if (/(auto|scroll)/.test(style.overflowY) && node.scrollHeight > node.clientHeight + 1) return node;
  }
  return null;
}

/**
 * Focuses `el` and brings it into view: its row scrolled so it is the row's first, which is also where
 * a row's snapping would put it, and its column so it sits in the middle. Each is placed outright
 * rather than by `scrollIntoView`, whose "nearest" a row's snapping then undoes, leaving the card cut
 * off at the row's edge.
 */
export function focusElement(el) {
  el.focus({ preventScroll: true, focusVisible: true });
  const behavior = reduceMotion() ? "auto" : "smooth";
  const row = rowOf(el);
  if (row) {
    const inset = parseFloat(getComputedStyle(row).paddingLeft) || 0;
    row.scrollTo({ left: Math.max(0, row.scrollLeft + rectOf(el).left - rectOf(row).left - inset), behavior });
  }
  const column = columnOf(el);
  if (column) {
    const r = rectOf(el);
    const c = rectOf(column);
    column.scrollTo({ top: Math.max(0, column.scrollTop + (r.top + r.height / 2) - (c.top + c.height / 2)), behavior });
  }
  // Anything else that scrolls sideways around it, such as a strip of tabs too wide for its column,
  // moves as little as brings it in.
  for (let node = el.parentElement; node && node !== document.body; node = node.parentElement) {
    if (node === row || node.scrollWidth <= node.clientWidth + 1) continue;
    if (!/(auto|scroll)/.test(getComputedStyle(node).overflowX)) continue;
    const r = rectOf(el);
    const c = rectOf(node);
    const by = r.left < c.left ? r.left - c.left - SideMargin : r.right > c.right ? r.right - c.right + SideMargin : 0;
    if (by !== 0) node.scrollBy({ left: by, behavior });
  }
}

/** The room left beside an element brought in sideways. */
const SideMargin = 16;

/** The first control of a region outside its navigation, preferring one marked `data-tv-initial`. */
function initialIn(region) {
  const all = candidates(region);
  return all.find((el) => el.hasAttribute("data-tv-initial"))
    || all.find((el) => !el.closest("nav"))
    || all[0]
    || null;
}

function focusInitial(within) {
  const main = within === document.body ? document.querySelector("main") : null;
  const target = (main && initialIn(main)) || initialIn(within);
  if (target) focusElement(target);
}

function nearestTo(rect, within) {
  let best = null;
  let bestDistance = Infinity;
  const x = rect.left + rect.width / 2;
  const y = rect.top + rect.height / 2;
  for (const el of candidates(within)) {
    const r = rectOf(el);
    const distance = Math.hypot(r.left + r.width / 2 - x, r.top + r.height / 2 - y);
    if (distance < bestDistance) { bestDistance = distance; best = el; }
  }
  return best;
}

function move(direction) {
  const region = scope();
  const from = document.activeElement;
  if (!from || from === document.body || !region.contains(from) || from === region) {
    focusInitial(region);
    return;
  }
  const origin = rectOf(from);
  const home = areaOf(from);
  let best = null;
  let bestCost = Infinity;
  let bestHome = false;
  for (const el of candidates(region)) {
    if (el === from || el.contains(from) || from.contains(el)) continue;
    const c = cost(origin, rectOf(el), direction);
    if (c === null) continue;
    // Anything that way in the area focus is in comes before anything in another.
    const same = areaOf(el) === home;
    if ((same && !bestHome) || (same === bestHome && c < bestCost)) { bestCost = c; best = el; bestHome = same; }
  }
  if (best) focusElement(landing(best, from));
}

/** The part of the page an element is in: the sidebar, the page's column, or whatever holds it. */
const areaOf = (el) => el.closest("aside, main");

/** Whether a key the element already answers should stay with it. */
function keptByElement(el, key) {
  if (!el || el === document.body) return false;
  const sideways = key === "ArrowLeft" || key === "ArrowRight";
  if (role(el) === "slider" || (el.tagName === "INPUT" && el.type === "range")) return sideways;
  if (el.tagName === "TEXTAREA" || el.isContentEditable) return true;
  if (el.tagName === "INPUT" && sideways && typeof el.selectionStart === "number") {
    const atStart = el.selectionStart === 0 && el.selectionEnd === 0;
    const atEnd = el.selectionStart === el.value.length;
    return key === "ArrowLeft" ? !atStart : !atEnd;
  }
  return false;
}

function onKey(event) {
  following = null;
  if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
  const active = document.activeElement;

  if (event.key === "Escape") {
    // An open overlay closes on Escape by itself; anything else is Back.
    if (!topOverlay() && back()) event.preventDefault();
    return;
  }
  if (event.key === "ContextMenu") {
    if (menu()) event.preventDefault();
    return;
  }
  if (event.key === "Enter") {
    if (!active || active === document.body) {
      event.preventDefault();
      focusInitial(scope());
    } else if (!isNative(active) && active.tabIndex >= 0) {
      // A focusable element that answers a click but not a key: OK presses it.
      event.preventDefault();
      active.click();
    }
    return;
  }
  const direction = Directions[event.key];
  if (!direction || keptByElement(active, event.key)) return;
  event.preventDefault();
  move(direction);
}

function remember(event) {
  const el = event.target;
  if (!(el instanceof HTMLElement) || el === document.body) return;
  // Focus put anywhere the page's first control was not chosen ends the following: a key, a field
  // that focuses itself, an overlay that opened.
  if (following && el !== following.chose) following = null;
  last = { el, rect: rectOf(el) };
  const row = rowOf(el);
  if (row) rowMemory.set(row, el);
  const main = document.querySelector("main");
  if (main && main.contains(el)) {
    pageMemory.set(window.location.href, describe(el, main));
  }
}

function describe(el, main) {
  return {
    href: el.getAttribute("href"),
    label: label(el),
    index: candidates(main).indexOf(el),
  };
}

const label = (el) => (el.getAttribute("aria-label") || el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 80);

function recall(main) {
  const memory = pageMemory.get(window.location.href);
  if (!memory) return null;
  const all = candidates(main);
  return all.find((el) => memory.href && el.getAttribute("href") === memory.href && label(el) === memory.label)
    || all.find((el) => label(el) === memory.label)
    || all[memory.index]
    || null;
}

/**
 * The page's first control, which focus follows while the page's content arrives: until a key is
 * pressed or focus goes anywhere else, however long the page takes. Nobody has asked for anything
 * else yet, so there is nothing to lose by it.
 */
let following = null;

/** A page has been shown in `main`: focus its first control, or the one it was left on. */
export function pageShown(main) {
  if (!main) return;
  const back = returning;
  returning = false;
  following = { pick: () => (back && recall(main)) || initialIn(main) };
  follow();
}

function follow() {
  if (!following) return false;
  const target = following.pick();
  if (target && target !== document.activeElement) {
    following.chose = target;
    focusElement(target);
  }
  return true;
}

/** Keeps focus inside an overlay that opened, back where it was when one closed, and never on nothing. */
function settle() {
  const overlay = topOverlay();
  if (!overlay && follow()) return;
  const active = document.activeElement;
  if (overlay && !overlay.contains(active)) {
    if (!opener || !opener.isConnected || overlay.contains(opener)) opener = active !== document.body ? active : last && last.el;
    const choice = candidates(overlay);
    const target = choice.find((el) => el.getAttribute("aria-checked") === "true" || el.getAttribute("aria-current") || el.getAttribute("aria-selected") === "true") || choice[0];
    if (target) focusElement(target);
    return;
  }
  if (active && active !== document.body) return;
  if (!overlay && opener && opener.isConnected && drawn(opener)) {
    focusElement(opener);
    opener = null;
    return;
  }
  opener = null;
  const region = scope();
  const target = last ? nearestTo(last.rect, region) : null;
  if (target) focusElement(target);
}

let pending = false;
function settleSoon() {
  if (pending) return;
  pending = true;
  requestAnimationFrame(() => { pending = false; settle(); });
}

/** Menu on a card that holds controls of its own goes in among them; Back comes out. */
function enterCard() {
  const active = document.activeElement;
  if (!active || active === document.body || !active.matches(Control)) return false;
  const own = [...active.querySelectorAll(Focusable)].filter((el) => usable(el) && drawn(el));
  if (own.length === 0) return false;
  inside = active;
  focusElement(own[0]);
  return true;
}

function leaveCard() {
  if (!inside) return false;
  const card = inside;
  inside = null;
  if (card.isConnected && drawn(card)) focusElement(card);
  return true;
}

function closeOverlay() {
  if (!topOverlay()) return false;
  const target = document.activeElement || document;
  target.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true, cancelable: true }));
  return true;
}

/** Moves focus to the current page's item in the navigation, or answers false when it is already there. */
export function focusNavigation() {
  const active = document.activeElement;
  if (active && active.closest("nav")) return false;
  for (const nav of document.querySelectorAll("nav")) {
    const current = nav.querySelector('[aria-current="page"]');
    if (current && usable(current) && drawn(current)) {
      focusElement(current);
      return true;
    }
  }
  return false;
}

/** Starts taking the remote's keys for the whole document. Called once, in TV mode only. */
export function startNavigator() {
  // The bubbling phase, so an element that answers a key itself, and the player's remote, which
  // listens while capturing, are asked first.
  window.addEventListener("keydown", onKey);
  document.addEventListener("focusin", remember);
  document.addEventListener("focusout", settleSoon);
  window.addEventListener("popstate", () => { returning = true; });
  document.addEventListener("submit", (event) => {
    const form = event.target;
    const active = document.activeElement;
    if (!(form instanceof HTMLFormElement) || !form.contains(active) || active.tagName !== "INPUT") return;
    const button = form.querySelector('[type="submit"]');
    if (button) button.focus({ focusVisible: true });
    else active.blur();
  }, true);
  new MutationObserver(settleSoon).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["aria-hidden", "inert", "style", "class", "hidden"] });
  onBack(() => closeOverlay() || leaveCard(), Rank.overlay);
  onMenu(enterCard, Rank.page);
}
