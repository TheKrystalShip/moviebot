import { Button, CarouselPager, Icon, Skeleton, useCarousel } from "@thekrystalship/krystal";
import { readySlides } from "../heroes.js";
import { ANIME, discoverFilmPath, moviePath, showPath } from "../paths.js";
import { linkProps } from "../router.js";
import { WatchButton } from "./Film.jsx";
import { FilmMatchActions } from "./FilmActions.jsx";
import { Credits } from "./FilmDetails.jsx";
import { HeroGroup, kindWord, TitleFacts } from "./TitleHero.jsx";
import { WantButton } from "./WantButton.jsx";

/** How long a slide is shown before the next, in milliseconds. */
export const SLIDE_MS = 8000;

/**
 * The top of Home and of Discover: a title's page's hero (`TitleHero`), turning to the next of `slides`
 * (`heroes.js`) by itself. Each slide says why it is here and what kind of title it is, then the title's
 * name, facts, story and credits as its page has them, and what can be done with it, with the pager at
 * the far end of the buttons' row. Every slide described is laid in the same place, so the top is as tall
 * as its tallest slide and nothing below it moves as it turns or as later slides' artwork arrives; it
 * turns among those whose artwork has (`readySlides`), and the artwork fades from one to the next.
 * `where` joins a catalogue title with the library when it is drawn.
 */
export function Spotlight({ slides, where }) {
  const turning = readySlides(slides);
  const carousel = useCarousel({ count: turning.length, interval: SLIDE_MS });
  if (!slides) return <Skeleton w="100%" h={480} r={6} />;
  if (slides.length === 0) return null;
  const shown = carousel.index;
  return (
    <section className="kc-tt kc-spotlight" aria-label="Featured" {...carousel.rootProps}>
      {turning.map((s, i) => s.spot.art && (
        <div key={s.key} className={"kc-tt__art kc-spotlight__art" + (i === shown ? " is-shown" : "")} style={{ backgroundImage: `url("${s.spot.art}")` }} />
      ))}
      <div className="kc-tt__shade" />
      <div className="kc-spotlight__slides" aria-live={carousel.running ? "off" : "polite"}>
        {slides.map((s, i) => (
          <div key={s.key} className={"kc-tt__body kc-spotlight__slide" + (i === shown ? " is-shown" : "")} aria-roledescription="slide"
            aria-label={`${i + 1} of ${turning.length}`} aria-hidden={i === shown ? undefined : "true"} inert={i === shown ? undefined : ""}>
            <Slide slide={s} where={where} />
          </div>
        ))}
        <CarouselPager carousel={carousel} className="kc-spotlight__pager" labels={turning.map((s) => `Show ${s.spot.title}`)} />
      </div>
    </section>
  );
}

function Slide({ slide, where }) {
  const { spot } = slide;
  const f = spot.facts || {};
  const kind = spot.kind || slide.kind || "film";
  const category = spot.category || (slide.found || (slide.film && slide.film.card) || {}).category || null;
  return (
    <>
      <HeroGroup>
        <span className="kc-eyebrow kc-spotlight__eyebrow">
          {slide.icon && <Icon name={slide.icon} size={14} />}
          {spot.eyebrow} · {kindWord(kind, category === ANIME)}
        </span>
        <h1>{spot.title}</h1>
        <TitleFacts rating={f.rating} votes={f.votes} ratedBy={f.ratedBy} kind={kind} category={category}
          parts={[f.year, f.cert && { cert: f.cert }, f.length]} genres={f.genres || []} />
      </HeroGroup>
      {(spot.overview || spot.credits) && (
        <HeroGroup>
          {spot.overview && <p className="kc-tt__overview kc-tt__overview--clamp">{spot.overview}</p>}
          {spot.credits && <Credits film={spot.credits} />}
        </HeroGroup>
      )}
      <SlideActions slide={slide} where={where} />
    </>
  );
}

/**
 * What can be done with a slide's title, as its own page offers it: Watch, or Join the room people
 * are watching it in, and Want for a film here; Watch or Want for a film that has to be fetched; Want
 * for a show; and More info for every one of them.
 */
function SlideActions({ slide, where }) {
  const here = slide.title || (slide.found && where ? where(slide.found).here : slide.here);
  const page = slide.show || slide.kind === "show"
    ? showPath(slide.imdbId, { held: slide.held, category: ((slide.show && slide.show.card) || slide.found || {}).category })
    : here ? moviePath(here.id, here.card && here.card.category) : discoverFilmPath(slide.imdbId);
  const info = <Button size="xl" icon="info" {...linkProps(page)}>More info</Button>;

  if (slide.show || slide.kind === "show") {
    return (
      <div className="kc-actions">
        <Button variant="primary" size="xl" icon="info" {...linkProps(page)}>More info</Button>
        <WantButton imdbId={slide.imdbId} kind="show" title={slide.spot.title} />
      </div>
    );
  }
  if (here) {
    const room = slide.room;
    return (
      <div className="kc-actions">
        {room
          ? <WatchButton variant="primary" size="xl" icon="users" titleId={here.id} sessionId={room.sessionId}>{`Join the room (${room.participants})`}</WatchButton>
          : here.status === "ready" && <WatchButton variant="primary" size="xl" icon="play" titleId={here.id}>Watch</WatchButton>}
        {info}
        {slide.imdbId && <WantButton imdbId={slide.imdbId} kind="film" title={slide.spot.title} />}
      </div>
    );
  }
  return <FilmMatchActions found={{ ...slide.found, title: slide.spot.title }} compact>{info}</FilmMatchActions>;
}
