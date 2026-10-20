import { Button, CarouselPager, Icon, Skeleton, useCarousel } from "@thekrystalship/krystal";
import { readySlides } from "../heroes.js";
import { discoverFilmPath, moviePath, showPath } from "../paths.js";
import { linkProps } from "../router.js";
import { WatchButton } from "./Film.jsx";
import { FilmMatchActions } from "./FilmActions.jsx";
import { FilmScore } from "./FilmDetails.jsx";
import { WantButton } from "./WantButton.jsx";

/** How long a slide is shown before the next, in milliseconds. */
export const SLIDE_MS = 8000;

/**
 * The top of Home and of Discover: one title large over its artwork, with why it is here, its facts,
 * its story and what can be done with it, turning to the next of `slides` (`heroes.js`) by itself.
 * Every slide described is laid in the same place, so the top is as tall as its tallest slide and
 * nothing below it moves as it turns or as later slides' artwork arrives; it turns among those whose
 * artwork has (`readySlides`), and the artwork fades from one to the next. `more` follows each slide's
 * actions. `where` joins a title from the film database with the library when it is drawn.
 */
export function Spotlight({ slides, more, where }) {
  const turning = readySlides(slides);
  const carousel = useCarousel({ count: turning.length, interval: SLIDE_MS });
  if (!slides) return <Skeleton w="100%" h={380} r={6} />;
  if (slides.length === 0) return null;
  const shown = carousel.index;
  return (
    <section className="kc-featured kc-spotlight" aria-label="Featured" {...carousel.rootProps}>
      {turning.map((s, i) => s.spot.art && (
        <div key={s.key} className={"kc-featured__art" + (i === shown ? " is-shown" : "")} style={{ backgroundImage: `url("${s.spot.art}")` }} />
      ))}
      <div className="kc-featured__scrim" />
      <div className="kc-spotlight__slides" aria-live={carousel.running ? "off" : "polite"}>
        {slides.map((s, i) => (
          <div key={s.key} className={"kc-featured__body" + (i === shown ? " is-shown" : "")} aria-roledescription="slide"
            aria-label={`${i + 1} of ${turning.length}`} aria-hidden={i === shown ? undefined : "true"} inert={i === shown ? undefined : ""}>
            <Slide slide={s} more={more} where={where} />
          </div>
        ))}
      </div>
      <CarouselPager carousel={carousel} className="kc-spotlight__pager" labels={turning.map((s) => `Show ${s.spot.title}`)} />
    </section>
  );
}

function Slide({ slide, more, where }) {
  const { spot } = slide;
  const f = spot.facts || {};
  return (
    <>
      <span className="kc-eyebrow kc-spotlight__eyebrow">{slide.icon && <Icon name={slide.icon} size={14} />}{spot.eyebrow}</span>
      <h1>{spot.title}</h1>
      <div className="kc-featured__meta">
        {f.rating ? <FilmScore rating={f.rating} votes={f.votes} compact /> : null}
        {f.year && <span>{f.year}</span>}
        {f.cert && <span className="kc-cert">{f.cert}</span>}
        {f.length && <span>{f.length}</span>}
        {f.genres && f.genres.length > 0 && <span>{f.genres.join(" · ")}</span>}
      </div>
      {spot.overview && <p className="kc-featured__overview">{spot.overview}</p>}
      {spot.credit && <span className="kc-featured__starring">{spot.credit.label} <b>{spot.credit.names}</b></span>}
      <SlideActions slide={slide} more={more} where={where} />
    </>
  );
}

/**
 * What can be done with a slide's title, as its own page offers it: Watch, or Join the room people
 * are watching it in, for a film here; Watch or Want for a film that has to be fetched; Want for a
 * show; and More info for every one of them.
 */
function SlideActions({ slide, more, where }) {
  const here = slide.title || (slide.found && where ? where(slide.found).here : slide.here);
  const page = slide.show || slide.kind === "show"
    ? showPath(slide.imdbId, { held: slide.held, category: (slide.show || slide.found || {}).category })
    : here ? moviePath(here.id, here.category) : discoverFilmPath(slide.imdbId);
  const info = <Button size="xl" icon="info" {...linkProps(page)}>More info</Button>;

  if (slide.show || slide.kind === "show") {
    return (
      <div className="kc-actions">
        <Button variant="primary" size="xl" icon="info" {...linkProps(page)}>More info</Button>
        <WantButton imdbId={slide.imdbId} kind="show" title={slide.spot.title} />
        {more}
      </div>
    );
  }
  if (here) {
    const room = slide.room;
    return (
      <div className="kc-actions">
        {room
          ? <WatchButton variant="primary" size="xl" icon="users" titleId={here.id} sessionId={room.sessionId}>Join the room</WatchButton>
          : here.status === "ready" && <WatchButton variant="primary" size="xl" icon="play" titleId={here.id}>Watch</WatchButton>}
        {info}
        {more}
      </div>
    );
  }
  return <FilmMatchActions found={{ ...slide.found, title: slide.spot.title }} compact>{info}{more}</FilmMatchActions>;
}
