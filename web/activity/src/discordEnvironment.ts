import { DiscordSDK } from '@discord/embedded-app-sdk';
import type { Environment, Identity } from './environment';

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
  user: { id: string; username: string; displayName: string };
}

export async function createDiscordEnvironment(clientId: string): Promise<Environment> {
  const sdk = new DiscordSDK(clientId);
  await sdk.ready();

  // identify alone. The Activity needs to know who is watching so the room can attribute a pause;
  // it has no business reading anyone's guilds, connections or email.
  const { code } = await sdk.commands.authorize({
    client_id: clientId,
    response_type: 'code',
    state: '',
    prompt: 'none',
    scope: ['identify']
  });

  // The secret that redeems this code lives on the server. The player only ever handles the code
  // and the token that comes back, which is its own.
  const response = await fetch('/api/auth/discord/callback', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ code })
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

  return {
    name: 'discord-activity',
    apiUrl: (path) => `${origin}${path.startsWith('/') ? path : `/${path}`}`,
    mediaUrl: (titleId, relative) => `${origin}/media/${titleId}/${relative}`,
    hubUrl: () => `${origin}/hub/session`,
    // The instance is the room: everyone Discord put in this Activity, in this voice channel,
    // shares it. It is the same idea as the bot naming a session after the channel.
    sessionId: () => sdk.instanceId,
    titleId: () => new URLSearchParams(window.location.search).get('title'),
    identity: async () => identity
  };
}
