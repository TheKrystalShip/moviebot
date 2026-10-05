import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { beginSignIn, completeSignIn } from "../session.js";
import { navigate } from "../router.js";

/**
 * The door. Signing in is the identity provider's: the button goes there, and the provider sends the
 * browser back here with a code, which is redeemed on arrival. Somebody already signed in there comes
 * straight back without typing anything.
 */
export function SignIn({ query }) {
  const returning = query.has("code") || query.has("error");
  const [error, setError] = React.useState(null);
  const [busy, setBusy] = React.useState(returning);

  React.useEffect(() => {
    if (!returning) return;
    completeSignIn(query).then(
      (returnTo) => navigate(returnTo, { replace: true }),
      (e) => { setError(e.message); setBusy(false); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const start = () => {
    setBusy(true);
    beginSignIn(query.get("next") || "/").catch(() => {
      setError("The sign-in service is not answering. Try again in a moment.");
      setBusy(false);
    });
  };

  return (
    <div className="kc-signin">
      <div className="kc-signin__card">
        <img src={markUrl} width="48" height="48" alt="" />
        <h1>Krystal Cinema</h1>
        {error && <span className="kc-note kc-note--danger">{error}</span>}
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
