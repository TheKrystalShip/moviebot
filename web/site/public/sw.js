// Krystal Cinema's service worker.
//
// It does three small things: satisfies the browser's installability criteria with a functional
// `fetch` handler, gives an installed app a shell it can open while the host is unreachable without
// ever touching the live data path, and shows the API's pushes, opening the page each names.
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

// A push: the API's notification, shown as it was worded, opening the page it names. A browser
// subscribes with `userVisibleOnly`, so every push shows something; one that cannot be read says
// little rather than inventing what it was about.
self.addEventListener("push", (event) => {
  let payload = { title: "Krystal Cinema", body: "There is something new for you.", url: "/", tag: null };
  try {
    if (event.data) payload = { ...payload, ...event.data.json() };
  } catch {
    try { payload.body = event.data.text() || payload.body; } catch { /* the default stands */ }
  }

  event.waitUntil(
    self.registration.showNotification(payload.title, {
      body: payload.body,
      icon: "/icons/icon-192.png",
      badge: "/icons/icon-192.png",
      // The same news twice replaces itself on the lock screen rather than stacking.
      tag: payload.tag ? "kc-" + payload.tag : undefined,
      data: { url: payload.url || "/" },
    })
  );
});

// A tap opens the page the notification names: in a window of this site already open, else a new one.
self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const target = new URL((event.notification.data && event.notification.data.url) || "/", self.location.origin);
  if (target.origin !== self.location.origin) return;

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((windows) => {
      for (const win of windows) {
        if (new URL(win.url).origin !== self.location.origin) continue;
        return win.focus().then((w) => (w && "navigate" in w ? w.navigate(target.href) : w));
      }
      return self.clients.openWindow(target.href);
    })
  );
});

// A successful same-origin HTML document: the site's index, which the server answers for every page.
function isShell(res) {
  return !!res && res.status === 200 && res.type === "basic"
    && (res.headers.get("content-type") || "").startsWith("text/html");
}
