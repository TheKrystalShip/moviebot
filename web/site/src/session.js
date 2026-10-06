// Who is signed in, and the token that proves it.
//
// Krystal Cinema signs nobody in. It is a public OpenID Connect client of the provider the API names
// on /api/config: authorization code with PKCE, through `oidc-client-ts`. A page opened with no session
// sends the browser to the provider, which signs the person in on its own pages and sends it back to
// /signin with a code the library redeems for an access token and a refresh token. The session lives in this browser's localStorage, so a tab opened days later
// renews without a bounce; a round trip's verifier and state stay in sessionStorage, because they
// belong to the one tab that left.
//
// The access token lives five minutes. It is renewed by the refresh grant alone, a minute before it
// runs out and whenever a call is refused, never through an iframe. The token lists the Cinema
// actions the person holds, in the claim the API names, and that is what the page shows or hides
// controls by; the API checks every one of them again.
//
// A page opened with nothing held leaves for the provider at once, because nothing is on screen to lose.
// A session that ends while a page is open does not: that is shown, with the way back offered, because
// leaving unasked would discard whatever somebody was in the middle of.
//
// Signing out is the provider's: its end-session endpoint ends the provider's own session and every
// session minted under it, and sends the browser back to the site, which finds nothing held and leaves
// for the provider's sign-in.
//
// A television never goes to the provider. It signs in as the device client the API names, through
// the device authorization grant (RFC 8628): it asks for a code, shows it for a phone to approve, and
// polls the token endpoint until somebody decides. The tokens that come back are stored as the
// library's own user, so renewal, restoring and every caller are the same on a television as anywhere.
// Signing out there revokes the refresh token (RFC 7009) and forgets the session; there is no provider
// session in the television's browser to end.

import { createStore, useStore } from "@thekrystalship/krystal-ui";
import { ErrorResponse, User, UserManager, WebStorageStateStore } from "oidc-client-ts";
import { onTv } from "./tv/tv.js";

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
/** Whether this page held a session that has since ended: what tells an ended session from a cold load. */
let lost = false;
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
  const { issuer, clientId, tvClientId } = await config();
  const origin = window.location.origin;
  client = new UserManager({
    authority: issuer,
    // The provider continues a session only for the client it was minted for.
    client_id: onTv ? tvClientId : clientId,
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
  if (!session && sessionStore.getState()) lost = true;
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

/**
 * Signs out at the provider, which ends every session minted under its own and sends the browser back
 * to the site. With nothing to end there, or no provider to ask, this browser's tokens are forgotten
 * and the site's first page opened.
 */
export async function signOut() {
  if (onTv) return signOutTv();
  let manager = null;
  let user = null;
  try {
    manager = await clientOf();
    user = await manager.getUser();
    await manager.removeUser();
  } catch {
    // Nothing was held, or the provider's address was never learned; either way nothing is held now.
  }
  hold(null);
  lost = false;
  if (manager && user && user.id_token) {
    try {
      await manager.signoutRedirect({ id_token_hint: user.id_token });
      return;
    } catch {
      // The provider could not be asked; this browser has forgotten the session regardless.
    }
  }
  window.location.assign("/");
}

/**
 * Signs a television out: its refresh token is revoked at the provider, which ends the session, and
 * forgotten here. A provider that cannot be asked leaves the session to run out there, or to be ended
 * from the account's page; this television holds nothing either way.
 */
async function signOutTv() {
  try {
    const manager = await clientOf();
    await manager.revokeTokens(["refresh_token"]).catch(() => {});
    await manager.removeUser();
  } catch {
    // Nothing was held, or the provider's address was never learned; either way nothing is held now.
  }
  hold(null);
  lost = false;
}

/**
 * Why a television's sign-in stopped: the provider could not be reached (`unreachable`), the person
 * refused (`refused`), the code ran out (`expired`), or the provider will not sign this client in
 * this way (`misconfigured`).
 */
export class DeviceSignInError extends Error {
  constructor(kind, message) {
    super(message || kind);
    this.kind = kind;
  }
}

/** What an OAuth error answer means for a television waiting on a code. */
const DeviceOutcomes = {
  authorization_pending: "pending",
  slow_down: "slow",
  access_denied: "refused",
  expired_token: "expired",
  invalid_grant: "expired",
  temporarily_unavailable: "unreachable",
};

/** Posts a form to the provider, answering its JSON, and throws a `DeviceSignInError` it can name. */
async function postForm(url, fields) {
  let response;
  try {
    response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded", Accept: "application/json" },
      body: new URLSearchParams(fields),
      signal: AbortSignal.timeout(PROVIDER_TIMEOUT_S * 1000),
    });
  } catch {
    throw new DeviceSignInError("unreachable");
  }
  const body = await response.json().catch(() => null);
  if (response.ok && body) return body;
  if (response.status >= 500 || response.status === 429 || !body) throw new DeviceSignInError("unreachable");
  throw new DeviceSignInError(DeviceOutcomes[body.error] || "misconfigured", body.error_description || body.error);
}

/**
 * Asks the provider for a code a person approves on another device: `{ deviceCode, userCode, uri,
 * uriComplete, expiresAt, interval }`, the expiry in milliseconds and the interval in seconds.
 */
export async function requestDeviceCode() {
  let manager;
  let endpoint;
  try {
    manager = await clientOf();
    endpoint = (await manager.metadataService.getMetadata()).device_authorization_endpoint;
  } catch {
    throw new DeviceSignInError("unreachable");
  }
  if (!endpoint) throw new DeviceSignInError("misconfigured", "The provider serves no device sign-in.");
  const answer = await postForm(endpoint, { client_id: manager.settings.client_id, scope: "openid" });
  return {
    deviceCode: answer.device_code,
    userCode: answer.user_code,
    uri: answer.verification_uri,
    uriComplete: answer.verification_uri_complete || answer.verification_uri,
    expiresAt: Date.now() + Number(answer.expires_in) * 1000,
    interval: Math.max(1, Number(answer.interval) || 5),
  };
}

/**
 * Asks once whether the person has decided on `code`: `pending` or `slow` while they have not, the
 * session once they approved. A refusal, an expiry and a provider that cannot be reached throw.
 */
export async function pollDeviceCode(code) {
  const manager = await clientOf();
  let endpoint;
  try {
    endpoint = await manager.metadataService.getTokenEndpoint();
  } catch {
    throw new DeviceSignInError("unreachable");
  }
  let answer;
  try {
    answer = await postForm(endpoint, {
      grant_type: "urn:ietf:params:oauth:grant-type:device_code",
      device_code: code.deviceCode,
      client_id: manager.settings.client_id,
    });
  } catch (error) {
    if (error.kind === "pending" || error.kind === "slow") return error.kind;
    throw error;
  }
  const claims = claimsOf(answer.access_token) || {};
  const user = new User({
    access_token: answer.access_token,
    refresh_token: answer.refresh_token,
    id_token: answer.id_token || undefined,
    token_type: answer.token_type || "Bearer",
    scope: answer.scope || "openid",
    expires_at: Math.floor(Date.now() / 1000) + Number(answer.expires_in || 0),
    profile: { sub: claims.sub, name: claims.name, preferred_username: claims.preferred_username },
  });
  const session = await sessionFor(user);
  if (!session) throw new DeviceSignInError("misconfigured", "The provider sent back no usable token.");
  await manager.storeUser(user);
  lost = false;
  hold(session);
  return session;
}

/** Forgets a session the API refused even once renewed: it has ended, and the page says so. */
export async function forget() {
  try {
    const manager = await clientOf();
    await manager.removeUser();
  } catch {
    // Nothing was held, or the provider's address was never learned; either way nothing is held now.
  }
  hold(null);
}

/** Whether a session this page held has ended, which is shown rather than followed to the provider. */
export function sessionEnded() {
  return lost;
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
