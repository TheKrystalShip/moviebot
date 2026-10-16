import { browserLandscape, type Environment } from './environment';
import type { GuestInvitation, ViewerPreferences } from './types';

/** Where this browser keeps how a guest watches, and the name they last gave. */
const PreferencesKey = 'moviebot.guest.preferences.v1';
const NameKey = 'moviebot.guest.name.v1';

/** The name this browser last joined a room as, or an empty string. */
export function keptGuestName(): string {
  try {
    return window.localStorage.getItem(NameKey) ?? '';
  } catch {
    return '';
  }
}

function keepGuestName(name: string): void {
  try {
    window.localStorage.setItem(NameKey, name);
  } catch {
    /* Asked for again next time. */
  }
}

function keptPreferences(): ViewerPreferences {
  try {
    const raw = window.localStorage.getItem(PreferencesKey);
    return raw ? (JSON.parse(raw) as ViewerPreferences) : {};
  } catch {
    return {};
  }
}

function keepPreferences(preferences: ViewerPreferences): void {
  try {
    window.localStorage.setItem(PreferencesKey, JSON.stringify(preferences));
  } catch {
    /* This film still plays as chosen; the next one starts on the film's defaults. */
  }
}

/**
 * The player on a room's guest link, at `/g/<code>`. The room is the one the link opens and the viewer
 * is nobody: the link is the only proof, carried wherever a token would be, and it opens that room's
 * film and nothing else. No launch, no saved films, no presence, no link to hand on, and every Cinema
 * action is somebody else's.
 */
export function createGuestEnvironment(
  code: string, invitation: GuestInvitation, name: string, online: () => boolean
): Environment {
  const origin = window.location.origin;
  const credential = `guest.${code}`;
  keepGuestName(name);
  return {
    name: 'guest',
    tv: false,
    apiUrl: (path) => `${origin}${path.startsWith('/') ? path : `/${path}`}`,
    mediaUrl: (title, relative) => `${origin}/media/${title}/${relative}`,
    hubUrl: () => `${origin}/hub/session`,
    sessionId: () => invitation.sessionId,
    titleId: () => null,
    startAt: () => null,
    forgetLaunch: () => {},
    authToken: () => credential,
    freshToken: () => Promise.resolve(credential),
    holds: () => false,
    // Who the room has them as arrives when they join; until then they are the name they gave.
    identity: () => Promise.resolve({ userId: 'guest', displayName: name }),
    roomUrl: () => null,
    setPresence: () => Promise.resolve(),
    landscape: browserLandscape,
    solo: false,
    deviceCopy: () => Promise.resolve(null),
    keptPreferences: () => null,
    online,
    resumeAt: () => Promise.resolve(null),
    recordProgress: () => {},
    sendProgress: () => {},
    aheadStore: () => null,
    guest: {
      name,
      preferences: keptPreferences,
      keepPreferences
    }
  };
}
