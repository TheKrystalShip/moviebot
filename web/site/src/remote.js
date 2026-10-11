/**
 * A phone's side of driving a television: the screens this browser's session is paired with, the
 * connection it hears them on, and whether it is driving one now.
 *
 * A pairing belongs to the session that scanned the TV's code (`docs/api-contract.md`, "Screens"), so it
 * is this browser's, or this installed app's, and no other's of the same person. While the session has
 * a screen paired it holds a hub connection of the person's (`personConnection.js`), on which the server
 * tells it each paired screen arriving, changing room and leaving; with none paired it holds nothing.
 *
 * Remote mode is the device's own: on says this phone is driving its screen now, and it is kept across
 * the app closing under `cinema:remote` ("1" or "0", as krystal-ui's `useStoredFlag` reads it). Turning
 * it off unpairs nothing, and a pairing turns it on.
 *
 * While remote mode is on and a paired screen is connected, the phone is a remote of that screen
 * (`remoteRoom.js`): in the room the screen is in, wherever it goes. With remote mode off, or no screen
 * connected, it holds no room connection.
 */

import { createStore, useStore } from "@thekrystalship/krystal-ui";
import { api } from "./api.js";
import { holdPersonConnection } from "./personConnection.js";
import { holdRemoteRoom, releaseRemoteRoom } from "./remoteRoom.js";

/** Where remote mode is kept on the device. */
export const REMOTE_MODE_KEY = "cinema:remote";

function storedMode() {
  try {
    return localStorage.getItem(REMOTE_MODE_KEY) === "1";
  } catch {
    return false;
  }
}

const modeStore = createStore(storedMode());

/** Turns remote mode on or off on this device. */
export function setRemoteMode(on) {
  try {
    localStorage.setItem(REMOTE_MODE_KEY, on ? "1" : "0");
  } catch {
    // Storage refused: remote mode lasts as long as the page does.
  }
  modeStore.setState(on);
}

/** Whether this device is driving its screen, and a way to change it. */
export function useRemoteMode() {
  return [useStore(modeStore), setRemoteMode];
}

/** The screens this session is paired with, as the server last told them, or null before it has. */
const pairedStore = createStore(null);

let held = null;

/** The screen chosen to be driven, when the person chose among several. */
let chosen = null;

/** The screen this phone is a remote of: the chosen one while it is connected, else the first that is. */
function drivenScreen() {
  const connected = (pairedStore.getState() || []).filter((screen) => screen.connected);
  return connected.find((screen) => screen.id === chosen) || connected[0] || null;
}

function drive() {
  const screen = modeStore.getState() ? drivenScreen() : null;
  if (screen) holdRemoteRoom(screen.id);
  else releaseRemoteRoom();
}

pairedStore.subscribe(drive);
modeStore.subscribe(drive);

/** Drives this screen rather than the first that is connected, for as long as it is paired and connected. */
export function chooseScreen(id) {
  chosen = id;
  drive();
}

const put = (screen) => pairedStore.setState((screens) => [...(screens || []).filter((s) => s.id !== screen.id), screen]);

function hold() {
  if (held) return;
  held = holdPersonConnection({
    listen: (connection) => {
      connection.on("ScreenArrived", put);
      connection.on("ScreenChanged", put);
      connection.on("ScreenLeft", (id) => {
        pairedStore.setState((screens) => (screens || []).filter((s) => s.id !== id));
        if ((pairedStore.getState() || []).length === 0) release();
      });
    },
    // What changed while it was not connected is asked for again.
    opened: (connection) => connection.invoke("Screens").then((screens) => pairedStore.setState(screens), () => {}),
  });
}

function release() {
  if (!held) return;
  held.stop();
  held = null;
}

/**
 * Asks which screens this session is paired with, and holds the connection while there are any. Called
 * when somebody is signed in and the server can be reached; `forgetPairings` lets go on signing out.
 */
export async function followPairings() {
  const screens = await api.screens();
  pairedStore.setState(screens);
  if (screens.length > 0) hold();
  else release();
}

/** Lets go of the connection and what it heard, for a session that ended. */
export function forgetPairings() {
  release();
  pairedStore.setState(null);
}

/** Pairs this session with the screen whose code it scanned, and answers that screen. Remote mode turns on. */
export async function pair(code) {
  const screen = await api.pairScreen(code);
  put(screen);
  hold();
  setRemoteMode(true);
  return screen;
}

/** Ends this session's pairing with a screen. Remote mode turns off once nothing is paired. */
export async function unpair(id) {
  await api.unpairScreen(id);
  pairedStore.setState((screens) => (screens || []).filter((s) => s.id !== id));
  if ((pairedStore.getState() || []).length === 0) {
    release();
    setRemoteMode(false);
  }
}

/** The screens this session is paired with, or null before the server has said. */
export function usePairedScreens() {
  return useStore(pairedStore, (screens) => screens);
}
