// Leaving Krystal Cinema in this browser.

import { accountUrl, signOut } from "./session.js";

/** Signs out at the provider, which ends the person's sign-in there and comes back to the site. */
export function leave() {
  void signOut();
}

/** Opens the provider's page for the account, where its sessions are listed and ended everywhere. */
export function openAccount() {
  void accountUrl().then((url) => window.location.assign(url));
}
