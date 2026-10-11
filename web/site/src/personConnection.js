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
 * A page that comes back (it becomes visible again, is restored from the browser's cache, or the network
 * returns) does not wait for the schedule: a connection that is not connected is replaced at once, and
 * one that says it is connected is asked for the server's clock and replaced if that is not answered in
 * `ProbeMs`, since a phone out of a pocket holds sockets that died while it slept and does not know it
 * until a timer says so.
 *
 * `listen(connection)` adds the handlers to each connection as it is built; `opened(connection)` runs
 * each time one connects or reconnects, which is when to ask for whatever was missed while it was not;
 * `reconnecting()` runs when one is lost and being tried again; `closed()` runs when one is lost for good.
 */

import { buildHubConnection } from "./player/engine/session/hub.ts";
import { freshToken } from "./session.js";

const RetryMs = 5000;
const RetryCapMs = 60_000;
const ProbeMs = 2000;

export function holdPersonConnection({ listen = () => {}, opened = () => {}, reconnecting = () => {}, closed = () => {} } = {}) {
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
    next.onreconnecting(() => { if (connection === next) reconnecting(); });
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

  /** Replaces a connection that cannot be relied on with a new one, now. */
  const replace = (stale) => {
    if (!held || connection !== stale) return;
    connection = null;
    if (retry !== null) window.clearTimeout(retry);
    retry = null;
    if (stale) {
      reconnecting();
      void stale.stop();
    }
    void connect(RetryMs);
  };

  const wake = () => {
    if (!held || document.visibilityState === "hidden") return;
    const current = connection;
    if (current && current.state === "Connected") {
      const answered = current.invoke("ServerTime");
      const silent = new Promise((_, reject) => window.setTimeout(() => reject(new Error("silent")), ProbeMs));
      Promise.race([answered, silent]).catch(() => replace(current));
    } else if (!current || current.state !== "Connecting") {
      replace(current);
    }
  };
  document.addEventListener("visibilitychange", wake);
  window.addEventListener("pageshow", wake);
  window.addEventListener("online", wake);

  return {
    /** The connection while it is connected, else null. */
    connected: () => (connection && connection.state === "Connected" ? connection : null),
    /** Closes it and stops trying again. */
    stop() {
      held = false;
      document.removeEventListener("visibilitychange", wake);
      window.removeEventListener("pageshow", wake);
      window.removeEventListener("online", wake);
      if (retry !== null) window.clearTimeout(retry);
      retry = null;
      const closing = connection;
      connection = null;
      if (closing) void closing.stop();
    },
  };
}
