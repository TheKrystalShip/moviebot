import React from "react";
import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api, clock, filmName, posterUrl } from "../api.js";
import { useLoad, staysFor } from "../data.js";
import { navigate } from "../router.js";
import { Empty, Failed, PosterArt, watchLink } from "../components/Film.jsx";

/**
 * One film in the library: what it is, what it can be watched with, and how long it stays.
 *
 * Only what the API knows is shown. The catalogue gives the name, the year and the billing; the
 * manifest gives the length, the picture and the tracks; the retention rule gives how long it stays.
 */
export function Title({ id }) {
  const manifest = useLoad(() => api.title(id), [id]);
  const retention = useLoad(() => api.retention(id).catch(() => null), [id]);
  const rooms = useLoad(() => api.rooms(), [id]);
  const [busy, setBusy] = React.useState(false);

  if (manifest.error && manifest.error.status === 404) {
    return <div className="kc-page"><Empty title="Not in the library">This film has left the library, or never arrived.</Empty></div>;
  }
  if (!manifest.data) return <div className="kc-page"><Failed error={manifest.error} onRetry={manifest.reload} /></div>;

  const m = manifest.data;
  const title = { id: m.id, title: m.title, poster: m.poster, film: m.film };
  const film = m.film || {};
  const kept = retention.data;
  const room = (rooms.data || []).find((r) => r.titleId === m.id);
  const audio = m.audio.filter((a) => a.kind === "feature");
  const subtitles = m.subtitles.filter((s) => s.kind === "feature");
  const poster = posterUrl(title);

  const keep = async (keeping) => {
    setBusy(true);
    try {
      const reply = keeping ? await api.keep(kept.hash) : await api.release(kept.hash);
      if (reply.outcome === "unavailable" || reply.outcome === "notFound") toast.error(keepWords(reply, filmName(title)));
      else toast.success(keepWords(reply, filmName(title)));
      retention.reload();
    } catch (error) {
      toast.fromError(error, "That did not change anything");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="kc-page kc-title">
      <button type="button" className="kc-back" onClick={() => window.history.length > 1 ? window.history.back() : navigate("/library")}>
        <Icon name="arrow-left" size={14} />Back
      </button>

      <section className="kc-title__head">
        {poster && <div className="kc-title__backdrop" style={{ backgroundImage: `url("${poster}")` }} />}
        <PosterArt title={title} className="kc-title__poster" />
        <div className="kc-title__body">
          <span className="kc-eyebrow">Film</span>
          <h1>{film.name || m.title}</h1>
          <div className="kc-featured__meta">
            {film.year && <span>{film.year}</span>}
            <span>{clock(m.durationSeconds)}</span>
            {m.video && pictureClass(m.video) && <span className="kc-tag">{pictureClass(m.video)}</span>}
            {m.video && m.video.sourceHdr && m.video.sourceHdr !== "sdr" && <span className="kc-tag">{m.video.sourceHdr.toUpperCase()}</span>}
          </div>
          {m.status === "transcoding" && (
            <span className="kc-note">Still being prepared{m.headSeconds ? `: watchable up to ${clock(m.headSeconds)}` : ""}.</span>
          )}
          {m.status === "failed" && <span className="kc-note kc-note--danger">This film could not be prepared, so it cannot be played.</span>}
          {film.starring && <span className="kc-featured__starring">Starring <b>{film.starring}</b></span>}

          <div className="kc-actions">
            {m.status !== "failed" && (
              <a className="kc-btn kc-btn--primary" {...watchLink(m.id, room && room.sessionId)}>
                <Icon name={room ? "users" : "play"} size={18} />{room ? `Join the room (${room.participants})` : "Watch"}
              </a>
            )}
            {kept && (kept.keeper
              ? <button type="button" className="kc-btn" disabled={busy} onClick={() => keep(false)}><Icon name="pin-off" size={17} />Let it go</button>
              : <button type="button" className="kc-btn" disabled={busy} onClick={() => keep(true)}><Icon name="pin" size={17} />Keep</button>)}
            {kept && <span className="kc-pill">{staysFor(kept)}</span>}
            {film.imdbId && (
              <a className="kc-btn kc-btn--ghost" href={`https://www.imdb.com/title/${film.imdbId}/`} target="_blank" rel="noreferrer">
                <Icon name="external-link" size={16} />IMDb
              </a>
            )}
          </div>
        </div>
      </section>

      <section className="kc-facts">
        <Fact label="Audio">{audio.length ? audio.map((a) => a.label).join(" · ") : "None"}</Fact>
        <Fact label="Subtitles">
          {subtitles.length
            ? subtitles.map((s) => s.label + (s.available ? "" : " (picture track, not shown)")).join(" · ")
            : "None yet. Ask the assistant to fetch some."}
        </Fact>
        {m.chapters && m.chapters.length > 0 && <Fact label="Chapters">{m.chapters.length}</Fact>}
        <Fact label="On disk">
          {kept
            ? kept.keeper ? "Kept, so it stays." : kept.isFinished ? `Leaves after ${staysFor(kept).replace(" left", "")} more of seeding, unless somebody keeps it.` : "Still downloading."
            : "Added by hand, so it is on no clock."}
        </Fact>
      </section>
    </div>
  );
}

/**
 * The picture's class, read from its width: a scope film is cropped top and bottom, so its height
 * says 804 where the release, and anybody choosing it, says 1080p.
 */
function pictureClass(video) {
  if (!video.width) return null;
  if (video.width >= 3200) return "4K";
  if (video.width >= 1800) return "1080p";
  if (video.width >= 1200) return "720p";
  return "SD";
}

function Fact({ label, children }) {
  return (
    <div className="kc-fact">
      <span className="kc-fact__label">{label}</span>
      <span className="kc-fact__value">{children}</span>
    </div>
  );
}

export function keepWords(reply, name) {
  switch (reply && reply.outcome) {
    case "kept": return `${name} is kept, so it stays.`;
    case "alreadyKept": return `${name} was already kept.`;
    case "released": return `${name} is back on the clock.`;
    case "notKept": return `${name} was not kept.`;
    case "unavailable": return "The torrent client is not answering, so nothing changed.";
    default: return `Nothing on disk answers to ${name} any more.`;
  }
}
