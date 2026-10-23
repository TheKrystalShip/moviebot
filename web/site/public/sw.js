// Cinema's service worker.
//
// It does three things: satisfies the browser's installability criteria with a functional `fetch`
// handler, holds the whole build so the installed app opens and gets around with no connection, and
// shows the API's pushes. A tap on any notification of the site's, a push or one the page drew through
// this worker (Download's progress, `src/offline/notice.js`), opens the page it names.
//
// The build writes `BUILD` below: its version and every file it put in dist/, the index as `/`. A
// worker installing fetches all of them into a cache named by that version, and installs only when
// every one arrived, so the cache it serves is always one whole build: the index and the chunks it
// names, the player's lazy chunk, the fonts and the icons (the launch screens are Safari's, read when
// the app is put on the Home Screen, and pass through untouched). Activating drops every other build's
// cache. A deploy is a new version, which the browser finds on the next online page load.
//
// It allowlists. Only page loads and the files the build wrote are ever served from the cache; every
// other request reaches the network untouched. The origin serves far more than the API's /api prefix:
// a film's segments and subtitles under /media, the room hub under /hub, and whatever else the API
// grows. A cached authenticated 200 hides an ended session from the page and can hand one person's
// history to whoever opens the app next, and a cached segment is a film copied into the browser's
// storage, so no API answer and no media is ever kept here. Films saved for offline are kept in the
// Origin Private File System by the page (`src/offline/`), never in this cache.

// Written by the build (vite.config.js); in development nothing registers this worker.
const BUILD = { version: "dev", files: [] };

importScripts("/krystal-notifications.js");

const PREFIX = "krystal-cinema-build-";
const CACHE = PREFIX + BUILD.version;

// The build's files, by path: the only requests ever answered from the cache.
const BUILT = new Set(BUILD.files);

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      // Fetched past the HTTP cache, so the worker holds what this deploy serves. One file failing
      // fails the install, which leaves the worker before it, and its whole build, in place.
      .then((cache) => cache.addAll(BUILD.files.map((url) => new Request(url, { cache: "reload" }))))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k.startsWith(PREFIX) && k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  const req = event.request;
  const url = new URL(req.url);

  if (req.method !== "GET" || url.origin !== self.location.origin) return;

  // A page: from the network, and this build's index when that fails or is answered with an error
  // (a gateway's for a server that is down). The server answers every page with the same index, so
  // the one copy under "/" stands for all of them. It is never replaced from a page load: it has to be
  // the index whose chunks this build's cache holds. A file opened by a link, the phone app at
  // /phone.apk, is a navigation answered with no page, and goes through as it came.
  if (req.mode === "navigate") {
    event.respondWith(
      fetch(req)
        .then((res) => (isShell(res) || (res.ok && res.type === "basic") ? res : caches.match("/", { cacheName: CACHE }).then((shell) => shell || res)))
        .catch(() => caches.match("/", { cacheName: CACHE }))
    );
    return;
  }

  if (!BUILT.has(url.pathname) || url.pathname === "/") return;

  // A file of the build: from the cache, and from the network for one the cache somehow lacks.
  event.respondWith(caches.match(url.pathname, { cacheName: CACHE }).then((cached) => cached || fetch(req)));
});

// A push is shown as the API worded it, and a tap opens the page it names: krystal's notifications
// worker script does both (`notificationsWorker()` in vite.config.js emits it).
self.addEventListener("push", (event) => self.krystalNotifications.handleNotificationPush(event, {
  title: "Cinema",
  icon: "/icons/icon-192.png",
  badge: "/icons/icon-192.png",
}));
self.addEventListener("notificationclick", (event) => self.krystalNotifications.handleNotificationClick(event));

// A successful same-origin HTML document: the site's index, which the server answers for every page.
function isShell(res) {
  return !!res && res.status === 200 && res.type === "basic"
    && (res.headers.get("content-type") || "").startsWith("text/html");
}
