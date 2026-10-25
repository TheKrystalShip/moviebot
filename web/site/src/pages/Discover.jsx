import React from "react";
import { Avatar, Button, CollectionTile, FilterDrawer, Icon, Pill, SearchField, Skeleton } from "@thekrystalship/krystal";
import { catalogue, libraryApi, meApi, pagesOf } from "../api/index.js";
import { useLoad, usePaged } from "../data.js";
import { useOnline } from "../connectivity.js";
import { askAssistant } from "../assistantHandoff.js";
import { useCan } from "../session.js";
import { discoverFilterPath, discoverFranchisePath, discoverPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { Empty, Failed, FilmCard, PageHead, ShowCard, watchHref } from "../components/Film.jsx";
import { FilmMatchActions } from "../components/FilmActions.jsx";
import { Spotlight } from "../components/Spotlight.jsx";
import { useDiscoverSlides } from "../heroes.js";
import { FilmScore } from "../components/FilmDetails.jsx";
import { WantButton } from "../components/WantButton.jsx";
import { PlanButton } from "../components/PlanButton.jsx";
import { Wanters } from "../components/Wanters.jsx";
import { CardsSkeleton, FoundCard, foundPage } from "../components/TitleCards.jsx";
import {
  BrowseHead, BrowseSkeleton, CategoryPage, DiscoverRows, FrontSkeleton, LIST_ORDERS, ListPage, MoreCards, OWN_ORDERS, UNWATCHED,
  hasOwnOrder, useDiscoverOrder, useHideWatched, useWhere,
} from "./DiscoverShelves.jsx";
import { withOrder } from "../orders.js";
import { FILTER_BASE, PICK_PARAMS, apiPicks, drawerValues, pickCount, picksOfValues, readPicks, sectionsOf, useFacets, withPicks } from "../filters.js";
import { openingEpisode, watchOpening } from "../watchNow.js";

/** The class the page's field carries, so the shortcut that opens the page can find it. */
export const DISCOVER_FIELD = "kc-discover-field";

/** The address parameters that are the page's own, never a list query's. */
const PAGE_PARAMS = new Set(["q", "person", "franchise", "browse", "list", "filter", "sort", "tv", ...PICK_PARAMS]);

/** A list's query as the address holds it beside `list`: every parameter that is not the page's own or a pick. */
function listQueryOf(query) {
  const list = {};
  for (const [key, value] of query) if (!PAGE_PARAMS.has(key)) (list[key] ||= []).push(value);
  return list;
}

/**
 * Finding anything to watch, here or not, every list of it a list query over the catalogue. With
 * nothing typed, the field with the filter in it, a spotlight on the season's shelf, and the page's
 * rows (`/api/catalogue/discover`): shelves of posters, the moods and the franchises as tiles, and the
 * browse categories. `browse` is one category, `list` the titles a list query names (a shelf, a genre,
 * a keyword, an award) under that name, `person` one person's work, `franchise` a franchise's titles in
 * its own order, and `filter` the drawer's picks over the whole catalogue.
 *
 * With words typed, the catalogue's titles by those words, the closest match first: the first large,
 * the rest as cards, followed by anything the server can play by that name that the catalogue does not
 * list. The same words are read as a question (`/api/catalogue/ask`): when they are one, the reading as
 * a line of parts and the titles it names come first, and a question naming something the catalogue
 * does not know says so. Ask the assistant is offered when nothing came back, when the question was
 * refused, and when the words are no question and no title found carries them.
 *
 * The filter drawer in the field narrows whatever the page shows, its sections the catalogue's facets
 * for it; on a list it holds the list's order too (`useDiscoverOrder`). What is shown is in the
 * address, so it can be shared and reloaded.
 */
export function Discover({ query }) {
  const online = useOnline();
  const q = (query.get("q") || "").trim();
  const person = /^\d+$/.test(query.get("person") || "") ? query.get("person") : null;
  const franchise = (query.get("franchise") || "").trim() || null;
  const category = (query.get("browse") || "").trim() || null;
  const listName = query.has("list") ? query.get("list") : null;
  const filtering = query.has("filter");
  const mode = q ? "search" : person ? "person" : franchise ? "franchise" : category ? "browse"
    : listName !== null ? "list" : filtering ? "filter" : "index";

  const [draft, setDraft] = React.useState(q);
  React.useEffect(() => setDraft(q), [q]);
  const field = useFocusOnceShown();
  const picks = readPicks(query);
  const asked = apiPicks(picks);
  const narrowed = pickCount(picks) > 0;
  const [hideWatched, setHideWatched] = useHideWatched();
  const where = useWhere();

  const listQuery = mode === "list" ? listQueryOf(query) : null;
  const base = mode === "search" ? { q }
    : mode === "person" ? { person }
      : mode === "franchise" ? { franchise }
        : mode === "list" ? listQuery
          : FILTER_BASE;
  const sorts = mode === "franchise" ? OWN_ORDERS
    : mode === "list" ? (hasOwnOrder(listQuery) ? OWN_ORDERS : LIST_ORDERS)
      : mode === "person" || mode === "filter" ? LIST_ORDERS : [];
  const ordering = useDiscoverOrder(query, sorts, { listed: mode === "person" || mode === "franchise" || mode === "list", filtering: mode === "filter" });
  const sort = ordering ? ordering.sort : null;

  const index = useLoad(() => (mode === "index" || mode === "browse" ? catalogue.discover() : Promise.resolve(null)), [mode === "index" || mode === "browse"]);

  // The facets of what the page shows, the drawer's sections made of them.
  const faceted = { ...base, ...asked, ...(hideWatched ? UNWATCHED : {}) };
  const facets = useFacets(online ? () => catalogue.facets(faceted) : null, JSON.stringify(faceted) + online);
  const sections = sectionsOf(facets, picks);
  const filterable = sections.some((s) => s.kind !== "toggle");
  const change = (next) => {
    if (next.watched !== hideWatched) setHideWatched(next.watched);
    const nextPicks = picksOfValues(next);
    if (JSON.stringify(apiPicks(nextPicks)) === JSON.stringify(asked)) return;
    const landing = mode === "index";
    navigate(withPicks(landing ? new URLSearchParams(discoverFilterPath().split("?")[1]) : query, nextPicks), { replace: !landing });
  };
  const drawn = { sections, values: drawerValues(picks, hideWatched), onValues: change };
  const offered = ordering && ordering.orders.length > 0;
  const ordered = offered ? withOrder(drawn, ordering.orders, ordering.order, ordering.pick) : drawn;
  const drawer = filterable || offered
    ? <FilterDrawer sections={ordered.sections} values={ordered.values} onChange={ordered.onValues} />
    : undefined;

  const submit = (typed) => navigate(discoverPath(typed.trim()));
  const listed = { sort, picks, where };

  return (
    <div className="kc-page kc-discover">
      <PageHead title="Discover" />
      <div className="kc-discover-bar">
        <SearchField ref={field} size="xl" className={DISCOVER_FIELD} value={draft} onChange={setDraft} onSubmit={submit}
          disabled={!online} placeholder="Any movie or show" filters={drawer} />
      </div>

      {mode === "person" && <PersonWork key={person} personId={person} {...listed} />}
      {mode === "franchise" && <FranchiseTitles key={franchise} id={franchise} {...listed} />}
      {mode === "list" && (
        <ListPage key={JSON.stringify(listQuery)} query={listQuery} {...listed}
          head={(count) => <BrowseHead eyebrow="Discover" title={listName || "Titles"} count={count} />} />
      )}
      {mode === "filter" && (
        <ListPage query={FILTER_BASE} {...listed} icon="sliders-horizontal"
          head={(count) => <BrowseHead eyebrow="Discover" icon="sliders-horizontal" title="Filter" count={count} />} />
      )}
      {mode === "browse" && <CategoryPage key={category} id={category} index={index} picks={picks} where={where} />}
      {mode === "index" && index.error && !index.data && <Failed error={index.error} onRetry={index.reload} />}
      {mode === "index" && !index.data && !index.error && <FrontSkeleton />}
      {mode === "index" && index.data && (
        <>
          <DiscoverSpotlight key={index.data.spotlight} index={index.data} where={where} />
          <DiscoverRows index={index.data} where={where} />
        </>
      )}
      {mode === "search" && <Search key={q} q={q} picks={picks} narrowed={narrowed} hideWatched={hideWatched} where={where} />}
    </div>
  );
}

/**
 * What words typed find: the question they ask, when they are one, and its titles first; then the
 * catalogue's titles by those words, the closest match large; then what the library holds by them that
 * the catalogue does not list. With picks, the titles found by name are narrowed by them too.
 */
function Search({ q, picks, narrowed, hideWatched, where }) {
  const online = useOnline();
  const asked = { ...apiPicks(picks), ...(hideWatched ? UNWATCHED : {}) };
  const key = JSON.stringify(asked);
  const found = usePaged(pagesOf({ q, ...asked }), [q, key]);
  const reading = useLoad(() => (online ? catalogue.ask(q).catch(() => null) : Promise.resolve(null)), [q, online]);
  const ask = reading.data;
  const question = ask && ask.question && ask.query ? { ...ask.query, ...asked } : null;
  const answers = usePaged(question ? pagesOf(question) : () => Promise.resolve({ titles: [] }), [JSON.stringify(question)]);
  const held = useLoad(() => (online ? libraryApi.page("all", { q }).catch(() => null) : Promise.resolve(null)), [q, online]);
  const shows = useLoad(() => libraryApi.shows().catch(() => []), []);

  const titles = found.titles || [];
  const questionTitles = question ? answers.titles || [] : [];
  const listedIds = new Set(titles.map((t) => t.id));
  const heldItems = held.data ? held.data.items : [];
  const libraryFilms = heldItems.filter((i) => i.film && !(i.film.film && listedIds.has(i.film.film.imdbId))).map((i) => i.film);
  const libraryShows = heldItems.filter((i) => i.show && !listedIds.has(i.show.imdbId)).map((i) => i.show);
  const showsHere = new Map((shows.data || []).filter((s) => s.imdbId).map((s) => [s.imdbId, s]));
  const [best, ...rest] = narrowed ? [] : titles;
  const others = narrowed ? 0 : libraryFilms.length + libraryShows.length;
  const answered = !!found.titles && !reading.loading && (!question || !!answers.titles);
  const everything = questionTitles.length + (narrowed ? 0 : titles.length + others);
  const words = q.toLowerCase().split(/\s+/).filter(Boolean);
  const carriesWords = [...questionTitles, ...titles].some((t) => words.every((w) => (t.title || "").toLowerCase().includes(w)));
  const offerAssistant = online && answered
    && (everything === 0 || !!(ask && ask.question && ask.message) || (!(ask && ask.question) && !carriesWords));

  return (
    <>
      {!answered && <ResultsSkeleton />}
      {ask && ask.question && ask.parts.length > 0 && (
        <div className="kc-keywords">{ask.parts.map((p, i) => <Pill key={i + p.part + p.text} tone="accent">{p.text}</Pill>)}</div>
      )}
      {ask && ask.question && ask.message && <p className="kc-note">{ask.message}</p>}
      {questionTitles.length > 0 && (
        <>
          <div className="kc-grid">{questionTitles.map((t) => <FoundCard key={t.kind + t.id} found={t} {...where(t)} />)}</div>
          <MoreCards paged={answers} />
        </>
      )}
      {best && <BestMatch key={best.id} found={best} {...where(best)} shelf={showsHere.get(best.id)} />}
      {(rest.length + others > 0 || (narrowed && titles.length > 0)) && (
        <section className="kc-shelf">
          <div className="kc-shelf__head"><h2>{questionTitles.length > 0 || best ? "More results" : "Results"}</h2></div>
          <div className="kc-grid">
            {(narrowed ? titles : rest).map((t) => <FoundCard key={t.kind + t.id} found={t} {...where(t)} />)}
            {!narrowed && libraryFilms.map((t) => <FilmCard key={t.id} title={t} />)}
            {!narrowed && libraryShows.map((s) => <ShowCard key={s.imdbId || s.firstTitleId} show={s} />)}
          </div>
          <MoreCards paged={found} />
        </section>
      )}
      {answered && everything === 0 && <Empty icon="search" title="Nothing by that name" />}
      {offerAssistant && <Button size="xl" variant="primary" icon="bot" onClick={() => askAssistant(q)}>Ask the assistant</Button>}
    </>
  );
}

/** The page's spotlight: the films on the season's shelf, or the week's trending films out of season. */
function DiscoverSpotlight({ index, where }) {
  const slides = useDiscoverSlides(index, where);
  return <Spotlight slides={slides} where={where} />;
}

/** The department the catalogue knows somebody for, as a word for them. */
const KNOWN_AS = { Acting: "Actor", Directing: "Director", Writing: "Writer", Production: "Producer" };

/** One person's work, the best known first unless another order is picked, each card with their part in it. */
function PersonWork({ personId, sort, picks, where }) {
  const person = useLoad(() => catalogue.person(personId), [personId]);
  const p = person.data;
  if (person.error && !p) return <Failed error={person.error} onRetry={person.reload} />;
  if (!p) return <BrowseSkeleton portrait />;
  return (
    <ListPage query={{ person: personId }} sort={sort} picks={picks} where={where} icon="user" empty="No credits"
      head={(count) => (
        <BrowseHead eyebrow={KNOWN_AS[p.department] || p.department} title={p.name} count={count}
          face={<Avatar src={p.portrait} name={p.name} tone="muted" size="css" className="kc-cast__face kc-browse-head__face" />} />
      )} />
  );
}

/** A franchise's titles in its own order, under its name; a universe with the collections it holds as tiles above them. */
function FranchiseTitles({ id, sort, picks, where }) {
  const franchise = useLoad(() => catalogue.franchise(id), [id]);
  const f = franchise.data;
  if (franchise.error && !f) return <Failed error={franchise.error} onRetry={franchise.reload} />;
  if (!f) return <BrowseSkeleton />;
  return (
    <>
      {f.parts.length > 0 && (
        <div className="kc-browse-grid">
          {f.parts.map((part) => (
            <CollectionTile key={part.id} variant="fan" size="sm" name={part.name} posters={part.poster ? [part.poster] : []}
              caption={`${part.titles} titles`} {...linkProps(discoverFranchisePath(part.id))} />
          ))}
        </div>
      )}
      <ListPage query={{ franchise: id }} sort={sort} picks={picks} where={where} icon="library"
        head={(count) => <BrowseHead eyebrow={f.card.kind === "universe" ? "Universe" : "Franchise"} icon="library" title={f.card.name} count={count} />} />
    </>
  );
}

/**
 * The catalogue's first answer to words, which is usually the one meant: large enough to recognise, with
 * its cast to tell it from another of the same name, and the actions its own page offers, asked for as
 * soon as it is shown, so it can be watched without opening it.
 */
function BestMatch({ found, here, held, shelf }) {
  const show = found.kind === "show";
  const page = foundPage(found, here, held);
  const playable = here && here.status === "ready";
  return (
    <article className="kc-hit">
      {found.backdrop && <div className="kc-hit__art" style={{ backgroundImage: `url("${found.backdrop}")` }} />}
      <div className="kc-hit__shade" />
      <div className="kc-hit__grid">
        <a className="kc-hit__poster" {...linkProps(page)} tabIndex={-1} aria-hidden="true">
          {found.poster
            ? <img src={found.poster} alt="" />
            : <Icon name={show ? "tv" : "clapperboard"} size={32} />}
        </a>
        <div className="kc-hit__body">
          <span className="kc-eyebrow">{show ? "TV series" : "Movie"}</span>
          <h2><a {...linkProps(page)}>{found.title}</a></h2>
          <div className="kc-tt__meta">
            {found.rating && <FilmScore rating={found.rating.value} votes={found.rating.votes} by={found.rating.by} compact />}
            {found.year && <span className="kc-tt__year">{found.year}</span>}
          </div>
          {found.starring.length > 0 && <span className="kc-hit__cast">{found.starring.join(", ")}</span>}
        </div>
        <div className="kc-hit__more">
          {found.plot && <p className="kc-hit__overview">{found.plot}</p>}
          {show
            ? <ShowMatchActions found={found} shelf={shelf} />
            : here
              ? (
                <div className="kc-actions">
                  {playable && (
                    <Button variant="primary" size="xl" icon="play" onClick={() => navigate(watchHref(here.id))}>Watch</Button>
                  )}
                  <WantButton imdbId={found.id} kind="film" title={found.title} />
                  <PlanButton imdbId={found.id} kind="film" />
                  <Wanters imdbId={found.id} />
                </div>
              )
              : <FilmMatchActions found={found} />}
        </div>
      </div>
    </article>
  );
}

/**
 * A show as its own page offers it: Watch or Resume when an episode can be played now, otherwise Watch
 * its first episode while it is prepared, from its seasons as the person has them.
 */
function ShowMatchActions({ found, shelf }) {
  const downloading = useCan("cinema:download");
  const described = useLoad(() => (shelf || !downloading ? Promise.resolve(null) : meApi.show(found.id).catch(() => null)), [found.id, shelf, downloading]);
  const [starting, setStarting] = React.useState(false);
  const s = described.data;
  const opening = s && openingEpisode(s);
  const resume = shelf && shelf.resume;

  const watchFirst = async () => {
    setStarting(true);
    await watchOpening(s);
    setStarting(false);
  };

  return (
    <div className="kc-actions">
      {shelf && (
        <Button variant="primary" size="xl" icon="play"
          onClick={() => navigate(watchHref(resume ? resume.titleId : shelf.firstTitleId, null, resume && resume.positionSeconds))}>
          {resume ? "Resume" : "Watch"}
        </Button>
      )}
      {!shelf && downloading && !s && described.loading && <Skeleton w={120} h={46} r={8} />}
      {!shelf && opening && (
        <Button variant="primary" size="xl" icon="play" disabled={starting} onClick={watchFirst}>Watch</Button>
      )}
      <WantButton imdbId={found.id} kind="show" title={found.title} />
      <PlanButton imdbId={found.id} kind="show" />
      <Wanters imdbId={found.id} />
    </div>
  );
}

/** The frame of the results while the catalogue is asked, so they settle in place. */
function ResultsSkeleton() {
  return (
    <>
      <div className="kc-hit">
        <div className="kc-hit__grid">
          <span className="k-skeleton kc-hit__poster" />
          <div className="kc-hit__body">
            <Skeleton w={70} h={12} />
            <Skeleton w="55%" h={28} />
            <Skeleton w={44} h={14} />
            <Skeleton w="40%" h={14} />
            <Skeleton w={120} h={40} r={8} />
          </div>
        </div>
      </div>
      <CardsSkeleton count={6} />
    </>
  );
}

/**
 * The field, focused once its page is on screen, where a keyboard is what types: on a touch screen it
 * would raise the keyboard over the shelves below before anybody asked to type. A page navigated to
 * is prepared out of sight first (`PageStage`), where nothing takes focus, so this waits for it to be
 * shown.
 */
function useFocusOnceShown() {
  const ref = React.useRef(null);
  React.useEffect(() => {
    if (!window.matchMedia || !window.matchMedia("(pointer: fine)").matches) return undefined;
    let frame = 0;
    const deadline = performance.now() + 2000;
    const tryFocus = () => {
      const el = ref.current;
      if (!el || performance.now() > deadline) return;
      if (el.closest(".is-incoming")) {
        frame = requestAnimationFrame(tryFocus);
        return;
      }
      el.focus({ preventScroll: true });
    };
    tryFocus();
    return () => cancelAnimationFrame(frame);
  }, []);
  return ref;
}

