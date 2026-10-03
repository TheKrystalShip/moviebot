import React from "react";
import { Icon, Rail, ShowcaseCard, toast } from "@thekrystalship/krystal-ui";
import { api, filmName, showHref } from "../api.js";
import { useLoad } from "../data.js";
import { linkProps, navigate } from "../router.js";
import { Failed, FilmCard, PageHead, ShowCard, watchHref } from "../components/Film.jsx";

/** The class the page's field carries, so the shortcut that opens the page can find it. */
export const DISCOVER_FIELD = "kc-search--discover";

/**
 * Finding anything to watch, here or not. With nothing typed, the field and what is popular this
 * week; with a query, what the library holds first, then the catalogue's films and shows, each
 * leading to its own page, and the tracker's releases on request. The query is in the address, so a
 * search can be shared and reloaded.
 */
export function Discover({ query }) {
  const q = (query.get("q") || "").trim();
  const [draft, setDraft] = React.useState(q);
  React.useEffect(() => setDraft(q), [q]);
  const field = useFocusOnceShown();

  const titles = useLoad(() => api.titles(), []);
  // Each answer carries the query it is for: until this query's comes back, the one held is the last
  // query's, and says nothing about whether the catalogue knows this name.
  const foundFor = useLoad(() => (q ? api.findShows(q).catch(() => []) : Promise.resolve([])).then((list) => ({ q, list })), [q]);
  const filmsFor = useLoad(() => (q ? api.findFilms(q).catch(() => []) : Promise.resolve([])).then((list) => ({ q, list })), [q]);
  const answered = foundFor.data && foundFor.data.q === q && filmsFor.data && filmsFor.data.q === q;
  const found = { data: answered ? foundFor.data.list : null };
  const films = { data: answered ? filmsFor.data.list : null };
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

  const submit = (e) => {
    e.preventDefault();
    const typed = draft.trim();
    navigate(typed ? "/discover?q=" + encodeURIComponent(typed) : "/discover");
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
          <section className="kc-shelf">
            <div className="kc-shelf__head"><h2>In the library</h2></div>
            {inLibrary.length + showsHere.length > 0
              ? (
                <div className="kc-grid">
                  {inLibrary.map((t) => <FilmCard key={t.id} title={t} />)}
                  {showsHere.map((s) => <ShowCard key={s.imdbId || s.firstTitleId} show={s} />)}
                </div>
              )
              : <span className="kc-quiet">Nothing in the library is called that.</span>}
          </section>

          {(films.data || []).length > 0 && (
            <section className="kc-shelf">
              <div className="kc-shelf__head"><h2>Movies</h2></div>
              <div className="kc-rows">
                {films.data.map((f) => {
                  const held = (titles.data || []).find((t) => t.film && t.film.imdbId === f.imdbId && t.kind !== "show");
                  return (
                    <a key={f.imdbId} className="kc-row kc-row--link"
                      {...linkProps(held ? "/title/" + encodeURIComponent(held.id) : "/film/" + encodeURIComponent(f.imdbId))}>
                      <span className="kc-row__icon"><Icon name="clapperboard" size={16} /></span>
                      <div className="kc-row__text">
                        <span className="kc-row__title">{f.display}</span>
                        <span className="kc-row__sub">Movie{held ? " · in the library" : ""}</span>
                      </div>
                      <span className="kc-btn kc-btn--ghost"><Icon name={held ? "play" : "chevron-right"} size={16} />{held ? "Open" : "See how to watch"}</span>
                    </a>
                  );
                })}
              </div>
            </section>
          )}

          {(found.data || []).length > 0 && (
            <section className="kc-shelf">
              <div className="kc-shelf__head"><h2>Shows</h2></div>
              <div className="kc-rows">
                {found.data.map((s) => (
                  <a key={s.imdbId} className="kc-row kc-row--link" {...linkProps(showHref(s.imdbId))}>
                    <span className="kc-row__icon"><Icon name="tv" size={16} /></span>
                    <div className="kc-row__text">
                      <span className="kc-row__title">{s.display}</span>
                      <span className="kc-row__sub">TV series{heldShows.has(s.imdbId) ? " · in the library" : ""}</span>
                    </div>
                    <span className="kc-btn kc-btn--ghost"><Icon name={heldShows.has(s.imdbId) ? "play" : "download"} size={16} />{heldShows.has(s.imdbId) ? "Open" : "Pick episodes"}</span>
                  </a>
                ))}
              </div>
            </section>
          )}

          {answered && (
            <TrackerShelf key={q} q={q} inLibrary={inLibrary}
              open={(films.data || []).length === 0 && (found.data || []).length === 0} />
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
  const page = show ? showHref(entry.imdbId) : here ? "/title/" + encodeURIComponent(here.id) : "/film/" + encodeURIComponent(entry.imdbId);
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

/** A film the catalogue knows and the tracker has nothing of yet. */
export function NotOutYet({ film, explanation }) {
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
      <button type="button" className={"kc-btn" + (asked ? " kc-btn--ghost" : " kc-btn--primary")} disabled={asked} onClick={wait}>
        <Icon name={asked ? "bell-ring" : "bell-plus"} size={16} />{asked ? "Waiting for it" : "Notify me"}
      </button>
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
