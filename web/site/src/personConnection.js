/**
 * A hub connection that belongs to the signed-in person and to no room, held open until it is let go.
 *
 * A television holds one for as long as it is signed in, which is what makes it a screen
 * (`tv/screen.js`); a phone holds one while it has screens paired with it, which is where it hears them
 * (`remote.js`). It is built as a room's is (`buildHubConnection`): the token comes fresh on every
 * connect and the connection reconnects by itself once it has connected. A first connect that fails,
 * and a connection that gave up reconnecting, are tried again on a timer for as long as it is held. It
 * never joins a room, so it is in no participant list and reports neither buffering nor quality.
 *
 * `listen(connection)` adds the handlers to each connection as it is built; `opened(connection)` runs
 * each time one connects or reconnects, which is when to ask for whatever was missed while it was not;
 * `closed()` runs when one is lost for good.
 */

import { buildHubConnection } from "./player/engine/session/hub.ts";
import { freshToken } from "./session.js";

const RetryMs = 5000;
const RetryCapMs = 60_000;

export function holdPersonConnection({ listen = () => {}, opened = () => {}, closed = () => {} } = {}) {
  let connection = null;
  let retry = null;
  let held = true;

  const connect = async (delay) => {
    retry = null;
    if (!held) return;
    const next = buildHubConnection(`${window.location.origin}/hub/session`, () => freshToken());
    connection = next;
    listen(next);
    const again = (wait) => {
      if (!held || connection !== next || retry !== null) return;
      retry = window.setTimeout(() => void connect(wait), wait);
    };
    next.onreconnected(() => { if (connection === next) opened(next); });
    next.onclose(() => {
      if (connection !== next) return;
      closed();
      again(RetryMs);
    });
    try {
      await next.start();
      if (connection === next) opened(next);
    } catch {
      again(Math.min(delay * 2, RetryCapMs));
    }
  };
  void connect(RetryMs);

  return {
    /** The connection while it is connected, else null. */
    connected: () => (connection && connection.state === "Connected" ? connection : null),
    /** Closes it and stops trying again. */
    stop() {
      held = false;
      if (retry !== null) window.clearTimeout(retry);
      retry = null;
      const closing = connection;
      connection = null;
      if (closing) void closing.stop();
    },
  };
}
