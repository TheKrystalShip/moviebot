// The signed-in person as the site's pages read them, made from the sign-in client's snapshot.

/**
 * The signed-in session, `{ user: { id, name, username, device }, actions, stale }`, or null when nobody
 * is signed in, the session is restoring, or it has ended. A session the client is recovering is still
 * somebody signed in: `stale` marks it and nothing else about it changes. `previous` is the last view,
 * answered again when nothing a page draws has changed, so a renewal re-renders nothing.
 */
export function viewOf(snapshot, actionsClaim, previous = null) {
  const account = snapshot.account;
  if (!account || (snapshot.status !== "live" && snapshot.status !== "stale")) return null;
  const listed = actionsClaim ? snapshot.claims[actionsClaim] : null;
  const next = {
    user: {
      id: account.id,
      name: account.name,
      username: account.username,
      // A television's name as its session at the provider carries it, which its owner may have changed.
      device: snapshot.claims.tks_device_name || null,
    },
    actions: Array.isArray(listed) ? listed : typeof listed === "string" ? [listed] : [],
    stale: snapshot.status === "stale",
  };
  return previous && same(previous, next) ? previous : next;
}

function same(a, b) {
  return a.stale === b.stale
    && a.user.id === b.user.id && a.user.name === b.user.name
    && a.user.username === b.user.username && a.user.device === b.user.device
    && a.actions.length === b.actions.length && a.actions.every((action, i) => action === b.actions[i]);
}

/** Whether a snapshot is the end of a session nothing can recover: the one state a page draws. */
export function isEnded(snapshot) {
  return snapshot.status === "ended";
}
