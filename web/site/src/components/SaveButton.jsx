import React from "react";
import { Button, Drawer, Icon, Input, Popover, Toggle, toast, usePhone } from "@thekrystalship/krystal";
import { api } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { changeSaved, markSaved, useSaved } from "../saved.js";

/** Puts a title on the person's Watch later, marked saved at once and unmarked again if it fails. */
async function saveForLater(imdbId, kind, title) {
  try {
    await changeSaved(() => api.saveToList("later", imdbId, kind), () => markSaved(imdbId, true));
    toast.success(`Saved ${title} to Watch later.`);
  } catch (error) {
    toast.fromError(error, "Could not save it");
  }
}

/**
 * Save a film or a show for later. One press puts it on the person's Watch later; once it is on any
 * list the button opens the lists they can add to, each switched on where it holds the title, and
 * makes a new one. A popover beside the button on a wide screen, a drawer on a phone.
 */
export function SaveButton({ imdbId, kind, title }) {
  const saved = useSaved(imdbId);
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const ref = React.useRef(null);

  // With no connection to say what is saved, it is drawn as it starts, out of reach.
  if (saved === null) return online ? null : <Button size="xl" icon="bookmark" disabled>Save</Button>;

  if (!saved) {
    const save = async () => {
      setBusy(true);
      await saveForLater(imdbId, kind, title);
      setBusy(false);
    };
    return <Button size="xl" icon="bookmark" disabled={busy || !online} onClick={save}>Save</Button>;
  }

  return (
    <>
      <Button ref={ref} variant="accent" size="xl" icon="bookmark-check" iconRight="chevron-down" disabled={!online}
        aria-haspopup="dialog" aria-expanded={open} onClick={() => setOpen(!open)}>
        Saved
      </Button>
      {open && <SavePopover anchor={ref.current} imdbId={imdbId} kind={kind} title={title} onClose={() => setOpen(false)} />}
    </>
  );
}

/**
 * A card's ribbon for saving its title, as `ShowcaseCard`'s `ribbon` prop, and the lists it opens.
 * A press on a title not saved puts it on Watch later; on one saved, it opens the lists beside the
 * ribbon. While what is saved is not known there is no ribbon. The lists are drawn over the page, so
 * they go beside the card rather than in it, where a press in them would open the card.
 */
export function useSaveRibbon(imdbId, kind, title) {
  const saved = useSaved(imdbId);
  const online = useOnline();
  const [anchor, setAnchor] = React.useState(null);
  const [busy, setBusy] = React.useState(false);

  if (saved === null) return [null, null];

  const press = async (e) => {
    if (saved) {
      setAnchor(anchor ? null : e.currentTarget);
      return;
    }
    setBusy(true);
    await saveForLater(imdbId, kind, title);
    setBusy(false);
  };

  const ribbon = {
    icon: "bookmark", iconOn: "bookmark-check", on: saved, label: saved ? "Saved" : "Save",
    disabled: busy || !online, onClick: press,
  };
  const menu = anchor && saved
    ? <SavePopover anchor={anchor} imdbId={imdbId} kind={kind} title={title} onClose={() => setAnchor(null)} />
    : null;
  return [ribbon, menu];
}

/**
 * The lists a saved title can be on, read when it opens: beside `anchor` on a wide screen, following it
 * while the page scrolls, and a drawer on a phone. A press outside it or Escape closes it.
 */
function SavePopover({ anchor, imdbId, kind, title, onClose }) {
  const lists = useLoad(() => api.lists(imdbId), [imdbId]);
  const online = useOnline();
  const [busy, setBusy] = React.useState(false);
  const narrow = usePhone();
  const anchorRef = React.useMemo(() => ({ current: anchor }), [anchor]);

  const editable = (lists.data || []).filter((l) => l.role !== "viewer");

  const run = async (work, savedAfter, failure) => {
    setBusy(true);
    try {
      await changeSaved(work, () => markSaved(imdbId, savedAfter));
      lists.reload();
    } catch (error) {
      toast.fromError(error, failure);
    } finally {
      setBusy(false);
    }
  };

  const toggle = (list, on) => run(async () => {
    if (on) await api.saveToList(list.id, imdbId, kind);
    else await api.removeFromList(list.id, imdbId);
  }, on || editable.some((l) => l.holds && l.id !== list.id), "Could not change that list");

  const create = (name) => run(async () => {
    const made = await api.createList(name);
    await api.saveToList(made.list.id, imdbId, kind);
    toast.success(`Saved ${title} to ${made.list.name}.`);
  }, true, "Could not make that list");

  const menu = lists.data
    ? <SaveMenu lists={editable} busy={busy || !online} onToggle={toggle} onCreate={create} />
    : null;
  if (narrow) return <Drawer title="Save to" onClose={onClose}>{menu}</Drawer>;
  return (
    <Popover open={!!menu} anchorRef={anchorRef} placement="bottom-start" label="Save to" className="kc-save__menu"
      onOpenChange={(next) => { if (!next) onClose(); }}>
      {menu}
    </Popover>
  );
}

function SaveMenu({ lists, busy, onToggle, onCreate }) {
  const [naming, setNaming] = React.useState(false);
  const [name, setName] = React.useState("");

  const submit = (e) => {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    onCreate(trimmed);
    setName("");
    setNaming(false);
  };

  return (
    <>
      {lists.map((l) => (
        <label key={l.id} className="kc-save__row">
          <span className="kc-save__name">
            <Icon name={l.watchLater ? "clock" : l.members.length > 0 ? "users" : "list"} size={15} />
            {l.name}
          </span>
          <Toggle on={!!l.holds} disabled={busy} label={l.name} onChange={(on) => onToggle(l, on)} />
        </label>
      ))}
      {naming ? (
        <form className="kc-save__new" onSubmit={submit}>
          <Input size="lg" wide autoFocus value={name} maxLength={80} onChange={(e) => setName(e.target.value)} placeholder="List name" aria-label="List name" />
          <Button type="submit" variant="primary" size="lg" disabled={busy || !name.trim()}>Create</Button>
        </form>
      ) : (
        <Button variant="ghost" icon="list-plus" onClick={() => setNaming(true)}>New list</Button>
      )}
    </>
  );
}
