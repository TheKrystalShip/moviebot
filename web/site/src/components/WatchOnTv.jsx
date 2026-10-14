import React from "react";
import { Button } from "@thekrystalship/krystal";
import { setRemoteMode, useDrivenScreen } from "../remote.js";
import { watchOnTv } from "../remoteWatch.js";
import { useWatchLink } from "./Film.jsx";

/** Whether this phone is driving a television: remote mode on, with a paired screen connected. */
export function useDriving() {
  return Boolean(useDrivenScreen());
}

/**
 * Watch, with the phone glyph: plays on this phone, and turns remote mode off since the phone is the
 * screen now. Nothing is unpaired.
 */
export function WatchHere({ titleId, sessionId, at, size = "xl" }) {
  const link = useWatchLink(titleId, sessionId, at);
  return (
    <Button variant="secondary" size={size} icon="smartphone" {...link}
      onClick={(e) => { setRemoteMode(false); if (link.onClick) link.onClick(e); }}>
      Watch
    </Button>
  );
}

/**
 * A title's main actions while the phone drives a television: Watch on TV, and Watch beside it. `target`
 * is what the question before a playing film is replaced says of the title (`name`, `meta`, `art`).
 */
export function WatchOnTvActions({ titleId, sessionId, at, target }) {
  const [busy, setBusy] = React.useState(false);
  const go = async () => {
    setBusy(true);
    await watchOnTv({ titleId, startSeconds: at > 0 ? at : null, ...target });
    setBusy(false);
  };
  return (
    <>
      <Button variant="primary" size="xl" icon="tv" busy={busy} onClick={go}>Watch on TV</Button>
      <WatchHere titleId={titleId} sessionId={sessionId} at={at} />
    </>
  );
}
