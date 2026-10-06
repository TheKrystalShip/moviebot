import React from "react";
import { Button } from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { pollDeviceCode, requestDeviceCode } from "../session.js";

/** The first wait before asking a provider that could not be reached again, and the longest. */
const RETRY_FIRST_MS = 2000;
const RETRY_LONGEST_MS = 60000;
/** What `slow_down` adds to the wait between polls (RFC 8628 §3.5). */
const SLOW_DOWN_S = 5;

/**
 * A television signing in: a code a phone approves at the provider, shown as the code itself, the
 * address to enter it at, a QR code of the address with the code already in it, and the time left on
 * it. Every answer the provider gives has one handling. An expired code is replaced unasked; a poll
 * that fails on the network keeps the code and keeps polling, and says so only if it is still failing
 * when the code runs out; a provider that cannot be reached for a code is asked again on a lengthening
 * wait, with Retry; a refusal waits for New code, since somebody decided it; and a provider that will
 * not sign this client in this way is said once and not asked again.
 *
 * `over` draws it over a page whose session has ended, which approval returns to.
 */
export function TvSignIn({ over = false }) {
  const [state, again] = useDeviceSignIn();
  return (
    <div className={"kc-tv-signin" + (over ? " is-over" : "")}>
      <div className="kc-tv-signin__head">
        <img src={markUrl} width="40" height="40" alt="" />
        <span>Krystal Cinema</span>
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
      <Qr value={code.uriComplete} />
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

/** A QR code of `value`, drawn as SVG by `lean-qr`, which only a television ever fetches. */
function Qr({ value }) {
  const [drawn, setDrawn] = React.useState(null);
  React.useEffect(() => {
    let live = true;
    Promise.all([import("lean-qr"), import("lean-qr/extras/svg")]).then(([{ generate }, { toSvgPath }]) => {
      if (!live) return;
      const code = generate(value);
      setDrawn({ size: code.size, path: toSvgPath(code) });
    }, () => {});
    return () => { live = false; };
  }, [value]);
  return (
    <div className="kc-tv-signin__qr">
      {drawn && (
        // Four modules of quiet zone on every side, as a scanner expects.
        <svg viewBox={`-4 -4 ${drawn.size + 8} ${drawn.size + 8}`} shapeRendering="crispEdges" aria-hidden="true">
          <path d={drawn.path} />
        </svg>
      )}
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
 * Runs one sign-in: asks for a code and polls until somebody decides, answering the phase to show and
 * a way to start over. Approval stores the session, which takes this screen away.
 */
function useDeviceSignIn() {
  const [state, setState] = React.useState({ phase: "starting" });
  const [round, setRound] = React.useState(0);

  React.useEffect(() => {
    let live = true;
    let timer = null;
    const wait = (ms) => new Promise((resolve) => { timer = window.setTimeout(resolve, ms); });

    (async () => {
      let retry = RETRY_FIRST_MS;
      for (;;) {
        let code;
        try {
          code = await requestDeviceCode();
        } catch (error) {
          if (!live) return;
          if (error.kind !== "unreachable") {
            setState({ phase: "misconfigured" });
            return;
          }
          setState({ phase: "unreachable" });
          await wait(retry);
          retry = Math.min(retry * 2, RETRY_LONGEST_MS);
          if (!live) return;
          continue;
        }
        if (!live) return;
        retry = RETRY_FIRST_MS;
        setState({ phase: "code", code });

        let interval = code.interval;
        let failing = false;
        for (;;) {
          await wait(interval * 1000);
          if (!live) return;
          if (Date.now() >= code.expiresAt) {
            if (failing) setState({ phase: "unreachable" });
            break;
          }
          try {
            const answer = await pollDeviceCode(code);
            if (!live) return;
            failing = false;
            if (answer === "slow") interval += SLOW_DOWN_S;
            else if (answer !== "pending") return;
          } catch (error) {
            if (!live) return;
            if (error.kind === "unreachable") failing = true;
            else if (error.kind === "expired") break;
            else {
              setState({ phase: error.kind === "refused" ? "refused" : "misconfigured" });
              return;
            }
          }
        }
      }
    })();

    return () => {
      live = false;
      window.clearTimeout(timer);
    };
  }, [round]);

  return [state, () => setRound((r) => r + 1)];
}
