import React from "react";
import { createPortal } from "react-dom";
import { Button, Icon, Input, Toggle, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { Drawer } from "./Drawer.jsx";

const NARROW = "(max-width: 720px)";

export function useNarrow() {
  const query = () => typeof window !== "undefined" && !!window.matchMedia && window.matchMedia(NARROW).matches;
  const [narrow, setNarrow] = React.useState(query);
  React.useEffect(() => {
    if (!window.matchMedia) return undefined;
    const media = window.matchMedia(NARROW);
    const change = () => setNarrow(media.matches);
    media.addEventListener("change", change);
    return () => media.removeEventListener("change", change);
  }, []);
  return narrow;
}

/**
 * Save a film or a show for later. One press puts it on the person's Watch later; once it is on any
 * list the button opens the lists they can add to, each switched on where it holds the title, and
 * makes a new one. A popover beside the button on a wide screen, a drawer on a phone.
 */
export function SaveButton({ imdbId, kind, title }) {
  const lists = useLoad(() => api.lists(imdbId), [imdbId]);
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const narrow = useNarrow();
  const ref = React.useRef(null);
  const menuRef = React.useRef(null);
  const [pos, setPos] = React.useState(null);

  // The popover is drawn over the page rather than inside the button's card, which clips what
  // overflows it; it follows the button while the page scrolls.
  React.useLayoutEffect(() => {
    if (!open || narrow || !ref.current) return undefined;
    const place = () => {
      const r = ref.current.getBoundingClientRect();
      setPos({ top: Math.round(r.bottom + 6), left: Math.round(Math.min(r.left, window.innerWidth - 300)) });
    };
    place();
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, true);
    return () => {
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", place, true);
    };
  }, [open, narrow]);

  React.useEffect(() => {
    if (!open || narrow) return undefined;
    const outside = (e) => {
      if (ref.current?.contains(e.target) || menuRef.current?.contains(e.target)) return;
      setOpen(false);
    };
    const escape = (e) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", outside);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("mousedown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open, narrow]);

  const editable = (lists.data || []).filter((l) => l.role !== "viewer");
  const saved = editable.some((l) => l.holds);

  const run = async (work, failure) => {
    setBusy(true);
    try {
      await work();
      lists.reload();
    } catch (error) {
      toast.fromError(error, failure);
    } finally {
      setBusy(false);
    }
  };

  const saveLater = () => run(async () => {
    await api.saveToList("later", imdbId, kind);
    toast.success(`Saved ${title} to Watch later.`);
  }, "Could not save it");

  const toggle = (list, on) => run(async () => {
    if (on) await api.saveToList(list.id, imdbId, kind);
    else await api.removeFromList(list.id, imdbId);
  }, "Could not change that list");

  const create = (name) => run(async () => {
    const made = await api.createList(name);
    await api.saveToList(made.list.id, imdbId, kind);
    toast.success(`Saved ${title} to ${made.list.name}.`);
  }, "Could not make that list");

  // With no connection to say which lists hold it, it is drawn as it starts, out of reach.
  if (!lists.data) return online ? null : <Button size="xl" icon="bookmark" disabled>Save</Button>;

  if (!saved) {
    return (
      <Button size="xl" icon="bookmark" disabled={busy || !online} onClick={saveLater}>Save</Button>
    );
  }

  const menu = <SaveMenu lists={editable} busy={busy || !online} onToggle={toggle} onCreate={create} />;
  return (
    <div className="kc-follow" ref={ref}>
      <Button variant="accent" size="xl" icon="bookmark-check" iconRight="chevron-down" disabled={!online}
        aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}>
        Saved
      </Button>
      {open && !narrow && createPortal(
        <div ref={menuRef} className="kc-follow__menu kc-save__menu" role="menu" style={pos || { visibility: "hidden" }}>{menu}</div>,
        document.body)}
      {open && narrow && <Drawer title="Save to" onClose={() => setOpen(false)} role="menu">{menu}</Drawer>}
    </div>
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
        <label key={l.id} className="kc-follow__option">
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
        <button type="button" className="kc-follow__unfollow" role="menuitem" onClick={() => setNaming(true)}>
          <Icon name="list-plus" size={15} />New list
        </button>
      )}
    </>
  );
}
