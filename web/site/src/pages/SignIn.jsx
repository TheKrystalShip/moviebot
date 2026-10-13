import React from "react";
import { Button, CardScreen, Notice } from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { beginSignIn, completeSignIn } from "../session.js";
import { navigate } from "../router.js";
import { holdSplash } from "../splash.js";

const UNREACHABLE = "The sign-in service is not answering. Try again in a moment.";

/**
 * Nothing to show yet: the page before a session is known, or while the browser leaves for the
 * provider. It keeps the splash up (`splash.js`), and is its canvas once the splash has gone.
 */
export function Dark() {
  React.useLayoutEffect(() => holdSplash(), []);
  return <div style={{ position: "fixed", inset: 0, background: "#0B0F14" }} />;
}

/**
 * Where the provider sends the browser back: the code is redeemed and the page that was asked for
 * opens. Arriving here with no code is somebody who opened the address itself, and they are sent to
 * the provider.
 */
export function SignInReturn({ query }) {
  const [note, setNote] = React.useState(null);

  React.useEffect(() => {
    if (query.has("code") || query.has("error")) {
      completeSignIn(query).then(
        (returnTo) => navigate(returnTo, { replace: true }),
        (e) => setNote(e.message));
    } else {
      beginSignIn(query.get("next") || "/").catch(() => setNote(UNREACHABLE));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return note ? <Waiting note={note} returnTo="/" /> : <Dark />;
}

/**
 * The site waiting to be asked before it leaves for the provider: a session that ended while a page was
 * open, a sign-in the provider refused, or a provider that is not answering. The button signs in again
 * and comes back to the page that was asked for.
 */
export function Waiting({ note, returnTo }) {
  const [busy, setBusy] = React.useState(false);
  const [failed, setFailed] = React.useState(null);

  const start = () => {
    setBusy(true);
    beginSignIn(returnTo).catch(() => { setFailed(UNREACHABLE); setBusy(false); });
  };

  const shown = failed || note;
  return (
    <CardScreen mark={markUrl} wordmark="Krystal Cinema">
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
    <CardScreen mark={markUrl} wordmark="Krystal Cinema">
      <Notice>Your account has no access to Krystal Cinema yet.</Notice>
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
    <CardScreen mark={markUrl} wordmark="Krystal Cinema">
      <Notice tone="danger">{note}</Notice>
    </CardScreen>
  );
}
