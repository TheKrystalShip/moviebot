// Where the page column stands, per history entry. The column scrolls, not the window, so the browser
// restores nothing of its own: a page pushed opens at its top, a page come back to by Back or Forward,
// or reloaded, opens where it was left, and a page redrawn in place by a replace (a season, a tab, a
// filter) stays where it is.

import React from "react";
import { sessionRecord } from "./visits.js";

/** How long a page come back to is held at its place while what it shows fills in under it. */
const HOLD_MS = 2500;

/** What a person does that ends the hold: the place is theirs from then on. */
const INPUTS = ["wheel", "touchstart", "pointerdown", "keydown"];

const positions = sessionRecord("cinema:scroll", 100);

if ("scrollRestoration" in window.history) window.history.scrollRestoration = "manual";

/**
 * The column's place kept for each entry as it scrolls, and `shown(route, promoted)` for `PageStage`'s
 * `onShown`, which puts the column where the page shown belongs. A page come back to is held at its
 * place for `HOLD_MS`, put back each frame its content moves it, until the person scrolls, presses or
 * types; its reads and lazy shelves fill in during that time.
 */
export function useScrollMemory(columnRef) {
  const shownEntry = React.useRef(null);
  const hold = React.useRef(null);
  // The first page is shown before the column's ref is set, and is placed once it is.
  const waiting = React.useRef(null);

  const place = React.useCallback((column, route, promoted) => {
    if (route.move === "pop" || route.move === "load") {
      const top = positions.get(route.entry);
      if (top > 0) hold.current = holdAt(column, top, () => {
        hold.current = null;
        positions.set(route.entry, Math.round(column.scrollTop));
      });
      else column.scrollTop = 0;
    } else if (route.move === "push" || promoted) {
      column.scrollTop = 0;
    }
  }, []);

  React.useEffect(() => {
    const column = columnRef.current;
    if (!column) return undefined;
    if (waiting.current) place(column, ...waiting.current);
    waiting.current = null;
    let frame = 0;
    const onScroll = () => {
      if (hold.current || frame) return;
      frame = requestAnimationFrame(() => {
        frame = 0;
        if (shownEntry.current && !hold.current) positions.set(shownEntry.current, Math.round(column.scrollTop));
      });
    };
    column.addEventListener("scroll", onScroll, { passive: true });
    return () => {
      column.removeEventListener("scroll", onScroll);
      cancelAnimationFrame(frame);
      if (hold.current) hold.current();
      hold.current = null;
    };
  }, [columnRef, place]);

  return React.useCallback((route, promoted) => {
    if (hold.current) hold.current();
    hold.current = null;
    shownEntry.current = route.entry;
    if (columnRef.current) place(columnRef.current, route, promoted);
    else waiting.current = [route, promoted];
  }, [columnRef, place]);
}

/**
 * Holds `column` at `top` until `HOLD_MS` has passed or a person acts, then calls `done`. Answers the
 * function that lets go without calling `done`, for the next page shown.
 */
function holdAt(column, top, done) {
  const until = performance.now() + HOLD_MS;
  let frame = 0;
  const stop = () => {
    cancelAnimationFrame(frame);
    for (const type of INPUTS) window.removeEventListener(type, finish, true);
  };
  function finish() {
    stop();
    done();
  }
  const step = () => {
    if (Math.abs(column.scrollTop - top) > 1) column.scrollTop = top;
    if (performance.now() >= until) finish();
    else frame = requestAnimationFrame(step);
  };
  for (const type of INPUTS) window.addEventListener(type, finish, { capture: true, passive: true });
  step();
  return stop;
}
