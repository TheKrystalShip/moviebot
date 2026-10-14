import React from "react";
import { Button, Icon } from "@thekrystalship/krystal";
import { api, filmName } from "../api.js";
import { activityRoom } from "../discord/launch.js";
import { roomPath } from "../paths.js";
import { navigate } from "../router.js";

/** How often the call's room is asked what it holds while somebody browses. */
const EVERY_MS = 4000;

/**
 * What a room's state holds, as `{ key, titleId?, name? }`: a title, or something arriving, which has
 * a name and no title yet. The key changes whenever the room is given something else. Null for nothing.
 */
function holdingOf(state) {
  if (state && state.titleId) return { key: "title:" + state.titleId, titleId: state.titleId };
  const arriving = state && state.preparing;
  if (arriving) return { key: `arriving:${arriving.imdbId}:${arriving.season ?? ""}:${arriving.episode ?? ""}`, name: arriving.name };
  return null;
}

/**
 * The call's room while somebody in the Activity browses the site: what it holds, named, or null.
 *
 * The call watches together, so the room being given something else takes this viewer to the player,
 * wherever they are on the site. What the room held when they started browsing does not: somebody who
 * left the film to look around stays where they are until the call moves on. The room is read rather
 * than joined, so somebody browsing is never counted as watching. A room private to this viewer
 * answers nothing, and they meet its door when they press Watch.
 */
function useCallRoom() {
  const [holding, setHolding] = React.useState(null);

  React.useEffect(() => {
    let live = true;
    let timer = 0;
    // Undefined until the room first answers: what it holds then is where browsing started.
    let seen;
    const names = new Map();

    const nameOf = async (titleId) => {
      if (!names.has(titleId)) names.set(titleId, api.title(titleId).then(filmName, () => null));
      return names.get(titleId);
    };

    const look = async () => {
      if (document.visibilityState === "visible") {
        let held;
        try {
          held = holdingOf((await api.room(activityRoom)).state);
        } catch {
          held = undefined;
        }
        if (!live) return;
        if (held !== undefined) {
          const key = held ? held.key : null;
          if (seen !== undefined && key !== null && key !== seen) {
            navigate(roomPath(activityRoom));
            return;
          }
          seen = key;
          const name = held && (held.name || await nameOf(held.titleId));
          if (!live) return;
          setHolding(held ? { key: held.key, name } : null);
        }
      }
      timer = window.setTimeout(look, EVERY_MS);
    };
    void look();
    return () => {
      live = false;
      window.clearTimeout(timer);
    };
  }, []);

  return holding;
}

/** Above every page in the Discord Activity, while the call is watching something: what, and the way back to it. */
export function CallBar() {
  const holding = useCallRoom();
  if (!holding) return null;
  return (
    <div className="kc-callbar">
      <div className="kc-callbar__bar">
        <Icon name="clapperboard" size={16} />
        <span className="kc-callbar__label">In this call</span>
        {holding.name && <span className="kc-callbar__name">{holding.name}</span>}
        <Button size="sm" variant="primary" icon="play" onClick={() => navigate(roomPath(activityRoom))}>Watch</Button>
      </div>
    </div>
  );
}
