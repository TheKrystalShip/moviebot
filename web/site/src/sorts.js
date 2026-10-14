// The order a person last picked on each page that sorts, which follows the person: the API keeps one
// per page (`GET /api/me/sorts`), so the site, the Activity and the television open a page the way it
// was left wherever it was left. Every answer is also kept per account on the device, so a page opens
// in that order before the API answers, and with no connection.
//
// A page is named here and nowhere else in the API: the names below are the pages that sort.

import React from "react";
import { toast } from "@thekrystalship/krystal";
import { api } from "./api.js";
import { useOnline } from "./connectivity.js";
import { useSession } from "./session.js";

/** The pages that sort, by the name the API keeps each one's order under. */
export const SORTED = {
  list: "list",
  films: "library.film",
  shows: "library.show",
  shelf: "discover.shelf",
  filter: "discover.filter",
};

const KEY = "cinema.sorts.";
const heard = new Set();

function keep(sub, sorts) {
  try { localStorage.setItem(KEY + sub, JSON.stringify(sorts)); } catch { /* the next answer is kept instead */ }
  for (const listener of heard) listener();
}

function kept(sub) {
  if (!sub) return null;
  try {
    return JSON.parse(localStorage.getItem(KEY + sub) || "null");
  } catch {
    return null;
  }
}

/** Forgets an account's orders on this device. */
export function forgetSorts(sub) {
  try { localStorage.removeItem(KEY + sub); } catch { /* nothing was kept */ }
}

/** The page's own order: its first option, in that option's direction. */
const opening = (options) => ({ key: options[0].value, dir: options[0].defaultDir || "asc" });

/** A kept order the page still offers, or the page's own. */
function orderOf(sorts, surface, options) {
  const o = sorts && sorts[surface];
  return o && options.some((option) => option.value === o.key) && (o.direction === "asc" || o.direction === "desc")
    ? { key: o.key, dir: o.direction }
    : opening(options);
}

/**
 * One page's order, `[{key, dir}, change]`, for krystal's ToolbarSort: what the person last picked
 * on `surface`, else the first of `options`. It opens with the order kept on the device and takes the
 * API's when it answers, unless the person has picked one meanwhile; `change(key, dir)` shows the new
 * order at once and keeps it. With no connection a change is shown and not kept.
 */
export function useSort(surface, options) {
  const session = useSession();
  const sub = session && session.user.id;
  const online = useOnline();
  const [order, setOrder] = React.useState(() => orderOf(kept(sub), surface, options));
  const picked = React.useRef(false);

  React.useEffect(() => {
    if (!sub) return undefined;
    const update = () => { if (!picked.current) setOrder(orderOf(kept(sub), surface, options)); };
    heard.add(update);
    update();
    api.sorts().then((sorts) => keep(sub, sorts), () => {});
    return () => heard.delete(update);
    // The options are the page's own and do not change while it is open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sub, surface]);

  const change = (key, dir) => {
    picked.current = true;
    setOrder({ key, dir });
    if (!sub || !online) return;
    api.setSort(surface, { key, direction: dir }).then(
      (sorts) => keep(sub, sorts),
      (error) => { if (error.status !== 0) toast.fromError(error, "Could not keep that order"); },
    );
  };

  return [order, change];
}
