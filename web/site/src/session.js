// Who is signed in, and the token that proves it.
//
// Discord is the only door. Signing in sends the browser to Discord's consent page asking for
// `identify guilds.members.read`, Discord sends it back to /signin with a code, and the API redeems
// the code, asks Discord whether this person is in the server, and answers with a token only if they
// are. The token is the API's own, signed and short-lived; the site keeps it in this browser and
// sends it as the bearer on every call to the API and to the assistant. Nothing else is stored, and
// a token past its expiry is a signed-out browser.

import { createStore, useStore } from "@thekrystalship/krystal-ui";

const KEY = "cinema:session";
const STATE_KEY = "cinema:signin-state";
const RETURN_KEY = "cinema:signin-return";

const read = () => {
  try {
    const stored = JSON.parse(localStorage.getItem(KEY) || "null");
    return stored && stored.expiresAt * 1000 > Date.now() ? stored : null;
  } catch {
    return null;
  }
};

export const sessionStore = createStore(read());

export function useSession() {
  return useStore(sessionStore);
}

export function currentToken() {
  const session = sessionStore.getState();
  if (session && session.expiresAt * 1000 <= Date.now()) {
    signOut();
    return null;
  }
  return session ? session.token : null;
}

export function signOut() {
  try { localStorage.removeItem(KEY); } catch {}
  sessionStore.setState(() => null);
}

/** The address Discord sends the browser back to. It is registered with the Discord application. */
export const redirectUri = () => window.location.origin + "/signin";

/** Sends the browser to Discord to sign in, remembering where to come back to. */
export async function beginSignIn(returnTo) {
  const config = await fetch("/api/config").then((r) => r.json());
  const state = crypto.randomUUID();
  try {
    sessionStorage.setItem(STATE_KEY, state);
    sessionStorage.setItem(RETURN_KEY, returnTo || "/");
  } catch {}

  const url = new URL("https://discord.com/oauth2/authorize");
  url.searchParams.set("client_id", config.discordClientId);
  url.searchParams.set("response_type", "code");
  url.searchParams.set("redirect_uri", redirectUri());
  url.searchParams.set("scope", "identify guilds.members.read");
  url.searchParams.set("state", state);
  url.searchParams.set("prompt", "none");
  window.location.assign(url.toString());
}

/**
 * Finishes a sign-in Discord sent back, and answers where to go next. Throws with the reason a
 * person can read when Discord, or the API, said no.
 */
export async function completeSignIn(params) {
  let expected = null;
  let returnTo = "/";
  try {
    expected = sessionStorage.getItem(STATE_KEY);
    returnTo = sessionStorage.getItem(RETURN_KEY) || "/";
    sessionStorage.removeItem(STATE_KEY);
    sessionStorage.removeItem(RETURN_KEY);
  } catch {}

  if (params.get("error")) throw new Error("Discord did not sign you in.");
  if (!params.get("code")) throw new Error("Discord sent no sign-in back.");
  // A code that arrives without the state this browser sent was not asked for here.
  if (!expected || params.get("state") !== expected) throw new Error("That sign-in was not started here. Try again.");

  const response = await fetch("/api/auth/discord/callback", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code: params.get("code"), redirectUri: redirectUri() }),
  });
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error((body && body.error) || "Signing in did not work. Try again.");

  const payload = JSON.parse(atob(body.roomToken.split(".")[0].replace(/-/g, "+").replace(/_/g, "/")));
  const session = {
    token: body.roomToken,
    expiresAt: payload.exp,
    user: { id: body.user.id, name: body.user.displayName, username: body.user.username },
  };
  try { localStorage.setItem(KEY, JSON.stringify(session)); } catch {}
  sessionStore.setState(() => session);
  return returnTo;
}
