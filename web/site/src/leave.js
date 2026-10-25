// Leaving Cinema in this browser.
//
// Somebody with films saved on this device is asked first whether to remove them: they are the
// account's, kept under its `sub`, and signing out alone leaves them for the next sign-in of the same
// account (`components/SignOutDialog.jsx`).

import { createStore } from "@thekrystalship/krystal";
import { meApi } from "./api/index.js";
import { forgotten, listSaves, pendingForgets } from "./offline/store.js";
import { removeAllFromDevice, stopAccount } from "./offline/saver.js";
import { forgetPreferences } from "./offline/preferences.js";
import { forgetKeptProgress } from "./offline/progress.js";
import { forgetSorts } from "./sorts.js";
import { sessionStore, signOut } from "./session.js";

/** The question being asked before signing out: `{ sub, count, bytes }`, or null. */
export const leaving = createStore(null);

/** Signs out at the provider, which ends the person's sign-in there and comes back to the site. */
export function leave() {
  void (async () => {
    const session = sessionStore.getState();
    const sub = session && session.user.id;
    const saves = sub ? await listSaves(sub).catch(() => []) : [];
    if (saves.length === 0) {
      await signOut();
      return;
    }
    leaving.setState(() => ({ sub, count: saves.length, bytes: saves.reduce((sum, s) => sum + s.receivedBytes, 0) }));
  })();
}

/** Answers the question: signs out, removing the account's saved films first when `remove`. */
export async function finishLeaving(remove) {
  const asked = leaving.getState();
  // A save under way stops either way: its transfer needs the session that is ending.
  if (asked) stopAccount(asked.sub);
  if (asked && remove) {
    await removeAllFromDevice(asked.sub).catch(() => {});
    forgetPreferences(asked.sub);
    forgetKeptProgress(asked.sub);
    forgetSorts(asked.sub);
    // The API's records of them are forgotten now, while the token still works; with no connection
    // they stay queued for this account's next sign-in on the device.
    for (const id of pendingForgets(asked.sub)) {
      await meApi.forgetSave(id).then(() => forgotten(asked.sub, id), () => {});
    }
  }
  leaving.setState(() => null);
  await signOut();
}

/** Stays signed in. */
export function stay() {
  leaving.setState(() => null);
}
