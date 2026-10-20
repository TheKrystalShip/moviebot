import React from "react";
import { api, filmName, posterUrl } from "../api.js";
import { useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { ANIME, moviePath, tvPath } from "../paths.js";
import { navigate } from "../router.js";
import { useCan, useSession } from "../session.js";
import { ago, clock, since, timeLeft } from "../time.js";
import { Empty, Failed, PosterArt, WatchButton } from "../components/Film.jsx";
import { WatchOnTvActions, useDriving } from "../components/WatchOnTv.jsx";
import { CastGrid, Credits, FactGrid, FilmScore, Genres, Keywords, Tagline, TitleSkeleton, originalTitle } from "../components/FilmDetails.jsx";
import { WantButton } from "../components/WantButton.jsx";
import { PlanButton } from "../components/PlanButton.jsx";
import { Wanters } from "../components/Wanters.jsx";
import { DownloadButton, useOffersSaving } from "../components/DownloadButton.jsx";
import { useDeviceTitle } from "../offline/hooks.js";
import { Unreachable } from "./Offline.jsx";

/**
 * One film in the library: what it is, who made it and who is in it, watch, and Download.
 *
 * What the film is comes from the film database through the API, which keeps it; what is on this
 * server — its length, its picture, its size, how far this person got — comes from the library.
 * `fromDevice` is the page opened with no connection, drawn from the film's download on this device
 * (`useDeviceTitle`); a film not downloaded here cannot be drawn. `category` is the shelf the address
 * is under; a film the API shelves elsewhere moves to its own shelf's address.
 */
export function Title({ id, category = null, fromDevice = false }) {
  const device = useDeviceTitle(id, fromDevice);
  const served = useLoad(() => (fromDevice ? Promise.resolve(null) : api.title(id)), [id]);
  const manifest = fromDevice ? device.manifest : served;
  const listing = useLoad(() => (fromDevice ? Promise.resolve(null) : api.titles().then((all) => all.find((t) => t.id === id) || null)), [id]);
  const imdbId = !fromDevice && manifest.data && manifest.data.film && manifest.data.film.imdbId;
  const described = useLoad(() => (imdbId ? api.film(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId]);
  const details = fromDevice ? device.details : described;
  const rooms = useLoad(() => (fromDevice ? Promise.resolve([]) : api.rooms()), [id]);
  const session = useSession();
  const me = session && session.user;
  const saving = useOffersSaving();
  const driving = useDriving();
  // A film still being made is downloaded as it is made, which fetches onto the server.
  const fetching = useCan("cinema:download") && !fromDevice;

  useCrumb(manifest.data
    ? (details.data && details.data.title) || (manifest.data.film && manifest.data.film.name) || manifest.data.title
    : manifest.error && manifest.error.status === 404 ? "Not found" : null);

  // An episode is shown on its show's page, at its season.
  const episodeOf = manifest.data && manifest.data.episode && manifest.data.film && manifest.data.film.imdbId
    ? tvPath(manifest.data.film.imdbId, manifest.data.episode.season) : null;
  React.useEffect(() => {
    if (episodeOf) navigate(episodeOf, { replace: true });
  }, [episodeOf]);
  // Under Anime or Movies, as the API shelves it. A film the listing does not name stays where it is.
  const shelved = listing.data ? (listing.data.category === ANIME ? ANIME : null) : category;
  React.useEffect(() => {
    if (!episodeOf && shelved !== category) navigate(moviePath(id, shelved), { replace: true });
  }, [episodeOf, shelved, category, id]);
  if (episodeOf) return <div className="kc-page" />;
  if (fromDevice && !manifest.data) return manifest.loading ? <div className="kc-page" /> : <Unreachable />;

  if (manifest.error && manifest.error.status === 404) {
    return <div className="kc-page"><Empty title="Not found">Search for it in Discover to watch it.</Empty></div>;
  }
  if (!manifest.data && !manifest.error) return <TitleSkeleton kind="Movie" />;
  if (!manifest.data) return <div className="kc-page"><Failed error={manifest.error} onRetry={manifest.reload} /></div>;

  const m = manifest.data;
  const title = { id: m.id, title: m.title, poster: m.poster, film: m.film, details: details.data };
  const film = m.film || {};
  const d = details.data;
  const summary = fromDevice ? device.summary : listing.data;
  // A room with nobody in it is not one to join: it is kept a while after the last person leaves.
  const room = (rooms.data || []).find((r) => r.titleId === m.id && r.participants > 0);
  const backdrop = (d && d.backdropUrl) || posterUrl(title);
  const progress = summary && summary.progress && !summary.progress.finished && summary.progress.positionSeconds > 0
    ? summary.progress : null;
  const played = summary && summary.lastPlayed;
  const playedBy = played && (me && played.byId === me.id ? "you" : played.byName);

  const facts = [
    ["Original title", d ? originalTitle(d.title, d.originalTitle) : null],
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
            <span className="kc-eyebrow kc-eyebrow--muted">{shelved === ANIME ? "Anime movie" : "Movie"}</span>
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
            {d && <Tagline text={d.tagline} />}
            {d && d.overview && <p className="kc-tt__overview">{d.overview}</p>}
            {m.status === "transcoding" && (
              <span className="kc-note">Still being prepared{m.headSeconds ? `: watchable up to ${clock(m.headSeconds)}` : ""}.</span>
            )}
            {m.status === "failed" && <span className="kc-note kc-note--danger">This film could not be prepared, so it cannot be played.</span>}
            {d ? <Credits film={d} /> : film.starring && <Credits film={{ directors: [], writers: [], cast: film.starring.split(", ").map((name) => ({ name })) }} />}

            <div className="kc-actions">
              {m.status !== "failed" && driving && (
                <WatchOnTvActions titleId={m.id} sessionId={room && room.sessionId} at={progress && progress.positionSeconds}
                  target={{ name: (d && d.title) || filmName(m), meta: [film.year || (d && d.year), clock(m.durationSeconds)].filter(Boolean).join(" · "), art: backdrop }} />
              )}
              {m.status !== "failed" && !driving && (
                <WatchButton variant="primary" size="xl" icon={room ? "users" : "play"}
                  titleId={m.id} sessionId={room && room.sessionId} at={progress && progress.positionSeconds}>
                  {room ? `Join the room (${room.participants})` : progress ? "Resume" : "Watch"}
                  {!room && progress && <span className="kc-btn__sub">{timeLeft(m.durationSeconds - progress.positionSeconds)}</span>}
                </WatchButton>
              )}
              {film.imdbId && <WantButton imdbId={film.imdbId} kind="film" title={filmName(m)} />}
              {film.imdbId && <PlanButton imdbId={film.imdbId} kind="film" />}
              {saving && (m.status === "ready" || (fetching && m.status === "transcoding" && film.imdbId)) && (
                <DownloadButton titleId={m.id} imdbId={film.imdbId || null} ready={m.status === "ready"} head={{
                  name: (d && d.title) || filmName(m), context: film.year || (d && d.year) || null,
                  runtime: clock(m.durationSeconds), still: posterUrl(title), portrait: true, backdrop,
                }} />
              )}
            </div>
            {film.imdbId && !fromDevice && <Wanters imdbId={film.imdbId} />}
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

      {d && <Keywords names={d.keywords} />}

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
