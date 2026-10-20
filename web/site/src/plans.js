// A plan page is told when a plan changes (`PlanChanged` on the person's own hub connection, which the
// API sends to every person in the plan), so it reads the plan again instead of polling. The connection
// is held only while a page uses it, and a reconnect asks again for whatever was missed.

import React from "react";
import { holdPersonConnection } from "./personConnection.js";

/** Calls `onChange(id)` when a plan the person is in changes, and `onChange(null)` after a reconnect. */
export function usePlanChanges(onChange) {
  const latest = React.useRef(onChange);
  latest.current = onChange;
  React.useEffect(() => {
    let connected = false;
    const held = holdPersonConnection({
      listen: (connection) => connection.on("PlanChanged", (id) => latest.current(id)),
      opened: () => {
        if (connected) latest.current(null);
        connected = true;
      },
    });
    return () => held.stop();
  }, []);
}
