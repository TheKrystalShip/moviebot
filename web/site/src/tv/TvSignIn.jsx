import React from "react";
import { Button } from "@thekrystalship/krystal";
import markUrl from "@thekrystalship/krystal/assets/tks-mark.png";
import { startDeviceSignIn } from "../session.js";
import { Qr } from "./Qr.jsx";

/** The first wait before asking a provider that could not be reached again, and the longest. */
const RETRY_FIRST_MS = 2000;
const RETRY_LONGEST_MS = 60000;

/**
 * A television signing in: a code a phone approves at the provider, shown as the code itself, the
 * address to enter it at, a QR code of the address with the code already in it (`Qr.jsx`), and the
 * time left on it. Every answer the provider gives has one handling. An expired code is replaced unasked;
 * a provider that cannot be reached for a code is asked again on a lengthening wait, with Retry; a
 * refusal waits for New code, since somebody decided it; and a provider that will not sign this client
 * in this way is said once and not asked again.
 *
 * `over` draws it over a page whose session has ended, which approval returns to.
 */
export function TvSignIn({ over = false }) {
  const [state, again] = useDeviceSignIn();
  return (
    <div className={"kc-tv-signin" + (over ? " is-over" : "")}>
      <div className="kc-tv-signin__head">
        <img src={markUrl} width="40" height="40" alt="" />
        <span>Cinema</span>
      </div>
      {state.phase === "code" && <Code code={state.code} />}
      {state.phase === "unreachable" && (
        <Stopped title="Can't reach sign-in" action="Retry" icon="refresh-cw" onAction={again} />
      )}
      {state.phase === "refused" && (
        <Stopped title="Sign-in refused" action="New code" icon="refresh-cw" onAction={again} />
      )}
      {state.phase === "misconfigured" && <Stopped title="This TV can't sign in" />}
    </div>
  );
}

function Code({ code }) {
  const left = useSecondsLeft(code.expiresAt);
  const address = code.uri.replace(/^https?:\/\//, "");
  return (
    <div className="kc-tv-signin__body">
      <div className="kc-tv-signin__text">
        <h1>Sign in</h1>
        <p className="kc-tv-signin__address">{address}</p>
        <p className="kc-tv-signin__code" aria-label={code.userCode.split("").join(" ")}>{code.userCode}</p>
        <p className="kc-tv-signin__left">{Math.floor(left / 60)}:{String(left % 60).padStart(2, "0")}</p>
      </div>
      <Qr value={code.uriComplete} className="kc-tv-signin__qr" />
    </div>
  );
}

function Stopped({ title, action, icon, onAction }) {
  const ref = React.useRef(null);
  React.useEffect(() => {
    const button = ref.current && ref.current.querySelector("button");
    if (button) button.focus({ focusVisible: true });
  }, []);
  return (
    <div className="kc-tv-signin__stopped" ref={ref}>
      <h1>{title}</h1>
      {action && <Button variant="primary" size="lg" icon={icon} onClick={onAction}>{action}</Button>}
    </div>
  );
}

function useSecondsLeft(until) {
  const [now, setNow] = React.useState(Date.now());
  React.useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  return Math.max(0, Math.round((until - now) / 1000));
}

/**
 * Runs one sign-in: asks for a code and waits until somebody decides, answering the phase to show and
 * a way to start over. Approval stores the session, which takes this screen away.
 */
function useDeviceSignIn() {
  const [state, setState] = React.useState({ phase: "starting" });
  const [round, setRound] = React.useState(0);

  React.useEffect(() => {
    let live = true;
    let timer = null;
    let code = null;
    const wait = (ms) => new Promise((resolve) => { timer = window.setTimeout(resolve, ms); });

    (async () => {
      let retry = RETRY_FIRST_MS;
      while (live) {
        try {
          code = await startDeviceSignIn();
        } catch (error) {
          if (!live) return;
          if (error.kind !== "unreachable") {
            setState({ phase: "misconfigured" });
            return;
          }
          setState({ phase: "unreachable" });
          await wait(retry);
          retry = Math.min(retry * 2, RETRY_LONGEST_MS);
          continue;
        }
        if (!live) {
          code.cancel();
          return;
        }
        retry = RETRY_FIRST_MS;
        setState({
          phase: "code",
          code: {
            userCode: code.userCode,
            uri: code.verificationUri,
            uriComplete: code.verificationUriComplete,
            expiresAt: Date.now() + code.expiresIn * 1000,
          },
        });
        const outcome = await code.poll();
        if (!live) return;
        if (outcome === "refused") {
          setState({ phase: "refused" });
          return;
        }
        if (outcome !== "expired") return;
      }
    })();

    return () => {
      live = false;
      window.clearTimeout(timer);
      if (code) code.cancel();
    };
  }, [round]);

  return [state, () => setRound((r) => r + 1)];
}
