import { askForDisplayName } from './ui/nameGate';

/**
 * The seam between the player and whatever front door it is served through.
 *
 * Everything that differs between a plain browser page and an embedded surface lives here:
 * where the API is, how a media path becomes a URL, which room this is, which title the launch
 * named, and who the viewer is. The rest of the app asks the environment and never reads
 * `location` or builds a URL of its own, so a front door with rewritten paths and an externally
 * supplied identity is a different implementation of this interface and nothing else.
 */
export interface Identity {
  userId: string;
  displayName: string;
}

export interface Environment {
  readonly name: string;
  /** Absolute URL for an API path such as `/api/titles`. */
  apiUrl(path: string): string;
  /** Absolute URL for a manifest-relative media path such as `v0/index.m3u8`. */
  mediaUrl(titleId: string, relative: string): string;
  /** Absolute URL of the SignalR hub. */
  hubUrl(): string;
  /** Which room this viewer is joining. Opaque: never parsed, never validated. */
  sessionId(): string;
  /** The title the launch named, or null when nothing was named. */
  titleId(): string | null;
  /**
   * What this viewer presents to the server as proof they came through Discord, or null when
   * there is nothing to present. Every closed route refuses a request without one.
   */
  authToken(): string | null;
  identity(): Promise<Identity>;
}

const IdentityKey = 'moviebot.identity.v1';

function query(name: string): string | null {
  const value = new URLSearchParams(window.location.search).get(name);
  return value === null || value === '' ? null : value;
}

function token(): string {
  return crypto.randomUUID().replaceAll('-', '').slice(0, 10);
}

function defaultApiBase(): string {
  const override = query('api');
  if (override) return override.replace(/\/+$/, '');

  const configured = import.meta.env.VITE_API_BASE as string | undefined;
  if (configured) return configured.replace(/\/+$/, '');

  // The dev server serves the page but not the API, so point at the API's own address.
  if (window.location.port === '5173') return 'http://127.0.0.1:8099';

  return window.location.origin;
}

/**
 * The room, from the launch link. A page opened without one names its own and writes it into the
 * address bar, so the person who opened it has a link to hand to somebody else.
 */
function resolveSessionId(): string {
  const named = query('session');
  if (named !== null) return named;

  const generated = `room-${token()}`;
  const url = new URL(window.location.href);
  url.searchParams.set('session', generated);
  window.history.replaceState(null, '', url.toString());
  return generated;
}

/**
 * Who this viewer is. The link deliberately carries no identity, so the name is asked for once
 * and kept in this browser along with a stable id.
 */
async function localIdentity(): Promise<Identity> {
  const stored = window.localStorage.getItem(IdentityKey);
  if (stored !== null) {
    try {
      const identity = JSON.parse(stored) as Identity;
      if (identity.userId && identity.displayName) return identity;
    } catch {
      // A corrupt entry is worth no more than an absent one.
    }
  }

  const identity: Identity = { userId: `viewer-${token()}`, displayName: await askForDisplayName() };
  window.localStorage.setItem(IdentityKey, JSON.stringify(identity));
  return identity;
}

export const browserEnvironment: Environment = {
  name: 'browser',
  // A plain page has no way to prove anyone came through Discord, so it holds nothing and every
  // closed route refuses it. It exists to say so.
  authToken: () => null,
  apiUrl: (path) => `${defaultApiBase()}${path.startsWith('/') ? path : `/${path}`}`,
  mediaUrl: (titleId, relative) => `${defaultApiBase()}/media/${titleId}/${relative}`,
  hubUrl: () => `${defaultApiBase()}/hub/session`,
  sessionId: resolveSessionId,
  titleId: () => query('title'),
  identity: localIdentity
};

let current: Environment = browserEnvironment;

export function environment(): Environment {
  return current;
}

export function setEnvironment(next: Environment): void {
  current = next;
}
