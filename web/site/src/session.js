// Who is signed in, and the token that proves it.
//
// Krystal Cinema signs nobody in. It is a public OpenID Connect client of the provider the API names
// on /api/config: authorization code with PKCE, through `oidc-client-ts`. Signing in sends the browser
// to the provider, which sends it back to /signin with a code the library redeems for an access token
// and a refresh token. The session lives in this browser's localStorage, so a tab opened days later
// renews without a bounce; a round trip's verifier and state stay in sessionStorage, because they
// belong to the one tab that left.
//
// The access token lives five minutes. It is renewed by the refresh grant alone, a minute before it
// runs out and whenever a call is refused, never through an iframe. The token lists the Cinema
// actions the person holds, in the claim the API names, and that is what the page shows or hides
// controls by; the API checks every one of them again.
//
// Signing out forgets the tokens here and nothing more: the provider stays signed in, so the next
// visit signs back in without a prompt. Ending every session is the provider's account page.

import { createStore, useStore } from "@thekrystalship/krystal-ui";
import { ErrorResponse, UserManager, WebStorageStateStore } from "oidc-client-ts";

const PREFIX = "cinema.oidc.";
const RETURN_KEY = "cinema:signin-return";

/** How long before an access token runs out it is renewed, in seconds. */
const RENEW_AHEAD_S = 60;

/** Every request to the provider is bounded: a token endpoint that never answers holds every renewal. */
const PROVIDER_TIMEOUT_S = 10;

export const sessionStore = createStore(null);

/** The signed-in session: `{ token, expiresAt, user: { id, name, username }, actions }`, or null. */
export function useSession() {
  return useStore(sessionStore);
}

/** Whether the signed-in person holds a Cinema action, such as `cinema:download`. */
export function can(session, action) {
  return !!(session && session.actions.includes(action));
}

/** Whether the signed-in person holds a Cinema action: what a control is shown by. */
export function useCan(action) {
  return can(useSession(), action);
}

let configured = null;
let client = null;
let renewing = null;
let timer = null;

/** What the API says about where to sign in. Asked once. */
function config() {
  configured ??= fetch("/api/config").then((response) => {
    if (!response.ok) throw new Error(`MovieBot answered ${response.status}.`);
    return response.json();
  }).catch((error) => {
    configured = null;
    throw error;
  });
  return configured;
}

async function clientOf() {
  if (client) return client;
  const { issuer, clientId } = await config();
  const origin = window.location.origin;
  client = new UserManager({
    authority: issuer,
    client_id: clientId,
    redirect_uri: origin + "/signin",
    post_logout_redirect_uri: origin + "/",
    response_type: "code",
    scope: "openid",
    userStore: new WebStorageStateStore({ prefix: PREFIX, store: window.localStorage }),
    stateStore: new WebStorageStateStore({ prefix: PREFIX, store: window.sessionStorage }),
    automaticSilentRenew: false,
    monitorSession: false,
    loadUserInfo: false,
    requestTimeoutInSeconds: PROVIDER_TIMEOUT_S,
  });
  return client;
}

/** The claims an access token carries, read without verifying anything: the API verifies it. */
function claimsOf(token) {
  try {
    const segment = String(token).split(".")[1];
    const json = atob(segment.replace(/-/g, "+").replace(/_/g, "/"));
    return JSON.parse(decodeURIComponent(Array.from(json, (c) => "%" + c.charCodeAt(0).toString(16).padStart(2, "0")).join("")));
  } catch {
    return null;
  }
}

/** The session the page holds for a user the library returned, or null for one with no usable token. */
async function sessionFor(user) {
  if (!user || !user.access_token || user.expired) return null;
  const { actionsClaim } = await config();
  const claims = claimsOf(user.access_token) || {};
  const profile = user.profile || {};
  const listed = claims[actionsClaim];
  return {
    token: user.access_token,
    expiresAt: user.expires_at,
    user: {
      id: claims.sub || profile.sub,
      name: claims.name || profile.name || claims.preferred_username || profile.preferred_username || "Somebody",
      username: claims.preferred_username || profile.preferred_username || null,
    },
    actions: Array.isArray(listed) ? listed : typeof listed === "string" ? [listed] : [],
  };
}

function hold(session) {
  sessionStore.setState(() => session);
  if (timer) window.clearTimeout(timer);
  timer = null;
  if (!session) return;
  const wait = Math.max(5, session.expiresAt - Date.now() / 1000 - RENEW_AHEAD_S) * 1000;
  timer = window.setTimeout(() => { void renew(); }, wait);
}

/**
 * Renews through the refresh grant. A refusal is checked against storage once before it is believed:
 * every tab on this origin shares the stored session, so two tabs renewing together present the same
 * refresh token, and the provider rotates it for whichever arrived first.
 */
function renew() {
  renewing ??= (async () => {
    const manager = await clientOf();
    const before = await manager.getUser().catch(() => null);
    if (!before || !before.refresh_token) {
      hold(null);
      return null;
    }
    try {
      const user = await manager.signinSilent({ silentRequestTimeoutInSeconds: PROVIDER_TIMEOUT_S });
      const session = await sessionFor(user);
      hold(session);
      return session;
    } catch (error) {
      if (!(error instanceof ErrorResponse)) {
        // The provider could not be asked: nothing is known about the session, so it is kept and
        // asked about again shortly.
        timer = window.setTimeout(() => { void renew(); }, 15000);
        return sessionStore.getState();
      }
      const after = await manager.getUser().catch(() => null);
      if (after && after.refresh_token && after.refresh_token !== before.refresh_token && !after.expired) {
        const session = await sessionFor(after);
        hold(session);
        return session;
      }
      await manager.removeUser().catch(() => {});
      hold(null);
      return null;
    }
  })().finally(() => { renewing = null; });
  return renewing;
}

/** Picks up the session this browser holds, renewing it when its access token has run out. */
export async function restore() {
  const manager = await clientOf();
  const user = await manager.getUser().catch(() => null);
  if (!user) {
    hold(null);
    return null;
  }
  if (!user.expired && user.expires_at - Date.now() / 1000 > RENEW_AHEAD_S) {
    const session = await sessionFor(user);
    hold(session);
    return session;
  }
  return renew();
}

/** The access token while it is good, or null. A token about to run out is renewed in the background. */
export function currentToken() {
  const session = sessionStore.getState();
  if (!session) return null;
  if (session.expiresAt * 1000 <= Date.now()) {
    void renew();
    return null;
  }
  return session.token;
}

/** A good access token, renewing first when the held one has run out or was refused. */
export async function freshToken({ refused = false } = {}) {
  const session = sessionStore.getState();
  if (session && !refused && session.expiresAt - Date.now() / 1000 > 5) return session.token;
  if (!session && !refused) return null;
  const renewed = await renew();
  return renewed ? renewed.token : null;
}

/** Forgets this browser's session. The provider stays signed in. */
export async function signOut() {
  try {
    const manager = await clientOf();
    await manager.removeUser();
  } catch {
    // Nothing was held, or the provider's address was never learned; either way nothing is held now.
  }
  hold(null);
}

/** Sends the browser to the provider to sign in, remembering where to come back to. */
export async function beginSignIn(returnTo) {
  try { sessionStorage.setItem(RETURN_KEY, returnTo || "/"); } catch { /* the home page is a fine place to land */ }
  const manager = await clientOf();
  await manager.signinRedirect();
}

/**
 * Finishes a sign-in the provider sent back, and answers where to go next. Throws with the reason a
 * person can read when the provider said no.
 */
export async function completeSignIn(params) {
  let returnTo = "/";
  try {
    returnTo = sessionStorage.getItem(RETURN_KEY) || "/";
    sessionStorage.removeItem(RETURN_KEY);
  } catch { /* the home page is a fine place to land */ }

  if (params.get("error")) throw new Error("The sign-in service did not sign you in.");
  const manager = await clientOf();
  let user;
  try {
    user = await manager.signinRedirectCallback(window.location.href);
  } catch {
    throw new Error("That sign-in was not started here, or has expired. Try again.");
  }
  const session = await sessionFor(user);
  if (!session) throw new Error("The sign-in service sent back no usable token. Try again.");
  hold(session);
  return returnTo;
}

/** The provider's own page for the account: its sessions, and signing out everywhere. */
export async function accountUrl() {
  const { issuer } = await config();
  return issuer.replace(/\/+$/, "") + "/account";
}
