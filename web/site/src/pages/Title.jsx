import React from "react";
import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api, filmName, posterUrl } from "../api.js";
import { leaveRule, stayWords, useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { tvPath } from "../paths.js";
import { navigate } from "../router.js";
import { useCan, useSession } from "../session.js";
import { ago, clock, since, timeLeft } from "../time.js";
import { Empty, Failed, PosterArt, watchLink } from "../components/Film.jsx";
import { CastGrid, Credits, FactGrid, FilmScore, Genres, bytes } from "../components/FilmDetails.jsx";
import { SaveButton } from "../components/SaveButton.jsx";

/**
 * One film in the library: what it is, who made it and who is in it, how long it stays, and watch.
 *
 * What the film is comes from the film database through the API, which keeps it; what is on this
 * server — its length, its picture, its size, how long it stays, how far this person got — comes from
 * the library.
 */
export function Title({ id }) {
  const manifest = useLoad(() => api.title(id), [id]);
  const listing = useLoad(() => api.titles().then((all) => all.find((t) => t.id === id) || null), [id]);
  const imdbId = manifest.data && manifest.data.film && manifest.data.film.imdbId;
  const details = useLoad(() => (imdbId ? api.film(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId]);
  const stay = useLoad(() => api.stay(id).catch(() => null), [id]);
  const rooms = useLoad(() => api.rooms(), [id]);
  const [busy, setBusy] = React.useState(false);
  const session = useSession();
  const keeping = useCan("cinema:keep");
  const me = session && session.user;

  useCrumb(manifest.data
    ? (details.data && details.data.title) || (manifest.data.film && manifest.data.film.name) || manifest.data.title
    : manifest.error && manifest.error.status === 404 ? "Not in the library" : null);

  // An episode is shown on its show's page, at its season.
  const episodeOf = manifest.data && manifest.data.episode && manifest.data.film && manifest.data.film.imdbId
    ? tvPath(manifest.data.film.imdbId, manifest.data.episode.season) : null;
  React.useEffect(() => {
    if (episodeOf) navigate(episodeOf, { replace: true });
  }, [episodeOf]);
  if (episodeOf) return <div className="kc-page" />;

  if (manifest.error && manifest.error.status === 404) {
    return <div className="kc-page"><Empty title="Not in the library">This film has left the library, or never arrived.</Empty></div>;
  }
  if (!manifest.data) return <div className="kc-page"><Failed error={manifest.error} onRetry={manifest.reload} /></div>;

  const m = manifest.data;
  const title = { id: m.id, title: m.title, poster: m.poster, film: m.film };
  const film = m.film || {};
  const d = details.data;
  const summary = listing.data;
  const held = stay.data && stay.data.settled ? stay.data : null;
  // A room with nobody in it is not one to join: it is kept a while after the last person leaves.
  const room = (rooms.data || []).find((r) => r.titleId === m.id && r.participants > 0);
  const backdrop = (d && d.backdropUrl) || posterUrl(title);
  const progress = summary && summary.progress && !summary.progress.finished && summary.progress.positionSeconds > 0
    ? summary.progress : null;
  const played = summary && summary.lastPlayed;
  const playedBy = played && (me && played.byId === me.id ? "you" : played.byName);

  const keep = async (keeping) => {
    setBusy(true);
    try {
      if (keeping) await api.keep(m.id);
      else await api.unkeep(m.id);
      toast.success(keeping ? `${filmName(title)} is kept, so it stays.` : `${filmName(title)} is back on the clock.`);
      stay.reload();
    } catch (error) {
      toast.fromError(error, "That did not change anything");
    } finally {
      setBusy(false);
    }
  };

  const facts = [
    ["Released", d && d.releaseDate ? new Date(d.releaseDate).getFullYear() : film.year],
    ["Language", d && d.languages.length ? d.languages.join(", ") : null],
    ["Country", d && d.countries.length ? d.countries.join(", ") : null],
    ["On this server", [pictureClass(m.video), summary && summary.sizeBytes ? bytes(summary.sizeBytes) : null].filter(Boolean).join(" · ") || null, true],
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
                <a className="kc-btn kc-btn--primary kc-btn--lg" {...watchLink(m.id, room && room.sessionId, progress && progress.positionSeconds)}>
                  <Icon name={room ? "users" : "play"} size={18} />
                  {room ? `Join the room (${room.participants})` : progress ? "Resume" : "Watch"}
                  {!room && progress && <span className="kc-btn__sub">{timeLeft(m.durationSeconds - progress.positionSeconds)}</span>}
                </a>
              )}
              {held && keeping && (held.keeperId
                ? <button type="button" className="kc-btn kc-btn--lg" disabled={busy} onClick={() => keep(false)}><Icon name="pin-off" size={17} />Un-keep</button>
                : <button type="button" className="kc-btn kc-btn--lg" disabled={busy} onClick={() => keep(true)}><Icon name="pin" size={17} />Keep</button>)}
              {film.imdbId && <SaveButton imdbId={film.imdbId} kind="film" title={filmName(m)} />}
              {held && (
                <span className="kc-tt__stay">
                  <span className="kc-pill">{stayWords(held, me)}</span>
                  <span className="kc-tt__stay-note">
                    {held.keeperId
                      ? "Stays for everyone until someone un-keeps it"
                      : leaveRule(held)}
                  </span>
                </span>
              )}
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
