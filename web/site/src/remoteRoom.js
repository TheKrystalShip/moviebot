/**
 * A phone's room connection as a remote of one television: it is in the room that screen is in, follows
 * the screen to the next, and drives the room's shared timeline from there.
 *
 * It is a connection of its own (`personConnection.js`), so the token, the reconnect and the retry are
 * the room player's own, and it asks the server to make it a remote with `JoinAsRemote` each time it
 * connects. The server decides it is one, from the token and the pairing; the page names only the screen
 * it wants to drive. A remote is no participant: it is not in the roster, plays nothing and reports
 * neither buffering nor quality, so none of that is sent from here. The room's state is applied by the
 * rules the player's own are (`RevisionGate`, `ServerClock`, `derivePosition`), and the position shown
 * is derived from it, never read as a ticking number.
 *
 * `remote.js` decides which screen is driven and when; this holds the connection and the commands.
 */

import { createStore, useStore } from "@thekrystalship/krystal-ui";
import { holdPersonConnection } from "./personConnection.js";
import { ServerClock } from "./player/engine/session/clock.ts";
import { derivePosition } from "./player/engine/session/hub.ts";
import { RevisionGate } from "./player/engine/session/revision.ts";

const ProbeMs = 30_000;

const nothing = { screenId: null, live: false, state: null };

/** Which screen is driven, whether the remote can be heard from, and the room's state (null while the screen is in none). */
const roomStore = createStore(nothing);

let held = null;
let gate = new RevisionGate();
let clock = new ServerClock();
let probe = null;

/** Takes the room's state, or drops it for a screen in no room. A move to another room settles the comparison afresh. */
function apply(push, settled = false) {
  if (!push) {
    roomStore.setState({ state: null });
    return;
  }
  const current = roomStore.getState().state;
  // A push of the room the screen has left, still in flight, is not the room it is in.
  if (!settled && current && push.state.sessionId !== current.sessionId) return;
  if (gate.admits(push.state, settled)) roomStore.setState({ state: push.state });
}

async function measureClock(connection) {
  const sentAt = Date.now();
  try {
    clock.observe(sentAt, Date.now(), await connection.invoke("ServerTime"));
  } catch {
    // A missed probe costs accuracy, never correctness: the previous offset stands.
  }
}

/** Makes this phone a remote of the screen, holding the connection until `releaseRemoteRoom`. The same screen again does nothing. */
export function holdRemoteRoom(screenId) {
  if (held && roomStore.getState().screenId === screenId) return;
  releaseRemoteRoom();
  gate = new RevisionGate();
  clock = new ServerClock();
  roomStore.setState({ screenId, live: false, state: null });
  const mine = holdPersonConnection({
    listen: (connection) => {
      connection.on("StateChanged", (push) => apply(push));
      connection.on("RemoteMoved", (reply) => apply(reply.push, true));
    },
    opened: async (connection) => {
      if (held !== mine) return;
      await measureClock(connection);
      try {
        const reply = await connection.invoke("JoinAsRemote", screenId);
        if (held === mine) {
          apply(reply.push, true);
          roomStore.setState({ live: true });
        }
      } catch {
        // Not paired with it any more: the person connection hears the pairing end and lets this go.
      }
    },
    reconnecting: () => roomStore.setState({ live: false }),
    closed: () => roomStore.setState({ live: false }),
  });
  held = mine;
  probe = window.setInterval(() => {
    const connection = mine.connected();
    if (connection) void measureClock(connection);
  }, ProbeMs);
}

/** Lets go of the connection, and of the room it was in. */
export function releaseRemoteRoom() {
  if (!held) return;
  window.clearInterval(probe);
  probe = null;
  held.stop();
  held = null;
  roomStore.setState(nothing);
}

/** The screen driven, whether the room can be heard from, and its state. Nothing is derived forward from a room that cannot. */
export function useRemoteRoom() {
  return useStore(roomStore, (room) => room);
}

/** The same, once, for code that is not a component. */
export function currentRemoteRoom() {
  return roomStore.getState();
}

/** Where the room is now, derived from its anchor and the server's clock, or null while it holds no state. */
export function remotePosition() {
  const { state, live } = roomStore.getState();
  return state && live ? derivePosition(state, clock.now()) : state ? state.positionSeconds : null;
}

function send(method, ...args) {
  const connection = held && held.connected();
  return connection ? connection.invoke(method, ...args) : Promise.reject(new Error("The remote is not connected."));
}

function here() {
  const at = remotePosition();
  if (at === null) throw new Error("The screen is in no room.");
  return at;
}

/**
 * What a remote asks of the room. Each is intent, answered by the state the server decided, which the
 * room's push brings back like everybody's. A position the room is at is the derived one, so a pause
 * lands where the film is.
 */
export const roomCommands = {
  play: async () => send("Play", here()),
  pause: async () => send("Pause", here()),
  seek: (toSeconds) => send("Seek", Math.max(0, toSeconds)),
  /** Moves the room by `bySeconds` from where it is now, forward or back. */
  skip: async (bySeconds) => send("Seek", Math.max(0, here() + bySeconds)),
  loadTitle: (titleId, startSeconds = null) => send("LoadTitle", titleId, startSeconds),
  next: () => send("Next"),
  previous: () => send("Previous"),
  playEpisode: (season, number) => send("PlayEpisode", season, number),
};
