import React from "react";
import "./player.css";
import { Player } from "./Player.jsx";
import { createSiteEnvironment, setEnvironment } from "./engine/environment.ts";
import { createDiscordEnvironment } from "./engine/discordEnvironment.ts";
import { can, currentToken, freshToken, sessionStore } from "../session.js";
import { onTv } from "../tv/tv.js";

/** The site's signed-in person, as the player asks for them. */
const viewer = {
  token: currentToken,
  freshToken: () => freshToken(),
  identity: () => {
    const session = sessionStore.getState();
    return session ? { userId: session.user.id, displayName: session.user.name } : null;
  },
  holds: (action) => can(sessionStore.getState(), action),
};

/**
 * A room on the site, at `/r/<room>`. The room is the address and whoever is signed in is the
 * viewer; a film named on the address is put on when the room is showing nothing.
 */
export function SiteRoom({ room, title, onBack, onEmpty }) {
  const [ready] = React.useState(() => {
    setEnvironment(createSiteEnvironment(room, title, viewer, onTv));
    return true;
  });
  return ready && <Page><Player key={room} onBack={onBack} onEmpty={onEmpty} /></Page>;
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
