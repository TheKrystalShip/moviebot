import React from "react";
import { Pill } from "@thekrystalship/krystal";
import { catalogue, certificateOf, libraryApi, runtimeText } from "../api/index.js";
import { useLoad } from "../data.js";
import { useCan } from "../session.js";
import { useCrumb } from "../crumbs.js";
import { ANIME, moviePath } from "../paths.js";
import { navigate } from "../router.js";
import { Empty, Failed } from "../components/Film.jsx";
import { FilmActions } from "../components/FilmActions.jsx";
import { TitleSkeleton } from "../components/FilmDetails.jsx";
import { TitleSections, titleFacts } from "../components/TitleSections.jsx";
import { HeroGroup, kindWord, TitleAbout, TitleFacts, TitleHero, TitleHonours, TrailerButton } from "../components/TitleHero.jsx";

/**
 * A film by its IMDb id, here or not: what it is, and the way to it. A film that plays now goes to its
 * own page. One being prepared or one the tracker offers is watched while it is prepared; one that is
 * not out digitally yet is wanted, to be told when it is.
 */
export function CatalogueFilm({ imdbId }) {
  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const film = useLoad(() => catalogue.title(imdbId), [imdbId]);
  // Whether it can be fetched is asked by somebody who may fetch it or wait on it.
  const state = useLoad(() => (downloading || wishing ? libraryApi.availability(imdbId) : Promise.resolve(null)), [imdbId, downloading, wishing]);

  useCrumb(film.data ? film.data.title : film.error && film.error.status === 404 ? "No such film" : null);

  const here = (state.data && state.data.libraryId) || (film.data && film.data.you && film.data.you.libraryId);
  const shelved = film.data && film.data.category === ANIME ? ANIME : null;
  React.useEffect(() => {
    if (here) navigate(moviePath(here, shelved), { replace: true });
  }, [here, shelved]);
  if (here) return <div className="kc-page" />;

  if (film.error && film.error.status === 404) {
    return <div className="kc-page"><Empty title="No such film">The catalogue has nothing under {imdbId}.</Empty></div>;
  }
  if (!film.data && !film.error) return <TitleSkeleton kind="Movie" />;
  if (!film.data) return <div className="kc-page"><Failed error={film.error} onRetry={film.reload} /></div>;

  const d = film.data;
  const a = state.data;
  const certificate = certificateOf(d);

  return (
    <div className="kc-page kc-title">
      <TitleHero art={d.backdrop || d.poster} eyebrow={kindWord("film", shelved === ANIME)} title={d.title}
        trailer={d.trailers[0]}
        facts={<TitleFacts rating={d.rating && d.rating.value} votes={d.rating && d.rating.votes} ratedBy={d.rating && d.rating.by}
          kind="film" category={shelved} parts={[d.year, certificate && { cert: certificate }, runtimeText(d.runtime)]}
          genres={d.genres} />}>
        <TitleHonours facts={d.facts} />
        <TitleAbout overview={d.overview || d.plot} credits={d} />
        <HeroGroup>
          {a && a.notOut && (
            <Pill tone="warn" icon="calendar-clock" className="kc-tt__notout">Not out digitally yet</Pill>
          )}
          <FilmActions imdbId={imdbId} film={d} state={state} vote afterWatch={<TrailerButton />} />
        </HeroGroup>
      </TitleHero>
      <TitleSections page={d} facts={titleFacts(d)} />
    </div>
  );
}
