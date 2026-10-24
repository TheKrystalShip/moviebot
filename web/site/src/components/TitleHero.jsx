import { clock } from "../time.js";
import { FilmScore, Genres, Tagline } from "./FilmDetails.jsx";

/**
 * A title's hero, the head of every page about one title: its backdrop behind, its poster, a line
 * saying what kind of title it is, its name, and under the name whatever the page puts there (the
 * facts line, the story, the credits, the actions). `poster` is the poster's node, a `PosterArt` or a
 * `HeroPoster`.
 */
export function TitleHero({ art, poster, eyebrow, title, className = "", children }) {
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

/** A poster in the hero from its address, or the name on a tinted card where there is none. */
export function HeroPoster({ url, name }) {
  return (
    <div className="kc-poster-art kc-tt__poster">
      {url ? <img src={url} alt="" /> : <span className="kc-poster-art__name">{name}</span>}
    </div>
  );
}

/** A film's facts line from the film database: its year, its certification and its running time. */
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

/** What a title is about, as the hero tells it: the score and genres, the tagline and the overview. */
export function TitleStory({ rating, votes, ratedBy, genres = [], tagline, overview }) {
  return (
    <>
      {(rating || genres.length > 0) && (
        <div className="kc-tt__score-row">
          {rating && <FilmScore rating={rating} votes={votes} by={ratedBy} />}
          <Genres names={genres} />
        </div>
      )}
      <Tagline text={tagline} />
      {overview && <p className="kc-tt__overview">{overview}</p>}
    </>
  );
}
