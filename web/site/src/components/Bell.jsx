import React from "react";
import { createPortal } from "react-dom";
import { Icon } from "@thekrystalship/krystal-ui";
import { profilePath } from "../paths.js";
import { navigate } from "../router.js";
import { ago } from "../time.js";
import { KIND, markRead, useNotifications } from "../notifications.js";
import { Drawer } from "./Drawer.jsx";

/**
 * The bell in the sidebar, above the person: "Notifications" and how many are unread, or the bell
 * alone with its count on the collapsed rail. It opens the list beside the sidebar.
 */
export function BellNavItem() {
  const { unread } = useNotifications();
  return (
    <Bell placement="beside">
      {(open, toggle) => (
        <div className={"nav-item kc-bell-item" + (open ? " nav-item--active" : "")} role="button" tabIndex={0}
          onClick={toggle} onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); toggle(); } }}
          data-tip={"Notifications" + (unread > 0 ? " · " + unread : "")}
          aria-label={"Notifications" + (unread > 0 ? `, ${unread} unread` : "")} aria-haspopup="dialog" aria-expanded={open}>
          <Icon name="bell" size={16} />
          <span className="nav-item__label">Notifications</span>
          {unread > 0 && <span className="nav-item__badge kc-bell-item__badge">{unread}</span>}
        </div>
      )}
    </Bell>
  );
}

/** The bell at the top right of a phone's front page, with a dot while something is unread. */
export function BellButton() {
  const { unread } = useNotifications();
  return (
    <Bell placement="sheet">
      {(open, toggle) => (
        <button type="button" className="kc-bell-button" onClick={toggle} aria-haspopup="dialog" aria-expanded={open}
          aria-label={"Notifications" + (unread > 0 ? `, ${unread} unread` : "")}>
          <Icon name="bell" size={20} />
          {unread > 0 && <span className="kc-bell-button__dot" />}
        </button>
      )}
    </Bell>
  );
}

/**
 * A trigger and the list it opens: beside the sidebar on a wide screen, in the site's drawer on a
 * phone. A press outside it, or Escape, closes it.
 */
function Bell({ placement, children }) {
  const [open, setOpen] = React.useState(false);
  const [pos, setPos] = React.useState(null);
  const triggerRef = React.useRef(null);
  const panelRef = React.useRef(null);
  const { unread } = useNotifications();
  const close = React.useCallback(() => setOpen(false), []);

  React.useLayoutEffect(() => {
    if (!open || placement !== "beside" || !triggerRef.current) return undefined;
    const place = () => {
      const sidebar = triggerRef.current.closest(".sidebar");
      const right = (sidebar || triggerRef.current).getBoundingClientRect().right;
      setPos({ left: Math.round(right + 12), bottom: 16 });
    };
    place();
    window.addEventListener("resize", place);
    return () => window.removeEventListener("resize", place);
  }, [open, placement]);

  React.useEffect(() => {
    if (!open || placement !== "beside") return undefined;
    const outside = (e) => {
      if (triggerRef.current?.contains(e.target) || panelRef.current?.contains(e.target)) return;
      setOpen(false);
    };
    const escape = (e) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", outside);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("mousedown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open, placement]);

  const markAll = unread > 0 && <button type="button" className="kc-bell__mark" onClick={() => markRead()}>Mark all read</button>;
  return (
    <div className="kc-bell" ref={triggerRef}>
      {children(open, () => setOpen((o) => !o))}
      {open && placement === "sheet" && (
        <Drawer title="Notifications" action={markAll} onClose={close} className="kc-bell__drawer">
          <BellList onClose={close} />
        </Drawer>
      )}
      {open && placement === "beside" && createPortal(
        <div ref={panelRef} className="kc-bell__panel" style={pos || { visibility: "hidden" }} role="dialog" aria-label="Notifications">
          <div className="kc-bell__head">
            <span className="kc-bell__title">Notifications</span>
            {markAll}
          </div>
          <BellList onClose={close} />
        </div>,
        document.body)}
    </div>
  );
}

/** What the API told this person, newest first, each opening the page it names. */
function BellList({ onClose }) {
  const { items, error } = useNotifications();

  const open = (n) => {
    onClose();
    if (!n.read) markRead([n.id]);
    if (n.url) navigate(n.url);
  };

  return (
    <>
      <div className="kc-bell__list">
        {items && items.length === 0 && (
          <div className="kc-bell__empty"><Icon name="bell-off" size={18} /><span>Nothing yet.</span></div>
        )}
        {!items && error && <div className="kc-bell__empty"><span>{error.message}</span></div>}
        {(items || []).map((n) => {
          const kind = KIND[n.kind] || KIND.kept;
          return (
            <div key={n.id} className={"kc-bell__item" + (n.read ? "" : " is-unread") + (n.url ? " is-link" : "")}
              role={n.url ? "button" : undefined} tabIndex={n.url ? 0 : undefined}
              onClick={() => open(n)} onKeyDown={(e) => { if (e.key === "Enter") open(n); }}>
              <span className={"kc-bell__icon kc-bell__icon--" + kind.tone}><Icon name={kind.icon} size={16} strokeWidth={2} /></span>
              <div className="kc-bell__body">
                <span className="kc-bell__text">{n.text}</span>
                <span className="kc-bell__meta">
                  <span className="kc-bell__time">{ago(n.createdAt)}</span>
                  {n.action && n.url && <span className="kc-bell__action">{n.action} →</span>}
                </span>
              </div>
            </div>
          );
        })}
      </div>
      <button type="button" className="kc-bell__settings" onClick={() => { onClose(); navigate(profilePath("notifications")); }}>
        <Icon name="settings-2" size={13} />Push notification settings
      </button>
    </>
  );
}
