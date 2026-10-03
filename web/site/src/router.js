// The site's addresses. A page is a path, so a film's page can be linked to and reloaded; the API
// answers any path that is not its own with the site's index.

import { createStore, useStore } from "@thekrystalship/krystal-ui";

const routeStore = createStore(window.location.pathname + window.location.search);

window.addEventListener("popstate", () => routeStore.setState(() => window.location.pathname + window.location.search));

export function navigate(to, { replace = false } = {}) {
  if (to === window.location.pathname + window.location.search) return;
  if (replace) window.history.replaceState(null, "", to);
  else window.history.pushState(null, "", to);
  routeStore.setState(() => to);
}

/** The page the address names: its name, its argument, and the query. */
export function useRoute() {
  const href = useStore(routeStore);
  const url = new URL(href, window.location.origin);
  const [, page = "", arg] = url.pathname.split("/");
  return { page: page || "home", arg: arg ? decodeURIComponent(arg) : null, query: url.searchParams, path: url.pathname };
}

/** A link that navigates in place. */
export function linkProps(to) {
  return {
    href: to,
    onClick: (e) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      e.preventDefault();
      navigate(to);
    },
  };
}
