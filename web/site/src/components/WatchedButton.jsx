import React from "react";
import { Button, toast } from "@thekrystalship/krystal";
import { meApi } from "../api/index.js";
import { useOnline } from "../connectivity.js";

/**
 * Saying something was watched, here or anywhere, or taking it back (`POST /api/me/watched`). The
 * answer is drawn at once and taken back if the API refuses it; `onChange` follows one it accepted,
 * for a page that reads again what the change moved.
 */
export function useWatchedChange(watched, onChange = null) {
  const [shown, setShown] = React.useState(watched);
  const [busy, setBusy] = React.useState(false);
  React.useEffect(() => setShown(watched), [watched]);

  const change = async (request, failure) => {
    setBusy(true);
    setShown(request.watched);
    try {
      await meApi.setWatched(request);
      if (onChange) onChange(request.watched);
    } catch (error) {
      setShown(watched);
      toast.fromError(error, failure);
    } finally {
      setBusy(false);
    }
  };

  return { shown, busy, change };
}

/** A film watched, or not: one press says which. */
export function WatchedButton({ imdbId, watched, onChange = null }) {
  const online = useOnline();
  const { shown, busy, change } = useWatchedChange(Boolean(watched), onChange);
  return (
    <Button size="xl" variant={shown ? "accent" : undefined} icon={shown ? "circle-check" : "circle"} aria-pressed={shown}
      disabled={busy || !online}
      onClick={() => change({ imdbId, kind: "film", watched: !shown }, "Could not change that")}>
      {shown ? "Watched" : "Mark watched"}
    </Button>
  );
}
