// The names the site gives its places, once: the sidebar, the footer, the phone's tab bar and the
// breadcrumb trail all read them here, so they cannot disagree.

import {
  ANIME, PROFILE_TABS, aboutPath, discoverPath, homePath, libraryPath, plansPath, profilePath, wantsPath,
} from "./paths.js";

const FACES = {
  history: { icon: "history", label: "History" },
  following: { icon: "tv", label: "Following" },
  notifications: { icon: "bell-ring", label: "Notifications" },
  playback: { icon: "play", label: "Playback" },
  remote: { icon: "smartphone", label: "Remote" },
  subtitles: { icon: "captions", label: "Subtitles" },
  appearance: { icon: "palette", label: "Appearance" },
  account: { icon: "user-cog", label: "Account" },
};

/** A profile tab as a place of its own: the tab's address, icon and name. */
const tabPlace = (id, icon = FACES[id].icon) => ({ path: profilePath(id), icon, label: FACES[id].label });

/** A place by id: its address, its icon and its name. */
export const PLACES = {
  home: { path: homePath, icon: "house", label: "Home" },
  discover: { path: discoverPath(), icon: "compass", label: "Discover" },
  wants: { path: wantsPath, icon: "bookmark", label: "Want to watch" },
  plans: { path: plansPath, icon: "calendar-clock", label: "Plans" },
  movies: { path: libraryPath("film"), icon: "clapperboard", label: "Movies" },
  tv: { path: libraryPath("show"), icon: "tv", label: "TV Shows" },
  anime: { path: libraryPath("show", ANIME), icon: "swords", label: "Anime" },
  about: { path: aboutPath, icon: "info", label: "About" },
  profile: { path: profilePath(), icon: "user", label: "Profile" },
  history: tabPlace("history"),
  // Its tab's icon is TV Shows' own, and the two sit in the same sidebar.
  following: tabPlace("following", "list-video"),
};

/** The profile's tabs in the order they are shown, each with its icon and name. */
export const PROFILE_TAB_FACES = PROFILE_TABS.map((id) => ({ id, ...FACES[id] }));
