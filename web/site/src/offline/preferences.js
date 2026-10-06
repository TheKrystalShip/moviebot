// How a person watches, kept on the device: the last `GET /api/me/preferences` (or `PUT`) answer for
// each account, so Profile's Playback and Subtitles and a film played from the device start the way
// the person watches with no connection. It is the API's answer as it was, read only while offline.

const KEY = "cinema.preferences.";

/** Keeps an account's preferences as the API answered them. */
export function keepPreferences(sub, preferences) {
  if (!sub || !preferences) return;
  try { localStorage.setItem(KEY + sub, JSON.stringify(preferences)); } catch { /* the next answer is kept instead */ }
}

/** An account's preferences as last answered on this device, or null. */
export function keptPreferences(sub) {
  if (!sub) return null;
  try {
    return JSON.parse(localStorage.getItem(KEY + sub) || "null");
  } catch {
    return null;
  }
}

/** Forgets an account's preferences on this device. */
export function forgetPreferences(sub) {
  try { localStorage.removeItem(KEY + sub); } catch { /* nothing was kept */ }
}
