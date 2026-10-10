// Watch now, as every page starts it: the API answers with the room straight away and the page goes
// there, where the room shows the film being found, prepared and starting. A film that can already be
// played goes to the room with the film in it.

import { toast } from "@thekrystalship/krystal-ui";
import { api } from "./api.js";
import { navigate } from "./router.js";
import { activityRoom } from "./discord/launch.js";

/**
 * Starts watching and goes to the room; false when it could not start, which is said. In the Discord
 * Activity the room is the call's.
 */
export async function watchNow(request, { limit } = {}) {
  try {
    const reply = await api.watchNow(activityRoom ? { ...request, room: activityRoom } : request);
    if (reply.outcome === "full") {
      toast.info(`The server is preparing ${limit ? `${limit} films` : "as many films as it can"} for people watching right now. Try again in a few minutes.`);
      return false;
    }
    if (!reply.room) {
      toast.error("That could not be started.");
      return false;
    }
    navigate(`/r/${encodeURIComponent(reply.room)}` + (reply.libraryId ? `?title=${encodeURIComponent(reply.libraryId)}` : ""));
    return true;
  } catch (error) {
    toast.fromError(error, "That could not be started");
    return false;
  }
}

/** The release Watch now picks by default for a film: 1080p where it is offered. */
export function defaultOffer(availability) {
  return (availability && (availability.hd1080 || availability.hd720)) || null;
}

/**
 * The release an episode plays from, out of a show's offers (`api.showReleases`): its season's pack at
 * `quality` where there is one, so a season stays one torrent, then the episode's own release at that
 * quality, then whatever else holds it. Null when nothing offered holds it.
 */
export function episodeRelease(offers, season, number, quality = "720p") {
  const so = offers && offers.seasons.find((x) => x.season === season);
  if (!so) return null;
  const own = so.episodes.find((x) => x.episode === number);
  return so.packs.find((p) => p.resolution === quality)
    || (own && own.releases.find((r) => r.resolution === quality))
    || (own && own.releases[0])
    || so.packs[0]
    || null;
}

/** A show's first episode, which Watch starts when nothing of the show is here. */
export function openingEpisode(show) {
  return show.seasons.filter((x) => x.number >= 1 && x.episodes.length > 0).map((x) => x.episodes[0])[0] || null;
}

/**
 * Watches a show's first episode while it arrives, at 720p out of whatever the tracker offers for its
 * season; says so when the tracker has nothing of that season to watch.
 */
export async function watchOpening(show) {
  const opening = openingEpisode(show);
  if (!opening) return;
  try {
    const offers = await api.showReleases(show.imdbId);
    const picked = episodeRelease(offers, opening.season, opening.number);
    if (!picked) {
      toast.info(`The tracker has nothing of ${show.title} Season ${opening.season} to watch yet.`);
      return;
    }
    await watchNow({ imdbId: show.imdbId, torrentId: picked.torrentId, episodes: [{ season: opening.season, episode: opening.number }] });
  } catch (error) {
    toast.fromError(error, "That could not be started");
  }
}
