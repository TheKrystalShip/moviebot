import React from "react";
import { Pill } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { useCan } from "../session.js";
import { useCrumb } from "../crumbs.js";
import { ANIME, moviePath } from "../paths.js";
import { navigate } from "../router.js";
import { clock } from "../time.js";
import { Empty, Failed } from "../components/Film.jsx";
import { FilmActions } from "../components/FilmActions.jsx";
import { CastGrid, Credits, FactGrid, FilmScore, Genres, Keywords, Tagline, TitleSkeleton, originalTitle } from "../components/FilmDetails.jsx";

/**
 * A film by its IMDb id, here or not: what it is, and the way to it. A film that plays now goes to its
 * own page. One being prepared or one the tracker offers is watched while it is prepared; one that is
 * not out digitally yet can be waited on.
 */
export function CatalogueFilm({ imdbId }) {
  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const film = useLoad(() => api.film(imdbId), [imdbId]);
  // Whether it can be fetched is asked by somebody who may fetch it or wait on it.
  const state = useLoad(() => (downloading || wishing ? api.availability(imdbId) : Promise.resolve(null)), [imdbId, downloading, wishing]);

  useCrumb(film.data ? film.data.title : film.error && film.error.status === 404 ? "No such film" : null);

  const here = state.data && state.data.libraryId;
  const shelved = film.data && film.data.category;
  React.useEffect(() => {
    if (here) navigate(moviePath(here, shelved), { replace: true });
  }, [here, shelved]);
  if (here) return <div className="kc-page" />;

  if (film.error && film.error.status === 404) {
    return <div className="kc-page"><Empty title="No such film">The film database has nothing under {imdbId}.</Empty></div>;
  }
  if (!film.data && !film.error) return <TitleSkeleton kind="Movie" />;
  if (!film.data) return <div className="kc-page"><Failed error={film.error} onRetry={film.reload} /></div>;

  const d = film.data;
  const a = state.data;
  // The library page's facts, less what only a held copy knows: its picture and when it was added.
  const facts = [
    ["Original title", originalTitle(d.title, d.originalTitle)],
    ["Released", d.releaseDate ? new Date(d.releaseDate).getFullYear() : d.year],
    ["Language", d.languages.length ? d.languages.join(", ") : null],
    ["Country", d.countries.length ? d.countries.join(", ") : null],
    ["IMDb", d.imdbId, true],
  ];

  return (
    <div className="kc-page kc-title">
      <section className="kc-tt">
        {(d.backdropUrl || d.posterUrl) && <div className="kc-tt__art" style={{ backgroundImage: `url("${d.backdropUrl || d.posterUrl}")` }} />}
        <div className="kc-tt__shade" />
        <div className="kc-tt__grid">
          <div className="kc-poster-art kc-tt__poster">
            {d.posterUrl ? <img src={d.posterUrl} alt="" /> : <span className="kc-poster-art__name">{d.title}</span>}
          </div>
          <div className="kc-tt__body">
            <span className="kc-eyebrow kc-eyebrow--muted">{d.category === ANIME ? "Anime movie" : "Movie"}</span>
            <h1>{d.title}</h1>
            {(d.year || d.certification || d.runtimeMinutes) && (
              <div className="kc-tt__meta">
                {d.year && <span>{d.year}</span>}
                {d.certification && <span className="kc-cert">{d.certification}</span>}
                {d.runtimeMinutes && <span>{clock(d.runtimeMinutes * 60)}</span>}
              </div>
            )}
            {a && a.notOut && (
              <Pill tone="warn" icon="calendar-clock" className="kc-tt__notout">Not out digitally yet</Pill>
            )}
            {(d.rating || d.genres.length > 0) && (
              <div className="kc-tt__score-row">
                {d.rating && <FilmScore rating={d.rating} votes={d.votes} />}
                <Genres names={d.genres} />
              </div>
            )}
            <Tagline text={d.tagline} />
            {d.overview && <p className="kc-tt__overview">{d.overview}</p>}
            <Credits film={d} />

            <FilmActions imdbId={imdbId} film={d} state={state} />
          </div>
        </div>
      </section>

      {d.cast.length > 0 && (
        <section className="kc-section">
          <h2>Cast</h2>
          <CastGrid cast={d.cast} />
        </section>
      )}

      <section className="kc-section">
        <h2>Details</h2>
        <FactGrid facts={facts} />
      </section>

      <Keywords names={d.keywords} />

      <p className="kc-attribution-note">Film details and artwork from TMDB.</p>
    </div>
  );
}
