// Krystal Cinema's service worker.
//
// Its job is small: satisfy the browser's installability criteria with a functional `fetch` handler,
// and give an installed app a shell it can open while the host is unreachable, without ever touching
// the live data path.
//
// It allowlists. Only page loads and the directories the build and the artwork are written to are
// ever intercepted; every other request reaches the network untouched. The origin serves far more
// than the API's /api prefix: a film's segments and subtitles under /media, the room hub under /hub,
// and whatever else the API grows. A cached authenticated 200 hides an ended session from the page
// and can hand one person's history to whoever opens the app next, and a cached segment is a film
// copied into the browser's storage, so a route this file has never heard of is left alone.
//
// Vite's content-hashed assets are cached on demand, cache-first, which is safe because their URL
// changes when their content does. The shell is network-first, so a deploy lands on the next online
// load.

const VERSION = "v1";
const PREFIX = "krystal-cinema-shell-";
const CACHE = PREFIX + VERSION;

// The bare shell, available offline as soon as the worker installs. Hashed build assets are not
// listed, since their names are not known here; they join the cache on first use.
const CORE = [
  "/",
  "/manifest.webmanifest",
  "/icons/icon.svg",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
  "/icons/icon-maskable-512.png",
];

// The only paths served from the cache: files the deploy wrote, none of them anything the API
// computes.
const CACHEABLE = ["/assets/", "/icons/", "/splash/"];
const CACHEABLE_EXACT = ["/manifest.webmanifest"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      // One missing file must not wedge the install.
      .then((cache) => Promise.allSettled(CORE.map((url) => cache.add(url))))
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

  // A page: from the network, and the cached shell when that fails. Only the shell itself becomes
  // the offline copy, since an error page stored under "/" would be what the installed app opens as
  // whenever the host is down. The server answers every page with the same index, so one copy under
  // "/" stands for all of them.
  if (req.mode === "navigate") {
    event.respondWith(
      fetch(req)
        .then((res) => {
          if (isShell(res)) {
            const copy = res.clone();
            caches.open(CACHE).then((c) => c.put("/", copy));
          }
          return res;
        })
        .catch(() => caches.match("/"))
    );
    return;
  }

  const cacheable = CACHEABLE.some((p) => url.pathname.startsWith(p)) || CACHEABLE_EXACT.includes(url.pathname);
  if (!cacheable) return;

  // Static files: from the cache, refreshed behind it.
  event.respondWith(
    caches.match(req).then((cached) => {
      const network = fetch(req)
        .then((res) => {
          if (res && res.status === 200 && res.type === "basic") {
            const copy = res.clone();
            caches.open(CACHE).then((c) => c.put(req, copy));
          }
          return res;
        })
        .catch(() => cached);
      return cached || network;
    })
  );
});

// A successful same-origin HTML document: the site's index, which the server answers for every page.
function isShell(res) {
  return !!res && res.status === 200 && res.type === "basic"
    && (res.headers.get("content-type") || "").startsWith("text/html");
}
