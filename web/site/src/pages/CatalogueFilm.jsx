import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { useCan } from "../session.js";
import { useCrumb } from "../crumbs.js";
import { moviePath } from "../paths.js";
import { navigate } from "../router.js";
import { Empty, Failed } from "../components/Film.jsx";
import { FilmActions } from "../components/FilmActions.jsx";
import { CastGrid, Credits, FilmScore, Genres, TitleSkeleton } from "../components/FilmDetails.jsx";

/**
 * A film by its IMDb id, here or not: what it is, and the way to it. A film in the library goes to its
 * own page. One on its way can be watched as it arrives; one the tracker offers can be watched now or
 * downloaded at 720p or 1080p; one that is not out digitally yet can be waited on.
 */
export function CatalogueFilm({ imdbId }) {
  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const film = useLoad(() => api.film(imdbId), [imdbId]);
  // Whether it can be fetched is asked by somebody who may fetch it or wait on it.
  const state = useLoad(() => (downloading || wishing ? api.availability(imdbId) : Promise.resolve(null)), [imdbId, downloading, wishing]);

  useCrumb(film.data ? film.data.title : film.error && film.error.status === 404 ? "No such film" : null);

  const here = state.data && state.data.libraryId;
  React.useEffect(() => {
    if (here) navigate(moviePath(here), { replace: true });
  }, [here]);
  if (here) return <div className="kc-page" />;

  if (film.error && film.error.status === 404) {
    return <div className="kc-page"><Empty title="No such film">The film database has nothing under {imdbId}.</Empty></div>;
  }
  if (!film.data && !film.error) return <TitleSkeleton kind="Movie" />;
  if (!film.data) return <div className="kc-page"><Failed error={film.error} onRetry={film.reload} /></div>;

  const d = film.data;
  const a = state.data;

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
            <span className="kc-eyebrow kc-eyebrow--muted">Movie</span>
            <h1>{d.title} {d.year && <span className="kc-quiet kc-tt__year">{d.year}</span>}</h1>
            {a && a.notOut && (
              <span className="kc-pill kc-pill--warn kc-tt__notout"><Icon name="calendar-clock" size={15} />Not out digitally yet</span>
            )}
            {(d.rating || d.genres.length > 0) && (
              <div className="kc-tt__score-row">
                {d.rating && <FilmScore rating={d.rating} votes={d.votes} />}
                <Genres names={d.genres} />
              </div>
            )}
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

      <p className="kc-attribution-note">Film details and artwork from TMDB.</p>
    </div>
  );
}
