import React from "react";
import { Drawer, IconButton, MenuGroup, MenuItem, MenuSeparator, toast } from "@thekrystalship/krystal";
import { useOnline } from "../connectivity.js";
import { useLongPress } from "../longPress.js";
import { navigate } from "../router.js";
import { setVote, useVote } from "../votes.js";
import { kindWord } from "./TitleHero.jsx";
import { VoteButtons } from "./VoteButtons.jsx";
import { useWantActions, WantItems } from "./WantButton.jsx";
import { useWant } from "../wants.js";

/**
 * A title's card with its verdict on it, around the poster card it is given as `children`. With a
 * pointer, Like and Dislike appear at the foot of the poster under hover or keyboard focus, beside the
 * card rather than in it so a press on them never opens it. On a touch screen a long press on the card
 * opens its sheet (`TitleSheet`). A card with no IMDb id has nothing to vote on and is drawn as it is.
 *
 * `kind` is `film` or `show`, `category` the card's shelf, `art` the poster as a CSS background value,
 * and `to` where the card leads.
 */
export function VotableCard({ imdbId, kind, name, year, category, art, to, children }) {
  const [sheet, setSheet] = React.useState(false);
  const press = useLongPress(() => setSheet(true));
  if (!imdbId) return children;
  return (
    <>
      <div className="kc-card" {...press}>
        {children}
        <div className="kc-card__votes">
          <VoteButtons kind="title" id={imdbId} name={name} size="sm" onArt className="kc-card__vote" />
        </div>
      </div>
      {sheet && <TitleSheet imdbId={imdbId} kind={kind} name={name} year={year} category={category} art={art} to={to} onClose={() => setSheet(false)} />}
    </>
  );
}

/**
 * What a long press on a title's card opens, as a bottom sheet of menu items: the title, Like and
 * Dislike (the lit one checked; choosing it clears it), the want's actions and the title's page.
 */
function TitleSheet({ imdbId, kind, name, year, category, art, to, onClose }) {
  const online = useOnline();
  const vote = useVote("title", imdbId);
  const want = useWant(imdbId);
  const actions = useWantActions(imdbId, kind, name);
  const off = vote === undefined || !online;
  const cast = (pick) => {
    onClose();
    setVote("title", imdbId, vote === pick ? 0 : pick, { name }).catch((error) => toast.fromError(error, "Could not keep that vote"));
  };
  const choice = (pick, icon, words) => (
    <MenuItem role="menuitemradio" aria-checked={vote === pick} icon={icon} disabled={off} aside={vote === pick ? "✓" : undefined}
      onSelect={() => cast(pick)}>
      {words}
    </MenuItem>
  );
  const meta = [year, kindWord(kind === "show" ? "show" : "film", category === "anime")].filter(Boolean).join(" · ");
  return (
    <Drawer role="menu" label={name} onClose={onClose} className="kc-sheet" head={(
      <div className="kc-sheet__head">
        <span className="kc-sheet__art" style={art ? { backgroundImage: art } : undefined} aria-hidden="true" />
        <span className="kc-sheet__text">
          <span className="kc-sheet__name">{name}</span>
          <span className="kc-sheet__meta">{meta}</span>
        </span>
        <IconButton variant="ghost" icon="x" label="Close" onClick={onClose} />
      </div>
    )}>
      <MenuGroup>
        {choice(1, "thumbs-up", "Like")}
        {choice(-1, "thumbs-down", "Dislike")}
      </MenuGroup>
      <MenuSeparator />
      {want === undefined
        ? null
        : want
          ? <MenuGroup label="Wanted"><WantItems want={want} actions={actions} disabled={actions.busy || !online} onChosen={onClose} /></MenuGroup>
          : <MenuItem icon="bookmark" disabled={actions.busy || !online} onSelect={() => { onClose(); void actions.make(); }}>Want</MenuItem>}
      <MenuItem icon="info" onSelect={() => { onClose(); navigate(to); }}>More info</MenuItem>
    </Drawer>
  );
}
