// The installed app's service worker (public/sw.js), registered in a production build only: under
// `npm run dev` a worker would sit in front of Vite's module server and hand back stale modules.
//
// The Discord Activity never registers it. It runs inside Discord, at Discord's proxy origin, where
// there is nothing to install and the worker would only stand between the player and its media.
//
// It registers once `load` has fired, so the install never competes with the first paint, and a
// failed registration is swallowed: the site works the same uninstalled.

import { inActivity } from "./discord/launch.js";

export function registerServiceWorker() {
  if (!import.meta.env.PROD) return;
  if (typeof navigator === "undefined" || !("serviceWorker" in navigator)) return;
  if (inActivity) return;

  const register = () => { navigator.serviceWorker.register("/sw.js").catch(() => {}); };
  if (document.readyState === "complete") register();
  else window.addEventListener("load", register, { once: true });
}
