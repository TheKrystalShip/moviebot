// Whether this page is the Discord Activity, and which room it is.
//
// Discord opens the Activity at `/embed/` with its launch on the query string: the frame, the
// instance and the channel it runs in. The site navigates away from that address as soon as it is
// signed in, so the launch is kept in this frame's sessionStorage, and a frame reloaded at another
// address of the site is still the Activity. A top-level tab is never the Activity, whatever this
// origin keeps.
//
// The Embedded App SDK is not imported here: the site's own pages ask this module, and only the
// Activity pays for the SDK (`activity.js`).

const KEY = "cinema:activity-launch";

function launchSearch() {
  // A worker has no window, and is never the Activity's page.
  if (typeof window === "undefined") return null;
  const search = window.location.search;
  if (new URLSearchParams(search).has("frame_id")) {
    try { sessionStorage.setItem(KEY, search); } catch { /* a reload of the frame cannot find its launch */ }
    return search;
  }
  if (window.parent === window) return null;
  try { return sessionStorage.getItem(KEY); } catch { return null; }
}

/** The query string Discord launched the Activity with, or null outside the Activity. */
export const activityLaunch = launchSearch();

export const inActivity = activityLaunch !== null;

/**
 * The room the Activity watches in: the channel it runs in, a voice channel, a DM or a group DM, so
 * everybody in the call is in the same room, and the room the bot loads a film into for that
 * channel. An Activity launched with no channel watches in its own instance. Null outside it.
 */
export const activityRoom = (() => {
  if (!inActivity) return null;
  const query = new URLSearchParams(activityLaunch);
  return query.get("channel_id") || query.get("instance_id");
})();
