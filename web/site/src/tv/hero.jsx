import React from "react";
import { Button, CarouselPager, TvBackdrop, TvSpotlight, createStore, useCarousel, useStore } from "@thekrystalship/krystal";
import { discoverFilmPath, moviePath, showPath } from "../paths.js";
import { linkProps } from "../router.js";
import { watchLink } from "../components/Film.jsx";
import { WantButton } from "../components/WantButton.jsx";
import { SLIDE_MS } from "../components/Spotlight.jsx";
import { readySlides } from "../heroes.js";
import { SpotFacts, SpotStage } from "./spot.jsx";
import { startFilm } from "./pages/TvFilm.jsx";

/**
 * The top of Home and of Discover on a television: one of `slides` (`heroes.js`) whose artwork has
 * arrived, large over it, turning to the next by itself while it is on the screen. A press starts the
 * slide's time over, and focus stays on the same action as the slide turns, so OK chooses what is on
 * the screen.
 *
 * The slides hold still while focus is on the page below them. With `store`, a page's spotlight
 * (`useSpotlight`), the artwork is the page's stage, and they hold still while focus is in its rows. `initial` puts the page's first focus on the first
 * action; `more` follows each slide's actions. `className` and `rest` go on the slides' section.
 */
export function TvHero({ slides: laid, store, initial = false, more, className, ...rest }) {
  const slides = readySlides(laid);
  const idle = React.useMemo(() => createStore({ row: -1 }), []);
  const inRows = useStore(store || idle, (s) => s.row >= 0);
  const section = React.useRef(null);
  const below = useFocusBelow(section);
  const carousel = useCarousel({ count: slides.length, interval: SLIDE_MS, pauseOnFocus: false, paused: inRows || below });
  const focused = React.useRef(-1);
  const slide = slides[carousel.index];

  // Each slide's words arrive afresh, its actions with them: where focus was on the last slide's
  // actions, it goes to the action in the same place on this one, or its last.
  React.useLayoutEffect(() => {
    const el = section.current;
    if (!el || focused.current < 0 || el.contains(document.activeElement)) return;
    const actions = el.querySelectorAll(ACTIONS);
    const to = actions[Math.min(focused.current, actions.length - 1)];
    if (to) to.focus({ preventScroll: true });
  }, [carousel.index]);

  const track = {
    onFocus: (e) => {
      focused.current = [...section.current.querySelectorAll(ACTIONS)].indexOf(e.target);
    },
    onBlur: (e) => { if (e.relatedTarget && !section.current.contains(e.relatedTarget)) focused.current = -1; },
  };

  if (!slide) return null;
  const { spot } = slide;
  return (
    <>
      {store ? <SpotStage store={store} art={spot.art} /> : <TvBackdrop src={spot.art} size="tall" />}
      <section ref={section} className={className} {...rest} {...carousel.rootProps}
        onFocus={(e) => { carousel.rootProps.onFocus(e); track.onFocus(e); }}
        onBlur={(e) => { carousel.rootProps.onBlur(e); track.onBlur(e); }}>
        <TvSpotlight key={slide.key} className="kc-tv-hero" size="lg" eyebrow={spot.eyebrow} title={spot.title} meta={<SpotFacts facts={spot.facts} />}
          overview={spot.overview} actions={<div className="kc-tv-hero__actions"><TvSlideActions slide={slide} initial={initial} more={more} /></div>}>
          <CarouselPager carousel={carousel} indicator className="kc-tv-hero__pager" />
        </TvSpotlight>
      </section>
    </>
  );
}

/** A slide's actions, in the order focus moves through them. */
const ACTIONS = ".kc-tv-hero__actions :is(a[href], button)";

/**
 * Whether focus is on the page below `section`, in the rows under the slides, which have gone up the
 * screen with it.
 */
function useFocusBelow(section) {
  const [below, setBelow] = React.useState(false);
  React.useEffect(() => {
    const moved = (e) => {
      const el = section.current;
      if (!el || el.contains(e.target) || !e.target.getBoundingClientRect) { setBelow(false); return; }
      setBelow(e.target.getBoundingClientRect().top >= el.getBoundingClientRect().bottom);
    };
    document.addEventListener("focusin", moved);
    return () => document.removeEventListener("focusin", moved);
  }, [section]);
  return below;
}

/**
 * What can be done with a slide's title on a television: Watch, or Join the room, for a film here;
 * Watch for a film that is out and has to be fetched, which fetches it into a room; More info for
 * every title; and Want.
 */
function TvSlideActions({ slide, initial, more }) {
  const [starting, setStarting] = React.useState(false);
  const first = initial ? { "data-tv-initial": "" } : {};
  const here = slide.title || slide.here;
  const show = slide.show || slide.kind === "show";
  const page = show
    ? showPath(slide.imdbId, { held: slide.held, category: (slide.show || slide.found || {}).category })
    : here ? moviePath(here.id, here.category) : discoverFilmPath(slide.imdbId);
  const fetch = async () => {
    setStarting(true);
    if (!(await startFilm(slide.imdbId))) setStarting(false);
  };

  let watch = null;
  if (!show && here && slide.room) {
    watch = <Button variant="primary" size="lg" icon="users" {...first} {...watchLink(here.id, slide.room.sessionId)}>Join the room</Button>;
  } else if (!show && here) {
    watch = <Button variant="primary" size="lg" icon="play" {...first} {...watchLink(here.id)}>Watch</Button>;
  } else if (!show && !slide.upcoming) {
    watch = <Button variant="primary" size="lg" icon="play" busy={starting} {...first} onClick={fetch}>Watch</Button>;
  }
  return (
    <>
      {watch}
      <Button variant={watch ? undefined : "primary"} size="lg" icon="info" {...(watch ? {} : first)} {...linkProps(page)}>More info</Button>
      {slide.imdbId && <WantButton imdbId={slide.imdbId} kind={show ? "show" : "film"} title={slide.spot.title} />}
      {more}
    </>
  );
}
