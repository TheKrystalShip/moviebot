import React from "react";
import { Button, Icon, Rail, ShowcaseCard, Skeleton } from "@thekrystalship/krystal-ui";
import { api, filmName } from "../api.js";
import { useLoad } from "../data.js";
import { useOnline } from "../connectivity.js";
import { useCan } from "../session.js";
import { discoverFilmPath, discoverPath, moviePath, showPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { Empty, Failed, FilmCard, PageHead, ShowCard, watchHref } from "../components/Film.jsx";
import { FilmActions } from "../components/FilmActions.jsx";
import { FilmScore, initials } from "../components/FilmDetails.jsx";
import { SaveButton } from "../components/SaveButton.jsx";
import { CardsSkeleton, FoundCard, foundPage } from "../components/TitleCards.jsx";
import { openingEpisode, watchOpening } from "../watchNow.js";

/** The class the page's field carries, so the shortcut that opens the page can find it. */
export const DISCOVER_FIELD = "kc-search--discover";

/**
 * Finding anything to watch, here or not. With nothing typed, the field and what is popular this
 * week. With a query, the catalogue's films and shows in its own order, each by its poster: the first
 * answer large, the rest as cards, followed by anything the server can play by that name that the
 * catalogue did not answer with. A result is a result wherever it is played from: one that can be
 * played now plays from here, and nothing says where it comes from. With a person (`person`, the film database's id for them) instead, the films and shows
 * they are credited on, best known first; with a genre (`genre` and `kind`), the catalogue's most
 * popular films or shows in it. Every answer leads to its own page. What is shown is in the address,
 * so it can be shared and reloaded.
 */
export function Discover({ query }) {
  const online = useOnline();
  const q = (query.get("q") || "").trim();
  const person = /^\d+$/.test(query.get("person") || "") ? query.get("person") : null;
  const genre = (query.get("genre") || "").trim();
  const genreKind = query.get("kind") === "show" ? "show" : "film";
  const browsing = !q && !!(person || genre);
  const [draft, setDraft] = React.useState(q);
  React.useEffect(() => setDraft(q), [q]);
  const field = useFocusOnceShown();

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
  const libraryShows = useLoad(() => api.shows().catch(() => []), []);
  const popular = useLoad(() => (q || browsing ? Promise.resolve(null) : api.popular().catch(() => null)), [q, browsing]);

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
  const [best, ...rest] = found;

  const submit = (e) => {
    e.preventDefault();
    const typed = draft.trim();
    navigate(discoverPath(typed));
  };

  return (
    <div className="kc-page kc-discover">
      <PageHead title="Discover" />
      <form className={"kc-search " + DISCOVER_FIELD + (online ? "" : " is-off")} onSubmit={submit} role="search">
        <Icon name="search" size={20} />
        <input ref={field} value={draft} onChange={(e) => setDraft(e.target.value)} disabled={!online}
          placeholder="Any movie or show" aria-label="Any movie or show" enterKeyHint="search" />
        <Button type="submit" variant="primary" size="lg" disabled={!online}>Search</Button>
      </form>

      {!q && person && <PersonWork key={person} personId={person} where={where} />}
      {!q && !person && genre && <GenreTitles key={genreKind + genre} kind={genreKind} genre={genre} where={where} />}

      {!q && !browsing && popular.data && (
        <>
          {popular.data.films.length > 0 && (
            <Rail
              variant="posters" icon="flame" title="Popular movies now" count={popular.data.films.length}
              items={popular.data.films} itemKey={(f) => f.imdbId}
              renderItem={(f) => <PopularCard entry={f} here={filmsHere.get(f.imdbId)} />} />
          )}
          {popular.data.shows.length > 0 && (
            <Rail
              variant="posters" icon="flame" title="Popular shows now" count={popular.data.shows.length}
              items={popular.data.shows} itemKey={(s) => s.imdbId}
              renderItem={(s) => <PopularCard entry={s} show held={heldShows.has(s.imdbId)} />} />
          )}
          <p className="kc-attribution-note">Popular titles and artwork from TMDB.</p>
        </>
      )}

      {q && (
        <>
          {!answered && <ResultsSkeleton />}
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

          {answered && found.length + others === 0 && <Empty icon="search" title="Nothing by that name" />}
        </>
      )}
    </div>
  );
}

/**
 * A film or show trending this week. One that can be played now plays; any other opens its page,
 * which is where whether and how it can be watched is asked.
 */
function PopularCard({ entry, show = false, here, held }) {
  const page = show ? showPath(entry.imdbId, { held }) : here ? moviePath(here.id) : discoverFilmPath(entry.imdbId);
  const playable = here && here.status === "ready";
  return (
    <ShowcaseCard
      name={entry.title}
      art={entry.posterUrl ? `url("${entry.posterUrl}")` : null}
      onPick={() => navigate(page)}
      specs={[
        { icon: "star", value: entry.rating ? entry.rating.toFixed(1) : null, label: "Rating" },
        { icon: "calendar", value: entry.year || null, label: "Year" },
      ]}
      action={playable
        ? { label: "Watch", onClick: () => navigate(watchHref(here.id)) }
        : { label: "View" }} />
  );
}

/** The department the film database knows somebody for, as a word for them. */
const KNOWN_AS = { Acting: "Actor", Directing: "Director", Writing: "Writer", Production: "Producer" };

/**
 * The films and shows one person is credited on, best known first, each card with the part they
 * played or the job they did, marked and played like any other answer when the library holds it.
 */
function PersonWork({ personId, where }) {
  const work = useLoad(() => api.cataloguePerson(personId), [personId]);
  const p = work.data;
  return (
    <>
      <Failed error={work.error} onRetry={work.reload} />
      {!p && !work.error && <BrowseSkeleton portrait />}
      {p && (
        <section className="kc-shelf">
          <div className="kc-browse-head">
            <span className="kc-cast__face kc-browse-head__face">
              {p.portraitUrl ? <img src={p.portraitUrl} alt="" /> : initials(p.name)}
            </span>
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
            : <Empty icon="user" title="No credits" />}
          <p className="kc-attribution-note">Credits and artwork from TMDB.</p>
        </section>
      )}
    </>
  );
}

/** The catalogue's most popular films or shows in one genre, as the film database names it. */
function GenreTitles({ kind, genre, where }) {
  const listed = useLoad(() => api.catalogueGenre(kind, genre), [kind, genre]);
  const g = listed.data;
  return (
    <>
      <Failed error={listed.error} onRetry={listed.reload} />
      {!g && !listed.error && <BrowseSkeleton />}
      {g && (
        <section className="kc-shelf">
          <div className="kc-browse-head">
            <div className="kc-browse-head__text">
              <span className="kc-eyebrow">{kind === "show" ? "TV Shows" : "Movies"}</span>
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
            : <Empty icon={kind === "show" ? "tv" : "clapperboard"} title="Nothing in this genre" />}
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
            <FilmScore rating={found.rating} votes={found.votes} compact />
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
                  <SaveButton imdbId={found.imdbId} kind="film" title={found.title} />
                </div>
              )
              : <FilmMatchActions found={found} />}
        </div>
      </div>
    </article>
  );
}

/** A film that has to be fetched, as its own page offers it: whether it can be is asked as soon as it is shown. */
function FilmMatchActions({ found }) {
  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const state = useLoad(() => (downloading || wishing ? api.availability(found.imdbId) : Promise.resolve(null)), [found.imdbId, downloading, wishing]);
  const film = { title: found.title, year: found.year, posterUrl: found.posterUrl, directors: [] };
  return <FilmActions imdbId={found.imdbId} film={film} state={state} />;
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
      <SaveButton imdbId={found.imdbId} kind="show" title={found.title} />
    </div>
  );
}

/** The frame of the results while the catalogue is asked, so they settle in place. */
function ResultsSkeleton() {
  return (
    <>
      <div className="kc-hit">
        <div className="kc-hit__grid">
          <span className="skel kc-hit__poster" />
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

export function wishWords(reply, name) {
  switch (reply && reply.outcome) {
    case "subscribed": return `You will be told when ${name} can be watched.`;
    case "alreadySubscribed": return `You are already waiting on ${name}.`;
    case "alreadyAvailable":
    case "inLibrary":
    case "downloading": return `${(reply && reply.libraryName) || name} can be watched now.`;
    case "notAFilm": return `${name} is not a film, and only films can be waited on.`;
    default: return `Nothing in the catalogue answers to ${name}.`;
  }
}
