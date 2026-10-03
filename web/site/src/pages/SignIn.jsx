import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { beginSignIn, completeSignIn } from "../session.js";
import { navigate } from "../router.js";

/**
 * The door. Signing in goes through Discord, and only members of the server MovieBot plays films in
 * are let in. Discord sends the browser back here with a code, which is redeemed on arrival.
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
    const returnTo = query.get("next") || "/";
    beginSignIn(returnTo).catch(() => { setError("MovieBot is not answering. Try again in a moment."); setBusy(false); });
  };

  return (
    <div className="kc-signin">
      <div className="kc-signin__card">
        <img src={markUrl} width="48" height="48" alt="" />
        <h1>Krystal Cinema</h1>
        <p>Movies and shows for the server, watched together. Sign in with the Discord account you use there.</p>
        {error && <span className="kc-note kc-note--danger">{error}</span>}
        <button type="button" className="kc-btn kc-btn--primary kc-btn--wide" disabled={busy} onClick={start}>
          <Icon name="log-in" size={18} />{busy ? "Signing in…" : "Sign in with Discord"}
        </button>
      </div>
    </div>
  );
}
