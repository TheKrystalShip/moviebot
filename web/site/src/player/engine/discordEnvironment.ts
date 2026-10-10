import { Common } from '@discord/embedded-app-sdk';
import type { DiscordSDK } from '@discord/embedded-app-sdk';
import { createSiteEnvironment } from './environment';
import type { Environment, Presence, SiteViewer } from './environment';

/**
 * The player, in the Discord Activity.
 *
 * The Activity is the site, so the player is the site's: the same viewer, signed in through Discord,
 * and the same relative paths, which Discord's proxy forwards to this origin. Three things differ.
 * The room is always the channel the Activity runs in, so everybody in the call watches together.
 * What the viewer is watching is written beside their name in Discord. And Discord turns its own
 * window sideways on a phone.
 */
export interface DiscordActivity {
  sdk: DiscordSDK;
  room: string;
  /** Where Discord's own servers fetch a poster from, or null when the server names no public address. */
  publicBaseUrl: string | null;
}

/** Discord's activity type that renders as "Watching {name}". */
const Watching = 3;

export function createDiscordEnvironment(
  activity: DiscordActivity, titleId: string | null, viewer: SiteViewer, replaceAddress: (address: string) => void
): Environment {
  const { sdk, room, publicBaseUrl } = activity;
  const site = createSiteEnvironment(room, titleId, viewer, replaceAddress);

  // The page's origin is Discord's proxy, which is reachable from inside the Activity and from
  // nowhere else, so a poster Discord shows comes from the public address.
  const posterBase = publicBaseUrl ?? window.location.origin;

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
    ...site,
    name: 'discord-activity',
    // The public address is the site, where the same room is open to anyone signed in.
    roomUrl: () => (publicBaseUrl === null ? null : `${publicBaseUrl}/r/${encodeURIComponent(room)}`),
    setPresence,
    // Discord turns its own window and asks for no press. A desktop client has nothing to turn
    // and an older one lacks the command; neither is a reason to interrupt the film.
    landscape: () => {
      const lock = (lock_state: 1 | 3) => void sdk.commands.setOrientationLockState({ lock_state }).catch(() => {});
      lock(Common.OrientationLockStateTypeObject.LANDSCAPE);
      return { enter: () => {}, release: () => lock(Common.OrientationLockStateTypeObject.UNLOCKED) };
    }
  };
}
