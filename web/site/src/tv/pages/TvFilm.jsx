import React from "react";
import { Button, TvBackdrop, toast } from "@thekrystalship/krystal";
import { catalogue, certificateOf, libraryApi, posterUrl, roomsApi, runtimeText } from "../../api/index.js";
import { useLoad } from "../../data.js";
import { discoverFilmPath, moviePath } from "../../paths.js";
import { navigate } from "../../router.js";
import { useCan } from "../../session.js";
import { clock } from "../../time.js";
import { formatTime } from "../../player/engine/format.ts";
import { Failed, watchHref, watchLink } from "../../components/Film.jsx";
import { WantButton } from "../../components/WantButton.jsx";
import { defaultOffer, watchNow } from "../../watchNow.js";
import { SpotFacts } from "../spot.jsx";
import { TmdbCredit, TvCast, TvFacts } from "../TvCredits.jsx";
import { TvAbout, TvGroup, TvHonours, TvTitleHero } from "../TvHero.jsx";
import { TrailerButton } from "../../components/TitleHero.jsx";
import { TvTitleShelves } from "./TvDiscoverShelves.jsx";
import { dollars } from "../../components/FilmDetails.jsx";

/**
 * A film on a television, the same whether the server holds it or not: its artwork across the page, the
 * site's hero at a television's size (its name and facts, what it won and is based on, its story and
 * credits, and what can be done: Resume, Restart, Trailer and Want for a film somebody is part way
 * through, otherwise Watch, Trailer and Want, with how far they got under the buttons), then the cast,
 * the shelves of what is like it and of its director's and stars' work, and the details.
 */
function FilmFrame({ page: d, fallback, length, actions, progress, facts }) {
  const name = (d && d.title) || fallback.title;
  return (
    <div className="kc-tv-page kc-tvf">
      <TvBackdrop src={(d && (d.backdrop || d.poster)) || fallback.art} size="page" />
      <TvTitleHero eyebrow="Movie" title={name} trailer={d && d.trailers[0]}
        facts={<SpotFacts facts={{
          rating: d && d.rating && d.rating.value, ratedBy: d && d.rating && d.rating.by, year: (d && d.year) || fallback.year,
          cert: certificateOf(d), length, genres: d ? d.genres : [],
        }} />}>
        {d && <TvHonours facts={d.facts} />}
        <TvAbout overview={d && (d.overview || d.plot)} credits={d} />
        <TvGroup>
          <div className="k-tv-spot__actions">{actions}</div>
          {progress}
        </TvGroup>
      </TvTitleHero>
      {d && <TvCast cast={d.cast} />}
      {d && <TvTitleShelves page={d} />}
      {facts && <TvFacts facts={facts} />}
      {d && <TmdbCredit />}
    </div>
  );
}

/** A film's details on a television, from its catalogue page: the names as text, since a remote reaches the buttons, not these. */
const factsOf = (d, extra = []) => d && [
  ["Released", d.released ? new Date(d.released).getFullYear() : d.year],
  ["Language", d.languages.length ? d.languages.join(", ") : null],
  ["Country", d.countries.length ? d.countries.join(", ") : null],
  ["Rating", d.rating && d.rating.value ? `${d.rating.value.toFixed(1)} from ${d.rating.votes.toLocaleString("en-GB")}` : null],
  ["Studio", d.companies.slice(0, 3).map((c) => c.name).join(", ") || null],
  ["Franchise", d.franchises.map((f) => f.name).join(", ") || null],
  ["Budget", dollars(d.budget)],
  ["Box office", dollars(d.revenue)],
  ...extra,
];

/** A film the library holds, by its title id. */
export function TvTitle({ id }) {
  const manifest = useLoad(() => libraryApi.title(id), [id]);
  const listing = useLoad(() => libraryApi.titles().then((all) => all.find((t) => t.id === id) || null), [id]);
  const imdbId = manifest.data && manifest.data.film && manifest.data.film.imdbId;
  const details = useLoad(() => (imdbId ? catalogue.title(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId]);
  const rooms = useLoad(() => roomsApi.list(), [id]);

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
      <TrailerButton size="lg" />
      {film.imdbId && <WantButton imdbId={film.imdbId} kind="film" title={film.name || m.title} />}
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
    <FilmFrame page={d} fallback={{ title: film.name || m.title, year: film.year, art: posterUrl({ id: m.id, poster: m.poster, card: d }) }}
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
    const a = await libraryApi.availability(imdbId);
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
  const film = useLoad(() => catalogue.title(imdbId), [imdbId]);
  const state = useLoad(() => (downloading || wishing ? libraryApi.availability(imdbId) : Promise.resolve(null)), [imdbId, downloading, wishing]);
  const [busy, setBusy] = React.useState(false);

  const here = (state.data && state.data.libraryId) || (film.data && film.data.you && film.data.you.libraryId);
  const shelved = film.data && film.data.category;
  React.useEffect(() => { if (here) navigate(moviePath(here, shelved), { replace: true }); }, [here, shelved]);
  if (here || (!film.data && !film.error)) return <div className="kc-tv-page" />;
  if (!film.data) return <div className="kc-tv-page kc-tv-pad"><Failed error={film.error} onRetry={film.reload} /></div>;

  const d = film.data;
  const a = state.data;
  const offer = defaultOffer(a);
  const arriving = a && a.download;

  const watch = async () => {
    setBusy(true);
    const started = await watchNow({ imdbId, torrentId: arriving ? undefined : offer && offer.torrentId }, { limit: a.streams.limit });
    if (!started) setBusy(false);
  };
  const actions = (
    <>
      {a && downloading && (arriving || offer) && (
        <Button variant="primary" size="lg" icon="play" busy={busy} data-tv-initial="" onClick={watch}>Watch</Button>
      )}
      <TrailerButton size="lg" />
      <WantButton imdbId={imdbId} kind="film" title={d.title} />
    </>
  );

  return (
    <FilmFrame page={d} fallback={{ title: d.title, year: d.year }}
      length={runtimeText(d.runtime)} actions={actions}
      facts={factsOf(d, [["IMDb", imdbId, true]])} />
  );
}
