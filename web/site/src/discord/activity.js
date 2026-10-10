// The site as a Discord Activity: the Embedded App SDK, and the sign-in that turns the Discord account
// in the Activity into a Cinema session.
//
// The Activity has no browser to redirect, so the site's own sign-in never runs here. The SDK's
// `authorize` gives a code, which the API redeems with Cinema's Discord application and exchanges at
// the identity provider (`/api/auth/discord/callback`); the provider's token is held as the site's
// session (`session.js` `adopt`), so every page, the player and the assistant use it as they use a
// session signed in in a browser. Before it runs out the API is asked again with Discord's access
// token, which outlives it.
//
// Every request leaves through Discord's proxy, which reaches this site's own origin and nothing
// else. A link off the site is opened by Discord, outside the Activity.

import { DiscordSDK } from "@discord/embedded-app-sdk";
import { activityLaunch, activityRoom } from "./launch.js";
import { adopt } from "../session.js";

/** The SDK, reading the launch this frame kept rather than an address the site has since moved to. */
class LaunchedSDK extends DiscordSDK {
  _getSearch() {
    return activityLaunch;
  }
}

/** Asks the API to sign this viewer in: with the SDK's code the first time, with Discord's access token after. */
async function signIn(body) {
  const response = await fetch("/api/auth/discord/callback", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    const refusal = await response.json().catch(() => null);
    throw new Error((refusal && refusal.error) || `Signing in failed: the server answered ${response.status}.`);
  }
  return response.json();
}

let started = null;
let held = null;

/**
 * Starts the Activity once: the SDK's handshake, the sign-in, and links off the site handed to
 * Discord. Answers `{ sdk, room, publicBaseUrl }`; throws with the reason a person can read.
 */
export function startActivity() {
  started ??= (async () => {
    const response = await fetch("/api/config");
    if (!response.ok) throw new Error(`The server would not say which application this is (${response.status}).`);
    const { discordClientId, publicBaseUrl } = await response.json();
    if (!discordClientId) throw new Error("This server has no Discord application configured.");

    const sdk = new LaunchedSDK(discordClientId);
    await sdk.ready();

    // Two scopes and no more: who is watching, and what they watch written beside their name.
    const { code } = await sdk.commands.authorize({
      client_id: discordClientId,
      response_type: "code",
      state: "",
      prompt: "none",
      scope: ["identify", "rpc.activities.write"],
    });

    let reply = await signIn({ code });
    await sdk.commands.authenticate({ access_token: reply.accessToken });
    await adopt(reply, async () => {
      reply = await signIn({ accessToken: reply.accessToken });
      return reply;
    });

    // Software-decoding a film in the Discord webview will cook a laptop. Advisory: nothing depends on it.
    void sdk.commands.encourageHardwareAcceleration().catch(() => {});

    document.addEventListener("click", (e) => {
      const anchor = e.target instanceof Element ? e.target.closest("a[href]") : null;
      if (!anchor || e.defaultPrevented) return;
      const url = new URL(anchor.href, window.location.href);
      if (url.origin === window.location.origin) return;
      e.preventDefault();
      void sdk.commands.openExternalLink({ url: url.href }).catch(() => {});
    });

    held = { sdk, room: activityRoom, publicBaseUrl: publicBaseUrl ?? null };
    return held;
  })();
  return started;
}

/** The started Activity, for the player: `{ sdk, room, publicBaseUrl }`. Throws before it has started. */
export function activity() {
  if (held === null) throw new Error("The Activity has not started.");
  return held;
}
