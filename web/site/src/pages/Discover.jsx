import React from "react";
import { Avatar, Button, FilterDrawer, Icon, Pill, SearchField, Skeleton } from "@thekrystalship/krystal";
import { api, filmName } from "../api.js";
import { useLoad, usePaged } from "../data.js";
import { useOnline } from "../connectivity.js";
import { askAssistant } from "../assistantHandoff.js";
import { useCan } from "../session.js";
import { ANIME, discoverFilterPath, discoverPath } from "../paths.js";
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
  CategoryPage, DiscoverRows, FilterPage, FrontSkeleton, MoreCards, ShelfPage, useDiscoverOrder, useHideWatched,
} from "./DiscoverShelves.jsx";
import { withOrder } from "../orders.js";
import { apiPicks, drawerValues, filterQuestion, pickCount, picksOfValues, readPicks, sectionsOf, useFacets, withPicks } from "../filters.js";
import { openingEpisode, watchOpening } from "../watchNow.js";

/** The class the page's field carries, so the shortcut that opens the page can find it. */
export const DISCOVER_FIELD = "kc-discover-field";

/**
 * Finding anything to watch, here or not. With nothing typed, the field with the filter in it, a
 * spotlight on a film from the season's shelf, and the page's rows (`/api/discover`): shelves of
 * posters, the moods and the franchises as tiles, and the browse categories. `browse` is one
 * category, `shelf` every title on one shelf, and `filter` the filter's answers at the choices the
 * address holds. With a query, the catalogue's films and shows in its own order, each by its poster: the first
 * answer large, the rest as cards, followed by anything the server can play by that name that the
 * catalogue did not answer with. A result is a result wherever it is played from: one that can be
 * played now plays from here, and nothing says where it comes from. The same words are also asked as a
 * question of the catalogue (`/api/catalogue/ask`): when they are one, the reading as a line of parts
 * and the titles it found come first. Ask the assistant is offered when nothing came back, when the
 * question was refused, and when the words are no question and no title found carries them. With a person (`person`, the film database's id for them) instead, the films and shows
 * they are credited on, best known first; with a genre (`genre` and `kind`), the catalogue's most
 * popular films or shows in it. Every answer leads to its own page. What is shown is in the address,
 * so it can be shared and reloaded.
 *
 * The filter drawer in the field filters whatever the page answers: Discover's catalogue, a question, a
 * person's titles, a genre's, a shelf and a category's shelves. Its sections are the ones the answer's
 * facets report, and picking anything with nothing typed opens the filter's answers. On a shelf and on
 * the filter's answers, which are lists in an order, it holds their order too (`useDiscoverOrder`). With picks on a
 * search, the question's titles alone are shown, since nothing else found by name answers them.
 */
export function Discover({ query }) {
  const online = useOnline();
  const q = (query.get("q") || "").trim();
  const person = /^\d+$/.test(query.get("person") || "") ? query.get("person") : null;
  const genre = (query.get("genre") || "").trim();
  const genreKind = query.get("kind") === "show" ? "show" : "film";
  const genreCategory = query.get("category") === ANIME ? ANIME : null;
  const shelf = (query.get("shelf") || "").trim();
  const category = (query.get("browse") || "").trim();
  const filtering = query.has("filter");
  const browsing = !q && !!(person || genre || shelf || category || filtering);
  const [draft, setDraft] = React.useState(q);
  React.useEffect(() => setDraft(q), [q]);
  const field = useFocusOnceShown();
  const picks = readPicks(query);
  const asked = apiPicks(picks);
  const pickKey = JSON.stringify(asked);
  const narrowed = pickCount(picks) > 0;
  const [hideWatched, setHideWatched] = useHideWatched();
  const showsShelf = !q && !person && !genre && !!shelf;
  const showsFilter = !q && !person && !genre && !shelf && !category && filtering;

  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const finding = downloading || wishing;

  const titles = useLoad(() => api.titles(), []);
  // The answer carries the query it is for: until this query's comes back, the one held is the last
  // query's, and says nothing about whether the catalogue knows this name. The catalogue is searched
  // by somebody who may fetch a film or wait on one; shows are found for fetching.
  const foundFor = useLoad(() => (q && finding
    ? (downloading ? api.findTitles(q) : api.findFilms(q)).catch(() => [])
    : Promise.resolve([])).then((list) => ({ q, list })), [q, downloading, finding]);
  const answered = !finding || (foundFor.data && foundFor.data.q === q);
  const found = answered && foundFor.data && foundFor.data.q === q ? foundFor.data.list : [];
  // The question's titles are read a page at a time; the reading and any refusal are the first page's.
  const askedFor = usePaged((page) => (q && online ? api.ask(q, page, asked).catch(() => null) : Promise.resolve(null))
    .then((ask) => ({ q, ask, titles: ask && ask.question ? ask.titles : [], next: ask ? ask.next : undefined })), [q, online, pickKey]);
  const askAnswered = !q || !online || (askedFor.data && askedFor.data.q === q);
  const ask = askAnswered && askedFor.data && askedFor.data.q === q ? askedFor.data.ask : null;
  const questionTitles = ask && ask.question ? askedFor.titles.filter((t) => !hideWatched || !t.watched) : [];
  const libraryShows = useLoad(() => api.shows().catch(() => []), []);
  // The page's rows and categories, and what the filter picks from, for everything here that is not a
  // search, a person or a genre.
  const index = useLoad(() => (q || person || genre ? Promise.resolve(null) : api.discover()), [!!(q || person || genre)]);

  const words = q.toLowerCase().split(/\s+/).filter(Boolean);
  const named = (name) => words.every((w) => name.toLowerCase().includes(w));
  const inLibrary = q
    ? (titles.data || []).filter((t) => t.kind !== "show" && named(filmName(t) + " " + t.id))
    : [];
  const showsHere = q ? (libraryShows.data || []).filter((s) => named(s.title)) : [];
  const heldShows = new Set((libraryShows.data || []).map((s) => s.imdbId));

  const filmsHere = new Map((titles.data || []).filter((t) => t.kind !== "show" && t.film).map((t) => [t.film.imdbId, t]));

  // What the library holds by that name and the catalogue did not answer with, which has no other
  // place on the page.
  const answeredIds = new Set(found.map((t) => t.imdbId));
  const libraryFilms = inLibrary.filter((t) => !(t.film && answeredIds.has(t.film.imdbId)));
  const libraryShowsNamed = showsHere.filter((s) => !answeredIds.has(s.imdbId));
  const others = libraryFilms.length + libraryShowsNamed.length;
  const showsHereById = new Map((libraryShows.data || []).filter((s) => s.imdbId).map((s) => [s.imdbId, s]));
  const where = (t) => ({ here: t.kind === "film" ? filmsHere.get(t.imdbId) : null, held: t.kind === "show" && heldShows.has(t.imdbId) });
  const [best, ...rest] = narrowed ? [] : found;
  const everything = questionTitles.length + (narrowed ? 0 : found.length + others);
  const carriesWords = [...questionTitles, ...found].some((t) => named(t.title || ""));
  const offerAssistant = online && q && answered && askAnswered
    && (everything === 0 || !!(ask && ask.question && ask.message) || (!(ask && ask.question) && !carriesWords));

  const submit = (typed) => navigate(discoverPath(typed.trim()));

  // The facets of what the page answers, and whether its titles say what the person watched.
  const section = category && index.data ? index.data.sections.find((s) => s.id === category) : null;
  const facetsFor = q ? (online ? () => api.askFacets(q, asked) : null)
    : person ? () => api.cataloguePersonFacets(person, asked)
      : genre ? () => api.catalogueGenreFacets(genreKind, genre, genreCategory, asked)
        : shelf ? () => api.discoverShelfFacets(shelf, asked)
          : category ? (section && !section.series ? () => api.discoverSectionFacets(category, asked) : null)
            : online ? () => api.catalogueDiscoverFacets(filterQuestion(picks)) : null;
  const facets = useFacets(facetsFor, [q, person, genre, genreKind, genreCategory, shelf, category, !!section, online, pickKey].join("|"));
  const ordering = useDiscoverOrder(query, (facets && facets.sorts) || [], { shelf: showsShelf, filtering: showsFilter });
  const sections = facetsFor ? sectionsOf(facets, picks, { watched: !person && !genre }) : [];
  // A search that is no question has nothing to filter; its drawer appears once its facets say otherwise.
  const filterable = sections.some((s) => s.kind !== "toggle") && (!q || narrowed || (facets && facets.sections.length > 0));
  const change = (next) => {
    if (next.watched !== hideWatched) setHideWatched(next.watched);
    const nextPicks = picksOfValues(next);
    if (JSON.stringify(apiPicks(nextPicks)) === pickKey) return;
    const landing = !q && !browsing;
    navigate(withPicks(landing ? new URLSearchParams(discoverFilterPath().split("?")[1]) : query, nextPicks), { replace: !landing });
  };
  const drawn = { sections, values: drawerValues(picks, hideWatched), onValues: change };
  const offered = ordering && ordering.orders.length > 0;
  const ordered = offered ? withOrder(drawn, ordering.orders, ordering.order, ordering.pick) : drawn;
  const drawer = filterable || offered
    ? <FilterDrawer sections={ordered.sections} values={ordered.values} onChange={ordered.onValues} />
    : undefined;

  return (
    <div className="kc-page kc-discover">
      <PageHead title="Discover" />
      <div className="kc-discover-bar">
      <SearchField ref={field} size="xl" className={DISCOVER_FIELD} value={draft} onChange={setDraft} onSubmit={submit}
        disabled={!online} placeholder="Any movie or show" filters={drawer} />
      </div>

      {!q && person && <PersonWork key={person} personId={person} picks={picks} where={where} />}
      {!q && !person && genre && <GenreTitles key={genreKind + genre + genreCategory} kind={genreKind} genre={genre} category={genreCategory} picks={picks} where={where} />}
      {showsShelf && <ShelfPage key={shelf} id={shelf} sort={ordering.sort} picks={picks} where={where} />}
      {!q && !person && !genre && !shelf && category && <CategoryPage key={category} id={category} index={index} picks={picks} where={where} />}
      {showsFilter && <FilterPage sort={ordering.sort} picks={picks} where={where} />}
      {!q && !browsing && index.error && !index.data && <Failed error={index.error} onRetry={index.reload} />}
      {!q && !browsing && !index.data && !index.error && <FrontSkeleton />}
      {!q && !browsing && index.data && (
        <>
          <DiscoverSpotlight key={index.data.spotlight} index={index.data} where={where} />
          <DiscoverRows index={index.data} where={where} />
        </>
      )}

      {q && (
        <>
          {!(answered && askAnswered) && <ResultsSkeleton />}
          {ask && ask.question && (ask.parts.length > 0 || questionTitles.length > 0) && (
            <div className="kc-keywords">{ask.parts.map((p, i) => <Pill key={i + p.part + p.text} tone="accent">{p.text}</Pill>)}</div>
          )}
          {questionTitles.length > 0 && (
            <div className="kc-grid">
              {questionTitles.map((t) => <FoundCard key={t.kind + (t.imdbId || t.tmdbId)} found={t} {...where(t)} />)}
            </div>
          )}
          {questionTitles.length > 0 && <MoreCards paged={askedFor} />}
          {best && <BestMatch key={best.imdbId} found={best} {...where(best)} shelf={showsHereById.get(best.imdbId)} />}
          {(rest.length + others > 0) && (
            <section className="kc-shelf">
              <div className="kc-shelf__head"><h2>More results</h2></div>
              <div className="kc-grid">
                {rest.map((t) => <FoundCard key={t.imdbId} found={t} {...where(t)} />)}
                {libraryFilms.map((t) => <FilmCard key={t.id} title={t} />)}
                {libraryShowsNamed.map((s) => <ShowCard key={s.imdbId || s.firstTitleId} show={s} />)}
              </div>
            </section>
          )}

          {answered && askAnswered && everything === 0 && <Empty icon="search" title="Nothing by that name" />}
          {offerAssistant && <Button size="xl" variant="primary" icon="bot" onClick={() => askAssistant(q)}>Ask the assistant</Button>}
        </>
      )}
    </div>
  );
}

/**
 * The page's spotlight: the films on the season's shelf, or the week's popular films out of season,
 * as the top of Home draws its slides.
 */
function DiscoverSpotlight({ index, where }) {
  const slides = useDiscoverSlides(index, where);
  return <Spotlight slides={slides} where={where} />;
}

/** The department the film database knows somebody for, as a word for them. */
const KNOWN_AS = { Acting: "Actor", Directing: "Director", Writing: "Writer", Production: "Producer" };

/**
 * The films and shows one person is credited on, best known first, each card with the part they
 * played or the job they did, marked and played like any other answer when the library holds it.
 */
function PersonWork({ personId, picks, where }) {
  const asked = apiPicks(picks);
  const work = useLoad(() => api.cataloguePerson(personId, asked), [personId, JSON.stringify(asked)]);
  const p = work.data;
  return (
    <>
      <Failed error={work.error} onRetry={work.reload} />
      {!p && !work.error && <BrowseSkeleton portrait />}
      {p && (
        <section className="kc-shelf">
          <div className="kc-browse-head">
            <Avatar src={p.portraitUrl} name={p.name} tone="muted" size="css" className="kc-cast__face kc-browse-head__face" />
            <div className="kc-browse-head__text">
              {p.knownFor && <span className="kc-eyebrow">{KNOWN_AS[p.knownFor] || p.knownFor}</span>}
              <h2>{p.name}</h2>
            </div>
            <span className="kc-count">{p.titles.length}</span>
          </div>
          {p.titles.length > 0
            ? (
              <div className="kc-grid">
                {p.titles.map((t) => <FoundCard key={t.kind + t.imdbId} found={t} {...where(t)} line={t.role} />)}
              </div>
            )
            : <Empty icon="user" title={pickCount(picks) > 0 ? "Nothing matches" : "No credits"} />}
          <p className="kc-attribution-note">Credits and artwork from TMDB.</p>
        </section>
      )}
    </>
  );
}

/** The catalogue's most popular films or shows in one genre, as the film database names it; of its anime alone with `category`. */
function GenreTitles({ kind, genre, category, picks, where }) {
  const asked = apiPicks(picks);
  const listed = useLoad(() => api.catalogueGenre(kind, genre, category, asked), [kind, genre, category, JSON.stringify(asked)]);
  const g = listed.data;
  return (
    <>
      <Failed error={listed.error} onRetry={listed.reload} />
      {!g && !listed.error && <BrowseSkeleton />}
      {g && (
        <section className="kc-shelf">
          <div className="kc-browse-head">
            <div className="kc-browse-head__text">
              <span className="kc-eyebrow">{category === ANIME ? (kind === "show" ? "Anime series" : "Anime films") : kind === "show" ? "TV Shows" : "Movies"}</span>
              <h2>{g.genre}</h2>
            </div>
            <span className="kc-count">{g.titles.length}</span>
          </div>
          {g.titles.length > 0
            ? (
              <div className="kc-grid">
                {g.titles.map((t) => <FoundCard key={t.imdbId} found={t} {...where(t)} />)}
              </div>
            )
            : <Empty icon={category === ANIME ? "swords" : kind === "show" ? "tv" : "clapperboard"} title={pickCount(picks) > 0 ? "Nothing matches" : "Nothing in this genre"} />}
          <p className="kc-attribution-note">Popular titles and artwork from TMDB.</p>
        </section>
      )}
    </>
  );
}

/** The heading and cards of a person's work or a genre while the catalogue is asked. */
function BrowseSkeleton({ portrait = false }) {
  return (
    <section className="kc-shelf">
      <div className="kc-browse-head">
        {portrait && <Skeleton w={72} h={72} r={999} />}
        <div className="kc-browse-head__text">
          <Skeleton w={60} h={12} />
          <Skeleton w={220} h={26} />
        </div>
      </div>
      <CardsSkeleton count={12} />
    </section>
  );
}

/**
 * The catalogue's first answer to a name, which is usually the one meant: large enough to recognise,
 * with its cast to tell it from another of the same name, and the actions its own page offers, asked
 * for as soon as it is shown, so it can be watched without opening it.
 */
function BestMatch({ found, here, held, shelf }) {
  const show = found.kind === "show";
  const page = foundPage(found, here, held);
  const playable = here && here.status === "ready";
  return (
    <article className="kc-hit">
      {found.posterUrl && <div className="kc-hit__art" style={{ backgroundImage: `url("${found.posterUrl}")` }} />}
      <div className="kc-hit__shade" />
      <div className="kc-hit__grid">
        <a className="kc-hit__poster" {...linkProps(page)} tabIndex={-1} aria-hidden="true">
          {found.posterUrl
            ? <img src={found.posterUrl} alt="" />
            : <Icon name={show ? "tv" : "clapperboard"} size={32} />}
        </a>
        <div className="kc-hit__body">
          <span className="kc-eyebrow">{show ? "TV series" : "Movie"}</span>
          <h2><a {...linkProps(page)}>{found.title}</a></h2>
          <div className="kc-tt__meta">
            <FilmScore rating={found.rating} votes={found.votes} by={found.ratedBy} compact />
            {found.year && <span className="kc-tt__year">{found.year}</span>}
          </div>
          {found.starring && <span className="kc-hit__cast">{found.starring}</span>}
        </div>
        <div className="kc-hit__more">
          {found.overview && <p className="kc-hit__overview">{found.overview}</p>}
          {show
            ? <ShowMatchActions found={found} shelf={shelf} />
            : here
              ? (
                <div className="kc-actions">
                  {playable && (
                    <Button variant="primary" size="xl" icon="play" onClick={() => navigate(watchHref(here.id))}>Watch</Button>
                  )}
                  <WantButton imdbId={found.imdbId} kind="film" title={found.title} />
                  <PlanButton imdbId={found.imdbId} kind="film" />
                  <Wanters imdbId={found.imdbId} />
                </div>
              )
              : <FilmMatchActions found={found} />}
        </div>
      </div>
    </article>
  );
}

/**
 * A show as its own page offers it: Watch or Resume when an episode can be played now, otherwise
 * Watch its first episode while it is prepared, from its seasons as the film database lists them.
 */
function ShowMatchActions({ found, shelf }) {
  const downloading = useCan("cinema:download");
  const described = useLoad(() => (shelf || !downloading ? Promise.resolve(null) : api.show(found.imdbId).catch(() => null)), [found.imdbId, shelf, downloading]);
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
      <WantButton imdbId={found.imdbId} kind="show" title={found.title} />
      <PlanButton imdbId={found.imdbId} kind="show" />
      <Wanters imdbId={found.imdbId} />
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
