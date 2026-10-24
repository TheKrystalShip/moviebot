import React from "react";
import { Button, CollectionTile, TvCard, TvHeading, TvRow } from "@thekrystalship/krystal";
import { api } from "../../api.js";
import { apiPicks, filterQuestion, pickCount } from "../../discoverFilters.js";
import { useLoad } from "../../data.js";
import { discoverBrowsePath, discoverShelfPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { Failed } from "../../components/Film.jsx";
import { foundPage } from "../../components/TitleCards.jsx";
import { atLeast, surprisePool, titleShelfHeads, useSurprise, useWhere } from "../../pages/DiscoverShelves.jsx";
import { useDiscoverSlides } from "../../heroes.js";
import { TvHero } from "../hero.jsx";

// Discover on a television: the spotlight over the page's artwork, then the page's rows, the moods,
// the franchises and the browse categories as rows of tiles; a category's shelves as rows; and one
// shelf's titles in rows that wrap. Every shelf comes from `/api/discover`, as on every surface.

/** The spotlight: the films on the season's shelf, as Home's top turns through its own, with Surprise me. */
function TvDiscoverSpotlight({ index, where }) {
  const slides = useDiscoverSlides(index, where);
  const [busy, surprise] = useSurprise(where);
  if (!slides || slides.length === 0) return null;
  const more = <Button size="lg" icon="dices" busy={busy} onClick={() => surprise({ shelves: surprisePool(index) })}>Surprise me</Button>;
  return <TvHero slides={slides} more={more} className="kc-tvd__spot" data-tv-row="" />;
}

/** A shelf's tile: a mood's posters fanned, a series' films in order with how far the person got. */
function TvShelfTile({ head, series, ...rest }) {
  const link = { ...linkProps(discoverShelfPath(head.id)), ...rest };
  return series
    ? (
      <CollectionTile variant="strip" name={head.title} posters={head.posters || []} total={head.total} watched={head.watched || 0}
        next={head.next ? "Next: " + head.next : null} caption={head.total != null ? `${head.total} films` : null} {...link} />
    )
    : <CollectionTile variant="fan" name={head.title} icon={head.icon} posters={head.posters || []} caption={head.total ? `${atLeast(head.total, head.more)} titles` : null} {...link} />;
}

/**
 * A shelf's row of posters, drawn at once from what the index carried for it, else asked for once it
 * nears the screen; one with nothing on it is left out. `picks` are the filter's, which the shelf is
 * asked with.
 */
export function TvShelfRow({ head, kept, where, skip, least = 1, reserve = 0, cards = 20, picks }) {
  const ref = React.useRef(null);
  const [near, setNear] = React.useState(!!kept);
  const [answered, setAnswered] = React.useState(!!kept);
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
      {near && <TvShelfCards head={head} kept={kept} where={where} skip={skip} least={least} cards={cards} picks={picks} onAnswer={() => setAnswered(true)} />}
    </div>
  );
}

/** A shelf's cards, without `skip` (the page the row is on); a shelf with fewer than `least` is left out. */
function TvShelfCards({ head, kept, where, skip, least = 1, cards = 20, picks, onAnswer }) {
  const asked = picks ? apiPicks(picks) : {};
  const filtered = !!picks && pickCount(picks) > 0;
  const shelf = useLoad(() => api.discoverShelf(head.id, 1, null, asked), [head.id, JSON.stringify(asked)], { known: filtered ? undefined : kept });
  const done = !!(shelf.data || shelf.error);
  React.useEffect(() => { if (done && onAnswer) onAnswer(); }, [done, onAnswer]);
  const all = shelf.data ? shelf.data.titles : [];
  const titles = skip ? all.filter((t) => t.imdbId !== skip) : all;
  if (titles.length === 0 || titles.length < least) return null;
  return (
    <TvRow icon={head.icon} title={head.title} count={atLeast(titles.length, shelf.data && shelf.data.next)}>
      {titles.slice(0, cards).map((t) => {
        const w = where(t);
        return <TvCard key={t.kind + t.imdbId} art={t.posterUrl} label={t.title} {...linkProps(foundPage(t, w.here, w.held))} />;
      })}
    </TvRow>
  );
}

/** The page's rows on a television, in the order `/api/discover` gives them. */
export function TvDiscoverFront({ index, where }) {
  const sections = new Map(index.sections.map((s) => [s.id, s]));
  const kept = new Map((index.shelves || []).map((s) => [s.id, s]));
  return (
    <>
      <TvDiscoverSpotlight index={index} where={where} />
      <div className="kc-tvd__rows">
        {index.rows.map((row) => {
          if (row.kind === "shelf") return <TvShelfRow key={row.shelf.id} head={row.shelf} kept={kept.get(row.shelf.id)} where={where} />;
          if (row.kind === "browse") {
            return (
              <TvRow key="browse" icon="compass" title="Browse" count={index.sections.length}>
                {index.sections.map((s) => (
                  <CollectionTile key={s.id} variant="fan" name={s.label} icon={s.icon} caption={s.caption} posters={s.posters}
                    {...linkProps(discoverBrowsePath(s.id))} />
                ))}
              </TvRow>
            );
          }
          const section = sections.get(row.kind);
          if (!section) return null;
          const shelves = section.shelves.filter((h) => h.total !== 0);
          return (
            <TvRow key={row.kind} icon={section.icon} title={section.label} count={shelves.length}>
              {shelves.map((h) => <TvShelfTile key={h.id} head={h} series={section.series} />)}
            </TvRow>
          );
        })}
        <p className="kc-tv-tmdb">Titles and artwork from TMDB.</p>
      </div>
    </>
  );
}

/**
 * One browse category on a television: a series' tiles in rows that wrap, any other's shelves as rows,
 * each asked with the filter's `picks`.
 */
export function TvCategory({ id, index, where, picks }) {
  if (index.error && !index.data) return <div className="kc-tv-pad"><Failed error={index.error} onRetry={index.reload} /></div>;
  if (!index.data) return null;
  const section = index.data.sections.find((s) => s.id === id);
  if (!section) return <p className="kc-tvd__none">Nothing by that name.</p>;
  return (
    <section className="kc-tvd__browse">
      <TvHeading as="h1" size="lg" eyebrow="Discover" title={section.label} count={section.shelves.length} />
      {section.series
        ? (
          <div className="kc-tvsv__grid">
            {section.shelves.filter((h) => h.total !== 0).map((h, i) => (
              <TvShelfTile key={h.id} head={h} series data-tv-initial={i === 0 ? "" : undefined} />
            ))}
          </div>
        )
        : <div className="kc-tvd__rows">{section.shelves.map((h) => <TvShelfRow key={h.id} head={h} where={where} picks={picks} />)}</div>}
      <p className="kc-tv-tmdb">Titles and artwork from TMDB.</p>
    </section>
  );
}

/** Every title on one shelf, on a television, in rows that wrap, at the filter's `picks`. */
export function TvShelf({ id, where, picks }) {
  const asked = apiPicks(picks);
  const shelf = useLoad(() => api.discoverShelf(id, 1, null, asked), [id, JSON.stringify(asked)]);
  const s = shelf.data;
  if (shelf.error && !s) return <div className="kc-tv-pad"><Failed error={shelf.error} onRetry={shelf.reload} /></div>;
  if (!s) return null;
  return (
    <section className="kc-tvd__browse">
      <TvHeading as="h1" size="lg" eyebrow={s.section} title={s.title} count={atLeast(s.titles.length, s.next)} />
      {s.titles.length === 0 && <p className="kc-tvd__none">Nothing matches.</p>}
      <div className="kc-tvsv__grid">
        {s.titles.map((t, i) => {
          const w = where(t);
          return (
            <TvCard key={t.kind + t.imdbId} art={t.posterUrl} label={t.title} data-tv-initial={i === 0 ? "" : undefined}
              {...linkProps(foundPage(t, w.here, w.held))} />
          );
        })}
      </div>
      <p className="kc-tv-tmdb">Titles and artwork from TMDB.</p>
    </section>
  );
}

/** The catalogue's films and shows at the filter's `picks`, on a television, in rows that wrap. */
export function TvFilter({ picks, where }) {
  const question = filterQuestion(picks);
  const found = useLoad(() => api.catalogueDiscover(question), [JSON.stringify(question)]);
  const titles = found.data ? found.data.titles.filter((t) => t.imdbId) : null;
  if (found.error && !titles) return <div className="kc-tv-pad"><Failed error={found.error} onRetry={found.reload} /></div>;
  if (!titles) return null;
  return (
    <section className="kc-tvd__browse">
      <TvHeading as="h1" size="lg" eyebrow="Discover" title="Filter" count={atLeast(titles.length, found.data.next)} />
      {titles.length === 0 && <p className="kc-tvd__none">Nothing matches.</p>}
      <div className="kc-tvsv__grid">
        {titles.map((t, i) => {
          const w = where(t);
          return (
            <TvCard key={t.kind + t.imdbId} art={t.posterUrl} label={t.title} data-tv-initial={i === 0 ? "" : undefined}
              {...linkProps(foundPage(t, w.here, w.held))} />
          );
        })}
      </div>
      <p className="kc-tv-tmdb">Titles and artwork from TMDB.</p>
    </section>
  );
}

/** How many posters a title's shelf shows on a television, fewer than Discover's so its rows draw quickly. */
const TITLE_ROW_CARDS = 12;

/** A row of posters' height on a television, held for a title's shelf until it answers. */
const TV_ROW_HEIGHT = 232;

/** A title's page's shelves on a television: what is like it, and more from its director and first two stars. */
export function TvTitleShelves({ kind, imdbId, directors = [], stars = [] }) {
  const where = useWhere();
  return (
    <div className="kc-tvf__shelves">
      {titleShelfHeads(kind, imdbId, directors, stars).map((h) => (
        <TvShelfRow key={h.id} head={h} where={where} skip={imdbId} least={h.least} reserve={TV_ROW_HEIGHT} cards={TITLE_ROW_CARDS} />
      ))}
    </div>
  );
}
