import React from "react";
import { Button, TvBackdrop, TvSpotlight, toast } from "@thekrystalship/krystal";
import { api, posterUrl } from "../../api.js";
import { useLoad } from "../../data.js";
import { discoverFilmPath, moviePath } from "../../paths.js";
import { navigate } from "../../router.js";
import { useCan } from "../../session.js";
import { clock } from "../../time.js";
import { formatTime } from "../../player/engine/format.ts";
import { Failed, watchHref, watchLink } from "../../components/Film.jsx";
import { SaveButton } from "../../components/SaveButton.jsx";
import { wishWords } from "../../pages/Discover.jsx";
import { defaultOffer, watchNow } from "../../watchNow.js";
import { SpotFacts } from "../spot.jsx";
import { TmdbCredit, TvCast, TvFacts } from "../TvCredits.jsx";

/**
 * A film on a television, the same whether the server holds it or not: its artwork across the page,
 * its name, facts and story, who made it, and what can be done — Resume, Restart and Save for a film
 * somebody is part way through, otherwise Watch and Save. Under the buttons, how far they got. Below,
 * the cast and the details.
 */
function FilmFrame({ details: d, fallback, length, actions, progress, facts }) {
  const name = (d && d.title) || fallback.title;
  const credit = d && [
    d.directors.length > 0 ? ["Directed by", d.directors.join(", ")] : null,
    d.cast.length > 0 ? ["Starring", d.cast.slice(0, 2).map((c) => c.name).join(", ")] : null,
  ].filter(Boolean);
  return (
    <div className="kc-tv-page kc-tvf">
      <TvBackdrop src={(d && (d.backdropUrl || d.posterUrl)) || fallback.art} size="page" />
      <section className="kc-tvf__head" data-tv-top="">
        <TvSpotlight size="lg" eyebrow="Movie" title={name}
          meta={<SpotFacts facts={{
            rating: d && d.rating, year: (d && d.year) || fallback.year, cert: d && d.certification,
            length, genres: d ? d.genres : [],
          }} />}>
          {d && d.overview && <p className="k-tv-spot__overview">{d.overview}</p>}
          {credit && credit.length > 0 && (
            <span className="kc-tv-credit">
              {credit.map(([label, names], i) => (
                <React.Fragment key={label}>{i > 0 && " · "}{label} <b>{names}</b></React.Fragment>
              ))}
            </span>
          )}
          <div className="k-tv-spot__actions">{actions}</div>
          {progress}
        </TvSpotlight>
      </section>
      {d && <TvCast cast={d.cast} />}
      {facts && <TvFacts facts={facts} />}
      {d && <TmdbCredit />}
    </div>
  );
}

const factsOf = (d, extra = []) => d && [
  ["Released", d.releaseDate ? new Date(d.releaseDate).getFullYear() : d.year],
  ["Language", d.languages.length ? d.languages.join(", ") : null],
  ["Country", d.countries.length ? d.countries.join(", ") : null],
  ["Rating", d.rating ? `${d.rating.toFixed(1)} from ${d.votes.toLocaleString("en-GB")}` : null],
  ["Written by", d.writers.join(", ")],
  ...extra,
];

/** A film the library holds, by its title id. */
export function TvTitle({ id }) {
  const manifest = useLoad(() => api.title(id), [id]);
  const listing = useLoad(() => api.titles().then((all) => all.find((t) => t.id === id) || null), [id]);
  const imdbId = manifest.data && manifest.data.film && manifest.data.film.imdbId;
  const details = useLoad(() => (imdbId ? api.film(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId]);
  const rooms = useLoad(() => api.rooms(), [id]);

  if (!manifest.data && !manifest.error) return <div className="kc-tv-page" />;
  if (!manifest.data) return <div className="kc-tv-page kc-tv-pad"><Failed error={manifest.error} onRetry={manifest.reload} /></div>;

  const m = manifest.data;
  const film = m.film || {};
  const d = details.data;
  const summary = listing.data;
  const room = (rooms.data || []).find((r) => r.titleId === m.id && r.participants > 0);
  const progress = summary && summary.progress && !summary.progress.finished && summary.progress.positionSeconds > 0
    ? summary.progress : null;
  const playable = m.status !== "failed";

  const actions = (
    <>
      {playable && room && (
        <Button variant="primary" size="lg" icon="users" data-tv-initial="" {...watchLink(m.id, room.sessionId)}>
          {`Join the room (${room.participants})`}
        </Button>
      )}
      {playable && !room && (
        <Button variant="primary" size="lg" icon="play" data-tv-initial=""
          {...watchLink(m.id, null, progress && progress.positionSeconds)}>
          {progress ? "Resume" : "Watch"}
        </Button>
      )}
      {playable && !room && progress && <Button size="lg" icon="rotate-ccw" {...watchLink(m.id)}>Restart</Button>}
      {film.imdbId && <SaveButton imdbId={film.imdbId} kind="film" title={film.name || m.title} />}
    </>
  );
  const shown = progress && (
    <div className="kc-tv-progress">
      <span className="kc-tv-progress__bar"><span style={{ width: Math.round((progress.positionSeconds / m.durationSeconds) * 100) + "%" }} /></span>
      <span>
        <span className="kc-mono kc-tv-strong">{formatTime(progress.positionSeconds)}</span> of{" "}
        <span className="kc-mono">{formatTime(m.durationSeconds)}</span> - (<span className="kc-mono kc-tv-strong">{clock(m.durationSeconds - progress.positionSeconds)}</span> remaining)
      </span>
    </div>
  );

  return (
    <FilmFrame details={d} fallback={{ title: film.name || m.title, year: film.year, art: posterUrl({ id: m.id, poster: m.poster, details: d }) }}
      length={clock(m.durationSeconds)} actions={actions} progress={shown}
      facts={factsOf(d, [["IMDb", film.imdbId, true]])} />
  );
}

/**
 * Watch for a film by its IMDb id from anywhere but its own page: its room when the library holds it,
 * otherwise it starts arriving and its room opens. A film that cannot be started that way opens its
 * page, which says what can be done with it. False when nothing was opened.
 */
export async function startFilm(imdbId) {
  try {
    const a = await api.availability(imdbId);
    if (a.libraryId) { navigate(watchHref(a.libraryId)); return true; }
    const offer = defaultOffer(a);
    if (!a.download && !offer) { navigate(discoverFilmPath(imdbId)); return true; }
    return await watchNow({ imdbId, torrentId: a.download ? undefined : offer.torrentId }, { limit: a.streams.limit });
  } catch (error) {
    toast.fromError(error, "That could not be started");
    return false;
  }
}

/**
 * A film the library does not hold, by its IMDb id: Watch starts it arriving and opens its room. One
 * not out digitally yet offers to be told when it is. A film the library turns out to hold goes to its
 * own page.
 */
export function TvCatalogueFilm({ imdbId }) {
  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const film = useLoad(() => api.film(imdbId), [imdbId]);
  const state = useLoad(() => (downloading || wishing ? api.availability(imdbId) : Promise.resolve(null)), [imdbId, downloading, wishing]);
  const [busy, setBusy] = React.useState(false);

  const here = state.data && state.data.libraryId;
  const shelved = film.data && film.data.category;
  React.useEffect(() => { if (here) navigate(moviePath(here, shelved), { replace: true }); }, [here, shelved]);
  if (here || (!film.data && !film.error)) return <div className="kc-tv-page" />;
  if (!film.data) return <div className="kc-tv-page kc-tv-pad"><Failed error={film.error} onRetry={film.reload} /></div>;

  const d = film.data;
  const a = state.data;
  const offer = defaultOffer(a);
  const arriving = a && a.download;
  const name = d.year ? `${d.title} (${d.year})` : d.title;

  const watch = async () => {
    setBusy(true);
    const started = await watchNow({ imdbId, torrentId: arriving ? undefined : offer && offer.torrentId }, { limit: a.streams.limit });
    if (!started) setBusy(false);
  };
  const wish = async (on) => {
    setBusy(true);
    try {
      if (on) toast.info(wishWords(await api.wish(imdbId), name));
      else { await api.cancelWish(imdbId); toast.info(`Stopped notifying you about ${name}.`); }
      state.reload();
    } catch (error) {
      toast.fromError(error, on ? "Could not wait on it" : "Could not stop waiting");
    } finally {
      setBusy(false);
    }
  };

  const actions = (
    <>
      {a && downloading && (arriving || offer) && (
        <Button variant="primary" size="lg" icon="play" busy={busy} data-tv-initial="" onClick={watch}>Watch</Button>
      )}
      {a && wishing && a.notOut && (a.waiting
        ? <Button variant="accent" size="lg" icon="bell-ring" disabled={busy} data-tv-initial="" onClick={() => wish(false)}>Notifying</Button>
        : <Button variant="primary" size="lg" icon="bell-plus" disabled={busy} data-tv-initial="" onClick={() => wish(true)}>Notify me</Button>)}
      <SaveButton imdbId={imdbId} kind="film" title={d.title} />
    </>
  );

  return (
    <FilmFrame details={d} fallback={{ title: d.title, year: d.year }}
      length={d.runtimeMinutes ? clock(d.runtimeMinutes * 60) : null} actions={actions}
      facts={factsOf(d, [["IMDb", imdbId, true]])} />
  );
}
