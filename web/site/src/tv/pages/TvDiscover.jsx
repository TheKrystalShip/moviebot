import React from "react";
import { Avatar, Button, FilterDrawer, Icon, SearchField, TvCard, TvHeading, TvRow, TvSpotlight } from "@thekrystalship/krystal";
import { api, posterUrl } from "../../api.js";
import { useLoad } from "../../data.js";
import { ANIME, discoverFilterPath, discoverPath } from "../../paths.js";
import { linkProps, navigate } from "../../router.js";
import { useCan } from "../../session.js";
import { watchHref } from "../../components/Film.jsx";
import { WantButton } from "../../components/WantButton.jsx";
import { foundPage } from "../../components/TitleCards.jsx";
import { Failed } from "../../components/Film.jsx";
import { startFilm } from "./TvFilm.jsx";
import { TvCategory, TvDiscoverFront, TvFilter, TvShelf } from "./TvDiscoverShelves.jsx";
import { useDiscoverOrder } from "../../pages/DiscoverShelves.jsx";
import { withOrder } from "../../orders.js";
import {
  apiPicks, drawerValues, filterQuestion, pickCount, picksOfValues, readPicks, sectionsOf, useDrawer, useFacets, withPicks,
} from "../../filters.js";

/**
 * Finding anything to watch, on a television. The field is focused when the page opens; OK on it
 * raises the system keyboard, which types, speaks and takes a phone, and its search key searches.
 * With nothing typed, the spotlight and the page's rows (`/api/discover`), with the moods, the
 * franchises and the browse categories as rows of tiles; `browse` is one category and `shelf` every
 * title on one shelf. With a query, the catalogue's first answer large,
 * with Watch, More info and Want, then the rest of what it answered and whatever the library holds by
 * that name, all as one row. The query is in the address, as on every surface.
 *
 * The filter drawer in the field filters what the page shows with nothing typed: the catalogue, a
 * person's titles, a genre's, a shelf and a category's shelves, by the sections the answer's facets
 * report, and on a shelf and the filter's answers their order (`useDiscoverOrder`). Picking anything on
 * the front opens the filter's answers. Back goes to the drawer first, and
 * the page under the open drawer is inert, so the remote's arrows stay in it.
 */
export function TvDiscover({ query }) {
  const q = (query.get("q") || "").trim();
  const person = /^\d+$/.test(query.get("person") || "") ? query.get("person") : null;
  const genre = (query.get("genre") || "").trim();
  const genreKind = query.get("kind") === "show" ? "show" : "film";
  const genreCategory = query.get("category") === ANIME ? ANIME : null;
  const shelf = (query.get("shelf") || "").trim();
  const category = (query.get("browse") || "").trim();
  const filtering = query.has("filter");
  const browsing = !q && !!(person || genre || shelf || category || filtering);
  const showsShelf = !q && !person && !genre && !!shelf;
  const showsFilter = !q && !person && !genre && !shelf && !category && filtering;
  const picks = readPicks(query);
  const asked = apiPicks(picks);
  const pickKey = JSON.stringify(asked);
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

  // What the library holds by those words, as the API searches it, that the catalogue did not answer with.
  const held = useLoad(() => (q ? api.libraryPage("all", { q }).catch(() => null) : Promise.resolve(null))
    .then((page) => ({ q, items: page ? page.items : [] })), [q]);
  const heldNow = held.data && held.data.q === q ? held.data.items : [];
  const answeredIds = new Set(found.map((t) => t.imdbId));
  const libraryFilms = heldNow.filter((i) => i.film && !(i.film.film && answeredIds.has(i.film.film.imdbId))).map((i) => i.film);
  const libraryShowsNamed = heldNow.filter((i) => i.show && !answeredIds.has(i.show.imdbId)).map((i) => i.show);
  const [best, ...rest] = found;

  const submit = (value) => navigate(discoverPath(value.trim()));

  const drawer = useDrawer();
  const section = category && index.data ? index.data.sections.find((s) => s.id === category) : null;
  const facetsFor = q ? null
    : person ? () => api.cataloguePersonFacets(person, asked)
      : genre ? () => api.catalogueGenreFacets(genreKind, genre, genreCategory, asked)
        : shelf ? () => api.discoverShelfFacets(shelf, asked)
          : category ? (section && !section.series ? () => api.discoverSectionFacets(category, asked) : null)
            : () => api.catalogueDiscoverFacets(filterQuestion(picks));
  const facets = useFacets(facetsFor, [q, person, genre, genreKind, genreCategory, shelf, category, !!section, pickKey].join("|"));
  const ordering = useDiscoverOrder(query, (facets && facets.sorts) || [], { shelf: showsShelf, filtering: showsFilter });
  const sections = facetsFor ? sectionsOf(facets, picks, { watched: false }) : [];
  const change = (next) => {
    const nextPicks = picksOfValues(next);
    if (JSON.stringify(apiPicks(nextPicks)) === pickKey) return;
    const landing = !q && !browsing;
    navigate(withPicks(landing ? new URLSearchParams(discoverFilterPath().split("?")[1]) : query, nextPicks), { replace: !landing });
  };
  const drawn = { sections, values: drawerValues(picks, false), onValues: change };
  const offered = ordering && ordering.orders.length > 0;
  const ordered = offered ? withOrder(drawn, ordering.orders, ordering.order, ordering.pick) : drawn;
  const filters = sections.length > 0 || offered
    ? <FilterDrawer ref={drawer.ref} onOpenChange={drawer.onOpenChange} sections={ordered.sections} values={ordered.values} onChange={ordered.onValues} />
    : undefined;

  return (
    <div className={"kc-tv-page kc-tvd" + (q ? " has-query" : "")}>
      <SearchField size="xl" className="kc-tv-search" data-tv-top="" value={draft} onChange={setDraft} onSubmit={submit} submit={false}
        placeholder="Any movie or show" filters={filters} inputProps={{ className: "kc-tv-search__input", "data-tv-initial": q || browsing ? undefined : "" }} />

      <div className="kc-tvd__answer" inert={drawer.open ? "" : undefined} style={{ display: "contents" }}>
      {!q && person && <PersonWork key={person} personId={person} picks={picks} where={where} />}
      {!q && !person && genre && <GenreTitles key={genreKind + genre + genreCategory} kind={genreKind} genre={genre} category={genreCategory} picks={picks} where={where} />}

      {showsShelf && <TvShelf key={shelf} id={shelf} sort={ordering.sort} picks={picks} where={where} />}
      {!q && !person && !genre && !shelf && category && <TvCategory key={category} id={category} index={index} picks={picks} where={where} />}
      {showsFilter && <TvFilter sort={ordering.sort} picks={picks} where={where} />}
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
            <p className="kc-tv-none">Nothing is called that.</p>
          )}
        </div>
      )}
      </div>
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
function PersonWork({ personId, picks, where }) {
  const asked = apiPicks(picks);
  const work = useLoad(() => api.cataloguePerson(personId, asked), [personId, JSON.stringify(asked)]);
  const p = work.data;
  if (work.error && !p) return <div className="kc-tv-pad"><Failed error={work.error} onRetry={work.reload} /></div>;
  if (!p) return null;
  return (
    <section className="kc-tvd__browse">
      <TvHeading as="h1" size="lg" count={p.titles.length} title={p.name} eyebrow={p.knownFor && (KNOWN_AS[p.knownFor] || p.knownFor)}
        lead={<Avatar src={p.portraitUrl} name={p.name} tone="muted" size="css" imagePosition="center 20%" className="kc-tv-person__face kc-tvd__face" />} />
      {p.titles.length === 0 && pickCount(picks) > 0 && <p className="kc-tv-none">Nothing matches.</p>}
      <Posters titles={p.titles} where={where} />
      <p className="kc-tv-tmdb">Credits and artwork from TMDB.</p>
    </section>
  );
}

/** The catalogue's most popular films or shows in one genre; of its anime alone with `category`. */
function GenreTitles({ kind, genre, category, picks, where }) {
  const asked = apiPicks(picks);
  const listed = useLoad(() => api.catalogueGenre(kind, genre, category, asked), [kind, genre, category, JSON.stringify(asked)]);
  const g = listed.data;
  if (listed.error && !g) return <div className="kc-tv-pad"><Failed error={listed.error} onRetry={listed.reload} /></div>;
  if (!g) return null;
  return (
    <section className="kc-tvd__browse">
      <TvHeading as="h1" size="lg" count={g.titles.length} title={g.genre}
        eyebrow={category === ANIME ? (kind === "show" ? "Anime series" : "Anime films") : kind === "show" ? "TV Shows" : "Movies"} />
      {g.titles.length === 0 && pickCount(picks) > 0 && <p className="kc-tv-none">Nothing matches.</p>}
      <Posters titles={g.titles} where={where} />
      <p className="kc-tv-tmdb">Popular titles and artwork from TMDB.</p>
    </section>
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
    if (!(await startFilm(found.imdbId))) setBusy(false);
  };
  return (
    <section className="kc-tvd__best" data-tv-row="">
      {found.posterUrl && <img className="kc-tvd__poster" src={found.posterUrl} alt="" />}
      <TvSpotlight className="kc-tvd__body" titleSize="sm" overviewLines={2} eyebrow="Best match" title={found.title}
        meta={<>
          {found.rating ? <span className="kc-tv-rating"><Icon name="star" size={13} /><span className="kc-mono">{found.rating.toFixed(1)}</span></span> : null}
          {found.year && <span>{found.year}</span>}
          <span>{show ? "TV series" : "Movie"}</span>
          {found.starring && <span>{found.starring}</span>}
        </>}
        overview={found.overview}
        actions={<>
          {!show && <Button variant="primary" size="lg" icon="play" busy={busy} data-tv-initial="" onClick={watch}>Watch</Button>}
          <Button variant={show ? "primary" : "secondary"} size="lg" icon="info" data-tv-initial={show ? "" : undefined}
            {...linkProps(page)}>More info</Button>
          <WantButton imdbId={found.imdbId} kind={show ? "show" : "film"} title={found.title} />
        </>} />
    </section>
  );
}
