// Who is signed in, and the token that proves it.
//
// Krystal Cinema signs nobody in. It is a public client of the provider the API names on /api/config,
// and `@thekrystalship/auth-client` keeps the session: the code flow, the renewal, the outage and the
// tabs. This module is the site's glue over it: where to sign in (`config`), the Cinema actions the
// access token lists, and the shape the pages read.
//
// A page draws only a session that has ended. One that is being restored or recovered is somebody
// signed in, drawn as it was.
//
// The client the session signs in as is the site's, or the television's on a television, which signs
// in through the device authorization grant (`tv/TvSignIn.jsx`). The Discord Activity holds the token
// the API exchanges for the Discord account in it (`discord/activity.js`) through `adopt`.
//
// What `/api/config` said is kept in localStorage so a page opened with no connection still knows
// which provider's session it holds.

import React from "react";
import { createSession } from "@thekrystalship/auth-client";
import { isOnline, reached, unreached } from "./connectivity.js";
import { migrateSession } from "./migrateSession.js";
import { isEnded, viewOf } from "./sessionView.js";
import { deviceName, onTv } from "./tv/tv.js";

const CONFIG_KEY = "cinema.config";

/** How long the API is given to say where to sign in before the kept copy stands in. */
const CONFIG_TIMEOUT_MS = 5000;

/** The claim the access token lists Cinema actions in, once the API has said which. */
let actionsClaim = null;

let configured = null;

/**
 * What the API says about where to sign in. Asked once, and kept, so that offline the copy from the
 * last answer stands in.
 */
function config() {
  configured ??= (async () => {
    const kept = keptConfig();
    if (kept && !isOnline()) return kept;
    let response;
    try {
      response = await fetch("/api/config", { signal: AbortSignal.timeout(CONFIG_TIMEOUT_MS) });
    } catch (error) {
      unreached();
      if (kept) return kept;
      throw error;
    }
    if (!response.ok) {
      if (kept) return kept;
      throw new Error(`MovieBot answered ${response.status}.`);
    }
    reached();
    const answer = await response.json();
    try { localStorage.setItem(CONFIG_KEY, JSON.stringify(answer)); } catch { /* the next page asks again */ }
    return answer;
  })().catch((error) => {
    configured = null;
    throw error;
  });
  return configured;
}

function keptConfig() {
  try {
    const kept = JSON.parse(localStorage.getItem(CONFIG_KEY) || "null");
    return kept && kept.issuer ? kept : null;
  } catch {
    return null;
  }
}

async function resolve() {
  const answer = await config();
  actionsClaim = answer.actionsClaim;
  // The provider continues a session only for the client it was minted for.
  const clientId = onTv ? answer.tvClientId : answer.clientId;
  migrateSession({ issuer: answer.issuer, clientId, onTv, local: window.localStorage, session: window.sessionStorage });
  return { issuer: answer.issuer, clientId };
}

export const session = createSession({ resolve });

let view = null;
const viewListeners = new Set();

function refreshView() {
  const next = viewOf(session.getSnapshot(), actionsClaim, view);
  if (next === view) return;
  view = next;
  for (const listener of [...viewListeners]) listener();
}

/** The signed-in session as the pages read it: `{ user: { id, name, username, device }, actions, stale }`, or null. */
export const sessionStore = {
  getState: () => view,
  subscribe(listener) {
    viewListeners.add(listener);
    return () => viewListeners.delete(listener);
  },
};

session.subscribe(refreshView);

export function useSession() {
  return React.useSyncExternalStore(sessionStore.subscribe, sessionStore.getState);
}

/** Whether the signed-in person holds a Cinema action, such as `cinema:download`. */
export function can(current, action) {
  return !!(current && current.actions.includes(action));
}

/** Whether the signed-in person holds a Cinema action: what a control is shown by. */
export function useCan(action) {
  return can(useSession(), action);
}

/** Whether the session has ended: the one state a page draws in place of itself. */
export const hasEnded = () => isEnded(session.getSnapshot());

export function useEnded() {
  return React.useSyncExternalStore(session.subscribe, hasEnded);
}

/** The held access token, for code that cannot wait for a promise: the player's media requests. */
export function currentToken() {
  return session.token();
}

/**
 * A good access token, renewed first when the held one is about to run out, or null with no session.
 * `refused` renews regardless of expiry, for a caller whose token the server turned down.
 */
export function freshToken({ refused = false } = {}) {
  return session.fresh({ force: refused }).catch(() => null);
}

/**
 * Holds the session the API minted for a Discord account, renewed through `renewal`, which answers
 * the next `{ token, expiresIn }`.
 */
export async function adopt(answer, renewal) {
  await config();
  session.adopt(answer, renewal);
}

/**
 * Picks up the session this browser holds, or completes the sign-in the provider sent the browser back
 * with. Answers the path the sign-in carried, or null.
 */
export function restore() {
  return session.restore();
}

/** Sends the browser to the provider to sign in, and answers false when the provider could not be asked. */
export function signIn(returnTo) {
  return session.signIn({ returnTo });
}

/**
 * Asks the provider for a code a person approves on another device, under the television's own name,
 * which the approval page offers to the person to keep or change. Answers the client's device code;
 * throws a `DeviceSignInError` whose `kind` is `unreachable` or `misconfigured`.
 */
export function startDeviceSignIn() {
  return session.device.start({ name: deviceName() });
}

/** Signs out at the provider, which ends every session minted under the person's own and comes back to the site. */
export function signOut() {
  return session.signOut();
}

/** The provider's own page for the account: its sessions, and signing out everywhere. */
export async function accountUrl() {
  const { issuer } = await config();
  return issuer.replace(/\/+$/, "") + "/account";
}
