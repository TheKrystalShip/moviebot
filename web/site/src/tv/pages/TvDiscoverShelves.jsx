import React from "react";
import { Button, CollectionTile, TvCard, TvHeading, TvRow } from "@thekrystalship/krystal";
import { api } from "../../api.js";
import { useLoad } from "../../data.js";
import { discoverBrowsePath, discoverShelfPath } from "../../paths.js";
import { linkProps } from "../../router.js";
import { Failed } from "../../components/Film.jsx";
import { foundPage } from "../../components/TitleCards.jsx";
import { atLeast, surprisePool, useSurprise } from "../../pages/DiscoverShelves.jsx";
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
 * nears the screen; one with nothing on it is left out.
 */
function TvShelfRow({ head, kept, where }) {
  const ref = React.useRef(null);
  const [near, setNear] = React.useState(!!kept);
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
  return <div ref={ref} className="kc-tvd__lazy">{near && <TvShelfCards head={head} kept={kept} where={where} />}</div>;
}

function TvShelfCards({ head, kept, where }) {
  const shelf = useLoad(() => api.discoverShelf(head.id), [head.id], { known: kept });
  const titles = shelf.data ? shelf.data.titles : [];
  if (titles.length === 0) return null;
  return (
    <TvRow icon={head.icon} title={head.title} count={atLeast(titles.length, shelf.data && shelf.data.next)}>
      {titles.slice(0, 20).map((t) => {
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

/** One browse category on a television: a series' tiles in rows that wrap, any other's shelves as rows. */
export function TvCategory({ id, index, where }) {
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
        : <div className="kc-tvd__rows">{section.shelves.map((h) => <TvShelfRow key={h.id} head={h} where={where} />)}</div>}
      <p className="kc-tv-tmdb">Titles and artwork from TMDB.</p>
    </section>
  );
}

/** Every title on one shelf, on a television, in rows that wrap. */
export function TvShelf({ id, where }) {
  const shelf = useLoad(() => api.discoverShelf(id), [id]);
  const s = shelf.data;
  if (shelf.error && !s) return <div className="kc-tv-pad"><Failed error={shelf.error} onRetry={shelf.reload} /></div>;
  if (!s) return null;
  return (
    <section className="kc-tvd__browse">
      <TvHeading as="h1" size="lg" eyebrow={s.section} title={s.title} count={atLeast(s.titles.length, s.next)} />
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
