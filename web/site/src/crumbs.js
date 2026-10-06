// The trail above a page: Home, then each level of the address, every one but the last a link to the
// address prefix it names. A level's name is the section's (`nav.js`) or, for a film, a show or a list,
// the page's own once it has loaded; until then the trail says what the address knows, the id.

import React from "react";
import { createStore, useStore } from "@thekrystalship/krystal-ui";
import { PLACES, PROFILE_TAB_FACES } from "./nav.js";
import { profilePath, showPath } from "./paths.js";

/** The key of the page being drawn, so a page registers its name for itself and no other. */
export const PageKey = React.createContext(null);

const names = createStore({});

/**
 * The name the trail shows for this page's last level, once the page has it. Null says nothing, and
 * the name goes when the page does.
 */
export function useCrumb(label) {
  const key = React.useContext(PageKey);
  React.useEffect(() => {
    if (key == null || !label) return undefined;
    names.setState((all) => ({ ...all, [key]: label }));
    return () => names.setState((all) => {
      const rest = { ...all };
      delete rest[key];
      return rest;
    });
  }, [key, label]);
}

/** The name the page with this key has given, or null. */
export function useCrumbName(key) {
  return useStore(names)[key] || null;
}

const place = (id, last = false) => ({ label: PLACES[id].label, ...(last ? {} : { href: PLACES[id].path }) });

/** The trail for a route; `name` is what its page has called itself. Empty where there is no trail. */
export function trailFor(route, name) {
  const home = place("home");
  const id = name || route.arg;
  switch (route.view) {
    case "movies": return [home, place("movies", true)];
    case "movie": return [home, place("movies"), { label: id }];
    case "tv": return [home, place("tv", true)];
    case "show":
    case "discover-show": {
      const section = place(route.view === "show" ? "tv" : "discover");
      if (route.season == null) return [home, section, { label: id }];
      return [
        home, section,
        { label: id, href: showPath(route.arg, { held: route.view === "show" }) },
        { label: "Season " + route.season },
      ];
    }
    case "discover": return [home, place("discover", true)];
    case "discover-film": return [home, place("discover"), { label: id }];
    case "saved": return [home, place("saved", true)];
    case "list": return [home, place("saved"), { label: id }];
    case "profile": {
      if (route.tab == null) return [home, place("profile", true)];
      const face = PROFILE_TAB_FACES.find((t) => t.id === route.tab);
      return [home, { label: PLACES.profile.label, href: profilePath() }, { label: face.label }];
    }
    case "notify":
    case "about": return [home, place(route.view, true)];
    case "notfound": return [home, { label: "Not found" }];
    default: return [];
  }
}

