import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { beginSignIn, completeSignIn } from "../session.js";
import { navigate } from "../router.js";

const UNREACHABLE = "The sign-in service is not answering. Try again in a moment.";

/** Nothing to show yet: the page before a session is known, or while the browser leaves for the provider. */
export const Dark = () => <div style={{ position: "fixed", inset: 0, background: "#000" }} />;

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
    <div className="kc-signin">
      <div className="kc-signin__card">
        <img src={markUrl} width="48" height="48" alt="" />
        <h1>Krystal Cinema</h1>
        {shown && <span className="kc-note kc-note--danger">{shown}</span>}
        <button type="button" className="kc-btn kc-btn--primary kc-btn--wide" disabled={busy} onClick={start}>
          <Icon name="log-in" size={18} />{busy ? "Signing in…" : "Sign in"}
        </button>
      </div>
    </div>
  );
}

/** An approved account that holds no Cinema action that lets it watch: there is nothing here for it yet. */
export function NoAccess({ onSignOut }) {
  return (
    <div className="kc-signin">
      <div className="kc-signin__card">
        <img src={markUrl} width="48" height="48" alt="" />
        <h1>Krystal Cinema</h1>
        <p>Your account has no access to Krystal Cinema yet.</p>
        <button type="button" className="kc-btn kc-btn--ghost kc-btn--wide" onClick={onSignOut}>
          <Icon name="log-out" size={18} />Sign out
        </button>
      </div>
    </div>
  );
}
