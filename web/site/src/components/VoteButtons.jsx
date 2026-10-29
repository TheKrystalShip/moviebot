import React from "react";
import { toast, Verdict } from "@thekrystalship/krystal";
import { useOnline } from "../connectivity.js";
import { setVote, useVote } from "../votes.js";

/** Where a title's action row is a two-column grid, and the pair one cell of it (`site.css`, `.kc-tt .kc-actions`). */
const GRID_ROW = "(max-width: 720px)";

/** Whether the media query matches, following it as the window changes. */
function useMatches(query) {
  const [matches, setMatches] = React.useState(() => typeof window !== "undefined" && !!window.matchMedia && window.matchMedia(query).matches);
  React.useEffect(() => {
    if (!window.matchMedia) return undefined;
    const list = window.matchMedia(query);
    const follow = () => setMatches(list.matches);
    follow();
    list.addEventListener("change", follow);
    return () => list.removeEventListener("change", follow);
  }, [query]);
  return matches;
}

/**
 * Like and Dislike on one subject (`votes.js`), as krystal's `Verdict` at the `size` of what it sits
 * beside. The change is drawn at once and taken back with a toast if the API refuses it. `name` names
 * the subject in the group's label; `onChange(next, previous)` follows a change the API kept, `next`
 * being `0` for a cleared vote. `row` is a title's action row, where on a narrow screen the pair fills
 * its grid cell; `onArt` lays it over artwork.
 */
export function VoteButtons({ kind, id, name, size = "xl", row = false, onArt = false, className = "", onChange = null }) {
  const online = useOnline();
  const vote = useVote(kind, id);
  const grid = useMatches(GRID_ROW);
  const change = (next) => {
    setVote(kind, id, next, { name }).then(
      () => { if (onChange) onChange(next, vote); },
      (error) => toast.fromError(error, "Could not keep that vote"),
    );
  };
  return (
    <Verdict value={vote} onChange={change} size={size} grow={row && grid} onArt={onArt} disabled={!online}
      label={name ? `Your verdict on ${name}` : "Your verdict"} className={className || undefined} />
  );
}
