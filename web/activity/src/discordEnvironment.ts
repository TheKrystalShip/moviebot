import { DiscordSDK } from '@discord/embedded-app-sdk';
import type { Environment, Identity, Presence } from './environment';

/**
 * The player, as a Discord Activity.
 *
 * Only two things differ from the browser page, and they are the two the environment exists to
 * hold: the room is the Activity instance rather than a query parameter, and the viewer is who
 * Discord says they are rather than a name they typed. Everything else — the API, the media, the
 * hub — is reached by the same relative paths, because the Activity is served from one origin
 * whose root mapping forwards to it.
 */

/** Discord launches an Activity with a frame id on the query string. Nothing else sets one. */
export function isDiscordActivity(): boolean {
  return new URLSearchParams(window.location.search).has('frame_id');
}

interface AuthResult {
  accessToken: string;
  /** What this server accepts afterwards. Discord's own token proves nothing to it. */
  roomToken: string;
  user: { id: string; username: string; displayName: string };
}

/** Discord's activity type that renders as "Watching {name}". */
const Watching = 3;

export async function createDiscordEnvironment(
  clientId: string,
  publicBaseUrl: string | null
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
  // and the token that comes back, which is its own.
  const sessionId = sdk.channelId ?? sdk.instanceId;

  const response = await fetch('/api/auth/discord/callback', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ code, sessionId })
  });

  if (!response.ok) {
    throw new Error(`Discord sign-in failed: the server answered ${response.status}.`);
  }

  const auth = (await response.json()) as AuthResult;
  await sdk.commands.authenticate({ access_token: auth.accessToken });

  const identity: Identity = {
    userId: auth.user.id,
    displayName: auth.user.displayName
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
    apiUrl: (path) => `${origin}${path.startsWith('/') ? path : `/${path}`}`,
    mediaUrl: (titleId, relative) => `${origin}/media/${titleId}/${relative}`,
    hubUrl: () => `${origin}/hub/session`,
    // The voice channel is the room, exactly as the bot names it. The instance id would also be
    // shared by everyone in this Activity, but the bot cannot know one — so keying on it puts the
    // two halves in different rooms and the film the bot was asked for never arrives.
    sessionId: () => sessionId,
    authToken: () => auth.roomToken,
    titleId: () => new URLSearchParams(window.location.search).get('title'),
    identity: async () => identity,
    setPresence
  };
}
