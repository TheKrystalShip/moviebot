import React from "react";
import {
  Icon, Modal, Select, ShowcaseCard, Toolbar, ToolbarCount, ToolbarFilters, ToolbarSearch, ToolbarSort, ToolbarSpacer, toast, useFilters,
} from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { linkProps, navigate } from "../router.js";
import { useSession } from "../session.js";
import { ago, clock, dayGroup } from "../time.js";
import { Empty, Failed, PageHead, watchHref } from "../components/Film.jsx";
import { FilmScore } from "../components/FilmDetails.jsx";

const count = (n) => `${n} ${n === 1 ? "title" : "titles"}`;

/** Where a list is: `later` is the person's own Watch later. */
export const listHref = (id) => "/saved/" + encodeURIComponent(id);

/** Where an entry leads: the library's own page for a film it holds, otherwise the catalogue's. */
function entryHref(e) {
  if (e.kind === "show") return "/show/" + encodeURIComponent(e.imdbId);
  return e.libraryId ? "/title/" + encodeURIComponent(e.libraryId) : "/film/" + encodeURIComponent(e.imdbId);
}

/**
 * A saved title on a shelf, the same card as the library's: its poster and name, rating, year and
 * length, when it was saved, and Watch for a film the library holds.
 */
export function SavedCard({ entry: e }) {
  return (
    <ShowcaseCard
      name={e.title}
      art={e.posterUrl ? `url("${e.posterUrl}")` : null}
      onPick={() => navigate(entryHref(e))}
      specs={[
        { icon: "star", value: e.rating ? e.rating.toFixed(1) : null, label: "Rating" },
        { icon: "calendar", value: e.year || null, label: "Year" },
        { icon: "clock", value: e.runtimeMinutes ? clock(e.runtimeMinutes * 60) : null, label: "Length" },
      ]}
      status={{ icon: "bookmark", label: `Saved ${ago(e.addedAt)}` }}
      action={e.libraryId
        ? { label: "Watch", onClick: () => navigate(watchHref(e.libraryId)) }
        : { label: "View" }} />
  );
}

/** Every list the signed-in person can see, their Watch later first, and making a new one. */
export function Saved() {
  const lists = useLoad(() => api.lists(), []);
  const me = (useSession() || {}).user || {};
  const [naming, setNaming] = React.useState(false);
  const [name, setName] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  const create = async (e) => {
    e.preventDefault();
    if (!name.trim()) return;
    setBusy(true);
    try {
      const made = await api.createList(name.trim());
      navigate(listHref(made.list.id));
    } catch (error) {
      toast.fromError(error, "Could not make that list");
      setBusy(false);
    }
  };

  return (
    <div className="kc-page">
      <PageHead title="Saved">
        {!naming && (
          <button type="button" className="kc-btn kc-head__action" onClick={() => setNaming(true)}>
            <Icon name="list-plus" size={16} />New list
          </button>
        )}
      </PageHead>
      {naming && (
        <form className="kc-search" onSubmit={create}>
          <Icon name="list" size={17} />
          <input autoFocus value={name} maxLength={80} onChange={(e) => setName(e.target.value)} placeholder="List name" aria-label="List name" />
          <button type="button" className="kc-btn kc-btn--ghost" onClick={() => { setNaming(false); setName(""); }}>Cancel</button>
          <button type="submit" className="kc-btn kc-btn--primary" disabled={busy || !name.trim()}>Create</button>
        </form>
      )}
      <Failed error={lists.error} onRetry={lists.reload} />
      {lists.data && (
        <div className="kc-grid kc-lists">
          {lists.data.map((l) => <ListCard key={l.id} list={l} me={me.id} />)}
        </div>
      )}
    </div>
  );
}

function ListCard({ list, me }) {
  const theirs = list.owner.userId !== me;
  return (
    <a className="kc-list-card" {...linkProps(listHref(list.id))}>
      <span className={"kc-list-card__art kc-list-card__art--" + Math.min(list.posters.length, 4)}>
        {list.posters.length === 0 && <Icon name={list.watchLater ? "clock" : "list"} size={28} />}
        {list.posters.map((p) => <img key={p} src={p} alt="" loading="lazy" />)}
      </span>
      <span className="kc-list-card__name">
        {list.watchLater && <Icon name="clock" size={14} />}
        {!list.watchLater && list.members.length > 0 && <Icon name="users" size={14} />}
        {list.name}
      </span>
      <span className="kc-quiet">
        {count(list.count)}{theirs && ` · ${list.owner.name || "someone"}`}
      </span>
    </a>
  );
}

// Each comparator orders ascending; the toolbar's direction reverses it.
const SORTS = [
  { value: "added", label: "Date added", defaultDir: "desc", compare: (a, b) => a.addedAt.localeCompare(b.addedAt) },
  { value: "name", label: "Name", defaultDir: "asc", compare: (a, b) => a.title.localeCompare(b.title) },
  { value: "year", label: "Year", defaultDir: "desc", compare: (a, b) => (a.year || 0) - (b.year || 0) },
  { value: "rating", label: "Rating", defaultDir: "desc", compare: (a, b) => (a.rating || 0) - (b.rating || 0) },
  { value: "length", label: "Length", defaultDir: "asc", compare: (a, b) => (a.runtimeMinutes || 0) - (b.runtimeMinutes || 0) },
];

const KIND_FILTERS = [
  { value: "all", label: "Films and shows", test: () => true },
  { value: "film", label: "Films", test: (e) => e.kind === "film" },
  { value: "show", label: "Shows", test: (e) => e.kind === "show" },
];

const HERE_FILTERS = [
  { value: "any", label: "Any", test: () => true },
  { value: "here", label: "In the library", test: (e) => e.inLibrary },
  { value: "away", label: "Not in the library", test: (e) => !e.inLibrary },
];

const WATCHED_FILTERS = [
  { value: "any", label: "Any", test: () => true },
  { value: "unwatched", label: "Not watched", test: (e) => !e.watched },
  { value: "watched", label: "Watched", test: (e) => e.watched },
];

/**
 * One list: its titles searched, narrowed and ordered, newest saved first under the stretch of time
 * each was saved in, with who saved it on a shared list. Its owner renames it, deletes it and says
 * who else is on it; a member leaves it.
 */
export function ListPage({ id }) {
  const view = useLoad(() => api.list(id), [id]);
  const me = (useSession() || {}).user || {};
  const f = useFilters({ search: "", kind: "all", here: "any", watched: "any" });
  const [sort, setSort] = React.useState({ key: "added", dir: "desc" });
  const [renaming, setRenaming] = React.useState(null);
  const [people, setPeople] = React.useState(false);
  const [deleting, setDeleting] = React.useState(false);

  if (view.error && view.error.status === 404) {
    return <div className="kc-page"><Empty icon="list" title="No such list" /></div>;
  }
  if (!view.data) return <div className="kc-page"><Failed error={view.error} onRetry={view.reload} /></div>;

  const { list, entries } = view.data;
  const owner = list.role === "owner";
  const editable = list.role !== "viewer";
  const shared = list.members.length > 0 || list.owner.userId !== me.id;

  const kind = KIND_FILTERS.find((k) => k.value === f.values.kind) || KIND_FILTERS[0];
  const here = HERE_FILTERS.find((k) => k.value === f.values.here) || HERE_FILTERS[0];
  const watched = WATCHED_FILTERS.find((k) => k.value === f.values.watched) || WATCHED_FILTERS[0];
  const query = f.debouncedQuery.trim().toLowerCase();
  const order = SORTS.find((s) => s.value === sort.key) || SORTS[0];
  const shown = entries
    .filter((e) => (!query || e.title.toLowerCase().includes(query) || (e.note || "").toLowerCase().includes(query))
      && kind.test(e) && here.test(e) && watched.test(e))
    .sort((a, b) => (sort.dir === "asc" ? 1 : -1) * order.compare(a, b));

  const option = (o) => ({ value: o.value, label: o.label, count: entries.filter(o.test).length });
  const fields = [
    { id: "kind", label: "Kind", value: f.values.kind, onChange: (v) => f.set("kind", v), default: "all", options: KIND_FILTERS.map(option) },
    { id: "here", label: "Library", value: f.values.here, onChange: (v) => f.set("here", v), default: "any", options: HERE_FILTERS.map(option) },
    { id: "watched", label: "Watched", value: f.values.watched, onChange: (v) => f.set("watched", v), default: "any", options: WATCHED_FILTERS.map(option) },
  ];

  // Ordered by when things were saved, the rows sit under the stretch of time each was saved in.
  const groups = [];
  for (const e of shown) {
    const label = sort.key === "added" ? dayGroup(e.addedAt) : null;
    if (groups.length === 0 || groups[groups.length - 1].label !== label) groups.push({ label, rows: [] });
    groups[groups.length - 1].rows.push(e);
  }

  const rename = async (e) => {
    e.preventDefault();
    try {
      await api.renameList(list.id, renaming.trim());
      setRenaming(null);
      view.reload();
    } catch (error) {
      toast.fromError(error, "Could not rename it");
    }
  };

  const remove = async () => {
    try {
      await api.deleteList(list.id);
      toast.info(`Deleted ${list.name}.`);
      navigate("/saved", { replace: true });
    } catch (error) {
      toast.fromError(error, "Could not delete it");
    }
  };

  const leave = async () => {
    try {
      await api.removeListMember(list.id, me.id);
      toast.info(`You left ${list.name}.`);
      navigate("/saved", { replace: true });
    } catch (error) {
      toast.fromError(error, "Could not leave it");
    }
  };

  const title = renaming !== null ? (
    <form className="kc-list-rename" onSubmit={rename}>
      <input autoFocus value={renaming} maxLength={80} onChange={(e) => setRenaming(e.target.value)} aria-label="List name" />
      <button type="submit" className="kc-btn kc-btn--primary" disabled={!renaming.trim()}>Save</button>
      <button type="button" className="kc-btn kc-btn--ghost" onClick={() => setRenaming(null)}>Cancel</button>
    </form>
  ) : list.name;
  const sub = [count(list.count), list.owner.userId !== me.id && (list.owner.name || "someone")].filter(Boolean).join(" · ");

  return (
    <div className="kc-page">
      <PageHead title={title} sub={sub}>
        {renaming === null && (
          <div className="kc-head__action kc-list-actions">
            {!list.watchLater && (
              <button type="button" className="kc-btn" onClick={() => setPeople(true)}>
                <Icon name="users" size={16} />{list.members.length + 1}
              </button>
            )}
            {owner && !list.watchLater && (
              <button type="button" className="kc-btn" aria-label="Rename" onClick={() => setRenaming(list.name)}>
                <Icon name="pencil" size={16} />
              </button>
            )}
            {owner && !list.watchLater && (deleting
              ? <button type="button" className="kc-btn kc-btn--danger" onClick={remove}><Icon name="trash-2" size={16} />Delete for everyone</button>
              : <button type="button" className="kc-btn" aria-label="Delete" onClick={() => setDeleting(true)}><Icon name="trash-2" size={16} /></button>)}
            {!owner && (
              <button type="button" className="kc-btn" onClick={leave}><Icon name="log-out" size={16} />Leave</button>
            )}
          </div>
        )}
      </PageHead>

      {entries.length > 0 && (
        <Toolbar>
          <ToolbarSearch value={f.query} onChange={f.setQuery} pending={f.searchPending} placeholder="Search this list…" />
          <ToolbarFilters fields={fields} onReset={() => f.setQuery("")} />
          <ToolbarSort value={sort.key} dir={sort.dir} onChange={(key, dir) => setSort({ key, dir })} options={SORTS} />
          <ToolbarSpacer />
          <ToolbarCount shown={shown.length} total={entries.length} unit="titles" />
        </Toolbar>
      )}

      <Failed error={view.error} onRetry={view.reload} />
      {entries.length === 0 && <Empty icon="bookmark" title="Nothing saved yet" />}
      {entries.length > 0 && shown.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {groups.map((g) => (
        <section key={g.label || "all"} className="kc-list-group">
          {g.label && <h2 className="kc-list-group__head">{g.label}</h2>}
          <div className="kc-rows">
            {g.rows.map((e) => (
              <EntryRow key={e.imdbId} entry={e} listId={list.id} editable={editable} shared={shared} onChange={view.reload} />
            ))}
          </div>
        </section>
      ))}

      {people && <PeopleSheet list={list} me={me} onClose={() => setPeople(false)} onChange={view.reload} />}
    </div>
  );
}

function EntryRow({ entry: e, listId, editable, shared, onChange }) {
  const [note, setNote] = React.useState(null);
  const [busy, setBusy] = React.useState(false);

  const run = async (work, failure) => {
    setBusy(true);
    try {
      await work();
      onChange();
    } catch (error) {
      toast.fromError(error, failure);
    } finally {
      setBusy(false);
    }
  };

  const saveNote = (ev) => {
    ev.preventDefault();
    run(async () => { await api.saveToList(listId, e.imdbId, e.kind, note.trim()); setNote(null); }, "Could not save the note");
  };
  const remove = () => run(async () => {
    await api.removeFromList(listId, e.imdbId);
    toast.info(`Took ${e.title} off the list.`);
  }, "Could not take it off");

  const facts = [e.year, e.kind === "show" ? "Show" : null, e.runtimeMinutes ? clock(e.runtimeMinutes * 60) : null].filter(Boolean);
  return (
    <div className="kc-row kc-list-entry">
      {e.posterUrl ? <img className="kc-row__thumb" src={e.posterUrl} alt="" loading="lazy" /> : <span className="kc-row__thumb" />}
      <div className="kc-row__text">
        <a className="kc-row__title" {...linkProps(entryHref(e))}>{e.title}</a>
        <span className="kc-row__sub">
          {facts.join(" · ")}
          {e.rating ? <FilmScore rating={e.rating} votes={0} compact /> : null}
          {e.watched && <span className="kc-pill">watched</span>}
        </span>
        <span className="kc-row__sub" title={new Date(e.addedAt).toLocaleString()}>
          Saved {ago(e.addedAt)}{shared && ` by ${e.addedBy.name || "someone"}`}
        </span>
        {note !== null ? (
          <form className="kc-list-note" onSubmit={saveNote}>
            <input autoFocus value={note} maxLength={500} onChange={(ev) => setNote(ev.target.value)} aria-label="Note" />
            <button type="submit" className="kc-btn kc-btn--primary" disabled={busy}>Save</button>
            <button type="button" className="kc-btn kc-btn--ghost" onClick={() => setNote(null)}>Cancel</button>
          </form>
        ) : e.note && <span className="kc-list-entry__note">{e.note}</span>}
      </div>
      {editable && note === null && (
        <div className="kc-list-entry__actions">
          <button type="button" className="kc-btn kc-btn--ghost" aria-label="Note" disabled={busy} onClick={() => setNote(e.note || "")}>
            <Icon name="message-square-text" size={16} />
          </button>
          <button type="button" className="kc-btn kc-btn--ghost" aria-label="Remove" disabled={busy} onClick={remove}>
            <Icon name="x" size={16} />
          </button>
        </div>
      )}
    </div>
  );
}

const ROLES = [{ value: "editor", label: "Can add" }, { value: "viewer", label: "Can view" }];

/** Who is on a list. Its owner puts people on it, says what each may do, and takes them off. */
function PeopleSheet({ list, me, onClose, onChange }) {
  const owner = list.role === "owner";
  const everyone = useLoad(() => (owner ? api.people() : Promise.resolve([])), [owner]);
  const [pick, setPick] = React.useState("");
  const [role, setRole] = React.useState("editor");
  const [busy, setBusy] = React.useState(false);

  const on = new Set(list.members.map((m) => m.userId));
  const candidates = (everyone.data || []).filter((p) => !on.has(p.userId));

  const run = async (work, failure) => {
    setBusy(true);
    try {
      await work();
      onChange();
    } catch (error) {
      toast.fromError(error, failure);
    } finally {
      setBusy(false);
    }
  };

  const add = () => run(async () => { await api.setListMember(list.id, pick, role); setPick(""); }, "Could not add them");
  const change = (m, r) => run(() => api.setListMember(list.id, m.userId, r), "Could not change that");
  const remove = (m) => run(() => api.removeListMember(list.id, m.userId), "Could not take them off");

  return (
    <Modal onClose={onClose} canClose={!busy} scrimClassName="kc-sheet-scrim">
      <div className="kc-sheet" role="dialog" aria-label={`People on ${list.name}`}>
        <div className="kc-sheet__head">
          <div className="kc-sheet__title">
            <span className="kc-eyebrow kc-eyebrow--muted">People</span>
            <span className="kc-sheet__name">{list.name}</span>
          </div>
        </div>
        <div className="kc-people">
          <div className="kc-people__row">
            <span className="kc-people__name">{list.owner.name || "someone"}{list.owner.userId === me.id && " (you)"}</span>
            <span className="kc-quiet">Owner</span>
          </div>
          {list.members.map((m) => (
            <div key={m.userId} className="kc-people__row">
              <span className="kc-people__name">{m.name || "someone"}{m.userId === me.id && " (you)"}</span>
              {owner ? (
                <>
                  <Select variant="chip" value={m.role} options={ROLES} aria-label={`What ${m.name} may do`}
                    disabled={busy} onChange={(e) => change(m, e.target.value)} />
                  <button type="button" className="kc-btn kc-btn--ghost" aria-label={`Take ${m.name} off`} disabled={busy} onClick={() => remove(m)}>
                    <Icon name="x" size={15} />
                  </button>
                </>
              ) : <span className="kc-quiet">{ROLES.find((r) => r.value === m.role)?.label}</span>}
            </div>
          ))}
          {owner && candidates.length > 0 && (
            <div className="kc-people__add">
              <Select value={pick} aria-label="Person" onChange={(e) => setPick(e.target.value)}
                options={[{ value: "", label: "Add someone" }, ...candidates.map((p) => ({ value: p.userId, label: p.name }))]} />
              <Select value={role} options={ROLES} aria-label="What they may do" onChange={(e) => setRole(e.target.value)} />
              <button type="button" className="kc-btn kc-btn--primary" disabled={busy || !pick} onClick={add}>
                <Icon name="user-plus" size={15} />Add
              </button>
            </div>
          )}
        </div>
        <div className="kc-sheet__foot">
          <button type="button" className="kc-btn" onClick={onClose}>Done</button>
        </div>
      </div>
    </Modal>
  );
}
