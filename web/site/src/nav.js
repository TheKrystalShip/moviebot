// The names the site gives the tabs of the profile: the profile page, the television's profile and the
// router's table of places (`router.js`) read them here, so they cannot disagree.

import { PROFILE_TABS } from "./paths.js";

const FACES = {
  history: { icon: "history", label: "History" },
  following: { icon: "tv", label: "Following" },
  taste: { icon: "thumbs-up", label: "Taste" },
  notifications: { icon: "bell-ring", label: "Notifications" },
  playback: { icon: "play", label: "Playback" },
  remote: { icon: "smartphone", label: "Remote" },
  subtitles: { icon: "captions", label: "Subtitles" },
  appearance: { icon: "palette", label: "Appearance" },
  account: { icon: "user-cog", label: "Account" },
};

/** The profile's tabs in the order they are shown, each with its icon and name. */
export const PROFILE_TAB_FACES = PROFILE_TABS.map((id) => ({ id, ...FACES[id] }));
