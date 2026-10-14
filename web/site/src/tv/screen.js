/**
 * The television's connection to the hub that belongs to its person and to no room, and what pairing
 * a phone with the television asks of it.
 *
 * A television is a screen to the server while a hub connection signed in through the television
 * client is open, and the room connection the player opens exists only while a film's room is open.
 * This one is held for as long as the television is signed in (`personConnection.js`), so it is a
 * screen on Home, on a film's page and everywhere else too, and the screen's room is the room of the
 * player's own connection. The name the server shows for the screen is the token's, read when a
 * connection opens, so a rename made at the provider reaches it when this connection next reconnects.
 *
 * Pairing goes through it, since only a screen's connection is given a code or told its phones: the
 * code to show (`usePairingCode`, renewed on the server's span so the one on screen always has most of
 * its life left), the phones paired with the television (`usePairedPhones`, told again whenever one
 * pairs or unpairs) and unpairing one (`unpairPhone`).
 */

import React from "react";
import { createStore, useStore } from "@thekrystalship/krystal";
import { holdPersonConnection } from "../personConnection.js";
import { roomPath } from "../paths.js";
import { navigate } from "../router.js";

/** Whether the connection is open now, the phones paired with this television (null until told) and how many remotes drive it. */
const screenStore = createStore({ open: false, phones: null, remotes: 0 });

let held = null;

/** A room's address with the film to put on in it and where to start, as the site's Watch writes it. */
function watchAddress(room, titleId, startSeconds) {
  const query = [];
  if (titleId) query.push(`title=${encodeURIComponent(titleId)}`);
  if (titleId && startSeconds > 0) query.push(`at=${Math.floor(startSeconds)}`);
  return roomPath(room) + (query.length ? "?" + query.join("&") : "");
}

/** Opens the connection and keeps it open until `stopScreenConnection`. Opening it again does nothing. */
export function startScreenConnection() {
  if (held) return;
  held = holdPersonConnection({
    listen: (connection) => {
      connection.on("PhonesChanged", (phones) => screenStore.setState((s) => ({ ...s, phones })));
      connection.on("RemotesChanged", (remotes) => screenStore.setState((s) => ({ ...s, remotes })));
      // A phone put a film on the television while it was in no room: it opens the room, as the site's Watch does.
      connection.on("Watch", ({ room, titleId, startSeconds }) => navigate(watchAddress(room, titleId, startSeconds)));
    },
    opened: (connection) => {
      screenStore.setState((s) => ({ ...s, open: true }));
      connection.invoke("Phones").then((phones) => screenStore.setState((s) => ({ ...s, phones })), () => {});
      connection.invoke("Remotes").then((remotes) => screenStore.setState((s) => ({ ...s, remotes })), () => {});
    },
    closed: () => screenStore.setState((s) => ({ ...s, open: false })),
  });
}

/** Closes the connection, for a television that signed out. */
export function stopScreenConnection() {
  if (!held) return;
  held.stop();
  held = null;
  screenStore.setState({ open: false, phones: null, remotes: 0 });
}

/** The phones paired with this television, oldest first, or null while it has not been told. */
export function usePairedPhones() {
  return useStore(screenStore, (s) => s.phones);
}

/** Whether a phone is driving this television as a remote now. */
export function useRemoteAttached() {
  return useStore(screenStore, (s) => s.remotes > 0);
}

/** Ends a phone's pairing with this television. */
export async function unpairPhone(id) {
  const connection = held && held.connected();
  if (!connection) throw new Error("The television is not connected.");
  await connection.invoke("Unpair", id);
}

/** How long to wait before asking again for a code the server would not give. */
const RetryMs = 5000;

/**
 * The address a phone scans to pair with this television, while `shown`: a code the server signed for
 * this screen, replaced on the span the server gives, and null until the connection has one.
 */
export function usePairingCode(shown) {
  const open = useStore(screenStore, (s) => s.open);
  const [code, setCode] = React.useState(null);
  React.useEffect(() => {
    if (!shown || !open) return undefined;
    let live = true;
    let timer = null;
    const ask = async () => {
      const connection = held && held.connected();
      let wait = RetryMs;
      try {
        if (connection) {
          const issued = await connection.invoke("PairingCode");
          if (!live) return;
          setCode(window.location.origin + issued.path);
          wait = issued.renewSeconds * 1000;
        }
      } catch {
        // Asked again shortly; the code on screen still has most of its life.
      }
      if (live) timer = window.setTimeout(ask, wait);
    };
    void ask();
    return () => {
      live = false;
      window.clearTimeout(timer);
    };
  }, [shown, open]);
  return shown && open ? code : null;
}
