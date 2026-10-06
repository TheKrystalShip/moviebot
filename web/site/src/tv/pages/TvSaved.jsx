import React from "react";
import { Icon, TvCard, TvRow } from "@thekrystalship/krystal-ui";
import { api } from "../../api.js";
import { useLoad } from "../../data.js";
import { discoverFilmPath, listPath, moviePath, showPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { ago, clock } from "../../time.js";
import { Failed } from "../../components/Film.jsx";

const titles = (n) => `${n} ${n === 1 ? "title" : "titles"}`;

/** Where a saved title leads: the library's page for a film it holds, otherwise the catalogue's. */
function entryHref(e) {
  if (e.kind === "show") return showPath(e.imdbId);
  return e.libraryId ? moviePath(e.libraryId) : discoverFilmPath(e.imdbId);
}

/** A saved title's name, and under it its year, length and when it was saved. */
function EntryLine({ entry: e }) {
  if (!e) return <div className="kc-tvsv__line" />;
  const facts = [e.year, e.runtimeMinutes ? clock(e.runtimeMinutes * 60) : null, `saved ${ago(e.addedAt)}`].filter(Boolean);
  return (
    <div className="kc-tvsv__line">
      <span className="kc-tvsv__name">{e.title}</span>
      <span className="kc-tvsv__facts">{facts.join(" · ")}</span>
    </div>
  );
}

/**
 * Saved on a television: the person's Watch later as a row, with the focused title named under it,
 * then every other list they can see as a card. Lists are made and changed on the other surfaces.
 */
export function TvSaved() {
  const later = useLoad(() => api.list("later").catch(() => null), []);
  const lists = useLoad(() => api.lists(), []);
  const [focused, setFocused] = React.useState(null);

  if (lists.error && !lists.data) return <div className="kc-tv-page kc-tv-pad"><Failed error={lists.error} onRetry={lists.reload} /></div>;
  const entries = (later.data && later.data.entries) || [];
  const others = (lists.data || []).filter((l) => !l.watchLater);

  return (
    <div className="kc-tv-page kc-tvsv">
      {entries.length > 0 && (
        <section>
          <TvRow icon="bookmark" title="Watch later" count={entries.length}>
            {entries.map((e) => (
              <TvCard key={e.imdbId} art={e.posterUrl} label={e.title} {...linkProps(entryHref(e))} onFocus={() => setFocused(e)} />
            ))}
          </TvRow>
          <EntryLine entry={focused && entries.includes(focused) ? focused : null} />
        </section>
      )}
      {others.length > 0 && (
        <TvRow icon="users" title="Lists" count={others.length}>
          {others.map((l) => (
            <a key={l.id} className="kc-tvsv__list" {...linkProps(listPath(l.id))} onFocus={() => setFocused(null)}>
              <span className="tv-card tv-card--wide kc-tvsv__art">
                {l.posters.length === 0 && <Icon name="list" size={28} />}
                {l.posters.slice(0, 3).map((p) => <img key={p} src={p} alt="" loading="lazy" />)}
              </span>
              <span className="kc-tvsv__name">{l.name}</span>
              <span className="kc-tvsv__facts">
                {titles(l.count)}{l.members.length > 0 ? ` · with ${l.members.length} ${l.members.length === 1 ? "person" : "people"}` : ""}
              </span>
            </a>
          ))}
        </TvRow>
      )}
    </div>
  );
}

/** One list on a television: its name, and its titles, newest saved first, as posters in rows. */
export function TvList({ id }) {
  const list = useLoad(() => api.list(id), [id]);
  if (list.error && !list.data) return <div className="kc-tv-page kc-tv-pad"><Failed error={list.error} onRetry={list.reload} /></div>;
  if (!list.data) return <div className="kc-tv-page" />;
  const l = list.data;
  const entries = [...l.entries].sort((a, b) => b.addedAt.localeCompare(a.addedAt));
  return (
    <div className="kc-tv-page kc-tvsv kc-tvsv--list">
      <div className="kc-tvsv__head">
        <h1 className="tv-spot__title">{l.name}</h1>
        <span className="kc-tvsv__facts">{titles(entries.length)}</span>
      </div>
      <div className="kc-tvsv__grid">
        {entries.map((e) => (
          <TvCard key={e.imdbId} art={e.posterUrl} label={e.title} {...linkProps(entryHref(e))} />
        ))}
      </div>
    </div>
  );
}
