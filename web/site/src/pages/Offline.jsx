import React from "react";
import { Button, CardScreen, Icon, Notice } from "@thekrystalship/krystal";
import markUrl from "@thekrystalship/krystal/assets/tks-mark.png";
import { probe } from "../connectivity.js";
import { libraryPath } from "../paths.js";
import { linkProps } from "../router.js";

// What the site draws in place of what only the server can give: a place that needs it, reached with
// no connection, and a device with no session to sign in over. Every other page draws what the device
// holds (`offline/`).

const UNITS = ["B", "KB", "MB", "GB", "TB"];

/** A size in bytes the way a person reads it: 1.4 GB. */
export function size(bytes) {
  let n = Math.max(0, bytes || 0);
  let unit = 0;
  while (n >= 1000 && unit < UNITS.length - 1) {
    n /= 1000;
    unit += 1;
  }
  return `${n >= 10 || unit === 0 ? Math.round(n) : n.toFixed(1)} ${UNITS[unit]}`;
}

/** A control that needs the server, drawn dimmed and out of reach while there is no connection. */
export function Off({ children }) {
  return <div className="kc-nav-off" inert="" aria-disabled="true">{children}</div>;
}

/** What each place that needs the server is called where it says it is out of reach. */
const PLACES = {
  discover: "Discover isn't available offline.",
  assistant: "The assistant isn't available offline.",
};

/**
 * A place that needs the server, reached with no connection: Discover, the assistant, or any page the
 * device cannot draw. It leads to the library, which the device can. In the dock (`onClose`) it can be
 * closed, and leading to the library closes it.
 */
export function Unreachable({ place, onClose }) {
  const library = linkProps(libraryPath("film"));
  return (
    <div className="kc-page kc-unreachable">
      {onClose && <Button variant="ghost" size="sm" icon="x" className="kc-unreachable__close" aria-label="Close" onClick={onClose} />}
      <Icon name="wifi-off" size={32} />
      <span className="kc-unreachable__title">You're offline</span>
      <span className="kc-unreachable__note">{PLACES[place] || "This page isn't available offline."}</span>
      <Button size="lg" icon="clapperboard" className="kc-unreachable__library" href={library.href} onClick={(e) => { library.onClick(e); if (onClose) onClose(); }}>Library</Button>
    </div>
  );
}

/** A device that holds no session, with no connection to sign in over. */
export function FirstSignIn() {
  const [busy, setBusy] = React.useState(false);
  const retry = () => {
    setBusy(true);
    probe().finally(() => setBusy(false));
  };
  return (
    <CardScreen mark={markUrl} wordmark="Cinema">
      <Notice>Signing in needs a connection.</Notice>
      <Button variant="primary" size="xl" wide icon="refresh-cw" busy={busy} onClick={retry}>Try again</Button>
    </CardScreen>
  );
}
