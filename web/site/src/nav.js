// The names the site gives its places, once: the sidebar, the footer, the phone's tab bar and the
// breadcrumb trail all read them here, so they cannot disagree.

import {
  ANIME, PROFILE_TABS, aboutPath, discoverPath, homePath, libraryPath, notifyPath, profilePath, savedPath,
} from "./paths.js";

/** A place by id: its address, its icon and its name. */
export const PLACES = {
  home: { path: homePath, icon: "house", label: "Home" },
  discover: { path: discoverPath(), icon: "compass", label: "Discover" },
  saved: { path: savedPath, icon: "bookmark", label: "Saved" },
  movies: { path: libraryPath("film"), icon: "clapperboard", label: "Movies" },
  tv: { path: libraryPath("show"), icon: "tv", label: "TV Shows" },
  anime: { path: libraryPath("show", ANIME), icon: "swords", label: "Anime" },
  notify: { path: notifyPath, icon: "bell-plus", label: "Notify me" },
  about: { path: aboutPath, icon: "info", label: "About" },
  profile: { path: profilePath(), icon: "user", label: "Profile" },
};

const FACES = {
  history: { icon: "history", label: "History" },
  requests: { icon: "inbox", label: "Requests" },
  following: { icon: "tv", label: "Following" },
  notifications: { icon: "bell-ring", label: "Notifications" },
  playback: { icon: "play", label: "Playback" },
  remote: { icon: "smartphone", label: "Remote" },
  subtitles: { icon: "captions", label: "Subtitles" },
  appearance: { icon: "palette", label: "Appearance" },
  account: { icon: "user-cog", label: "Account" },
};

/** The profile's tabs in the order they are shown, each with its icon and name. */
export const PROFILE_TAB_FACES = PROFILE_TABS.map((id) => ({ id, ...FACES[id] }));
