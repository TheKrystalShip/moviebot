import React from "react";

/** How long a finger holds before the press counts as a long one, in milliseconds. */
export const HOLD_MS = 500;
/** How far a finger may drift while it holds, in pixels. */
export const SLOP = 10;
/** How long the click that follows a completed long press is still swallowed, in milliseconds. */
const CLICK_WINDOW_MS = 400;

/** Whether a pointer going down begins a long press: a finger on its main contact. */
export function beginsPress(pointerType, button) {
  return pointerType === "touch" && button === 0;
}

/** Whether a finger has drifted from where it went down by more than `slop`. */
export function drifted(from, to, slop = SLOP) {
  return Math.hypot(to.x - from.x, to.y - from.y) > slop;
}

/**
 * A long press with a finger, as props to spread on the element that holds the press: `onLongPress`
 * runs once the finger has held for `delay` without drifting more than `slop` or scrolling anything. A
 * mouse or a pen never begins one, so a pointer's own press stays an ordinary press. When it runs, the
 * click that follows the finger lifting is swallowed so the element does not also open, and the
 * browser's own context menu is held off while a finger is down. A tap, a swipe along a shelf and a
 * page scroll never reach it.
 */
export function useLongPress(onLongPress, { delay = HOLD_MS, slop = SLOP } = {}) {
  const latest = React.useRef(onLongPress);
  latest.current = onLongPress;
  const press = React.useRef({ timer: 0, settle: 0, from: null, down: false, fired: false });

  const { props, cancel } = React.useMemo(() => {
    const s = press.current;
    const cancel = () => {
      clearTimeout(s.timer);
      s.timer = 0;
      s.from = null;
      document.removeEventListener("scroll", cancel, true);
    };
    const lift = () => {
      cancel();
      s.down = false;
      if (s.fired) s.settle = setTimeout(() => { s.fired = false; }, CLICK_WINDOW_MS);
    };
    const props = {
      onPointerDown(e) {
        if (!beginsPress(e.pointerType, e.button)) return;
        cancel();
        clearTimeout(s.settle);
        s.fired = false;
        s.down = true;
        s.from = { x: e.clientX, y: e.clientY };
        document.addEventListener("scroll", cancel, true);
        s.timer = setTimeout(() => {
          cancel();
          s.fired = true;
          if (navigator.vibrate) navigator.vibrate(10);
          latest.current();
        }, delay);
      },
      onPointerMove(e) {
        if (s.from && drifted(s.from, { x: e.clientX, y: e.clientY }, slop)) cancel();
      },
      onPointerUp: lift,
      onPointerCancel: lift,
      onContextMenu(e) {
        const caps = e.nativeEvent && e.nativeEvent.sourceCapabilities;
        if (s.down || (caps && caps.firesTouchEvents)) e.preventDefault();
      },
      onClickCapture(e) {
        if (!s.fired) return;
        s.fired = false;
        e.preventDefault();
        e.stopPropagation();
      },
    };
    return { props, cancel };
  }, [delay, slop]);

  React.useEffect(() => () => {
    cancel();
    clearTimeout(press.current.settle);
  }, [cancel]);

  return props;
}
