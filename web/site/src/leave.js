// Leaving Krystal Cinema in this browser.

import { accountUrl, signOut } from "./session.js";
import { signInPath } from "./paths.js";
import { navigate } from "./router.js";

/**
 * Forgets this browser's session and shows the door. The provider stays signed in, so going back
 * through it signs the person in again without a prompt.
 */
export function leave() {
  void signOut().then(() => navigate(signInPath, { replace: true }));
}

/** Opens the provider's page for the account, where its sessions are listed and ended everywhere. */
export function openAccount() {
  void accountUrl().then((url) => window.location.assign(url));
}
