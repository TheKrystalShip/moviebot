import { Button, IconButton, NavItem, NotificationTray } from "@thekrystalship/krystal";
import { profilePath } from "../paths.js";
import { navigate } from "../router.js";
import { inbox } from "../notifications.js";

/** The bell's list, opening the page a row names and ending in the way to the push settings. */
export function Bell({ drawer, className, children }) {
  return (
    <NotificationTray inbox={inbox} drawer={drawer} trigger={children} className={className}
      onOpen={(n) => { if (n.url) navigate(n.url); }}
      footer={({ close }) => (
        <Button variant="ghost" size="sm" icon="settings-2" className="kc-bell__settings"
          onClick={() => { close(); navigate(profilePath("notifications")); }}>
          Push notification settings
        </Button>
      )} />
  );
}

/**
 * The bell in the sidebar, above the person: "Notifications" and how many are unread, or the bell
 * alone with its count on the collapsed rail. It opens the list beside the sidebar.
 */
export function BellNavItem() {
  return (
    <Bell>
      {({ open, toggle, unread }) => (
        <NavItem icon="bell" label="Notifications" badge={unread} active={open} onClick={toggle} role="button"
          tip={"Notifications" + (unread > 0 ? " · " + unread : "")} aria-haspopup="dialog" aria-expanded={open}
          aria-label={"Notifications" + (unread > 0 ? `, ${unread} unread` : "")} />
      )}
    </Bell>
  );
}

/** The bell at the top right of a phone's front page, with a dot while something is unread. */
export function BellButton() {
  return (
    <Bell className="kc-phonehead__bell">
      {({ open, toggle, unread }) => (
        <span className="kc-bell-button">
          <IconButton variant="ghost" size="xl" icon="bell" onClick={toggle} aria-haspopup="dialog" aria-expanded={open}
            label={"Notifications" + (unread > 0 ? `, ${unread} unread` : "")} />
          {unread > 0 && <span className="kc-bell-button__dot" />}
        </span>
      )}
    </Bell>
  );
}
