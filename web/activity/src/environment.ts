/**
 * The seam between the player and whatever front door it is served through.
 *
 * Everything that differs between a plain browser page and an embedded surface lives here:
 * where the API is, how a media path becomes a URL, which session this is, and who the viewer
 * is. The rest of the app asks the environment and never reads `location` or builds a URL of
 * its own, so a front door with rewritten paths and an externally supplied identity is a
 * different implementation of this interface and nothing else.
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
  /** Which room this viewer is joining. */
  sessionId(): string;
  identity(): Promise<Identity>;
}

const IdentityKey = 'moviebot.identity.v1';

function query(name: string): string | null {
  return new URLSearchParams(window.location.search).get(name);
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
 * Identity for a plain page: a name from the query string when one is given, otherwise a
 * viewer that persists across reloads so returning to a room does not double a person in the
 * participant list.
 */
function localIdentity(): Identity {
  const named = query('name');
  const asked = query('user');

  const stored = window.localStorage.getItem(IdentityKey);
  const existing = stored ? (JSON.parse(stored) as Identity) : null;

  const identity: Identity = {
    userId: asked ?? existing?.userId ?? `viewer-${Math.random().toString(36).slice(2, 8)}`,
    displayName: named ?? existing?.displayName ?? 'Viewer'
  };

  if (named || asked || !existing) {
    window.localStorage.setItem(IdentityKey, JSON.stringify(identity));
  }
  return identity;
}

export const browserEnvironment: Environment = {
  name: 'browser',
  apiUrl: (path) => `${defaultApiBase()}${path.startsWith('/') ? path : `/${path}`}`,
  mediaUrl: (titleId, relative) => `${defaultApiBase()}/media/${titleId}/${relative}`,
  hubUrl: () => `${defaultApiBase()}/hub/session`,
  sessionId: () => query('session') ?? 'lounge',
  identity: async () => localIdentity()
};

let current: Environment = browserEnvironment;

export function environment(): Environment {
  return current;
}

export function setEnvironment(next: Environment): void {
  current = next;
}
