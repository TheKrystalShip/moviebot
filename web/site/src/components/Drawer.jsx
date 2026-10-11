import React from "react";
import { createPortal } from "react-dom";
import { Icon } from "@thekrystalship/krystal-ui";
import "./drawer.css";

// A pull down by the head settles the drawer away when it has gone this far, as a fraction of the
// drawer's height (never under the floor or over the cap, so a short sheet and a tall one both ask for
// a deliberate pull), or when it is let go moving down faster than the flick speed.
const PULL_FRACTION = 0.3;
const PULL_FLOOR = 40;
const PULL_CAP = 120;
const FLICK_SPEED = 0.5; // px per ms, over the last 100 ms of the finger's travel
const FLICK_FLOOR = 16;  // a flick shorter than this is a twitch
const SLOP = 6;          // px a press travels before it is a drag rather than a tap
const SETTLE_MS = 180;

/**
 * Pulling by the head: the drawer follows the finger down and, let go, settles open again or hands
 * the pull to `onPull` (which closes it). Bound to the head's pointer events; the head holds
 * `touch-action: none`, so a pull never reaches the browser's own scrolling or pull to refresh.
 * Returns the props for the head, and `panel`/`scrim` refs whose styles it drives directly.
 */
function usePull(onPull) {
  const panel = React.useRef(null);
  const scrim = React.useRef(null);
  const grab = React.useRef(null);
  const timer = React.useRef(0);
  const pull = React.useRef(onPull);
  pull.current = onPull;
  React.useEffect(() => () => clearTimeout(timer.current), []);

  const place = (dy, animate) => {
    const el = panel.current;
    if (!el) return;
    el.style.animation = "none";
    el.style.transition = animate ? `transform ${SETTLE_MS}ms var(--ease-out)` : "none";
    el.style.transform = dy ? `translateY(${dy}px)` : "";
    if (scrim.current) {
      const h = el.offsetHeight || 1;
      scrim.current.style.transition = animate ? `opacity ${SETTLE_MS}ms var(--ease-out)` : "none";
      scrim.current.style.opacity = dy ? String(Math.max(0, 1 - dy / h)) : "";
    }
  };
  const settleOpen = () => {
    const animate = !window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    place(0, animate);
    // Once at rest the inline transition goes, so a drawer's own transform is never slowed by it.
    clearTimeout(timer.current);
    timer.current = setTimeout(() => {
      if (panel.current) panel.current.style.transition = "";
      if (scrim.current) scrim.current.style.transition = "";
    }, animate ? SETTLE_MS : 0);
  };

  const onPointerDown = (e) => {
    if (e.button > 0 || !e.isPrimary) return;
    grab.current = { id: e.pointerId, x: e.clientX, y: e.clientY, dragging: false, samples: [{ t: e.timeStamp, y: e.clientY }] };
    if (panel.current) panel.current.style.transition = "none";
  };
  const onPointerMove = (e) => {
    const g = grab.current;
    if (!g || g.id !== e.pointerId) return;
    const dy = e.clientY - g.y, dx = e.clientX - g.x;
    if (!g.dragging) {
      if (Math.abs(dx) > SLOP && Math.abs(dx) > Math.abs(dy)) { grab.current = null; return; }
      if (dy <= SLOP) return;
      g.dragging = true;
      e.currentTarget.setPointerCapture(e.pointerId);
      e.currentTarget.classList.add("is-held");
    }
    g.samples.push({ t: e.timeStamp, y: e.clientY });
    while (g.samples.length > 2 && e.timeStamp - g.samples[0].t > 100) g.samples.shift();
    place(Math.max(0, dy), false);
  };
  const finish = (e, cancelled) => {
    const g = grab.current;
    if (!g || g.id !== e.pointerId) return;
    grab.current = null;
    if (!g.dragging) return;
    e.currentTarget.classList.remove("is-held");
    // The press that was a drag is not a tap on whatever the finger started on.
    const swallow = (ev) => { ev.stopPropagation(); ev.preventDefault(); };
    window.addEventListener("click", swallow, { capture: true, once: true });
    setTimeout(() => window.removeEventListener("click", swallow, { capture: true }), 0);

    const el = panel.current;
    const first = g.samples[0], last = g.samples[g.samples.length - 1];
    const dy = Math.max(0, last.y - g.y);
    const speed = last.t > first.t ? (last.y - first.y) / (last.t - first.t) : 0;
    const far = Math.min(PULL_CAP, Math.max(PULL_FLOOR, (el?.offsetHeight || 0) * PULL_FRACTION));
    const pulled = !cancelled && (dy >= far || (speed >= FLICK_SPEED && dy >= FLICK_FLOOR));
    if (!pulled) { settleOpen(); return; }

    // Away it goes, then the pull is handed over; a drawer that stays (a close the page refuses)
    // comes back to rest rather than staying off screen.
    const handOver = () => { pull.current(); settleOpen(); };
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches || !el) { handOver(); return; }
    place(el.offsetHeight, true);
    clearTimeout(timer.current);
    timer.current = setTimeout(handOver, SETTLE_MS);
  };

  return {
    panel, scrim,
    head: {
      onPointerDown, onPointerMove,
      onPointerUp: (e) => finish(e, false),
      onPointerCancel: (e) => finish(e, true),
    },
  };
}

/**
 * A drawer from the bottom edge, the full width of the screen, over a scrim: a phone's way of
 * opening a menu or a list. A press on the scrim, or Escape, closes it.
 *
 * Its head (the tab along the top edge and `title`, `back` and `action`, or `head` in their place) is
 * the handle: pulled down it follows the finger, and let go far enough or fast enough it calls
 * `onPull`, which closes the drawer unless a drawer chooses otherwise by passing its own.
 *
 * `contained` draws it inside the nearest positioned ancestor rather than over the page, which is
 * how the player keeps its menus inside its own frame (the frame is the screen on a phone, and the
 * Activity's whole view in Discord). Otherwise it is portalled over the page and the tab bar.
 */
export function Drawer({ title, back, action, head, onClose, onPull, contained = false, label, className = "", role = "dialog", children }) {
  React.useEffect(() => {
    const escape = (e) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", escape);
    return () => document.removeEventListener("keydown", escape);
  }, [onClose]);

  const pull = usePull(onPull || onClose);

  const drawer = (
    <>
      <div ref={pull.scrim} className={"kc-drawer-scrim" + (contained ? " is-contained" : "")} onClick={onClose} />
      <div ref={pull.panel} className={"kc-drawer" + (contained ? " is-contained" : "") + (className ? " " + className : "")}
        role={role} aria-label={label || (typeof title === "string" ? title : undefined)}>
        <div className="kc-drawer__top" {...pull.head}>
          <div className="kc-drawer__grabber" />
          {head || ((title || back || action) && (
            <div className="kc-drawer__head">
              {back && (
                <button type="button" className="kc-drawer__back" aria-label={back.label} onClick={back.onClick}>
                  <Icon name="arrow-left" size={14} />
                </button>
              )}
              {title && <span className="kc-drawer__title">{title}</span>}
              {action && <span className="kc-drawer__action">{action}</span>}
            </div>
          ))}
        </div>
        {children}
      </div>
    </>
  );
  return contained ? drawer : createPortal(drawer, document.body);
}
