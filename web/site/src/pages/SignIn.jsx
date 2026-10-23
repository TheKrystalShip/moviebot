import React from "react";
import { Button, CardScreen, Notice } from "@thekrystalship/krystal";
import markUrl from "@thekrystalship/krystal/assets/tks-mark.png";
import { signIn } from "../session.js";
import { holdSplash } from "@thekrystalship/krystal";

const UNREACHABLE = "The sign-in service is not answering. Try again in a moment.";

/**
 * Nothing to show yet: the page before a session is known, or while the browser leaves for the
 * provider. It keeps the splash up, and is its canvas once the splash has gone.
 */
export function Dark() {
  React.useLayoutEffect(() => holdSplash(), []);
  return <div className="kc-dark" />;
}

/**
 * The site waiting to be asked before it leaves for the provider: a session that ended, or a provider
 * that is not answering. The button signs in again and comes back to the page that was asked for.
 */
export function Waiting({ note, returnTo }) {
  const [busy, setBusy] = React.useState(false);
  const [failed, setFailed] = React.useState(null);

  const start = () => {
    setBusy(true);
    signIn(returnTo).then((left) => {
      if (!left) { setFailed(UNREACHABLE); setBusy(false); }
    });
  };

  const shown = failed || note;
  return (
    <CardScreen mark={markUrl} wordmark="Cinema">
      {shown && <Notice tone="danger">{shown}</Notice>}
      <Button variant="primary" size="xl" wide icon="log-in" busy={busy} onClick={start}>
        {busy ? "Signing in…" : "Sign in"}
      </Button>
    </CardScreen>
  );
}

/**
 * An approved account that holds no Cinema action that lets it watch: there is nothing here for it yet.
 * Without `onSignOut` there is no way out offered, as in the Discord Activity, which is left in Discord.
 */
export function NoAccess({ onSignOut }) {
  return (
    <CardScreen mark={markUrl} wordmark="Cinema">
      <Notice>Your account has no access to Cinema yet.</Notice>
      {onSignOut && <Button variant="outline" size="xl" wide icon="log-out" onClick={onSignOut}>Sign out</Button>}
    </CardScreen>
  );
}

/**
 * The Discord Activity that could not sign in, or whose session ended: why, and nothing to press. It
 * signs in through Discord when it starts, so starting it again is the way back.
 */
export function ActivityProblem({ note }) {
  return (
    <CardScreen mark={markUrl} wordmark="Cinema">
      <Notice tone="danger">{note}</Notice>
    </CardScreen>
  );
}
