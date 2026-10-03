import React from "react";
import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api, filmName, showHref } from "../api.js";
import { useLoad } from "../data.js";
import { linkProps, navigate } from "../router.js";
import { Empty, Failed, FilmCard, PageHead, ShowCard } from "../components/Film.jsx";

/**
 * Finding a film: what the library holds first, then what the tracker offers to fetch, and, for a
 * film that has no release yet, a way to be told when it does. The query is in the address, so a
 * search can be shared and reloaded.
 */
export function Search({ query }) {
  const q = (query.get("q") || "").trim();
  const [draft, setDraft] = React.useState(q);
  React.useEffect(() => setDraft(q), [q]);

  const titles = useLoad(() => api.titles(), []);
  const tracker = useLoad(() => (q ? api.releases(q) : Promise.resolve(null)), [q]);
  const found = useLoad(() => (q ? api.findShows(q).catch(() => []) : Promise.resolve([])), [q]);
  const films = useLoad(() => (q ? api.findFilms(q).catch(() => []) : Promise.resolve([])), [q]);
  const libraryShows = useLoad(() => api.shows().catch(() => []), []);

  const words = q.toLowerCase().split(/\s+/).filter(Boolean);
  const named = (name) => words.every((w) => name.toLowerCase().includes(w));
  const inLibrary = q
    ? (titles.data || []).filter((t) => t.kind !== "show" && named(filmName(t) + " " + t.id))
    : [];
  const showsHere = q ? (libraryShows.data || []).filter((s) => named(s.title)) : [];
  const heldShows = new Set((libraryShows.data || []).map((s) => s.imdbId));

  const submit = (e) => {
    e.preventDefault();
    navigate("/search?q=" + encodeURIComponent(draft.trim()));
  };

  return (
    <div className="kc-page">
      <PageHead title="Search" sub="The library first, then everything the tracker can find." />
      <form className="kc-search" onSubmit={submit}>
        <Icon name="search" size={17} />
        <input autoFocus value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="Movies and shows" />
        <button type="submit" className="kc-btn kc-btn--primary">Search</button>
      </form>

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

          <section className="kc-shelf">
            <div className="kc-shelf__head"><h2>On the tracker</h2></div>
            <Failed error={tracker.error} onRetry={tracker.reload} />
            {tracker.loading && <span className="kc-quiet">Asking the tracker…</span>}
            {tracker.data && <TrackerResults ranked={tracker.data} inLibrary={inLibrary} />}
          </section>
        </>
      )}

      {!q && <Empty icon="search" title="Search movies and shows" />}
    </div>
  );
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
