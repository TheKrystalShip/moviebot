/**
 * Watch on TV: putting a title on the television a phone drives, from the phone's own pages.
 *
 * What the television is doing decides how the title gets there:
 *
 * - in no room (Home, a page of the site): the phone picks the room, as the site's Watch does, and tells
 *   the television to open it (`WatchOnScreen`), which navigates to the room's address with the title and
 *   where to start, so its Back returns to where it was. The phone takes the screen's next announcement
 *   showing that room and that title as the answer, and says so when none comes;
 * - in a room that holds nothing: the title is loaded into it;
 * - already on that title: left alone;
 * - in a room mid-film: the phone asks first (`replaceStore`, drawn by `components/ReplaceSheet.jsx`),
 *   and puts the title on only when asked to; in the closing stretch of what is playing it replaces at
 *   once, since what is left is the credits.
 *
 * A film the library does not hold is asked for through Watch now with the room the television is in, or
 * a room of the phone's choosing for a television in none.
 */

import { toast, createStore } from "@thekrystalship/krystal";
import { startWatching } from "./watchNow.js";
import { currentDrivenScreen, setRemoteOpen, whenScreen } from "./remote.js";
import { currentFilm } from "./remoteFilm.js";
import { currentRemoteRoom, remotePosition, roomCommands, subscribeRemoteRoom } from "./remoteRoom.js";
import { inClosingStretch } from "./player/stretches.js";

/** How long the phone waits for a television to show it has done what it was told. */
export const AnswerMs = 8000;
/** How long the phone waits to hear the room hold the title it put on before it stops expecting to play it. */
const StartMs = 5000;

/** The question the phone is asking before it replaces what is playing, or null. */
export const replaceStore = createStore(null);

const roomId = () => crypto.randomUUID().replaceAll("-", "").slice(0, 8);

function ask(screen, target) {
  if (replaceStore.getState()) return Promise.resolve(false);
  const { state } = currentRemoteRoom();
  return new Promise((resolve) => {
    replaceStore.setState({
      screen, target, position: remotePosition(), film: currentFilm(), playing: state && state.preparing ? state.preparing.name : null,
      answer: (replace) => {
        replaceStore.setState(null);
        resolve(replace);
      },
    });
  });
}

/**
 * Puts a title on the television this phone drives. `target` is `{titleId, startSeconds}` for a title in
 * the library, or `{request, limit}` (Watch now's request) for a film that has to be fetched, with
 * `name`, `meta` and `art` for the question when something is playing. True when the television is
 * showing it, false when it was not asked or did not answer, which is said.
 */
export async function watchOnTv(target) {
  const screen = currentDrivenScreen();
  if (!screen) {
    toast.error("No TV is connected.");
    return false;
  }
  const held = currentRemoteRoom().state;
  if (held && target.titleId && held.titleId === target.titleId) {
    setRemoteOpen(true);
    return true;
  }
  if (held && (held.titleId || held.preparing)) {
    const film = currentFilm();
    const ending = film && film.manifest && inClosingStretch(film.manifest, remotePosition() ?? 0);
    if (!ending && !(await ask(screen, target))) return false;
  }

  try {
    // Read again: the television may have moved on while the question was open.
    const room = currentRemoteRoom().state;
    if (target.request) return await fetchOnto(screen, room, target);
    if (room) {
      await roomCommands.loadTitle(target.titleId, target.startSeconds ?? null);
    } else {
      const opened = roomId();
      await roomCommands.watchOnScreen(opened, target.titleId, target.startSeconds ?? null);
      if (!(await answered(screen, opened, target.titleId))) return false;
    }
    return await startPlaying(target.titleId);
  } catch (error) {
    toast.fromError(error, "The TV did not take that");
    return false;
  }
}

/**
 * A title put on the television plays: a room is made paused, and one loaded while paused stays so. Once the
 * remote hears the room holding the title, a paused room is played from where it is.
 */
async function startPlaying(titleId) {
  const held = await untilRoom((state) => state.titleId === titleId, StartMs);
  if (!held) return true;
  if (currentRemoteRoom().state.paused) await roomCommands.play();
  return true;
}

/** Resolves true when the room the remote hears matches, now or within `ms`. */
function untilRoom(matches, ms) {
  const shown = () => {
    const { state } = currentRemoteRoom();
    return Boolean(state && matches(state));
  };
  if (shown()) return Promise.resolve(true);
  return new Promise((resolve) => {
    const done = (answer) => {
      window.clearTimeout(timer);
      stop();
      resolve(answer);
    };
    const stop = subscribeRemoteRoom(() => { if (shown()) done(true); });
    const timer = window.setTimeout(() => done(false), ms);
  });
}

async function fetchOnto(screen, room, target) {
  const opened = room ? room.sessionId : roomId();
  const reply = await startWatching({ ...target.request, room: opened }, { limit: target.limit });
  if (!reply) return false;
  if (room) return true;
  await roomCommands.watchOnScreen(reply.room, reply.libraryId || null, null);
  return answered(screen, reply.room, reply.libraryId || null);
}

async function answered(screen, room, titleId) {
  const shown = await whenScreen(screen.id, (s) => s.room === room && (titleId ? s.titleId === titleId : true), AnswerMs);
  if (!shown) toast.error(`${screen.name || "The TV"} did not answer.`);
  return shown;
}
