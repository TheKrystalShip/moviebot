import React from "react";
import { toast } from "@thekrystalship/krystal";
import { homePath } from "../paths.js";
import { navigate } from "../router.js";
import { pair, unpair } from "../remote.js";
import { Dark } from "./SignIn.jsx";

/** The codes this page has sent, so a code is sent once however often the page is drawn. */
const sent = new Set();

/**
 * Where a television's code lands on a phone, `/p/<code>`, once somebody is signed in (a phone with
 * nothing held signs in first and comes back here). It pairs this session with the television at once,
 * with nothing to confirm, turns remote mode on, and goes Home with a toast saying so that carries
 * Undo. A code that pairs nothing goes Home with the reason.
 */
export function Pairing({ code }) {
  React.useEffect(() => {
    if (sent.has(code)) return;
    sent.add(code);
    pair(code).then((screen) => {
      navigate(homePath, { replace: true });
      toast.success(`Paired with ${screen.name || "your TV"}`, {
        action: { label: "Undo", onClick: () => unpair(screen.id).catch((error) => toast.fromError(error, "Still paired")) },
      });
    }, (error) => {
      navigate(homePath, { replace: true });
      toast.fromError(error, "Not paired");
    });
  }, [code]);
  return <Dark />;
}
