import { Avatar, Tooltip } from "@thekrystalship/krystal";
import { api } from "../api.js";
import { useLoad } from "../data.js";

/**
 * The people with a visible want on a title, as faces: their initials, their name in a tooltip and as
 * the face's label. The asking person is left out by the API, and a title nobody else wants draws
 * nothing. A want kept to its person is never named.
 */
export function Wanters({ imdbId }) {
  const wanters = useLoad(() => (imdbId ? api.wanters(imdbId).catch(() => []) : Promise.resolve([])), [imdbId]);
  return <Faces people={wanters.data || []} />;
}

/** People as faces, as the API names them (`userId`, `name`); nothing for none. */
export function Faces({ people }) {
  if (people.length === 0) return null;
  return (
    <div className="kc-wanters" role="list">
      {people.map((p) => {
        const name = p.name || "Someone";
        return (
          <Tooltip key={p.userId} label={name}>
            <span role="listitem" className="kc-wanters__face" tabIndex={0} aria-label={name}>
              <Avatar name={name} tone="muted" size="css" className="kc-wanters__avatar" />
            </span>
          </Tooltip>
        );
      })}
    </div>
  );
}
