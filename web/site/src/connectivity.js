// Whether MovieBot can be reached from this page.
//
// `navigator.onLine` says only that there is no network at all: a phone on a captive portal, on a
// network whose uplink is dead, or beside a server that is down reads online. So offline is also
// what a request tells: a call to the API that fails on the network, or a probe of `/api/config`
// that does not answer within `PROBE_TIMEOUT_MS`. A server that answered is reachable whatever it
// answered, a 500 among it; what is not the server answering is a gateway's 502, 503 or 504 in front
// of an API that is down, and an HTML page where the API answers JSON or nothing, which is what a
// captive portal hands back for every address (`answeredByServer`).
//
// While offline the probe repeats on a lengthening wait, from `FIRST_WAIT_MS` doubling up to
// `LONGEST_WAIT_MS`, and at once on the browser's `online` event or the page coming back into view.
// Any request the server answers means online again.

import { createStore, useStore } from "@thekrystalship/krystal-ui";

const PROBE = "/api/config";
const PROBE_TIMEOUT_MS = 5000;
const FIRST_WAIT_MS = 2000;
const LONGEST_WAIT_MS = 60000;

const GATEWAY = new Set([502, 503, 504]);

const store = createStore({ online: typeof navigator === "undefined" || navigator.onLine !== false });

let wait = FIRST_WAIT_MS;
let timer = null;
let probing = null;
let started = false;
const comebacks = new Set();

/** Whether MovieBot can be reached, as last measured. */
export function isOnline() {
  return store.getState().online;
}

/** Whether MovieBot can be reached: what a page draws its offline state by. */
export function useOnline() {
  return useStore(store, (s) => s.online);
}

/** Calls `fn` each time the connection comes back after being lost. Answers the way to stop. */
export function onReconnect(fn) {
  comebacks.add(fn);
  return () => comebacks.delete(fn);
}

/**
 * Whether a response came from MovieBot rather than from something standing in front of it: a
 * gateway answering for an API that is down, or a captive portal answering every address with its
 * own page.
 */
export function answeredByServer(response) {
  const type = (response.headers.get("content-type") || "").toLowerCase();
  if (type.startsWith("text/html")) return false;
  return !(GATEWAY.has(response.status) && !type.includes("json"));
}

/** A request the server answered: online. */
export function reached() {
  set(true);
}

/** A request that did not reach the server: offline, and the probe starts. */
export function unreached() {
  set(false);
}

function set(online) {
  const was = store.getState().online;
  if (was === online) {
    if (!online) schedule();
    return;
  }
  store.setState({ online });
  if (online) {
    window.clearTimeout(timer);
    timer = null;
    wait = FIRST_WAIT_MS;
    for (const fn of comebacks) {
      try { fn(); } catch { /* one listener failing leaves the others to run */ }
    }
  } else {
    schedule();
  }
}

function schedule() {
  if (timer) return;
  timer = window.setTimeout(() => {
    timer = null;
    void probe();
  }, wait);
  wait = Math.min(LONGEST_WAIT_MS, wait * 2);
}

/** Asks the server whether it is there, and answers whether it was. One probe runs at a time. */
export function probe() {
  probing ??= (async () => {
    try {
      const response = await fetch(PROBE, {
        headers: { Accept: "application/json" },
        cache: "no-store",
        signal: AbortSignal.timeout(PROBE_TIMEOUT_MS),
      });
      const ok = answeredByServer(response);
      set(ok);
      return ok;
    } catch {
      set(false);
      return false;
    }
  })().finally(() => { probing = null; });
  return probing;
}

/** Starts listening for the network coming and going, and measures once. */
export function startConnectivity() {
  if (started || typeof window === "undefined") return;
  started = true;
  window.addEventListener("offline", () => set(false));
  window.addEventListener("online", () => {
    window.clearTimeout(timer);
    timer = null;
    wait = FIRST_WAIT_MS;
    void probe();
  });
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible" && !isOnline()) {
      window.clearTimeout(timer);
      timer = null;
      void probe();
    }
  });
  if (!isOnline()) schedule();
  else void probe();
}
