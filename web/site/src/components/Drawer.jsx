import React from "react";
import { createPortal } from "react-dom";
import { Icon } from "@thekrystalship/krystal-ui";
import "./drawer.css";

/**
 * A drawer from the bottom edge, the full width of the screen, over a scrim: a phone's way of
 * opening a menu or a list. A press on the scrim, or Escape, closes it.
 *
 * `contained` draws it inside the nearest positioned ancestor rather than over the page, which is
 * how the player keeps its menus inside its own frame (the frame is the screen on a phone, and the
 * Activity's whole view in Discord). Otherwise it is portalled over the page and the tab bar.
 * `title` heads it, `back` puts a way back before the title, and `action` sits at its far end.
 */
export function Drawer({ title, back, action, onClose, contained = false, label, className = "", role = "dialog", children }) {
  React.useEffect(() => {
    const escape = (e) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", escape);
    return () => document.removeEventListener("keydown", escape);
  }, [onClose]);

  const drawer = (
    <>
      <div className={"kc-drawer-scrim" + (contained ? " is-contained" : "")} onClick={onClose} />
      <div className={"kc-drawer" + (contained ? " is-contained" : "") + (className ? " " + className : "")}
        role={role} aria-label={label || (typeof title === "string" ? title : undefined)}>
        <div className="kc-drawer__grabber" />
        {(title || back || action) && (
          <div className="kc-drawer__head">
            {back && (
              <button type="button" className="kc-drawer__back" aria-label={back.label} onClick={back.onClick}>
                <Icon name="arrow-left" size={14} />
              </button>
            )}
            {title && <span className="kc-drawer__title">{title}</span>}
            {action && <span className="kc-drawer__action">{action}</span>}
          </div>
        )}
        {children}
      </div>
    </>
  );
  return contained ? drawer : createPortal(drawer, document.body);
}
