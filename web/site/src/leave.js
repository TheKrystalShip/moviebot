// Leaving Krystal Cinema in this browser.

import { signOut } from "./session.js";

/** Signs out at the provider, which ends the person's sign-in there and comes back to the site. */
export function leave() {
  void signOut();
}
