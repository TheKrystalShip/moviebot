// What a visit to a page leaves for Back, Forward and a reload to return to, by history entry (the
// `entry` of a route, `router.js`).

/**
 * A record kept in this tab's session storage under `name`: at most `cap` entries, the one set longest
 * ago forgotten first. Where session storage cannot be read or written (a private window, blocked site
 * data) the record lasts as long as the page.
 */
export function sessionRecord(name, cap) {
  const map = new Map();
  try {
    for (const [key, value] of JSON.parse(sessionStorage.getItem(name) || "[]")) map.set(key, value);
  } catch { /* an empty record */ }
  let timer = 0;
  const write = () => {
    clearTimeout(timer);
    timer = 0;
    try { sessionStorage.setItem(name, JSON.stringify([...map])); } catch { /* kept in memory */ }
  };
  window.addEventListener("pagehide", write);
  return {
    get: (key) => map.get(key),
    set(key, value) {
      map.delete(key);
      map.set(key, value);
      while (map.size > cap) map.delete(map.keys().next().value);
      if (!timer) timer = setTimeout(write, 250);
    },
  };
}
