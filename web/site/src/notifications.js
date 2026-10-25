// This person's notifications, as the bell and the profile's settings show them: the shared routes
// under /api/me, read every minute and whenever the page comes back into view, so a push that arrived
// while the app was closed is in the bell the moment it is opened.

import { createNotificationInbox, createNotificationsClient } from "@thekrystalship/krystal";
import { meApi } from "./api/index.js";

/** The calls of the notification routes, signed in through the site's api module. */
export const notificationsClient = createNotificationsClient({ request: meApi.request });

/** The bell's state, kept fresh while any component reading it is mounted. */
export const inbox = createNotificationInbox(notificationsClient);
