import React from "react";
import { Button, Input } from "@thekrystalship/krystal-ui";
import "./player.css";
import { Player } from "./Player.jsx";
import { createSiteEnvironment, setEnvironment } from "./engine/environment.ts";
import { createDiscordEnvironment } from "./engine/discordEnvironment.ts";
import { activity } from "../discord/activity.js";
import { inActivity } from "../discord/launch.js";
import { createGuestEnvironment, keptGuestName } from "./engine/guestEnvironment.ts";
import { can, currentToken, freshToken, sessionStore } from "../session.js";
import { isOnline, useOnline } from "../connectivity.js";
import { offlineSupported } from "../offline/store.js";
import { keptPreferences } from "../offline/preferences.js";
import { logProgress, resumeAt, sendProgress } from "../offline/progress.js";
import { navigate } from "../router.js";
import { playSavedPath } from "../paths.js";
import { onTv } from "../tv/tv.js";
import { deviceCopy } from "./deviceCopy.js";

const signedIn = () => {
  const session = sessionStore.getState();
  return session ? session.user.id : null;
};

/** The site's signed-in person, and what this device keeps for them, as the player asks for them. */
const viewer = {
  token: currentToken,
  freshToken: () => freshToken(),
  identity: () => {
    const session = sessionStore.getState();
    return session ? { userId: session.user.id, displayName: session.user.name } : null;
  },
  holds: (action) => can(sessionStore.getState(), action),
  online: isOnline,
  deviceCopy: (titleId) => (signedIn() && offlineSupported() ? deviceCopy(signedIn(), titleId) : Promise.resolve(null)),
  keptPreferences: () => keptPreferences(signedIn()),
  resumeAt: (titleId, durationSeconds) => resumeAt(signedIn(), titleId, durationSeconds),
  recordProgress: (titleId, positionSeconds) => logProgress(signedIn(), titleId, positionSeconds),
};

/**
 * A room on the site, at `/r/<room>`. The room is the address and whoever is signed in is the
 * viewer; a film named on the address is put on when the room is showing nothing. With `offline` the
 * address plays its film from the device alone, and a room whose film is saved here carries on that
 * way, from where it had got to, when the connection goes. In the Discord Activity the room is the
 * channel the Activity runs in (`engine/discordEnvironment.ts`).
 */
export function SiteRoom({ room, title, offline = false, onBack, onEmpty }) {
  const online = useOnline();
  const [ready] = React.useState(() => {
    const replace = (address) => navigate(address, { replace: true });
    setEnvironment(inActivity
      ? createDiscordEnvironment(activity(), title, viewer, replace)
      : createSiteEnvironment(room, title, viewer, replace, onTv, offline));
    return true;
  });
  // What was watched alone goes to the API as soon as the film is left, when there is a connection
  // to send it on; after the player's own last record of it.
  React.useEffect(() => (offline ? () => { window.setTimeout(() => void sendProgress(), 0); } : undefined), [offline]);
  const carryOn = (titleId, at) => navigate(playSavedPath(titleId, at), { replace: true });
  return ready && <Page><Player key={room} online={online} onBack={onBack} onEmpty={onEmpty} onCarryOn={carryOn} /></Page>;
}

/**
 * A room's guest link, at `/g/<code>`. Nobody signs in: the link is the proof. The page says what the
 * room is watching and asks the name to watch it as, and the press that enters is also the one the
 * browser wants before it plays anything. Once in, the player is all there is, with no way back to
 * the site and nothing that moves the room.
 */
export function GuestRoom({ code }) {
  const online = useOnline();
  // Undefined while asked, null for a link that is no longer good, false when the server did not answer.
  const [invitation, setInvitation] = React.useState(undefined);
  const [name, setName] = React.useState(keptGuestName);
  const [entered, setEntered] = React.useState(false);

  React.useEffect(() => {
    let live = true;
    fetch(`/api/guest/${encodeURIComponent(code)}`, { headers: { accept: "application/json" } })
      .then((response) => (response.ok ? response.json() : response.status === 404 ? null : false))
      .then((found) => { if (live) setInvitation(found); }, () => { if (live) setInvitation(false); });
    return () => { live = false; };
  }, [code]);

  if (entered) return <Page><Player online={online} /></Page>;
  if (invitation === undefined) return <Page />;
  if (invitation === null) return <Page message><span>This invite has ended.</span></Page>;
  if (invitation === false) return <Page message><span>The room can't be reached. Try again in a moment.</span></Page>;

  const enter = (e) => {
    e.preventDefault();
    setEnvironment(createGuestEnvironment(code, invitation, name.trim(), isOnline));
    setEntered(true);
  };
  return (
    <Page>
      <form className="mp-arrive" onSubmit={enter}>
        <span className="mp-arrive__eyebrow">Invited to watch</span>
        <span className="mp-arrive__name">{invitation.name || "Krystal Cinema"}</span>
        <div className="mp-arrive__form">
          <Input size="lg" wide autoFocus value={name} maxLength={32} onChange={(e) => setName(e.target.value)}
            placeholder="Your name" aria-label="Your name" />
          <Button type="submit" variant="primary" size="xl" icon="play" wide>Watch</Button>
        </div>
      </form>
    </Page>
  );
}

/** A film is watched in the dark, whatever the site's theme is. */
function Page({ message = false, children }) {
  return <div className={"mp-page" + (message ? " mp-page--message" : "")} data-theme="dark">{children}</div>;
}
