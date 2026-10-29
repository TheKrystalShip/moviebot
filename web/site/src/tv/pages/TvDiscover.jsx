import React from "react";
import { Avatar, Button, FilterDrawer, Icon, SearchField, TvCard, TvRow, TvSpotlight } from "@thekrystalship/krystal";
import { cardRating, catalogue, libraryApi, posterUrl } from "../../api/index.js";
import { useLoad } from "../../data.js";
import { discoverFilterPath, discoverPath } from "../../paths.js";
import { linkProps, navigate } from "../../router.js";
import { Failed, watchHref } from "../../components/Film.jsx";
import { WantButton } from "../../components/WantButton.jsx";
import { VoteButtons } from "../../components/VoteButtons.jsx";
import { foundPage } from "../../components/TitleCards.jsx";
import { startFilm } from "./TvFilm.jsx";
import { TvCategory, TvDiscoverFront, TvList, TvTitleCard } from "./TvDiscoverShelves.jsx";
import { LIST_ORDERS, OWN_ORDERS, hasOwnOrder, useDiscoverOrder, useWhere } from "../../pages/DiscoverShelves.jsx";
import { withOrder } from "../../orders.js";
import {
  FILTER_BASE, PICK_PARAMS, apiPicks, drawerValues, picksOfValues, readPicks, sectionsOf, useDrawer, useFacets, withPicks,
} from "../../filters.js";

/** The address parameters that are the page's own, never a list query's. */
const PAGE_PARAMS = new Set(["q", "person", "franchise", "browse", "list", "filter", "sort", "tv", ...PICK_PARAMS]);

/** A list's query as the address holds it beside `list`: every parameter that is not the page's own or a pick. */
function listQueryOf(query) {
  const list = {};
  for (const [key, value] of query) if (!PAGE_PARAMS.has(key)) (list[key] ||= []).push(value);
  return list;
}

/** The studio a list is of, by its id: the list that holds nothing but one company. */
function studioOf(list) {
  const keys = Object.keys(list);
  return keys.length === 1 && keys[0] === "company" && list.company.length === 1 ? list.company[0] : null;
}

/**
 * Finding anything to watch, on a television. The field is focused when the page opens; OK on it
 * raises the system keyboard, which types, speaks and takes a phone, and its search key searches.
 * With nothing typed, the spotlight and the page's rows (`/api/catalogue/discover`), with the moods,
 * the franchises and the browse categories as rows of tiles; `browse` is one category, `list` the
 * titles a list query names under that name, `person` one person's work, `franchise` a franchise's
 * titles in its own order, and `filter` the drawer's picks over the whole catalogue. With words typed,
 * the catalogue's closest match large, with Watch, More info and Want, then the rest of what it
 * answered and whatever the library holds by those words, all as one row. What is shown is in the
 * address, as on every surface.
 *
 * The filter drawer in the field narrows whatever the page shows with nothing typed, by the sections
 * the catalogue's facets report; on a list it holds the list's order too (`useDiscoverOrder`). Picking
 * anything on the front opens the filter's answers. Back goes to the drawer first, and the page under
 * the open drawer is inert, so the remote's arrows stay in it.
 */
export function TvDiscover({ query }) {
  const q = (query.get("q") || "").trim();
  const person = /^\d+$/.test(query.get("person") || "") ? query.get("person") : null;
  const franchise = (query.get("franchise") || "").trim() || null;
  const category = (query.get("browse") || "").trim() || null;
  const listName = query.has("list") ? query.get("list") : null;
  const filtering = query.has("filter");
  const mode = q ? "search" : person ? "person" : franchise ? "franchise" : category ? "browse"
    : listName !== null ? "list" : filtering ? "filter" : "index";
  const picks = readPicks(query);
  const asked = apiPicks(picks);
  const pickKey = JSON.stringify(asked);
  const [draft, setDraft] = React.useState(q);
  React.useEffect(() => setDraft(q), [q]);
  const where = useWhere();

  const listQuery = mode === "list" ? listQueryOf(query) : null;
  const base = mode === "person" ? { person } : mode === "franchise" ? { franchise } : mode === "list" ? listQuery : FILTER_BASE;
  const sorts = mode === "franchise" ? OWN_ORDERS
    : mode === "list" ? (hasOwnOrder(listQuery) ? OWN_ORDERS : LIST_ORDERS)
      : mode === "person" || mode === "filter" ? LIST_ORDERS : [];
  const ordering = useDiscoverOrder(query, sorts, { listed: mode === "person" || mode === "franchise" || mode === "list", filtering: mode === "filter" });
  const sort = ordering ? ordering.sort : null;
  const index = useLoad(() => (mode === "index" || mode === "browse" ? catalogue.discover() : Promise.resolve(null)), [mode === "index" || mode === "browse"]);

  const submit = (value) => navigate(discoverPath(value.trim()));

  const drawer = useDrawer();
  const faceted = { ...base, ...asked };
  const facets = useFacets(mode === "search" ? null : () => catalogue.facets(faceted), JSON.stringify(faceted) + mode);
  const sections = mode === "search" ? [] : sectionsOf(facets, picks, { watched: false });
  const change = (next) => {
    const nextPicks = picksOfValues(next);
    if (JSON.stringify(apiPicks(nextPicks)) === pickKey) return;
    const landing = mode === "index";
    navigate(withPicks(landing ? new URLSearchParams(discoverFilterPath().split("?")[1]) : query, nextPicks), { replace: !landing });
  };
  const drawn = { sections, values: drawerValues(picks, false), onValues: change };
  const offered = ordering && ordering.orders.length > 0;
  const ordered = offered ? withOrder(drawn, ordering.orders, ordering.order, ordering.pick) : drawn;
  const filters = sections.some((x) => x.kind !== "toggle") || offered
    ? <FilterDrawer ref={drawer.ref} onOpenChange={drawer.onOpenChange} sections={ordered.sections} values={ordered.values} onChange={ordered.onValues} />
    : undefined;
  const listed = { sort, picks, where };

  return (
    <div className={"kc-tv-page kc-tvd" + (q ? " has-query" : "")}>
      <SearchField size="xl" className="kc-tv-search" data-tv-top="" value={draft} onChange={setDraft} onSubmit={submit} submit={false}
        placeholder="Any movie or show" filters={filters} inputProps={{ className: "kc-tv-search__input", "data-tv-initial": mode === "index" ? "" : undefined }} />

      <div className="kc-tvd__answer" inert={drawer.open ? "" : undefined} style={{ display: "contents" }}>
      {mode === "person" && <PersonWork key={person} personId={person} {...listed} />}
      {mode === "franchise" && <FranchiseTitles key={franchise} id={franchise} {...listed} />}
      {mode === "list" && <TvList key={JSON.stringify(listQuery)} query={listQuery} {...listed} eyebrow="Discover" title={listName || "Titles"}
        aside={studioOf(listQuery) && <VoteButtons kind="company" id={studioOf(listQuery)} name={listName} size="lg" className="kc-tv-vote" />} />}
      {mode === "filter" && <TvList query={FILTER_BASE} {...listed} eyebrow="Discover" title="Filter" />}
      {mode === "browse" && <TvCategory key={category} id={category} index={index} picks={picks} where={where} />}
      {mode === "index" && index.error && !index.data && <div className="kc-tv-pad"><Failed error={index.error} onRetry={index.reload} /></div>}
      {mode === "index" && index.data && <TvDiscoverFront index={index.data} where={where} />}
      {mode === "search" && <Search key={q} q={q} where={where} />}
      </div>
    </div>
  );
}

/** What words typed find: the catalogue's closest match large, then the rest and what the library holds by them, as one row. */
function Search({ q, where }) {
  const found = useLoad(() => catalogue.titles({ q, size: 30 }), [q]);
  const held = useLoad(() => libraryApi.page("all", { q }).catch(() => null), [q]);
  if (found.error && !found.data) return <div className="kc-tv-pad"><Failed error={found.error} onRetry={found.reload} /></div>;
  if (!found.data || held.loading) return null;
  const titles = found.data.titles;
  const answeredIds = new Set(titles.map((t) => t.id));
  const heldNow = held.data ? held.data.items : [];
  const libraryFilms = heldNow.filter((i) => i.film && !(i.film.film && answeredIds.has(i.film.film.imdbId))).map((i) => i.film);
  const libraryShows = heldNow.filter((i) => i.show && !answeredIds.has(i.show.imdbId)).map((i) => i.show);
  const [best, ...rest] = titles;
  return (
    <div className="kc-tvd__rows">
      {best && <BestMatch key={best.id} found={best} {...where(best)} />}
      {rest.length + libraryFilms.length + libraryShows.length > 0 && (
        <TvRow title={<>More for <span className="kc-tvd__q">“{q}”</span></>}>
          {rest.map((t) => <TvTitleCard key={t.kind + t.id} card={t} where={where} />)}
          {libraryFilms.map((t) => <TvCard key={t.id} art={posterUrl(t)} label={t.title} {...linkProps(foundPage({ kind: "film", category: t.card && t.card.category }, t))} />)}
          {libraryShows.map((s) => (
            <TvCard key={s.imdbId || s.firstTitleId} art={s.card && s.card.poster} label={s.title}
              {...linkProps(foundPage({ kind: "show", id: s.imdbId, category: s.card && s.card.category }, null, true))} />
          ))}
        </TvRow>
      )}
      {titles.length === 0 && libraryFilms.length + libraryShows.length === 0 && <p className="kc-tv-none">Nothing is called that.</p>}
    </div>
  );
}

/** The department the catalogue knows somebody for, as a word for them. */
const KNOWN_AS = { Acting: "Actor", Directing: "Director", Writing: "Writer", Production: "Producer" };

/** Everything one person is credited on, best known first, under their portrait and name. */
function PersonWork({ personId, sort, picks, where }) {
  const person = useLoad(() => catalogue.person(personId), [personId]);
  const p = person.data;
  if (person.error && !p) return <div className="kc-tv-pad"><Failed error={person.error} onRetry={person.reload} /></div>;
  if (!p) return null;
  return (
    <TvList query={{ person: personId }} sort={sort} picks={picks} where={where} title={p.name} eyebrow={KNOWN_AS[p.department] || p.department}
      aside={<VoteButtons kind="person" id={personId} name={p.name} size="lg" className="kc-tv-vote" />}
      lead={<Avatar src={p.portrait} name={p.name} tone="muted" size="css" imagePosition="center 20%" className="kc-tv-person__face kc-tvd__face" />} />
  );
}

/** A franchise's titles in its own order, under its name. */
function FranchiseTitles({ id, sort, picks, where }) {
  const franchise = useLoad(() => catalogue.franchise(id), [id]);
  const f = franchise.data;
  if (franchise.error && !f) return <div className="kc-tv-pad"><Failed error={franchise.error} onRetry={franchise.reload} /></div>;
  if (!f) return null;
  return (
    <TvList query={{ franchise: id }} sort={sort} picks={picks} where={where} title={f.card.name}
      eyebrow={f.card.kind === "universe" ? "Universe" : "Franchise"}
      aside={<VoteButtons kind="franchise" id={id} name={f.card.name} size="lg" className="kc-tv-vote" />} />
  );
}

/** The catalogue's first answer: its poster, name, facts and story, and Watch, More info and Want. */
function BestMatch({ found, here, held }) {
  const show = found.kind === "show";
  const page = foundPage(found, here, held);
  const [busy, setBusy] = React.useState(false);
  const watch = async () => {
    if (here) { navigate(watchHref(here.id)); return; }
    setBusy(true);
    if (!(await startFilm(found.id))) setBusy(false);
  };
  return (
    <section className="kc-tvd__best" data-tv-row="">
      {found.poster && <img className="kc-tvd__poster" src={found.poster} alt="" />}
      <TvSpotlight className="kc-tvd__body" titleSize="sm" overviewLines={2} eyebrow="Best match" title={found.title}
        meta={<>
          {cardRating(found) ? <span className="kc-tv-rating"><Icon name="star" size={13} /><span className="kc-mono">{cardRating(found)}</span></span> : null}
          {found.year && <span>{found.year}</span>}
          <span>{show ? "TV series" : "Movie"}</span>
          {found.starring.length > 0 && <span>{found.starring.join(", ")}</span>}
        </>}
        overview={found.plot}
        actions={<>
          {!show && <Button variant="primary" size="lg" icon="play" busy={busy} data-tv-initial="" onClick={watch}>Watch</Button>}
          <Button variant={show ? "primary" : "secondary"} size="lg" icon="info" data-tv-initial={show ? "" : undefined}
            {...linkProps(page)}>More info</Button>
          <WantButton imdbId={found.id} kind={show ? "show" : "film"} title={found.title} />
        </>} />
    </section>
  );
}
