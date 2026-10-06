import React from "react";
import "./player.css";
import { Player } from "./Player.jsx";
import { createSiteEnvironment, setEnvironment } from "./engine/environment.ts";
import { createDiscordEnvironment } from "./engine/discordEnvironment.ts";
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
 * way, from where it had got to, when the connection goes.
 */
export function SiteRoom({ room, title, offline = false, onBack, onEmpty }) {
  const online = useOnline();
  const [ready] = React.useState(() => {
    setEnvironment(createSiteEnvironment(room, title, viewer, onTv, offline));
    return true;
  });
  // What was watched alone goes to the API as soon as the film is left, when there is a connection
  // to send it on; after the player's own last record of it.
  React.useEffect(() => (offline ? () => { window.setTimeout(() => void sendProgress(), 0); } : undefined), [offline]);
  const carryOn = (titleId, at) => navigate(playSavedPath(titleId, at), { replace: true });
  return ready && <Page><Player key={room} online={online} onBack={onBack} onEmpty={onEmpty} onCarryOn={carryOn} /></Page>;
}

/**
 * The Discord Activity, at `/embed/`. The room is the voice channel and the viewer is the account
 * linked to the Discord account in it, so nothing is known until the Embedded App SDK and the sign-in
 * have answered; a failure to sign in, an account waiting for approval among them, is said in place
 * of the film rather than falling back to anything.
 */
export function EmbedRoom() {
  const [state, setState] = React.useState({ ready: false, error: null });

  React.useEffect(() => {
    let live = true;
    (async () => {
      const response = await fetch("/api/config");
      if (!response.ok) throw new Error(`The server would not say which application this is (${response.status}).`);
      const { discordClientId, publicBaseUrl, actionsClaim } = await response.json();
      if (!discordClientId) throw new Error("This server has no Discord application configured.");
      setEnvironment(await createDiscordEnvironment(discordClientId, publicBaseUrl ?? null, actionsClaim));
      if (live) setState({ ready: true, error: null });
    })().catch((error) => {
      if (live) setState({ ready: false, error: error instanceof Error ? error.message : "Discord sign-in failed." });
    });
    return () => { live = false; };
  }, []);

  if (state.error) return <Page message><span>{state.error}</span></Page>;
  if (!state.ready) return <Page />;
  return <Page><Player embed /></Page>;
}

/** A film is watched in the dark, whatever the site's theme is. */
function Page({ message = false, children }) {
  return <div className={"mp-page" + (message ? " mp-page--message" : "")} data-theme="dark">{children}</div>;
}
