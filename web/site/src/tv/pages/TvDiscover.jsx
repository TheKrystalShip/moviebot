import React from "react";
import { Button, Icon, TvCard, TvRow } from "@thekrystalship/krystal-ui";
import { api, filmName, posterUrl } from "../../api.js";
import { useLoad } from "../../data.js";
import { ANIME, discoverPath } from "../../paths.js";
import { linkProps, navigate } from "../../router.js";
import { useCan } from "../../session.js";
import { watchHref } from "../../components/Film.jsx";
import { SaveButton } from "../../components/SaveButton.jsx";
import { foundPage } from "../../components/TitleCards.jsx";
import { Failed } from "../../components/Film.jsx";
import { initials } from "../../components/FilmDetails.jsx";
import { startFilm } from "./TvFilm.jsx";
import { TvCategory, TvDiscoverFront, TvShelf } from "./TvDiscoverShelves.jsx";

/**
 * Finding anything to watch, on a television. The field is focused when the page opens; OK on it
 * raises the system keyboard, which types, speaks and takes a phone, and its search key searches.
 * With nothing typed, the spotlight and the page's rows (`/api/discover`), with the moods, the
 * franchises and the browse categories as rows of tiles; `browse` is one category and `shelf` every
 * title on one shelf. With a query, the catalogue's first answer large,
 * with Watch, More info and Save, then the rest of what it answered and whatever the library holds by
 * that name, all as one row. The query is in the address, as on every surface.
 */
export function TvDiscover({ query }) {
  const q = (query.get("q") || "").trim();
  const person = /^\d+$/.test(query.get("person") || "") ? query.get("person") : null;
  const genre = (query.get("genre") || "").trim();
  const genreKind = query.get("kind") === "show" ? "show" : "film";
  const genreCategory = query.get("category") === ANIME ? ANIME : null;
  const shelf = (query.get("shelf") || "").trim();
  const category = (query.get("browse") || "").trim();
  const browsing = !q && !!(person || genre || shelf || category);
  const [draft, setDraft] = React.useState(q);
  React.useEffect(() => setDraft(q), [q]);
  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const finding = downloading || wishing;

  const titles = useLoad(() => api.titles(), []);
  const libraryShows = useLoad(() => api.shows().catch(() => []), []);
  const index = useLoad(() => (q || person || genre || shelf ? Promise.resolve(null) : api.discover()), [!!(q || person || genre || shelf)]);
  const foundFor = useLoad(() => (q && finding
    ? (downloading ? api.findTitles(q) : api.findFilms(q)).catch(() => [])
    : Promise.resolve([])).then((list) => ({ q, list })), [q, downloading, finding]);
  const answered = !finding || (foundFor.data && foundFor.data.q === q);
  const found = answered && foundFor.data ? foundFor.data.list : [];

  const filmsHere = new Map((titles.data || []).filter((t) => t.kind !== "show" && t.film).map((t) => [t.film.imdbId, t]));
  const heldShows = new Set((libraryShows.data || []).map((s) => s.imdbId));
  const where = (t) => ({ here: t.kind === "film" ? filmsHere.get(t.imdbId) : null, held: t.kind === "show" && heldShows.has(t.imdbId) });

  // What the library holds by that name that the catalogue did not answer with.
  const words = q.toLowerCase().split(/\s+/).filter(Boolean);
  const named = (name) => words.every((w) => name.toLowerCase().includes(w));
  const answeredIds = new Set(found.map((t) => t.imdbId));
  const libraryFilms = q ? (titles.data || []).filter((t) => t.kind !== "show" && named(filmName(t)) && !(t.film && answeredIds.has(t.film.imdbId))) : [];
  const libraryShowsNamed = q ? (libraryShows.data || []).filter((s) => named(s.title) && !answeredIds.has(s.imdbId)) : [];
  const [best, ...rest] = found;

  const submit = (e) => {
    e.preventDefault();
    navigate(discoverPath(draft.trim()));
  };

  return (
    <div className={"kc-tv-page kc-tvd" + (q ? " has-query" : "")}>
      <form className="kc-tvd__search" onSubmit={submit} role="search" data-tv-top="">
        <Icon name="search" size={22} />
        <input value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="Any movie or show"
          aria-label="Any movie or show" enterKeyHint="search" data-tv-initial={q || browsing ? undefined : ""} />
      </form>

      {!q && person && <PersonWork key={person} personId={person} where={where} />}
      {!q && !person && genre && <GenreTitles key={genreKind + genre + genreCategory} kind={genreKind} genre={genre} category={genreCategory} where={where} />}

      {!q && !person && !genre && shelf && <TvShelf key={shelf} id={shelf} where={where} />}
      {!q && !person && !genre && !shelf && category && <TvCategory key={category} id={category} index={index} where={where} />}
      {!q && !browsing && index.error && !index.data && <div className="kc-tv-pad"><Failed error={index.error} onRetry={index.reload} /></div>}
      {!q && !browsing && index.data && <TvDiscoverFront index={index.data} where={where} />}

      {q && answered && (
        <div className="kc-tvd__rows">
          {best && <BestMatch key={best.imdbId} found={best} {...where(best)} />}
          {rest.length + libraryFilms.length + libraryShowsNamed.length > 0 && (
            <TvRow title={<>More for <span className="kc-tvd__q">“{q}”</span></>}>
              {rest.map((t) => {
                const w = where(t);
                return <TvCard key={t.imdbId} art={t.posterUrl} label={t.title} {...linkProps(foundPage(t, w.here, w.held))} />;
              })}
              {libraryFilms.map((t) => <TvCard key={t.id} art={posterUrl(t)} label={t.title} {...linkProps(foundPage({ kind: "film" }, t))} />)}
              {libraryShowsNamed.map((s) => (
                <TvCard key={s.imdbId || s.firstTitleId} art={s.details && s.details.posterUrl} label={s.title}
                  {...linkProps(foundPage({ kind: "show", imdbId: s.imdbId }, null, true))} />
              ))}
            </TvRow>
          )}
          {found.length === 0 && libraryFilms.length + libraryShowsNamed.length === 0 && (
            <p className="kc-tvd__none">Nothing is called that.</p>
          )}
        </div>
      )}
    </div>
  );
}

/** The department the film database knows somebody for, as a word for them. */
const KNOWN_AS = { Acting: "Actor", Directing: "Director", Writing: "Writer", Production: "Producer" };

/** Titles from the catalogue as posters in rows that wrap, each leading to its own page. */
function Posters({ titles, where }) {
  return (
    <div className="kc-tvsv__grid">
      {titles.map((t, i) => {
        const w = where(t);
        return (
          <TvCard key={t.kind + t.imdbId} art={t.posterUrl} label={t.title} data-tv-initial={i === 0 ? "" : undefined}
            {...linkProps(foundPage(t, w.here, w.held))} />
        );
      })}
    </div>
  );
}

/** Everything one person is credited on, best known first, under their portrait and name. */
function PersonWork({ personId, where }) {
  const work = useLoad(() => api.cataloguePerson(personId), [personId]);
  const p = work.data;
  if (work.error && !p) return <div className="kc-tv-pad"><Failed error={work.error} onRetry={work.reload} /></div>;
  if (!p) return null;
  return (
    <section className="kc-tvd__browse">
      <div className="kc-tvd__browse-head">
        <span className="kc-tv-person__face kc-tvd__face">{p.portraitUrl ? <img src={p.portraitUrl} alt="" /> : initials(p.name)}</span>
        <span className="kc-tvd__browse-text">
          {p.knownFor && <span className="tv-spot__eyebrow">{KNOWN_AS[p.knownFor] || p.knownFor}</span>}
          <span className="kc-tvd__title">{p.name}</span>
        </span>
        <span className="tv-row__count">{p.titles.length}</span>
      </div>
      <Posters titles={p.titles} where={where} />
      <p className="kc-tv-tmdb">Credits and artwork from TMDB.</p>
    </section>
  );
}

/** The catalogue's most popular films or shows in one genre; of its anime alone with `category`. */
function GenreTitles({ kind, genre, category, where }) {
  const listed = useLoad(() => api.catalogueGenre(kind, genre, category), [kind, genre, category]);
  const g = listed.data;
  if (listed.error && !g) return <div className="kc-tv-pad"><Failed error={listed.error} onRetry={listed.reload} /></div>;
  if (!g) return null;
  return (
    <section className="kc-tvd__browse">
      <div className="kc-tvd__browse-head">
        <span className="kc-tvd__browse-text">
          <span className="tv-spot__eyebrow">{category === ANIME ? (kind === "show" ? "Anime series" : "Anime films") : kind === "show" ? "TV Shows" : "Movies"}</span>
          <span className="kc-tvd__title">{g.genre}</span>
        </span>
        <span className="tv-row__count">{g.titles.length}</span>
      </div>
      <Posters titles={g.titles} where={where} />
      <p className="kc-tv-tmdb">Popular titles and artwork from TMDB.</p>
    </section>
  );
}

/** The catalogue's first answer: its poster, name, facts and story, and Watch, More info and Save. */
function BestMatch({ found, here, held }) {
  const show = found.kind === "show";
  const page = foundPage(found, here, held);
  const [busy, setBusy] = React.useState(false);
  const watch = async () => {
    if (here) { navigate(watchHref(here.id)); return; }
    setBusy(true);
    if (!(await startFilm(found.imdbId))) setBusy(false);
  };
  return (
    <section className="kc-tvd__best" data-tv-row="">
      {found.posterUrl && <img className="kc-tvd__poster" src={found.posterUrl} alt="" />}
      <div className="kc-tvd__body">
        <span className="tv-spot__eyebrow">Best match</span>
        <h1 className="kc-tvd__title">{found.title}</h1>
        <div className="tv-spot__meta">
          {found.rating ? <span className="kc-tv-rating"><Icon name="star" size={13} /><span className="kc-mono">{found.rating.toFixed(1)}</span></span> : null}
          {found.year && <span>{found.year}</span>}
          <span>{show ? "TV series" : "Movie"}</span>
          {found.starring && <span>{found.starring}</span>}
        </div>
        {found.overview && <p className="tv-spot__overview">{found.overview}</p>}
        <div className="tv-spot__actions">
          {!show && <Button variant="primary" size="lg" icon="play" busy={busy} data-tv-initial="" onClick={watch}>Watch</Button>}
          <Button variant={show ? "primary" : "secondary"} size="lg" icon="info" data-tv-initial={show ? "" : undefined}
            {...linkProps(page)}>More info</Button>
          <SaveButton imdbId={found.imdbId} kind={show ? "show" : "film"} title={found.title} />
        </div>
      </div>
    </section>
  );
}
