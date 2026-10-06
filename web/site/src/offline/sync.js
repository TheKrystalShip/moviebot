// The device's saves set beside the API's records of them, whenever a page loads with a connection
// and whenever the connection comes back: the removals made with no connection are forgotten at the
// API, a save the API no longer lists is removed from the device, an account that no longer holds
// `cinema:watch` keeps nothing here, a save finished while the API could not be told is told now, and
// every save under way that is not paused carries on.

import React from "react";
import { api } from "../api.js";
import { onReconnect, useOnline } from "../connectivity.js";
import { can, useSession } from "../session.js";
import { forgotten, listSaves, offlineSupported, pendingForgets } from "./store.js";
import { removeAllFromDevice, removeFromDevice, resumeUnfinished, stopAccount } from "./saver.js";

let syncing = null;

/** Sets the account's saves on this device beside the API's records, once at a time. */
export function reconcile(session) {
  syncing ??= settle(session).catch(() => {}).finally(() => { syncing = null; });
  return syncing;
}

async function settle(session) {
  const sub = session.user.id;
  if (!can(session, "cinema:watch")) {
    await removeAllFromDevice(sub);
    await forgetPending(sub);
    return;
  }
  await forgetPending(sub);

  const asked = new Date().toISOString();
  const records = new Map((await api.offlineSaves()).map((s) => [s.id, s]));
  for (const save of await listSaves(sub)) {
    const id = save.meta.saveId;
    // A save started after the API was asked is one it could not have listed yet.
    if (!id || String(save.meta.startedAt || "") > asked) continue;
    const record = records.get(id);
    if (!record) await removeFromDevice(sub, save.titleId, { forget: false });
    else if (save.complete && !record.completedAt) await api.completeSave(id).catch(() => {});
  }
  await resumeUnfinished(sub);
}

async function forgetPending(sub) {
  for (const id of pendingForgets(sub)) {
    await api.forgetSave(id).then(() => forgotten(sub, id), () => {});
  }
}

/**
 * Keeps the signed-in account's saves on this device in step with the API, for as long as the page
 * is open: on loading, on the connection coming back, and when somebody else signs in, whose saves
 * are theirs, the last account's transfers stopping.
 */
export function useOfflineSync(enabled) {
  const session = useSession();
  const online = useOnline();
  const usable = enabled && offlineSupported() && session && !session.stale ? session : null;
  const sub = usable ? usable.user.id : null;
  const watching = can(usable, "cinema:watch");
  const held = React.useRef(usable);
  held.current = usable;

  React.useEffect(() => {
    if (!sub) return undefined;
    if (online) void reconcile(held.current);
    const stop = onReconnect(() => { if (held.current) void reconcile(held.current); });
    return () => {
      stop();
      stopAccount(sub);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sub, watching]);
}
