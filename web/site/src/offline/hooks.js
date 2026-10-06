// The saves on this device, for a page: read when it mounts and again whenever they change, here or
// in another tab.

import React from "react";
import { useSession } from "../session.js";
import { artworkUrl, listSaves, onSavesChanged, readSave, space } from "./store.js";

/** Reads `load` now and on every change to the saves: `{ data, error, loading }`. */
function useStoreRead(load, deps) {
  const [state, setState] = React.useState({ data: null, error: null, loading: true });
  const [tick, setTick] = React.useState(0);

  React.useEffect(() => onSavesChanged(() => setTick((t) => t + 1)), []);
  React.useEffect(() => {
    let live = true;
    load().then(
      (data) => { if (live) setState({ data, error: null, loading: false }); },
      (error) => { if (live) setState((prev) => ({ data: prev.data, error, loading: false })); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);

  return state;
}

/** The signed-in account's saves on this device, finished and under way. */
export function useSaves() {
  const session = useSession();
  const sub = session && session.user.id;
  return useStoreRead(() => listSaves(sub), [sub]);
}

/** One title's save for the signed-in account, or null when this device holds none. */
export function useSave(titleId) {
  const session = useSession();
  const sub = session && session.user.id;
  return useStoreRead(() => readSave(sub, titleId), [sub, titleId]);
}

/** What is left for saves on this device: `{ usage, quota, free }`, or null. */
export function useSpace() {
  return useStoreRead(() => space(), []);
}

/** A save's artwork, `poster` or `backdrop`, as an object URL for as long as the caller is drawn. */
export function useArtwork(save, role) {
  const [url, setUrl] = React.useState(null);
  const key = save ? save.meta.account + "/" + save.titleId + "/" + role : null;
  React.useEffect(() => {
    if (!save) return undefined;
    let live = true;
    let made = null;
    artworkUrl(save, role).then((u) => {
      made = u;
      if (live) setUrl(u);
      else if (u) URL.revokeObjectURL(u);
    }, () => {});
    return () => {
      live = false;
      if (made) URL.revokeObjectURL(made);
      setUrl(null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  return url;
}
