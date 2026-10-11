// A phone with no page around it, for verify/tv.mjs, verify/resume.mjs and verify/resumePhone.mjs: the
// site's own remote modules on a blank document of the site's origin, signed in with the session that
// origin's storage holds. The checks bundle this (verify/probeBundle.mjs) and inject it.
import { restore } from "../src/session.js";
import { followPairings, setRemoteMode } from "../src/remote.js";
import { currentRemoteRoom, remotePosition, roomCommands } from "../src/remoteRoom.js";

const PressMs = 100;
const GiveUpMs = 120_000;

/**
 * Waits for this page to be hidden and shown again; from the moment it is shown, presses play or pause
 * every `PressMs` until the room accepts one, and keeps what happened as `window.__phone.result`:
 * how long the page was away, and from its return the changes of whether the remote reports itself
 * connected, the presses that were refused with why, and when the first was accepted.
 */
function watchReturn() {
  const result = { hiddenAt: null, shownAt: null, awayMs: null, timeline: [], refused: [], landedMs: null, presses: 0 };
  window.__phone.result = result;
  document.addEventListener("visibilitychange", async () => {
    if (document.visibilityState === "hidden") {
      if (result.shownAt === null) result.hiddenAt = Date.now();
      return;
    }
    if (result.hiddenAt === null || result.shownAt !== null) return;
    result.shownAt = Date.now();
    result.awayMs = result.shownAt - result.hiddenAt;
    const t0 = performance.now();
    let seen = null;
    while (performance.now() - t0 < GiveUpMs && result.landedMs === null) {
      const room = currentRemoteRoom();
      if (room.live !== seen) result.timeline.push(`${room.live ? "live" : "not live"}@${Math.round(performance.now() - t0)}ms`);
      seen = room.live;
      result.presses++;
      try {
        await (room.state && room.state.paused === false ? roomCommands.pause() : roomCommands.play());
        result.landedMs = Math.round(performance.now() - t0);
      } catch (error) {
        const why = error.message;
        if (!result.refused.includes(why)) result.refused.push(why);
        await new Promise((resolve) => setTimeout(resolve, PressMs));
      }
    }
  });
}

window.__phone = {
  /** Signs in from what is stored, turns remote mode on and asks which screens are paired. */
  async start() {
    await restore();
    setRemoteMode(true);
    await followPairings();
  },
  room: currentRemoteRoom,
  position: remotePosition,
  commands: roomCommands,
  watchReturn,
};
