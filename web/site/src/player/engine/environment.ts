/**
 * The seam between the player and whatever front door it is served through.
 *
 * Everything that differs between the site and the Discord Activity lives here: where the API is,
 * how a manifest-relative media path becomes a URL, which room this is, which title the launch
 * named, and who the viewer is. The rest of the player asks the environment and never reads
 * `location` or builds a URL of its own, so a front door with rewritten paths and an externally
 * supplied identity is a different implementation of this interface and nothing else.
 */
export interface Identity {
  userId: string;
  displayName: string;
}

/**
 * What this viewer is watching, for the front door to show beside their name where it can.
 * Everything here is already known to the page; the front door only puts it into words.
 */
export interface Presence {
  titleId: string;
  filmName: string;
  durationSeconds: number;
  paused: boolean;
  /** Where the room is, as of now. */
  positionSeconds: number;
  /** How many other people are in the room. */
  others: number;
  /** The poster's file name under the title's media directory, when the film has one. */
  poster: string | null;
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
  /** The address somebody else opens to join this room, or null where there is none to hand out. */
  roomUrl(): string | null;
  /** Shows what this viewer is watching, or nothing when null. A front door with nowhere to show it does nothing. */
  setPresence(presence: Presence | null): Promise<void>;
}

/** Where Krystal Cinema keeps the signed-in session. */
const SiteSessionKey = 'cinema:session';

interface SiteSession {
  token: string;
  expiresAt: number;
  user: { id: string; name: string };
}

/** The session somebody signed into the site with, while it is good. */
function siteSession(): SiteSession | null {
  try {
    const stored = JSON.parse(window.localStorage.getItem(SiteSessionKey) ?? 'null') as SiteSession | null;
    return stored && stored.expiresAt * 1000 > Date.now() ? stored : null;
  } catch {
    return null;
  }
}

/**
 * The player on Krystal Cinema. The room and the film are the address, and the viewer is whoever
 * signed in to the site: their token is the proof, and their name is what the room calls them.
 */
export function createSiteEnvironment(sessionId: string, titleId: string | null): Environment {
  const origin = window.location.origin;
  return {
    name: 'site',
    apiUrl: (path) => `${origin}${path.startsWith('/') ? path : `/${path}`}`,
    mediaUrl: (title, relative) => `${origin}/media/${title}/${relative}`,
    hubUrl: () => `${origin}/hub/session`,
    sessionId: () => sessionId,
    titleId: () => titleId,
    authToken: () => siteSession()?.token ?? null,
    identity: async () => {
      const session = siteSession();
      if (session === null) throw new Error('Nobody is signed in.');
      return { userId: session.user.id, displayName: session.user.name };
    },
    roomUrl: () => `${origin}/r/${encodeURIComponent(sessionId)}`,
    setPresence: () => Promise.resolve()
  };
}

let current: Environment | null = null;

export function environment(): Environment {
  if (current === null) throw new Error('The player has no front door yet.');
  return current;
}

export function setEnvironment(next: Environment): void {
  current = next;
}
