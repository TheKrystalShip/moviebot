// The phone's tab dock as this site draws it: the person's bar (`tabBar.js`) bound to the device's
// storage, the API's preferences and the hub, and the dock's sections built from the router's table of
// places.

import React from "react";
import { toast } from "@thekrystalship/krystal";
import { meApi } from "./api/index.js";
import { holdPersonConnection } from "./personConnection.js";
import { DEFAULT_SLOTS, PLACES, SECTIONS, placesIn } from "./router.js";
import { createTabBar } from "./tabBar.js";

const bar = createTabBar({
  storage: window.localStorage,
  defaults: DEFAULT_SLOTS,
  load: () => meApi.preferences(),
  save: (preferences) => meApi.setPreferences(preferences),
  failed: (error) => toast.fromError(error, "Could not save the tab bar"),
});

/**
 * Holds the signed-in person's bar for as long as the caller is mounted and the API can be reached: it is
 * read when the connection opens, again whenever it reopens, and follows the preferences the API pushes.
 * Returns the four free slots and the setter the dock reports its choice to.
 */
export function useTabBar(online) {
  const slots = React.useSyncExternalStore(bar.subscribe, bar.slots);
  React.useEffect(() => {
    if (!online) return undefined;
    let connected = false;
    void bar.attach();
    const held = holdPersonConnection({
      listen: (connection) => connection.on("PreferencesChanged", (preferences) => bar.heard(preferences)),
      opened: () => {
        if (connected) void bar.refresh();
        connected = true;
      },
    });
    return () => {
      held.stop();
      bar.detach();
    };
  }, [online]);
  return [slots || DEFAULT_SLOTS, bar.change];
}

/**
 * The dock's sections: the sidebar's groups in order, then the places only the dock reaches. `item` draws
 * one place as the dock's item.
 */
export function dockSections(item) {
  return SECTIONS.map((section) => ({
    id: section.id,
    label: section.label,
    items: placesIn(section.id).map((id) => item(id, PLACES[id])),
  }));
}
