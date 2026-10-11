/**
 * The player's own menus (`player/Menus.jsx`) aimed at a television: the view they read and the room they
 * act on, made from what the API holds for the person and the film the television is playing.
 *
 * The labels and the checked rows are the records as the API keeps them (`remoteSettings.js`), and where
 * the person chose nothing for the film, what the film starts on (`startingTracks`, the player's own rule),
 * so the phone never names a track the television is not on. The quality is the setting, Auto or a fixed
 * height; the height playing is a measurement only the television's player has, so it is never shown.
 */

import React from "react";
import { toast } from "@thekrystalship/krystal-ui";
import { api } from "./api.js";
import { qualityLevelsOf } from "./player/engine/media/ladder.ts";
import { startingTracks } from "./player/engine/media/tracks.ts";
import { refreshRemoteFilm, useRemoteFilm } from "./remoteFilm.js";
import { settingsCommands, subtitleLanguage, useRemoteSettings } from "./remoteSettings.js";
import { useRemoteRoom } from "./remoteRoom.js";

/** Runs a change and says when the TV did not take it. */
function attempt(change) {
  return Promise.resolve().then(change).catch((error) => toast.fromError(error, "The TV did not take that"));
}

/**
 * What the player's menus read of a view, for the film a television holds: its manifest, the tracks and
 * quality as the API holds them, the film's own ladder, and the person's preferences.
 */
export function useTvView() {
  const { manifest } = useRemoteFilm();
  const { state } = useRemoteRoom();
  const { preferences, tracks } = useRemoteSettings();
  return React.useMemo(() => {
    if (!manifest) return null;
    const held = preferences || {};
    const chosen = {
      audioTrackId: (tracks && tracks.audioId) || undefined,
      subtitleTrackId: tracks && tracks.subtitleOff ? null : (tracks && tracks.subtitleId) || undefined,
    };
    const starting = startingTracks(manifest, chosen, held);
    return {
      manifest, state, solo: false, viewOnly: false,
      preferences: held,
      audioId: starting.audioId,
      subtitleId: starting.subtitleId,
      quality: held.qualityHeight ?? null,
      qualityLevels: qualityLevelsOf(manifest),
      playingHeight: null,
    };
  }, [manifest, state, preferences, tracks]);
}

/** The acts the menus ask of the player's room, carried out on the API's records for the film the television holds. */
export function useTvRoom(manifest) {
  const film = manifest ? manifest.id : null;
  return React.useMemo(() => {
    return {
      selectSubtitle: (id) => attempt(() => settingsCommands.chooseSubtitle(film, id)),
      selectAudio: (id) => attempt(() => settingsCommands.chooseAudio(film, id)),
      selectQuality: (height) => attempt(() => settingsCommands.chooseQuality(height)),
      setSubtitleStyle: (style) => settingsCommands.setStyle(style),
      refreshManifest: () => refreshRemoteFilm(),
      // This person's language first, and English beside it, as the player asks.
      searchSubtitles: () => {
        const wanted = subtitleLanguage();
        return api.searchSubtitles(film, wanted ? [wanted, "en"] : ["en"]);
      },
      fetchSubtitle: async (fileId) => {
        const reply = await api.addSubtitle(film, fileId);
        await refreshRemoteFilm();
        return reply.track;
      },
    };
  }, [film]);
}
