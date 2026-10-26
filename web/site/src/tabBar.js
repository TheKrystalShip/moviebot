// A person's phone tab bar: the four pages they keep in its free slots, held per account by the API
// (`tabBar` on their preferences) and copied on the device so the bar draws before the API answers.
// This file holds the rules and none of the browser: `tabDock.js` binds them to storage, the API and
// the hub.

import { Ordered } from "./player/engine/session/ordered.ts";

export const STORAGE_KEY = "cinema:tab-dock";

const FREE = 4;

/** Four non-empty ids, or null. */
function four(value) {
  return Array.isArray(value) && value.length === FREE && value.every((id) => typeof id === "string" && id)
    && new Set(value).size === FREE ? value : null;
}

/** The slots the device holds, or null for the defaults. */
export function readSlots(storage) {
  try {
    return four(JSON.parse(storage.getItem(STORAGE_KEY) || "null"));
  } catch {
    return null;
  }
}

/** Keeps the slots on the device; null forgets them. */
export function writeSlots(storage, slots) {
  try {
    if (slots) storage.setItem(STORAGE_KEY, JSON.stringify(slots));
    else storage.removeItem(STORAGE_KEY);
  } catch { /* the API keeps them */ }
}

/**
 * What the dock reports as a choice, made into what is kept: an empty slot takes its position's
 * default (the next default not already chosen when that one is), and a choice equal to the defaults is
 * null. A choice that cannot be four distinct ids is null too.
 */
export function completed(ids, defaults) {
  const taken = new Set(ids.filter(Boolean));
  const spare = defaults.filter((id) => !taken.has(id));
  const filled = ids.map((id, at) => {
    if (id) return id;
    const own = defaults[at];
    const pick = own && !taken.has(own) ? own : spare.shift();
    if (pick) taken.add(pick);
    return pick || "";
  });
  const kept = four(filled);
  return kept && kept.some((id, at) => id !== defaults[at]) ? kept : null;
}

const revisionOf = (record) => (record && record.revision) || 0;

/**
 * The bar of the signed-in person. `storage` is the device's, `defaults` the free slots when nothing is
 * chosen, `load` reads the preferences from the API and `save` writes them (each answering the record),
 * and `failed` hears a write that did not land.
 *
 * `slots()` is what the bar holds, null for the defaults. The API's record replaces it whenever one
 * arrives in the order the API kept its writes (`Ordered`): a push older than what is held is dropped,
 * and a push that lands while a write of this device is in flight waits for the write's answer. A read
 * carries no revision, so it replaces the bar when it is answered unless a push or a change of this
 * device came in while it was out, which are newer than it. A change
 * is shown and kept on the device at once and written to the API as the freshest record read this
 * session with `tabBar` replaced (the API stores the whole record a write carries), after a record is
 * read when none has been; writes go one at a time, each carrying the bar as it stands then.
 */
export function createTabBar({ storage, defaults, load, save, failed = () => {} }) {
  let slots = readSlots(storage);
  let signedIn = false;
  let record = null;
  let order = new Ordered(adopt);
  let chain = Promise.resolve();
  let newer = 0;
  const listeners = new Set();

  function put(next) {
    slots = next;
    writeSlots(storage, next);
    for (const listener of listeners) listener();
  }

  function adopt(pushed) {
    put(four(pushed.tabBar));
  }

  /** The newest record seen this session, which a write is made from. */
  function see(seen) {
    if (!record || revisionOf(seen) >= revisionOf(record)) record = seen;
  }

  const read = async () => {
    const answer = await load();
    see(answer);
    return answer;
  };

  async function write() {
    if (!signedIn) return;
    const mine = order;
    try {
      if (!record) await read();
      if (mine !== order) return;
      const done = order.writing();
      try {
        const answer = await save({ ...record, tabBar: slots });
        see(answer);
        done(answer);
      } catch (error) {
        done(null);
        throw error;
      }
    } catch (error) {
      failed(error);
    }
  }

  return {
    slots: () => slots,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },

    /** A person is signed in: their bar is read, and what they choose from here on is written. */
    attach() {
      signedIn = true;
      order = new Ordered(adopt);
      record = null;
      return this.refresh();
    },

    /** Nobody is signed in: the bar stays as the device holds it and nothing is written. */
    detach() {
      signedIn = false;
      order = new Ordered(adopt);
      record = null;
    },

    /** Reads the record again, which is how the bar catches up after the connection was away. */
    async refresh() {
      const mine = order;
      const seen = newer;
      try {
        const answer = await read();
        if (mine === order && seen === newer) adopt(answer);
      } catch { /* the bar stays as it is held */ }
    },

    /** The API pushed the person's preferences. */
    heard(pushed) {
      newer += 1;
      see(pushed);
      order.heard(pushed);
    },

    /** The dock reported a choice: four ids, an empty slot ''. */
    change(ids) {
      newer += 1;
      put(completed(ids, defaults));
      chain = chain.then(write);
      return chain;
    },
  };
}
