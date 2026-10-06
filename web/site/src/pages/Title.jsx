import React from "react";
import { Button } from "@thekrystalship/krystal-ui";
import { api, filmName, posterUrl } from "../api.js";
import { useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { tvPath } from "../paths.js";
import { navigate } from "../router.js";
import { useSession } from "../session.js";
import { ago, clock, since, timeLeft } from "../time.js";
import { Empty, Failed, PosterArt, watchLink } from "../components/Film.jsx";
import { CastGrid, Credits, FactGrid, FilmScore, Genres, TitleSkeleton } from "../components/FilmDetails.jsx";
import { SaveButton } from "../components/SaveButton.jsx";

/**
 * One film in the library: what it is, who made it and who is in it, and watch.
 *
 * What the film is comes from the film database through the API, which keeps it; what is on this
 * server — its length, its picture, its size, how far this person got — comes from the library.
 */
export function Title({ id }) {
  const manifest = useLoad(() => api.title(id), [id]);
  const listing = useLoad(() => api.titles().then((all) => all.find((t) => t.id === id) || null), [id]);
  const imdbId = manifest.data && manifest.data.film && manifest.data.film.imdbId;
  const details = useLoad(() => (imdbId ? api.film(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId]);
  const rooms = useLoad(() => api.rooms(), [id]);
  const session = useSession();
  const me = session && session.user;

  useCrumb(manifest.data
    ? (details.data && details.data.title) || (manifest.data.film && manifest.data.film.name) || manifest.data.title
    : manifest.error && manifest.error.status === 404 ? "Not found" : null);

  // An episode is shown on its show's page, at its season.
  const episodeOf = manifest.data && manifest.data.episode && manifest.data.film && manifest.data.film.imdbId
    ? tvPath(manifest.data.film.imdbId, manifest.data.episode.season) : null;
  React.useEffect(() => {
    if (episodeOf) navigate(episodeOf, { replace: true });
  }, [episodeOf]);
  if (episodeOf) return <div className="kc-page" />;

  if (manifest.error && manifest.error.status === 404) {
    return <div className="kc-page"><Empty title="Not found">Search for it in Discover to watch it.</Empty></div>;
  }
  if (!manifest.data && !manifest.error) return <TitleSkeleton kind="Movie" />;
  if (!manifest.data) return <div className="kc-page"><Failed error={manifest.error} onRetry={manifest.reload} /></div>;

  const m = manifest.data;
  const title = { id: m.id, title: m.title, poster: m.poster, film: m.film };
  const film = m.film || {};
  const d = details.data;
  const summary = listing.data;
  // A room with nobody in it is not one to join: it is kept a while after the last person leaves.
  const room = (rooms.data || []).find((r) => r.titleId === m.id && r.participants > 0);
  const backdrop = (d && d.backdropUrl) || posterUrl(title);
  const progress = summary && summary.progress && !summary.progress.finished && summary.progress.positionSeconds > 0
    ? summary.progress : null;
  const played = summary && summary.lastPlayed;
  const playedBy = played && (me && played.byId === me.id ? "you" : played.byName);

  const facts = [
    ["Released", d && d.releaseDate ? new Date(d.releaseDate).getFullYear() : film.year],
    ["Language", d && d.languages.length ? d.languages.join(", ") : null],
    ["Country", d && d.countries.length ? d.countries.join(", ") : null],
    ["Picture", pictureClass(m.video), true],
    ["Added", summary && summary.addedAt ? since(summary.addedAt) : null],
    ["IMDb", film.imdbId, true],
  ];

  return (
    <div className="kc-page kc-title">
      <section className="kc-tt">
        {backdrop && <div className="kc-tt__art" style={{ backgroundImage: `url("${backdrop}")` }} />}
        <div className="kc-tt__shade" />
        <div className="kc-tt__grid">
          <PosterArt title={title} className="kc-tt__poster" />
          <div className="kc-tt__body">
            <span className="kc-eyebrow kc-eyebrow--muted">Movie</span>
            <h1>{(d && d.title) || film.name || m.title}</h1>
            <div className="kc-tt__meta">
              {(film.year || (d && d.year)) && <span>{film.year || d.year}</span>}
              {d && d.certification && <span className="kc-cert">{d.certification}</span>}
              <span>{clock(m.durationSeconds)}</span>
            </div>
            {d && (d.rating || d.genres.length > 0) && (
              <div className="kc-tt__score-row">
                {d.rating && <FilmScore rating={d.rating} votes={d.votes} />}
                <Genres names={d.genres} />
              </div>
            )}
            {d && d.overview && <p className="kc-tt__overview">{d.overview}</p>}
            {m.status === "transcoding" && (
              <span className="kc-note">Still being prepared{m.headSeconds ? `: watchable up to ${clock(m.headSeconds)}` : ""}.</span>
            )}
            {m.status === "failed" && <span className="kc-note kc-note--danger">This film could not be prepared, so it cannot be played.</span>}
            {d ? <Credits film={d} /> : film.starring && <Credits film={{ directors: [], writers: [], cast: film.starring.split(", ").map((name) => ({ name })) }} />}

            <div className="kc-actions">
              {m.status !== "failed" && (
                <Button variant="primary" size="xl" icon={room ? "users" : "play"}
                  {...watchLink(m.id, room && room.sessionId, progress && progress.positionSeconds)}>
                  {room ? `Join the room (${room.participants})` : progress ? "Resume" : "Watch"}
                  {!room && progress && <span className="kc-btn__sub">{timeLeft(m.durationSeconds - progress.positionSeconds)}</span>}
                </Button>
              )}
              {film.imdbId && <SaveButton imdbId={film.imdbId} kind="film" title={filmName(m)} />}
            </div>
            {played && (
              <span className="kc-note">Played {ago(played.at)} by {playedBy}</span>
            )}
          </div>
        </div>
      </section>

      {d && d.cast.length > 0 && (
        <section className="kc-section">
          <h2>Cast</h2>
          <CastGrid cast={d.cast} />
        </section>
      )}

      <section className="kc-section">
        <h2>Details</h2>
        <FactGrid facts={facts} />
      </section>

      {d && <p className="kc-attribution-note">Film details and artwork from TMDB.</p>}
    </div>
  );
}

/**
 * The picture's class, read from its width: a scope film is cropped top and bottom, so its height
 * says 804 where the release, and anybody choosing it, says 1080p.
 */
export function pictureClass(video) {
  if (!video || !video.width) return null;
  if (video.width >= 3200) return "4K";
  if (video.width >= 1800) return "1080p";
  if (video.width >= 1200) return "720p";
  return "SD";
}
