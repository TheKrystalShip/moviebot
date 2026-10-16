// The browser storage the sign-in client starts from.
//
// A television's stored session is the same oidc-client-ts user the sign-in client keeps, so it is
// copied under the client's own key and carries on without a new code. Everything else under the
// site's own storage prefix is a website session or a sign-in in flight, which nothing reads, and
// is removed.

const SITE_PREFIX = "cinema.oidc.";

/** The key the sign-in client stores a client's user under. */
export const userKey = (issuer, clientId) => `tks-auth-client:${clientId}:user:${issuer}:${clientId}`;

function keysWith(store, prefix) {
  const keys = [];
  for (let i = 0; i < store.length; i += 1) {
    const key = store.key(i);
    if (key && key.startsWith(prefix)) keys.push(key);
  }
  return keys;
}

/**
 * Moves a television's stored user to the sign-in client's key unless the client already holds one,
 * then removes every key under the site's prefix from both storages. `onTv` is whether this page is
 * the television's, whose client is `clientId`.
 */
export function migrateSession({ issuer, clientId, onTv, local, session }) {
  try {
    if (onTv) {
      const old = local.getItem(`${SITE_PREFIX}user:${issuer}:${clientId}`);
      const key = userKey(issuer, clientId);
      if (old && !local.getItem(key)) local.setItem(key, old);
    }
    for (const key of keysWith(local, SITE_PREFIX)) local.removeItem(key);
    for (const key of keysWith(session, SITE_PREFIX)) session.removeItem(key);
  } catch {
    // Storage is unavailable; the sign-in client finds nothing and signs in afresh.
  }
}
