import React from "react";
import { Button, Icon, Meter } from "@thekrystalship/krystal";
import { clock } from "../time.js";
import { discoverGenrePath, discoverPath } from "../paths.js";
import { linkProps } from "../router.js";
import { Credits, FilmScore, Genres, Tagline, votes } from "./FilmDetails.jsx";
import { TrailerPlayer } from "./TrailerPlayer.jsx";

/**
 * The page's trailer, for its Trailer button: `open` plays it and `playing` says whether it is; absent
 * when the title has none.
 */
export const TrailerContext = React.createContext(null);

/**
 * A title's hero, the head of every page about one title: its backdrop across the page behind a column
 * of text, a line saying what kind of title it is, its name, the facts line (`facts`), and under them
 * whatever the page puts there, each a group (`HeroGroup`) with room between it and the next. With
 * `trailer` (a description's `{key, name}`), the page's `TrailerButton` plays it: in the hero's place on
 * a wide screen, and full screen on a phone (`TrailerPlayer`).
 *
 * With a `poster` (a `PosterArt` or a `HeroPoster`) the hero is the plan's compact one instead: the
 * poster beside the text, framed as a card.
 */
export function TitleHero({ art, poster, eyebrow, title, facts, trailer, className = "", children }) {
  const [playing, setPlaying] = React.useState(false);
  const open = React.useCallback(() => setPlaying(true), []);
  const close = React.useCallback(() => setPlaying(false), []);
  if (poster) {
    return (
      <section className={"kc-tt" + (className ? " " + className : "")}>
        {art && <div className="kc-tt__art" style={{ backgroundImage: `url("${art}")` }} />}
        <div className="kc-tt__shade" />
        <div className="kc-tt__grid">
          {poster}
          <div className="kc-tt__body">
            <span className="kc-eyebrow kc-eyebrow--muted">{eyebrow}</span>
            <h1>{title}</h1>
            {children}
          </div>
        </div>
      </section>
    );
  }
  return (
    <TrailerContext.Provider value={trailer ? { open, playing } : null}>
      <section className={"kc-tt kc-tt--wide" + (playing ? " is-trailer" : "") + (className ? " " + className : "")}>
        {art && <div className="kc-tt__art" style={{ backgroundImage: `url("${art}")` }} />}
        <div className="kc-tt__shade" />
        <div className="kc-tt__body" inert={playing ? "" : undefined}>
          <div className="kc-tt__group">
            <span className="kc-eyebrow">{eyebrow}</span>
            <h1>{title}</h1>
            {facts}
          </div>
          {children}
        </div>
        {playing && trailer && <TrailerPlayer trailer={trailer} title={title} onClose={close} />}
      </section>
    </TrailerContext.Provider>
  );
}

/** One group of the hero's column: its parts close together, with room between it and the next group. */
export function HeroGroup({ children, className = "" }) {
  return <div className={"kc-tt__group" + (className ? " " + className : "")}>{children}</div>;
}

/**
 * Trailer, among the hero's buttons, for a title the hero has a trailer for; nothing for one without.
 * When the trailer closes, focus comes back to it, so a keyboard or a remote is where it was.
 */
export function TrailerButton({ size = "xl" }) {
  const trailer = React.useContext(TrailerContext);
  const ref = React.useRef(null);
  const was = React.useRef(false);
  const playing = !!(trailer && trailer.playing);
  React.useEffect(() => {
    if (was.current && !playing && ref.current) ref.current.focus();
    was.current = playing;
  }, [playing]);
  if (!trailer) return null;
  return <Button ref={ref} variant="secondary" size={size} icon="film" onClick={trailer.open}>Trailer</Button>;
}

/**
 * The facts line under a title's name: its rating and whose, then `parts` (a year, a certificate, a
 * length or a count of seasons, each a node; a certificate is `{cert}`), then its genres, each leading
 * to Discover's catalogue of that genre for `kind`.
 */
export function TitleFacts({ rating, votes: count, ratedBy, parts = [], genres = [], kind = "film", category = null }) {
  const shown = parts.filter((p) => p !== null && p !== undefined && p !== false && p !== "");
  return (
    <div className="kc-tt__meta">
      {rating ? (
        <span className="kc-tt__rating" title={`${rating.toFixed(1)} out of 10${count ? ` from ${count} votes` : ""}`}>
          <Icon name="star" size={16} className="kc-score__star" />
          <span className="kc-tt__rating-value">{rating.toFixed(1)}</span>
          <span className="kc-tt__rating-of">{ratedBy === "imdb" ? "IMDb" : "TMDB"}{count ? ` · ${votes(count)} votes` : ""}</span>
        </span>
      ) : null}
      {shown.map((p, i) => (p && p.cert
        ? <span key={i} className="kc-cert">{p.cert}</span>
        : <span key={i}>{p}</span>))}
      {genres.length > 0 && (
        <span className="kc-tt__genres">
          {genres.map((g, i) => (
            <React.Fragment key={g}>
              {i > 0 && " · "}
              <a className="kc-tt__genre" {...linkProps(discoverGenrePath(kind, g, category))}>{g}</a>
            </React.Fragment>
          ))}
        </span>
      )}
    </div>
  );
}

/**
 * The line an awards summary reads: the top honour where it is one everybody knows, the Academy Awards
 * among the wins where every win is listed (a list cut to its newest could undercount them), the wins
 * and the nominations. Null when it won and was nominated for nothing.
 */
export function honoursLine(awards) {
  if (!awards || (awards.wins === 0 && awards.nominations === 0)) return null;
  const won = awards.won || [];
  const lead = won.some((a) => a.name === "Academy Award for Best Picture") ? "Best Picture"
    : won.some((a) => /^Palme d'Or/.test(a.name)) ? "Palme d'Or" : null;
  const oscars = won.length === awards.wins ? won.filter((a) => a.name.startsWith("Academy Award")).length : 0;
  const counts = [
    oscars > 0 ? `${oscars} Academy Award${oscars === 1 ? "" : "s"}` : null,
    awards.wins > 0 ? `${awards.wins} ${awards.wins === 1 ? "win" : "wins"}` : null,
    awards.nominations > 0 ? `${awards.nominations} ${awards.nominations === 1 ? "nomination" : "nominations"}` : null,
  ].filter(Boolean).join(" · ");
  return { lead, counts };
}

/**
 * What a film won and what it is based on, as Wikidata says: the awards line leading to the page's
 * Awards section, and the source with its author leading to the author's adaptations. Nothing when
 * Wikidata says neither.
 */
export function TitleHonours({ awards, basedOn = [] }) {
  const line = honoursLine(awards);
  const source = basedOn[0];
  if (!line && !source) return null;
  return (
    <HeroGroup className="kc-tt__honours">
      {line && (
        <a className="kc-tt__honour kc-tt__honour--link" href="#awards">
          <Icon name="trophy" size={18} />
          {line.lead && <b>{line.lead}</b>}
          <span>{line.counts}</span>
        </a>
      )}
      {source && (
        <span className="kc-tt__honour">
          <Icon name="book-open" size={18} />
          <span>
            Based on <i>{source.work}</i>
            {source.author && <> by <a {...linkProps(discoverPath(`${source.author} adaptations`))}>{source.author}</a></>}
          </span>
        </span>
      )}
    </HeroGroup>
  );
}

/** What a title is about and who made it: the overview, three lines until More opens it, and the credits table. */
export function TitleAbout({ overview, credits }) {
  const [open, setOpen] = React.useState(false);
  const ref = React.useRef(null);
  const [long, setLong] = React.useState(false);
  React.useLayoutEffect(() => {
    const el = ref.current;
    if (el) setLong(el.scrollHeight > el.clientHeight + 1);
  }, [overview]);
  if (!overview && !credits) return null;
  return (
    <HeroGroup>
      {overview && (
        <p ref={ref} className={"kc-tt__overview kc-tt__overview--clamp" + (open ? " is-open" : "")}>
          {overview}
        </p>
      )}
      {overview && (long || open) && (
        <Button variant="bare" className="kc-tt__more" onClick={() => setOpen(!open)}>{open ? "Less" : "More"}</Button>
      )}
      {credits && <Credits film={credits} />}
    </HeroGroup>
  );
}

/** A position under this is where a title was opened rather than watched, and draws no bar. */
const WATCHED_AT_LEAST = 60;

/** How far into a title this person is, under the hero's buttons: a bar, and where they are of how long. */
export function ResumeMeter({ position, duration }) {
  if (!position || !duration || position < WATCHED_AT_LEAST) return null;
  return (
    <Meter className="kc-tt__resume" value={Math.min(position, duration)} max={duration} label="Watched"
      end={`${clock(position)} of ${clock(duration)}`} />
  );
}

/** A poster in the plan's hero from its address, or the name on a tinted card where there is none. */
export function HeroPoster({ url, name }) {
  return (
    <div className="kc-poster-art kc-tt__poster">
      {url ? <img src={url} alt="" /> : <span className="kc-poster-art__name">{name}</span>}
    </div>
  );
}

/** A film's facts line in the plan's hero: its year, its certification and its running time. */
export function FilmFacts({ film }) {
  if (!film.year && !film.certification && !film.runtimeMinutes) return null;
  return (
    <div className="kc-tt__meta">
      {film.year && <span>{film.year}</span>}
      {film.certification && <span className="kc-cert">{film.certification}</span>}
      {film.runtimeMinutes && <span>{clock(film.runtimeMinutes * 60)}</span>}
    </div>
  );
}

/** What a title is about in the plan's hero: the score and genres, the tagline and the overview. */
export function TitleStory({ rating, votes: count, ratedBy, genres = [], tagline, overview }) {
  return (
    <>
      {(rating || genres.length > 0) && (
        <div className="kc-tt__score-row">
          {rating && <FilmScore rating={rating} votes={count} by={ratedBy} />}
          <Genres names={genres} />
        </div>
      )}
      <Tagline text={tagline} />
      {overview && <p className="kc-tt__overview">{overview}</p>}
    </>
  );
}

/**
 * What a film won, as its own section of the page, where the hero's awards line leads: the awards by
 * name with the year each was given, the newest first, and how many more there are when not every win
 * is listed. Nothing for a film that won nothing.
 */
export function TitleAwards({ awards }) {
  if (!awards || !awards.won || awards.won.length === 0) return null;
  const more = awards.wins - awards.won.length;
  return (
    <section className="kc-section" id="awards">
      <h2>Awards</h2>
      <ul className="kc-awards">
        {awards.won.map((a) => (
          <li key={a.name + a.year} className="kc-awards__item">
            <Icon name="trophy" size={15} />
            <span className="kc-awards__name">{a.name}</span>
            {a.year && <span className="kc-awards__year">{a.year}</span>}
          </li>
        ))}
      </ul>
      {more > 0 && <span className="kc-note">And {more} more {more === 1 ? "win" : "wins"}.</span>}
    </section>
  );
}
