import React from "react";
import { Button, Dialog, useStore } from "@thekrystalship/krystal-ui";
import { finishLeaving, leaving, stay } from "../leave.js";
import { size } from "../pages/Offline.jsx";

/** Signing out with films saved on this device: keep them for the next sign-in, or remove them. */
export function SignOutDialog() {
  const asked = useStore(leaving);
  const [busy, setBusy] = React.useState(null);
  if (!asked) return null;

  const finish = (remove) => {
    setBusy(remove ? "remove" : "keep");
    void finishLeaving(remove).finally(() => setBusy(null));
  };

  return (
    <Dialog title="Sign out" icon="log-out" onClose={stay} canClose={!busy}
      foot={<>
        <Button variant="outline" size="lg" disabled={!!busy} busy={busy === "keep"} onClick={() => finish(false)}>Keep them</Button>
        <Button variant="danger" size="lg" icon="trash-2" disabled={!!busy} busy={busy === "remove"} onClick={() => finish(true)}>Remove them</Button>
      </>}>
      <span className="kc-note">
        {asked.count === 1 ? "1 film" : `${asked.count} films`} saved on this device · {size(asked.bytes)}
      </span>
    </Dialog>
  );
}
