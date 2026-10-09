import { Common, DiscordSDK } from '@discord/embedded-app-sdk';
import { addressWithoutLaunch } from './environment';
import type { Environment, Identity, Presence } from './environment';

/**
 * The player, as a Discord Activity.
 *
 * Only two things differ from the browser page, and they are the two the environment exists to
 * hold: the room is the Activity instance rather than a query parameter, and the viewer is the
 * account the identity provider finds linked to the Discord account in the Activity. Everything
 * else — the API, the media, the hub — is reached by the same relative paths, because the Activity
 * is served from one origin whose root mapping forwards to it.
 */

/** Discord launches an Activity with a frame id on the query string. Nothing else sets one. */
export function isDiscordActivity(): boolean {
  return new URLSearchParams(window.location.search).has('frame_id');
}

interface SignInReply {
  /** Discord's access token, which the SDK's `authenticate` takes and the next renewal sends back. */
  accessToken: string;
  /** The identity provider's token for this viewer, which is what the API and the hub take. */
  token: string;
  /** How long `token` lives, in seconds. */
  expiresIn: number;
}

/** A refusal: the reason, and the provider's word for the account when the refusal is about it. */
interface SignInRefusal {
  error?: string;
  accountStatus?: string | null;
}

/** How long before the token runs out it is renewed, in seconds. */
const RenewAheadSeconds = 60;

/**
 * Asks the API to sign this viewer in: with the SDK's code the first time, and with Discord's access
 * token before each token runs out. The API redeems the code with Cinema's Discord application and
 * exchanges the Discord token at the identity provider; nothing here reaches the provider, which
 * Discord's URL mapping would not allow.
 */
async function signIn(body: { code: string } | { accessToken: string }): Promise<SignInReply> {
  const response = await fetch('/api/auth/discord/callback', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!response.ok) {
    const refusal = (await response.json().catch(() => null)) as SignInRefusal | null;
    throw new Error(refusal?.error ?? `Signing in failed: the server answered ${response.status}.`);
  }
  return (await response.json()) as SignInReply;
}

/** The claims an access token carries, read without verifying anything: the API verifies it. */
function claimsOf(token: string): Record<string, unknown> {
  try {
    const segment = token.split('.')[1];
    const json = atob(segment.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(decodeURIComponent(Array.from(json, (c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0')).join(''))) as Record<string, unknown>;
  } catch {
    return {};
  }
}

/** Discord's activity type that renders as "Watching {name}". */
const Watching = 3;

export async function createDiscordEnvironment(
  clientId: string,
  publicBaseUrl: string | null,
  actionsClaim: string
): Promise<Environment> {
  const sdk = new DiscordSDK(clientId);
  await sdk.ready();

  // Two scopes and no more. The Activity needs to know who is watching so the room can attribute
  // a pause, and it writes what they are watching beside their name; it has no business reading
  // anyone's guilds, connections or email.
  const { code } = await sdk.commands.authorize({
    client_id: clientId,
    response_type: 'code',
    state: '',
    prompt: 'none',
    scope: ['identify', 'rpc.activities.write']
  });

  // The secret that redeems this code lives on the server. The player only ever handles the code
  // and the tokens that come back, which are its own.
  const sessionId = sdk.channelId ?? sdk.instanceId;

  let auth = await signIn({ code });
  let expiresAt = Date.now() + auth.expiresIn * 1000;
  await sdk.commands.authenticate({ access_token: auth.accessToken });

  // The token lives minutes; Discord's lives far longer, so the API is asked again with it before
  // each one runs out. A renewal that fails is tried again shortly, and the token held meanwhile is
  // good until it runs out.
  let renewing: Promise<string | null> | null = null;
  const renew = (): Promise<string | null> => {
    renewing ??= signIn({ accessToken: auth.accessToken })
      .then((fresh) => {
        auth = fresh;
        expiresAt = Date.now() + fresh.expiresIn * 1000;
        schedule();
        return fresh.token;
      })
      .catch(() => {
        window.setTimeout(() => void renew(), 15_000);
        return expiresAt > Date.now() ? auth.token : null;
      })
      .finally(() => { renewing = null; });
    return renewing;
  };
  const schedule = () => {
    window.setTimeout(() => void renew(), Math.max(5, (expiresAt - Date.now()) / 1000 - RenewAheadSeconds) * 1000);
  };
  schedule();

  const claims = claimsOf(auth.token);
  const identity: Identity = {
    userId: String(claims.sub ?? ''),
    displayName: String(claims.name ?? claims.preferred_username ?? 'Somebody')
  };

  // Software-decoding a film in the Discord webview will cook a laptop. This is the supported way
  // to ask about it, and it is advisory: nothing depends on the answer.
  void sdk.commands.encourageHardwareAcceleration().catch(() => {
    /* An older client without the command is not a reason to refuse to play a film. */
  });

  const origin = window.location.origin;

  // Where Discord's own servers can fetch a poster from. The page's origin is Discord's proxy,
  // which is reachable from inside the Activity and from nowhere else.
  const posterBase = publicBaseUrl ?? origin;

  /**
   * What the viewer is watching, put beside their name. A film playing is a bar that runs from
   * where the room is to the end of the film; a paused one says so and carries no clock. Nothing
   * here is sent again on a timer: the bar is two instants, and Discord draws the rest.
   */
  async function setPresence(presence: Presence | null): Promise<void> {
    try {
      if (presence === null) {
        await sdk.commands.setActivity({ activity: null });
        return;
      }

      const now = Date.now();
      const start = now - presence.positionSeconds * 1000;
      const others = presence.others;
      const company = others === 0 ? null : others === 1 ? 'With one other person' : `With ${others} others`;

      await sdk.commands.setActivity({
        activity: {
          type: Watching,
          details: presence.filmName,
          state: presence.paused ? 'Paused' : company,
          timestamps: presence.paused
            ? null
            : { start: Math.floor(start), end: Math.floor(start + presence.durationSeconds * 1000) },
          assets: presence.poster === null
            ? null
            : {
              large_image: `${posterBase}/media/${presence.titleId}/${presence.poster}`,
              large_text: presence.filmName
            }
        }
      });
    } catch {
      // A client that will not show it is not a reason to interrupt the film.
    }
  }

  return {
    name: 'discord-activity',
    tv: false,
    apiUrl: (path) => `${origin}${path.startsWith('/') ? path : `/${path}`}`,
    mediaUrl: (titleId, relative) => `${origin}/media/${titleId}/${relative}`,
    hubUrl: () => `${origin}/hub/session`,
    // The voice channel is the room, exactly as the bot names it. The instance id would also be
    // shared by everyone in this Activity, but the bot cannot know one — so keying on it puts the
    // two halves in different rooms and the film the bot was asked for never arrives.
    sessionId: () => sessionId,
    authToken: () => (expiresAt > Date.now() ? auth.token : null),
    freshToken: () => (expiresAt - Date.now() > 5_000 ? Promise.resolve(auth.token) : renew()),
    holds: (action) => {
      const listed = claimsOf(auth.token)[actionsClaim];
      return Array.isArray(listed) ? listed.includes(action) : listed === action;
    },
    titleId: () => new URLSearchParams(window.location.search).get('title'),
    // An Activity is launched by Discord from a URL the site never writes, so there is nowhere for a
    // position to ride.
    startAt: () => null,
    // The Activity reloads at the address it was launched at, so the launch is taken off it there.
    forgetLaunch: () => {
      const address = addressWithoutLaunch();
      if (address !== null) window.history.replaceState(window.history.state, '', address);
    },
    identity: async () => identity,
    // The page's own origin is Discord's proxy, which opens nowhere outside the Activity. The
    // public address is the site, where the same room is open to anyone signed in.
    roomUrl: () => (publicBaseUrl === null ? null : `${publicBaseUrl}/r/${encodeURIComponent(sessionId)}`),
    setPresence,
    // Discord turns its own window and asks for no press. A desktop client has nothing to turn
    // and an older one lacks the command; neither is a reason to interrupt the film.
    landscape: () => {
      const lock = (lock_state: 1 | 3) => void sdk.commands.setOrientationLockState({ lock_state }).catch(() => {});
      lock(Common.OrientationLockStateTypeObject.LANDSCAPE);
      return { enter: () => {}, release: () => lock(Common.OrientationLockStateTypeObject.UNLOCKED) };
    },
    // The Activity runs at Discord's origin, where nothing is saved: every film is the room's.
    solo: false,
    deviceCopy: () => Promise.resolve(null),
    keptPreferences: () => null,
    online: () => navigator.onLine,
    resumeAt: () => Promise.resolve(null),
    recordProgress: () => {},
    guest: null
  };
}
