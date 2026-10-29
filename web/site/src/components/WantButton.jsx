import React from "react";
import { Button, Menu, MenuItem, MenuRadioGroup, MenuRadioItem, MenuSeparator, toast } from "@thekrystalship/krystal";
import { useOnline } from "../connectivity.js";
import { removeWant, setWant, useWant, wantWords } from "../wants.js";

/**
 * What a want's controls do, with the work of each one reported by a toast when it fails: make the
 * want (visible to everybody), say who sees it, and remove it. The store shows each change at once and
 * takes it back if the API refuses it.
 */
export function useWantActions(imdbId, kind, title) {
  const [busy, setBusy] = React.useState(false);

  const run = async (work, failure) => {
    setBusy(true);
    try {
      await work();
    } catch (error) {
      toast.fromError(error, failure);
    } finally {
      setBusy(false);
    }
  };

  return {
    busy,
    make: () => run(async () => {
      const words = wantWords(await setWant(imdbId, kind, { title }), title);
      if (words) toast.info(words);
    }, "Could not add it"),
    show: (visible) => run(() => setWant(imdbId, kind, { visible, title }), "Could not change that"),
    remove: () => run(() => removeWant(imdbId), "Could not remove it"),
  };
}

/** The menu of a want: who sees it, and removing it. `onChosen` follows any choice, for a menu that closes on one. */
export function WantItems({ want, actions, disabled, onChosen = null }) {
  const done = () => { if (onChosen) onChosen(); };
  return (
    <>
      <MenuRadioGroup value={want.visible ? "everyone" : "me"} onChange={(value) => { done(); void actions.show(value === "everyone"); }}>
        <MenuRadioItem value="me" icon="lock" disabled={disabled}>Just me</MenuRadioItem>
        <MenuRadioItem value="everyone" icon="users" disabled={disabled}>Everyone</MenuRadioItem>
      </MenuRadioGroup>
      <MenuSeparator />
      <MenuItem icon="x" tone="danger" disabled={disabled} onSelect={() => { done(); void actions.remove(); }}>Remove</MenuItem>
    </>
  );
}

/**
 * Want to watch a film or a show, here or not. One press makes the want, visible to everybody; once
 * it is made the button opens a menu on itself: Just me or Everyone, and Remove.
 */
export function WantButton({ imdbId, kind, title }) {
  const want = useWant(imdbId);
  const online = useOnline();
  const actions = useWantActions(imdbId, kind, title);
  const [open, setOpen] = React.useState(false);
  const anchor = React.useRef(null);

  // With no connection to say what is wanted, it is drawn as it starts, out of reach.
  if (want === undefined) return online ? null : <Button size="xl" icon="bookmark" disabled>Want</Button>;

  if (!want) return <Button size="xl" icon="bookmark" disabled={actions.busy || !online} onClick={actions.make}>Want</Button>;

  // The menu hangs from the button without taking its arrow keys, so a remote's Up and Down still move
  // focus off it to the rows around.
  return (
    <>
      <Button ref={anchor} variant="accent" size="xl" icon="bookmark-check" iconRight="chevron-down" disabled={!online}
        aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}>
        Wanted
      </Button>
      <Menu open={open} onOpenChange={setOpen} anchorRef={anchor} placement="bottom-start" label="Want to watch">
        <WantItems want={want} actions={actions} disabled={actions.busy} />
      </Menu>
    </>
  );
}

/**
 * A card's ribbon for wanting its title, as `PosterCard`'s `ribbon` prop, and the menu it opens. A
 * press on a title not wanted makes the want; on one wanted, it opens the menu beside the ribbon. While
 * what is wanted is not known there is no ribbon. The menu is drawn over the page, so it goes beside
 * the card rather than in it, where a press in it would open the card.
 */
export function useWantRibbon(imdbId, kind, title) {
  const want = useWant(imdbId);
  const online = useOnline();
  const actions = useWantActions(imdbId, kind, title);
  const [open, setOpen] = React.useState(false);
  const anchor = React.useRef(null);

  if (want === undefined) return [null, null];

  const press = (e) => {
    if (!want) {
      void actions.make();
      return;
    }
    anchor.current = e.currentTarget;
    setOpen(!open);
  };

  const ribbon = {
    icon: "bookmark", iconOn: "bookmark-check", on: !!want, label: want ? "Wanted" : "Want",
    disabled: actions.busy || !online, onClick: press,
  };
  const menu = want
    ? (
      <Menu open={open} onOpenChange={setOpen} anchorRef={anchor} placement="bottom-start" label="Want to watch">
        <WantItems want={want} actions={actions} disabled={actions.busy || !online} />
      </Menu>
    )
    : null;
  return [ribbon, menu];
}
