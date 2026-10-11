/**
 * The television's connection to the hub that belongs to its person and to no room.
 *
 * A television is a screen to the server while a hub connection signed in through the television
 * client is open, and the room connection the player opens exists only while a film's room is open.
 * This one is held for as long as the television is signed in, so it is a screen on Home, on a film's
 * page and everywhere else too. It never joins a room, so it is in no participant list and reports
 * neither buffering nor quality; the screen's room is the room of the player's own connection.
 *
 * It is built as a room's is (`buildHubConnection`): the token comes fresh on every connect and the
 * connection reconnects by itself once it has connected. A first connect that fails, and a connection
 * that gave up reconnecting, are tried again on a timer for as long as the television stays signed in.
 * The name the server shows for the screen is the token's, read when a connection opens, so a rename
 * made at the provider reaches it when this connection next reconnects.
 */

import { buildHubConnection } from "../player/engine/session/hub.ts";
import { freshToken } from "../session.js";

const RetryMs = 5000;
const RetryCapMs = 60_000;

let connection = null;
let retry = null;
let held = false;

/** Opens the connection and keeps it open until `stopScreenConnection`. Opening it again does nothing. */
export function startScreenConnection() {
  if (held) return;
  held = true;
  void connect(RetryMs);
}

/** Closes the connection, for a television that signed out. */
export function stopScreenConnection() {
  held = false;
  if (retry !== null) window.clearTimeout(retry);
  retry = null;
  const closing = connection;
  connection = null;
  if (closing) void closing.stop();
}

async function connect(delay) {
  retry = null;
  if (!held) return;
  const next = buildHubConnection(`${window.location.origin}/hub/session`, () => freshToken());
  connection = next;
  const again = (wait) => {
    if (!held || connection !== next || retry !== null) return;
    retry = window.setTimeout(() => void connect(wait), wait);
  };
  next.onclose(() => again(RetryMs));
  try {
    await next.start();
  } catch {
    again(Math.min(delay * 2, RetryCapMs));
  }
}
