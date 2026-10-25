import React from "react";
import { CollectionTile, TvCard, TvHeading, TvRow } from "@thekrystalship/krystal";
import { catalogue, narrowed, pagesOf, queryParams } from "../../api/index.js";
import { apiPicks, pickCount } from "../../filters.js";
import { useLoad, usePaged } from "../../data.js";
import { discoverBrowsePath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { Failed } from "../../components/Film.jsx";
import { foundPage } from "../../components/TitleCards.jsx";
import { shelfPath, titleShelfHeads, useWhere } from "../../pages/DiscoverShelves.jsx";
import { useDiscoverSlides } from "../../heroes.js";
import { TvHero } from "../hero.jsx";
import { TmdbCredit } from "../TvCredits.jsx";

// Discover on a television: the spotlight over the page's artwork, then the page's rows, the moods,
// the franchises and the browse categories as rows of tiles; a category's shelves as rows; and every
// title one list query names in rows that wrap. Every shelf is a list query over the catalogue
// (`/api/catalogue/discover` names them), as on every surface.

/** A catalogue card as a television's poster, leading where the card does. */
export function TvTitleCard({ card, where, ...rest }) {
  const w = where(card);
  return <TvCard art={card.poster} label={card.title} {...linkProps(foundPage(card, w.here, w.held))} {...rest} />;
}

/** The spotlight: the films on the season's shelf, as Home's top turns through its own. */
function TvDiscoverSpotlight({ index, where }) {
  const slides = useDiscoverSlides(index, where);
  // The top's place is held while its slides are described, so the rows never move under it.
  if (slides === null) return <div className="kc-tvd__spot" aria-hidden="true" />;
  if (slides.length === 0) return null;
  return <TvHero slides={slides} className="kc-tvd__spot" data-tv-row="" />;
}

/** A shelf's tile: a mood's posters fanned, a series' films in order with how far the person got. */
function TvShelfTile({ head, series, ...rest }) {
  const link = { ...linkProps(shelfPath(head)), ...rest };
  return series
    ? (
      <CollectionTile variant="strip" name={head.title} posters={head.posters || []} total={head.total} watched={head.watched || 0}
        next={head.next ? "Next: " + head.next.title : null} caption={head.total != null ? `${head.total} titles` : null} {...link} />
    )
    : <CollectionTile variant="fan" name={head.title} icon={head.icon} posters={head.posters || []} caption={head.total ? `${head.total} titles` : null} {...link} />;
}

/**
 * A shelf's row of posters, asked for once it nears the screen; one with nothing on it is left out.
 * `picks` are the filter's, which the shelf is asked with.
 */
export function TvShelfRow({ head, where, skip, least = 1, reserve = 0, cards = 20, picks }) {
  const ref = React.useRef(null);
  const [near, setNear] = React.useState(false);
  const [answered, setAnswered] = React.useState(false);
  React.useEffect(() => {
    const el = ref.current;
    if (!el || near) return undefined;
    if (typeof IntersectionObserver !== "function") {
      setNear(true);
      return undefined;
    }
    const watcher = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting)) setNear(true);
    }, { rootMargin: "400px 0px" });
    watcher.observe(el);
    return () => watcher.disconnect();
  }, [near]);
  // `reserve` holds a row's height until the shelf has answered, so what is under it does not move
  // when the row fills in.
  return (
    <div ref={ref} className="kc-tvd__lazy" style={reserve && !answered ? { minHeight: reserve } : undefined}>
      {near && <TvShelfCards head={head} where={where} skip={skip} least={least} cards={cards} picks={picks} onAnswer={() => setAnswered(true)} />}
    </div>
  );
}

/** A shelf's cards, without `skip` (the page the row is on); a shelf with fewer than `least` is left out. */
function TvShelfCards({ head, where, skip, least = 1, cards = 20, picks, onAnswer }) {
  const query = { ...queryParams(head.query), ...(picks ? apiPicks(picks) : {}), size: cards + (skip ? 1 : 0) };
  const shelf = useLoad(() => catalogue.titles(query), [JSON.stringify(query)]);
  const done = !!(shelf.data || shelf.error);
  React.useEffect(() => { if (done && onAnswer) onAnswer(); }, [done, onAnswer]);
  const all = shelf.data ? shelf.data.titles : [];
  const titles = skip ? all.filter((t) => t.id !== skip) : all;
  if (titles.length === 0 || titles.length < least) return null;
  const total = shelf.data.total - (all.length - titles.length);
  return (
    <TvRow icon={head.icon} title={head.title} count={total}>
      {titles.slice(0, cards).map((t) => <TvTitleCard key={t.kind + t.id} card={t} where={where} />)}
    </TvRow>
  );
}

/** The page's rows on a television, in the order `/api/catalogue/discover` gives them. */
export function TvDiscoverFront({ index, where }) {
  const sections = new Map(index.sections.map((s) => [s.id, s]));
  return (
    <>
      <TvDiscoverSpotlight index={index} where={where} />
      <div className="kc-tvd__rows">
        {index.rows.map((row) => {
          if (row.kind === "shelf") return <TvShelfRow key={row.shelf.id} head={row.shelf} where={where} />;
          if (row.kind === "browse") {
            return (
              <TvRow key="browse" icon="compass" title="Browse" count={index.sections.length}>
                {index.sections.map((s) => (
                  <CollectionTile key={s.id} variant="fan" name={s.label} icon={s.icon} caption={`${s.count} shelves`}
                    posters={s.shelves.slice(0, 3).map((h) => (h.posters || [])[0]).filter(Boolean)}
                    {...linkProps(discoverBrowsePath(s.id))} />
                ))}
              </TvRow>
            );
          }
          const section = sections.get(row.kind);
          if (!section) return null;
          return (
            <TvRow key={row.kind} icon={section.icon} title={section.label} count={section.count}>
              {section.shelves.map((h) => <TvShelfTile key={h.id} head={h} series={section.series} />)}
            </TvRow>
          );
        })}
        <TmdbCredit />
      </div>
    </>
  );
}

/**
 * One browse category on a television: the franchises' tiles in rows that wrap, any other's shelves as
 * rows, each asked with the filter's `picks`.
 */
export function TvCategory({ id, index, where, picks }) {
  if (index.error && !index.data) return <div className="kc-tv-pad"><Failed error={index.error} onRetry={index.reload} /></div>;
  if (!index.data) return null;
  const section = index.data.sections.find((s) => s.id === id);
  if (!section) return <p className="kc-tv-none">Nothing by that name.</p>;
  return (
    <section className="kc-tvd__browse">
      <TvHeading as="h1" size="lg" eyebrow="Discover" title={section.label} count={section.count} />
      {section.series
        ? <TvSeriesGrid />
        : <div className="kc-tvd__rows">{section.shelves.map((h) => <TvShelfRow key={h.id} head={h} where={where} picks={picks} />)}</div>}
      <TmdbCredit />
    </section>
  );
}

/**
 * The next page of a grid joining it as its end nears the screen, which focus moving down brings it
 * to, without moving focus. `paged` is what `usePaged` returned for the grid.
 */
function TvMore({ paged }) {
  const end = React.useRef(null);
  const latest = React.useRef(paged.loadMore);
  latest.current = paged.loadMore;
  const idle = !!paged.next && !paged.loadingMore && !paged.moreError;
  React.useEffect(() => {
    const el = end.current;
    if (!el || !idle || typeof IntersectionObserver !== "function") return undefined;
    const watcher = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting)) latest.current();
    }, { rootMargin: "800px 0px" });
    watcher.observe(el);
    return () => watcher.disconnect();
  }, [idle, paged.next]);
  return <div ref={end} style={{ minHeight: 1 }} />;
}

/** Every franchise of the category on a television, in rows that wrap, in the person's order, a page at a time. */
function TvSeriesGrid() {
  const series = usePaged((page) => catalogue.franchises(page).then((p) => ({ titles: p.franchises, next: p.next })), []);
  if (series.error && !series.data) return <div className="kc-tv-pad"><Failed error={series.error} onRetry={series.reload} /></div>;
  if (!series.titles) return null;
  return (
    <>
      <div className="kc-tvsv__grid">
        {series.titles.map((h, i) => <TvShelfTile key={h.id} head={h} series data-tv-initial={i === 0 ? "" : undefined} />)}
      </div>
      <TvMore paged={series} />
    </>
  );
}

/**
 * Every title one list query names on a television, in rows that wrap, a page at a time: `query` is the
 * list's own (a shelf's, a genre's, a person's, a franchise's, the filter's), narrowed by the filter's
 * `picks` and in the order `sort`. `eyebrow`, `title` and `lead` head it.
 */
export function TvList({ query, sort, picks, where, eyebrow, title, lead }) {
  const asked = { ...narrowed(query, apiPicks(picks)), ...(sort ? { sort } : {}) };
  const list = usePaged(pagesOf(asked), [JSON.stringify(asked)]);
  if (list.error && !list.data) return <div className="kc-tv-pad"><Failed error={list.error} onRetry={list.reload} /></div>;
  if (!list.titles) return null;
  return (
    <section className="kc-tvd__browse">
      <TvHeading as="h1" size="lg" eyebrow={eyebrow} title={title} count={list.data.total} lead={lead} />
      {list.titles.length === 0 && <p className="kc-tv-none">{pickCount(picks) > 0 ? "Nothing matches." : "Nothing here."}</p>}
      <div className="kc-tvsv__grid">
        {list.titles.map((t, i) => <TvTitleCard key={t.kind + t.id} card={t} where={where} data-tv-initial={i === 0 ? "" : undefined} />)}
      </div>
      <TvMore paged={list} />
      <TmdbCredit />
    </section>
  );
}

/** How many posters a title's shelf shows on a television, fewer than Discover's so its rows draw quickly. */
const TITLE_ROW_CARDS = 12;

/** A row of posters' height on a television, held for a title's shelf until it answers. */
const TV_ROW_HEIGHT = 232;

/** A title's page's shelves on a television, from its catalogue page: what is like it, and more from its director and first two stars. */
export function TvTitleShelves({ page }) {
  const where = useWhere();
  return (
    <div className="kc-tvf__shelves">
      {titleShelfHeads(page).map((h) => (
        <TvShelfRow key={h.id} head={h} where={where} skip={page.id} least={h.least} reserve={TV_ROW_HEIGHT} cards={TITLE_ROW_CARDS} />
      ))}
    </div>
  );
}
