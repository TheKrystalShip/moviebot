import React from "react";
import { Icon, Rail, ShowcaseCard, Skeleton, toast } from "@thekrystalship/krystal-ui";
import { api, filmName } from "../api.js";
import { useLoad } from "../data.js";
import { useCan } from "../session.js";
import { discoverFilmPath, discoverPath, moviePath, showPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { Failed, FilmCard, PageHead, ShowCard, watchHref } from "../components/Film.jsx";
import { SaveButton } from "../components/SaveButton.jsx";

/** The class the page's field carries, so the shortcut that opens the page can find it. */
export const DISCOVER_FIELD = "kc-search--discover";

/**
 * Finding anything to watch, here or not. With nothing typed, the field and what is popular this
 * week. With a query, the catalogue's films and shows in its own order, each by its poster: the first
 * answer large, the rest as cards, one the library holds marked and played from here. Then whatever
 * the library holds by that name that the catalogue did not answer with, and the tracker's releases
 * on request. Every answer leads to its own page. The query is in the address, so a search can be
 * shared and reloaded.
 */
export function Discover({ query }) {
  const q = (query.get("q") || "").trim();
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
  const popular = useLoad(() => (q ? Promise.resolve(null) : api.popular().catch(() => null)), [q]);

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
      <form className={"kc-search " + DISCOVER_FIELD} onSubmit={submit} role="search">
        <Icon name="search" size={20} />
        <input ref={field} value={draft} onChange={(e) => setDraft(e.target.value)}
          placeholder="Any movie or show" aria-label="Any movie or show" enterKeyHint="search" />
        <button type="submit" className="kc-btn kc-btn--primary">Search</button>
      </form>

      {!q && popular.data && (
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
          {best && <BestMatch found={best} {...where(best)} />}
          {rest.length > 0 && (
            <section className="kc-shelf">
              <div className="kc-shelf__head"><h2>More results</h2></div>
              <div className="kc-grid">
                {rest.map((t) => <FoundCard key={t.imdbId} found={t} {...where(t)} />)}
              </div>
            </section>
          )}

          {answered && (libraryFilms.length + libraryShowsNamed.length > 0 || found.length === 0) && (
            <section className="kc-shelf">
              <div className="kc-shelf__head"><h2>In the library</h2></div>
              {libraryFilms.length + libraryShowsNamed.length > 0
                ? (
                  <div className="kc-grid">
                    {libraryFilms.map((t) => <FilmCard key={t.id} title={t} />)}
                    {libraryShowsNamed.map((s) => <ShowCard key={s.imdbId || s.firstTitleId} show={s} />)}
                  </div>
                )
                : <span className="kc-quiet">Nothing in the library is called that.</span>}
            </section>
          )}

          {answered && downloading && (
            <TrackerShelf key={q} q={q} inLibrary={inLibrary} open={found.length === 0} />
          )}
        </>
      )}
    </div>
  );
}

/**
 * The tracker's releases for what was typed, asked for when opened: each film and show above already
 * leads to the release that suits it, so this is for a name the catalogue does not know. It opens by
 * itself when the catalogue found nothing.
 */
function TrackerShelf({ q, inLibrary, open: openAtFirst }) {
  const [open, setOpen] = React.useState(openAtFirst);
  return (
    <section className="kc-shelf">
      <button type="button" className="kc-shelf__head kc-shelf__head--toggle" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        <h2>On the tracker</h2>
        <Icon name={open ? "chevron-up" : "chevron-down"} size={18} />
      </button>
      {open && <TrackerReleases q={q} inLibrary={inLibrary} />}
    </section>
  );
}

function TrackerReleases({ q, inLibrary }) {
  const tracker = useLoad(() => api.releases(q), [q]);
  return (
    <>
      <Failed error={tracker.error} onRetry={tracker.reload} />
      {tracker.loading && <span className="kc-quiet">Asking the tracker…</span>}
      {tracker.data && <TrackerResults ranked={tracker.data} inLibrary={inLibrary} />}
    </>
  );
}

/**
 * A film or show trending this week. One the library holds says so and plays; any other opens its
 * page, which is where whether and how it can be watched is asked.
 */
function PopularCard({ entry, show = false, here, held }) {
  const inLibrary = show ? held : !!here;
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
      status={inLibrary ? { tone: "success", icon: "check", label: "In the library" } : null}
      action={playable
        ? { label: "Watch", onClick: () => navigate(watchHref(here.id)) }
        : { label: inLibrary ? "Open" : "View" }} />
  );
}

/** Where a found film or show leads: the library's own page when it is here, its catalogue page when not. */
function foundPage(found, here, held) {
  if (found.kind === "show") return showPath(found.imdbId, { held });
  return here ? moviePath(here.id) : discoverFilmPath(found.imdbId);
}

/**
 * The catalogue's first answer to a name, which is usually the one meant: large enough to recognise,
 * with its cast to tell it from another of the same name, and what can be done with it from here.
 */
function BestMatch({ found, here, held }) {
  const show = found.kind === "show";
  const page = foundPage(found, here, held);
  const inLibrary = show ? held : !!here;
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
            {found.year && <span className="kc-tt__year">{found.year}</span>}
            {inLibrary && <span className="kc-pill kc-pill--here"><Icon name="check" size={13} />In the library</span>}
          </div>
          {found.starring && <span className="kc-hit__cast">{found.starring}</span>}
          <div className="kc-hit__actions">
            {playable && (
              <button type="button" className="kc-btn kc-btn--primary" onClick={() => navigate(watchHref(here.id))}>
                <Icon name="play" size={16} />Watch
              </button>
            )}
            <SaveButton imdbId={found.imdbId} kind={show ? "show" : "film"} title={found.title} />
          </div>
        </div>
      </div>
    </article>
  );
}

/** Any other answer, as a poster card: its year, whether it is a film or a show, and its cast. */
function FoundCard({ found, here, held }) {
  const show = found.kind === "show";
  const inLibrary = show ? held : !!here;
  const playable = here && here.status === "ready";
  return (
    <ShowcaseCard
      className="kc-found"
      name={found.title}
      art={found.posterUrl ? `url("${found.posterUrl}")` : null}
      onPick={() => navigate(foundPage(found, here, held))}
      specs={[
        { icon: "calendar", value: found.year || null, label: "Year" },
        { icon: show ? "tv" : "clapperboard", value: show ? "Series" : "Movie", label: "Type" },
      ]}
      status={inLibrary
        ? { tone: "success", icon: "check", label: "In the library" }
        : found.starring ? { label: found.starring, title: found.starring } : null}
      action={playable
        ? { label: "Watch", onClick: () => navigate(watchHref(here.id)) }
        : { label: inLibrary ? "Open" : "View" }} />
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
      <div className="kc-grid" aria-hidden="true">
        {Array.from({ length: 6 }, (_, i) => (
          <div key={i} className="skel-card">
            <Skeleton className="skel-card__art" h="auto" r={0} style={{ aspectRatio: "2 / 3" }} />
            <div className="kc-found__skel-foot">
              <Skeleton h={30} />
              <Skeleton w="70%" h={12} />
            </div>
          </div>
        ))}
      </div>
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

function TrackerResults({ ranked, inLibrary }) {
  const here = ranked.film && inLibrary.some((t) => t.film && t.film.imdbId === ranked.film.imdbId);

  if (ranked.candidates.length === 0) {
    return ranked.film
      ? <NotOutYet film={ranked.film} explanation={ranked.emptyExplanation} />
      : <span className="kc-quiet">{ranked.emptyExplanation || "The tracker has nothing by that name."}</span>;
  }

  return (
    <div className="kc-rows">
      {here && <span className="kc-quiet">{ranked.film.display} is already in the library.</span>}
      {ranked.candidates.map((offer, i) => <Offer key={offer.torrentId} offer={offer} best={i === 0} />)}
    </div>
  );
}

/** One release the tracker offers, with the button that fetches it. */
function Offer({ offer, best }) {
  const [state, setState] = React.useState("idle");

  const fetchIt = async () => {
    setState("busy");
    try {
      const reply = await api.download(offer.torrentId);
      if (reply.outcome === "started") {
        setState("started");
        toast.success(`Downloading ${offer.display}. It appears in the library as soon as enough of it has arrived.`);
      } else {
        setState("idle");
        toast.error(downloadWords(reply));
      }
    } catch (error) {
      setState("idle");
      toast.fromError(error, "The download did not start");
    }
  };

  return (
    <div className="kc-row">
      <div className="kc-row__text">
        <span className="kc-row__title">{offer.display}{best && <span className="kc-tag">Best</span>}</span>
        <span className="kc-row__sub">{offer.summary}</span>
        <span className="kc-row__sub kc-mono">{offer.releaseName}</span>
      </div>
      {state === "started"
        ? <button type="button" className="kc-btn kc-btn--ghost" disabled><Icon name="check" size={16} />On its way</button>
        : <button type="button" className="kc-btn kc-btn--primary" disabled={state === "busy"} onClick={fetchIt}><Icon name="download" size={16} />Download</button>}
    </div>
  );
}

/** A film the catalogue knows and the tracker has nothing of yet; waiting on it is offered to whoever may wish. */
export function NotOutYet({ film, explanation }) {
  const wishing = useCan("cinema:wish");
  const [asked, setAsked] = React.useState(false);

  const wait = async () => {
    try {
      const reply = await api.wish(film.imdbId);
      setAsked(true);
      toast.info(wishWords(reply, film.display));
    } catch (error) {
      toast.fromError(error, "Could not wait on it");
    }
  };

  return (
    <div className="kc-row">
      <div className="kc-row__text">
        <span className="kc-row__title">{film.display}</span>
        <span className="kc-row__sub"><Icon name="calendar-clock" size={13} />{explanation || "Not out yet."}</span>
      </div>
      {wishing && (
        <button type="button" className={"kc-btn" + (asked ? " kc-btn--ghost" : " kc-btn--primary")} disabled={asked} onClick={wait}>
          <Icon name={asked ? "bell-ring" : "bell-plus"} size={16} />{asked ? "Waiting for it" : "Notify me"}
        </button>
      )}
    </div>
  );
}

export function downloadWords(reply) {
  switch (reply && reply.outcome) {
    case "noLongerOffered": return "That release is no longer on offer. Search again.";
    case "refused": return reply.refusal || "The download was refused.";
    default: return "The tracker or the torrent client is not answering. Try again in a minute.";
  }
}

export function wishWords(reply, name) {
  switch (reply && reply.outcome) {
    case "subscribed": return `You will be told when ${name} can be downloaded.`;
    case "alreadySubscribed": return `You are already waiting on ${name}.`;
    case "alreadyAvailable": return `${name} can be downloaded now. Search for it.`;
    case "inLibrary": return `${reply.libraryName || name} is already in the library.`;
    case "downloading": return `${name} is already on its way.`;
    case "notAFilm": return `${name} is not a film, and only films can be fetched here.`;
    default: return `Nothing in the catalogue answers to ${name}.`;
  }
}
