import React from "react";
import { toast } from "@thekrystalship/krystal";
import { meApi } from "./api/index.js";

/**
 * Taking an entry off Continue watching (`POST /api/me/progress/hidden`): a show by its IMDb id, so
 * none of its episodes stands in for it, and a film by its title. The entry leaves the row at once,
 * comes back if the API refuses, and the toast saying it went puts it back. `reload` asks the list
 * again, which also keeps the answer on the device for Home with no connection.
 */
export function useContinueRemoval(reload) {
  const [removed, setRemoved] = React.useState(() => new Set());
  const keyOf = (p) => (p.showImdbId ? "show:" + p.showImdbId : "film:" + p.titleId);
  const mark = (key, on) => setRemoved((prev) => {
    const next = new Set(prev);
    if (on) next.add(key); else next.delete(key);
    return next;
  });
  const remove = async (p) => {
    const change = p.showImdbId ? { showImdbId: p.showImdbId } : { titleId: p.titleId };
    const key = keyOf(p);
    mark(key, true);
    try {
      await meApi.hideProgress({ ...change, hidden: true });
    } catch (error) {
      mark(key, false);
      toast.fromError(error, "Could not remove that");
      return;
    }
    toast.info(`${p.name} removed from Continue watching`, {
      action: {
        label: "Undo",
        onClick: () => meApi.hideProgress({ ...change, hidden: false }).then(
          () => { mark(key, false); reload(); },
          (error) => toast.fromError(error, "Could not put that back")),
      },
    });
    reload();
  };
  return { gone: (p) => removed.has(keyOf(p)), remove };
}
