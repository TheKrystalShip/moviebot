import React from "react";
import { Button, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useOnline } from "../connectivity.js";
import { ANIME } from "../paths.js";
import { useCan } from "../session.js";

/**
 * Moves a film or a show between Anime and its kind's shelf, for somebody who fetches titles: the API
 * shelves a title by what the film database says of it, and this is the word of somebody who knows
 * better. `category` is where the title is now; `onMoved` takes where it went.
 */
export function ShelfButton({ imdbId, kind, category, onMoved }) {
  const allowed = useCan("cinema:download");
  const online = useOnline();
  const [busy, setBusy] = React.useState(false);
  if (!allowed || !imdbId) return null;

  const anime = category === ANIME;
  const general = kind === "show" ? "TV Shows" : "Movies";
  const move = async () => {
    setBusy(true);
    try {
      const moved = await api.setCategory(imdbId, kind, anime ? "general" : ANIME);
      toast.info(`Moved to ${moved.category === ANIME ? "Anime" : general}.`);
      onMoved(moved.category === ANIME ? ANIME : null);
    } catch (error) {
      toast.fromError(error, "Could not move it");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Button size="xl" icon={anime ? (kind === "show" ? "tv" : "clapperboard") : "swords"} disabled={busy || !online} onClick={move}>
      {anime ? `Move to ${general}` : "Move to Anime"}
    </Button>
  );
}
