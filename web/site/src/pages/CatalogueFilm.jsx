import React from "react";
import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { goBack, navigate } from "../router.js";
import { Empty, Failed } from "../components/Film.jsx";
import { CastGrid, Credits, FilmScore, Genres } from "../components/FilmDetails.jsx";
import { FilmRequestSheet } from "../components/RequestSheet.jsx";
import { SaveButton } from "../components/SaveButton.jsx";
import { wishWords } from "./Discover.jsx";
import { defaultOffer, watchNow } from "../watchNow.js";

/**
 * A film by its IMDb id, here or not: what it is, and the way to it. A film in the library goes to its
 * own page. One on its way can be watched as it arrives; one the tracker offers can be watched now or
 * downloaded at 720p or 1080p; one that is not out digitally yet can be waited on.
 */
export function CatalogueFilm({ imdbId }) {
  const film = useLoad(() => api.film(imdbId), [imdbId]);
  const state = useLoad(() => api.availability(imdbId), [imdbId]);
  const [requesting, setRequesting] = React.useState(false);
  const [busy, setBusy] = React.useState(false);

  const here = state.data && state.data.libraryId;
  React.useEffect(() => {
    if (here) navigate("/title/" + encodeURIComponent(here), { replace: true });
  }, [here]);
  if (here) return <div className="kc-page" />;

  if (film.error && film.error.status === 404) {
    return <div className="kc-page"><Empty title="No such film">The film database has nothing under {imdbId}.</Empty></div>;
  }
  if (!film.data) return <div className="kc-page"><Failed error={film.error} onRetry={film.reload} /></div>;

  const d = film.data;
  const a = state.data;
  const offer = defaultOffer(a);
  const full = a && a.streams.used >= a.streams.limit;
  const arriving = a && a.download;
  const name = d.year ? `${d.title} (${d.year})` : d.title;

  const watch = async () => {
    if (full && !arriving) {
      toast.info(`The server is streaming ${a.streams.limit} downloads right now. Download it instead and you'll get a push when it's ready.`);
      if (offer) setRequesting(true);
      return;
    }
    setBusy(true);
    const started = await watchNow({ imdbId, torrentId: arriving ? undefined : offer && offer.torrentId }, { limit: a.streams.limit });
    if (!started) setBusy(false);
  };

  const wait = async () => {
    setBusy(true);
    try {
      const reply = await api.wish(imdbId);
      toast.info(wishWords(reply, name));
      state.reload();
    } catch (error) {
      toast.fromError(error, "Could not wait on it");
    } finally {
      setBusy(false);
    }
  };
  const stopWaiting = async () => {
    setBusy(true);
    try {
      await api.cancelWish(imdbId);
      toast.info(`Stopped notifying you about ${name}.`);
      state.reload();
    } catch (error) {
      toast.fromError(error, "Could not stop waiting");
    } finally {
      setBusy(false);
    }
  };

  const others = a ? a.waiters - (a.waiting ? 1 : 0) : 0;
  const waitingWords = a && (a.waiting
    ? (others ? `You + ${others} ${others === 1 ? "other" : "others"} waiting` : "You're waiting")
    : (others ? `${others} waiting` : "Nobody's waiting yet"));

  return (
    <div className="kc-page kc-title">
      <button type="button" className="kc-back" onClick={() => goBack("/discover")}>
        <Icon name="arrow-left" size={14} />Back
      </button>

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

            <div className="kc-actions">
              {!a && state.loading && <span className="kc-quiet">Asking the tracker…</span>}
              {a && a.trackerUnanswered && <span className="kc-note">The tracker is not answering, so whether this can be fetched is not known. Try again in a minute.</span>}
              {a && a.notOffered && (
                <span className="kc-note">The tracker has it, but nothing this server takes. {a.emptyExplanation}</span>
              )}

              {a && (arriving || offer) && (
                <button type="button" className={"kc-btn kc-btn--primary kc-btn--lg" + (full && !arriving ? " is-dim" : "")} disabled={busy} onClick={watch}>
                  <Icon name="play" size={18} />Watch
                </button>
              )}
              {a && offer && !arriving && (
                <button type="button" className="kc-btn kc-btn--lg" onClick={() => setRequesting(true)}>
                  <Icon name="download" size={18} />Download
                </button>
              )}
              {arriving && <span className="kc-pill kc-pill--info">downloading · {Math.floor(arriving.progress * 100)}%</span>}

              {a && a.notOut && (a.waiting
                ? <button type="button" className="kc-btn kc-btn--accent kc-btn--lg" disabled={busy} onClick={stopWaiting}><Icon name="bell-ring" size={18} />Notifying</button>
                : <button type="button" className="kc-btn kc-btn--primary kc-btn--lg" disabled={busy} onClick={wait}><Icon name="bell-plus" size={18} />Notify</button>)}
              <SaveButton imdbId={imdbId} kind="film" title={d.title} />
              {a && a.notOut && <span className="kc-note">{waitingWords}</span>}
            </div>
            {a && offer && !arriving && (
              <span className="kc-quiet">
                Watch streams while it downloads · {Math.max(0, a.streams.limit - a.streams.used)} of {a.streams.limit} live slots free
              </span>
            )}
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

      {requesting && a && (
        <FilmRequestSheet film={d} availability={a} onClose={() => setRequesting(false)}
          onStarted={() => { setRequesting(false); state.reload(); }} />
      )}
    </div>
  );
}
