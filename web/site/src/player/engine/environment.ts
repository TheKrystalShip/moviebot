/**
 * The seam between the player and whatever front door it is served through.
 *
 * Everything that differs between the site and the Discord Activity lives here: where the API is,
 * how a manifest-relative media path becomes a URL, which room this is, which title the launch
 * named, who the viewer is, and how the film is held sideways on a phone. The rest of the player asks the environment and never reads
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

/**
 * The film held sideways on a phone while the player is open. A browser gives a page the whole
 * screen and its orientation only in answer to a press, so `enter` is called from one; a front door
 * that needs no press holds it from the start and `enter` does nothing.
 */
export interface Landscape {
  enter(): void;
  release(): void;
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
   * Where in the launched film it starts when the launch put it into an empty room, in seconds, or
   * null to start at the beginning. A room already holding a film ignores it.
   */
  startAt(): number | null;
  /**
   * The viewer's access token, the one the identity provider minted for them, while it is good, or
   * null when there is nothing to present. Every closed route refuses a request without one.
   */
  authToken(): string | null;
  /** The viewer's access token, renewed first when it is about to run out. */
  freshToken(): Promise<string | null>;
  /**
   * Whether the viewer's token lists a Cinema action, such as `cinema:download`: what a control is
   * shown by. The API checks every one again.
   */
  holds(action: string): boolean;
  identity(): Promise<Identity>;
  /** The address somebody else opens to join this room, or null where there is none to hand out. */
  roomUrl(): string | null;
  /** Shows what this viewer is watching, or nothing when null. A front door with nowhere to show it does nothing. */
  setPresence(presence: Presence | null): Promise<void>;
  /** Holds `frame` sideways on a phone; the player releases it when it closes. */
  landscape(frame: HTMLElement): Landscape;
}

type LockableOrientation = ScreenOrientation & { lock?(orientation: string): Promise<void> };

/**
 * On a phone's browser the frame takes the whole screen and the screen turns sideways, the one
 * order Chrome accepts: an orientation is locked only for a page that is full screen. A browser
 * that grants neither, which is every browser on an iPhone, leaves the turning to the person.
 * Leaving full screen lets go of the orientation with it, and the next press takes both back.
 */
function browserLandscape(frame: HTMLElement): Landscape {
  const phone = window.matchMedia('(pointer: coarse)').matches;
  return {
    enter: () => {
      if (!phone || !document.fullscreenEnabled || document.fullscreenElement !== null) return;
      frame.requestFullscreen({ navigationUI: 'hide' })
        .then(() => (screen.orientation as LockableOrientation).lock?.('landscape'))
        .catch(() => { /* A refusal leaves the film where it is, which is still watchable. */ });
    },
    release: () => {
      if (document.fullscreenElement === frame) void document.exitFullscreen().catch(() => {});
    }
  };
}

/** Whoever is signed in to the site, as the site's session holds them. */
export interface SiteViewer {
  token(): string | null;
  freshToken(): Promise<string | null>;
  identity(): Identity | null;
  holds(action: string): boolean;
}

/**
 * The player on Krystal Cinema. The room and the film are the address, and the viewer is whoever
 * signed in to the site: their token is the proof, and the name it carries is what the room calls them.
 */
export function createSiteEnvironment(sessionId: string, titleId: string | null, viewer: SiteViewer): Environment {
  const origin = window.location.origin;
  return {
    name: 'site',
    apiUrl: (path) => `${origin}${path.startsWith('/') ? path : `/${path}`}`,
    mediaUrl: (title, relative) => `${origin}/media/${title}/${relative}`,
    hubUrl: () => `${origin}/hub/session`,
    sessionId: () => sessionId,
    titleId: () => titleId,
    startAt: () => {
      const seconds = Number(new URLSearchParams(window.location.search).get('at'));
      return Number.isFinite(seconds) && seconds > 0 ? seconds : null;
    },
    authToken: () => viewer.token(),
    freshToken: () => viewer.freshToken(),
    holds: (action) => viewer.holds(action),
    identity: async () => {
      const identity = viewer.identity();
      if (identity === null) throw new Error('Nobody is signed in.');
      return identity;
    },
    roomUrl: () => `${origin}/r/${encodeURIComponent(sessionId)}`,
    setPresence: () => Promise.resolve(),
    landscape: browserLandscape
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
