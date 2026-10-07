import React from "react";
import { Button, Icon } from "@thekrystalship/krystal-ui";
import { api, episodeCode } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { showPath } from "../paths.js";
import { navigate } from "../router.js";
import { useCan, useSession } from "../session.js";
import { ago, airDay, clock, since, timeLeft } from "../time.js";
import { Empty, Failed, WatchButton, useWatchLink } from "../components/Film.jsx";
import { CastGrid, Credits, FactGrid, FilmScore, Genres, TitleSkeleton } from "../components/FilmDetails.jsx";
import { FollowButton } from "../components/FollowButton.jsx";
import { SaveButton } from "../components/SaveButton.jsx";
import { DownloadMark, downloadLine, useDownloadMarks, useOffersSaving } from "../components/DownloadButton.jsx";
import { episodeRelease, openingEpisode, watchNow, watchOpening } from "../watchNow.js";
import { useKeptShow } from "../offline/hooks.js";
import { refreshShow } from "../offline/shows.js";
import { Unreachable } from "./Offline.jsx";

/**
 * One show: what it is, its seasons with their episodes, where this person picks up, and watching any
 * episode, whether it plays now or once it is prepared. Works the same for a show the library holds
 * nothing of.
 *
 * What the show is comes from the film database through the API; which episodes play now and how far
 * this person got in each comes from the library.
 *
 * The address says which season is open (`season`) and where the library stands (`inLibrary`: the
 * shelf's address or Discover's). Both are the page's to correct once it knows the show: a show with
 * an episode here belongs at the shelf's address, one without at Discover's, and a season the show
 * does not have is dropped, each by replacing the address.
 *
 * `fromDevice` is the page opened with no connection, drawn from the show kept on the device for its
 * downloaded episodes (`offline/shows.js`); the episodes not downloaded are drawn and cannot be played.
 * Read from the API while the device keeps the show, the page writes it there again.
 */
export function Show({ imdbId, season, inLibrary, fromDevice = false }) {
  const fetched = useLoad(() => (fromDevice ? Promise.resolve(null) : api.show(imdbId)), [imdbId]);
  const kept = useKeptShow(imdbId, fromDevice);
  const show = fromDevice ? kept : fetched;
  const library = useLoad(() => (fromDevice ? Promise.resolve(null)
    : api.shows().then((all) => all.find((s) => s.imdbId === imdbId) || null).catch(() => null)), [imdbId]);
  const rooms = useLoad(() => (fromDevice ? Promise.resolve([]) : api.rooms()), [imdbId]);
  const downloading = useCan("cinema:download") && !fromDevice;
  // What the tracker offers, so every episode it holds can be played; kept a few minutes by the API,
  // and asked by somebody who may fetch an episode.
  const offers = useLoad(() => (downloading ? api.showReleases(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId, downloading]);
  const [starting, setStarting] = React.useState(false);
  const session = useSession();
  const me = session && session.user;
  const online = useOnline();
  const saving = useOffersSaving();
  const sub = me && me.id;
  React.useEffect(() => {
    if (!fromDevice && fetched.data) void refreshShow(sub, fetched.data);
  }, [fromDevice, sub, fetched.data]);

  useCrumb(show.data ? show.data.title : show.error && show.error.status === 404 ? "No such show" : null);

  const seasonsHere = show.data ? show.data.seasons : null;
  const episodesHere = seasonsHere ? seasonsHere.some((x) => x.episodes.some((e) => e.titleId)) : null;
  const hasSeason = seasonsHere ? seasonsHere.some((x) => x.number === season) : null;
  React.useEffect(() => {
    if (episodesHere === null) return;
    navigate(showPath(imdbId, { held: episodesHere, season: hasSeason ? season : undefined }), { replace: true });
  }, [imdbId, season, inLibrary, episodesHere, hasSeason]);
  if (episodesHere !== null && (episodesHere !== inLibrary || (season != null && !hasSeason))) return <div className="kc-page" />;

  if (fromDevice && !show.data) return show.loading ? <div className="kc-page" /> : <Unreachable place={inLibrary ? null : "discover"} />;
  if (show.error && show.error.status === 404) {
    return <div className="kc-page"><Empty icon="tv" title="No such show">The film database has no show under this id.</Empty></div>;
  }
  if (!show.data && !show.error) return <TitleSkeleton kind="TV series" />;
  if (!show.data) return <div className="kc-page"><Failed error={show.error} onRetry={show.reload} /></div>;

  const s = show.data;
  const seasons = s.seasons;
  const held = (n) => seasons.find((x) => x.number === n)?.episodes.some((e) => e.titleId);
  // The season opened first: the one this person picks up in, then one the library holds, then the first.
  const resume = s.resume;
  const current = seasons.find((x) => x.number === season)
    || seasons.find((x) => resume && x.number === resume.season)
    || seasons.find((x) => held(x.number))
    || seasons[0];
  const first = seasons.flatMap((x) => x.episodes).find((e) => e.titleId);
  const play = resume || (first && { titleId: first.titleId, season: first.season, episode: first.number, positionSeconds: 0 });
  const room = play && (rooms.data || []).find((r) => r.titleId === play.titleId && r.participants > 0);
  // With nothing of the show here, Watch starts its first episode while it arrives.
  const opening = openingEpisode(s);

  const watchFirst = async () => {
    setStarting(true);
    await watchOpening(s);
    setStarting(false);
  };
  const years = s.firstYear ? `${s.firstYear}–${s.inProduction ? "" : s.lastYear || ""}` : null;
  const episodeCount = seasons.reduce((n, x) => n + x.episodes.length, 0);

  const facts = [
    ["Aired", years],
    ["Status", s.status],
    ["Network", s.networks.join(", ") || null],
    ["Language", s.languages.length ? s.languages.join(", ") : null],
    ["Country", s.countries.length ? s.countries.join(", ") : null],
    ["Added", library.data && library.data.addedAt ? since(library.data.addedAt) : null],
    ["IMDb", s.imdbId, true],
  ];

  return (
    <div className="kc-page kc-title">
      <section className="kc-tt">
        {(s.backdropUrl || s.posterUrl) && <div className="kc-tt__art" style={{ backgroundImage: `url("${s.backdropUrl || s.posterUrl}")` }} />}
        <div className="kc-tt__shade" />
        <div className="kc-tt__grid">
          <div className="kc-poster-art kc-tt__poster">
            {s.posterUrl ? <img src={s.posterUrl} alt="" /> : <span className="kc-poster-art__name">{s.title}</span>}
          </div>
          <div className="kc-tt__body">
            <span className="kc-eyebrow kc-eyebrow--muted">TV series</span>
            <h1>{s.title}</h1>
            <div className="kc-tt__meta">
              {years && <span>{years}</span>}
              {s.certification && <span className="kc-cert">{s.certification}</span>}
              <span>{seasons.length} {seasons.length === 1 ? "season" : "seasons"} · {episodeCount} episodes</span>
              {s.status && <span>{s.status}</span>}
            </div>
            {(s.rating || s.genres.length > 0) && (
              <div className="kc-tt__score-row">
                {s.rating && <FilmScore rating={s.rating} votes={s.votes} />}
                <Genres names={s.genres} />
              </div>
            )}
            {s.overview && <p className="kc-tt__overview">{s.overview}</p>}
            <Credits film={{ creators: s.creators, personIds: s.personIds, cast: s.cast }} />

            <div className="kc-actions">
              {play && (
                <WatchButton variant="primary" size="xl" icon={room ? "users" : "play"}
                  titleId={play.titleId} sessionId={room && room.sessionId} at={play.positionSeconds}>
                  {room ? `Join the room (${room.participants})` : resume ? "Resume" : "Watch"}
                </WatchButton>
              )}
              {!play && opening && (
                <Button variant="primary" size="xl" icon="play" disabled={starting || !online} onClick={watchFirst}>Watch</Button>
              )}
              {(s.followable || s.follow) && <FollowButton imdbId={s.imdbId} title={s.title} follow={s.follow} onChange={show.reload} />}
              {s.imdbId && <SaveButton imdbId={s.imdbId} kind="show" title={s.title} />}
              {resume && (
                <span className="kc-mono kc-quiet">
                  {episodeCode({ season: resume.season, number: resume.episode })}
                  {resume.positionSeconds > 0 ? ` · ${timeLeft(resume.durationSeconds - resume.positionSeconds)}` : ""}
                </span>
              )}
            </div>
            {s.nextEpisode && (
              <span className="kc-mono kc-quiet">
                Next episode: {episodeCode({ season: s.nextEpisode.season, number: s.nextEpisode.episode })} · {airDay(s.nextEpisode.airDate)}
              </span>
            )}
          </div>
        </div>
      </section>

      {seasons.length > 0 && (
        <section className="kc-section">
          <div className="kc-seasons">
            {seasons.map((x) => (
              <button key={x.number} type="button"
                className={"kc-season-chip" + (x.number === current.number ? " is-on" : "")}
                onClick={() => navigate(showPath(imdbId, { held: inLibrary, season: x.number }), { replace: true })}>
                Season {x.number}
              </button>
            ))}
          </div>

          <SeasonPanel season={current} me={me} rooms={rooms.data || []}
            offers={offers.data} showImdbId={s.imdbId} showName={s.title} backdrop={s.backdropUrl || s.posterUrl} saving={saving} />
        </section>
      )}

      {s.cast.length > 0 && (
        <section className="kc-section">
          <h2>Cast</h2>
          <CastGrid cast={s.cast} />
        </section>
      )}

      <section className="kc-section">
        <h2>Details</h2>
        <FactGrid facts={facts} />
      </section>

      <p className="kc-attribution-note">Show details and artwork from TMDB.</p>
    </div>
  );
}

/**
 * One season: when it was last played, and its episodes with how far this person got in each. With
 * Download offered (`saving`), every row has its place for it at its end, so the rows line up, and a
 * row whose episode is downloaded or downloading says so under its name.
 */
function SeasonPanel({ season, me, rooms, offers, showImdbId, showName, backdrop, saving }) {
  const played = season.episodes.map((e) => e.lastPlayed).filter(Boolean).sort((a, b) => b.at.localeCompare(a.at))[0];
  const playedBy = played && (me && played.byId === me.id ? "you" : played.byName);
  const aired = season.airDate ? new Date(season.airDate).getFullYear() : null;
  const marks = useDownloadMarks();

  return (
    <div className="kc-season">
      <div className="kc-season__head">
        <div className="kc-season__text">
          <span className="kc-season__title">Season {season.number} · {season.episodes.length} episodes{aired ? ` · ${aired}` : ""}</span>
          <span className="kc-season__note">
            {played ? `Last played ${ago(played.at)} by ${playedBy}` : "Not played yet"}
          </span>
        </div>
      </div>

      {season.episodes.map((e) => {
        const row = <EpisodeRow key={e.number} episode={e} rooms={rooms} offers={offers} showImdbId={showImdbId}
          download={saving && e.titleId ? marks.film(e.titleId) : null} />;
        if (!saving) return row;
        return (
          <div key={e.number} className="kc-episode-line">
            {row}
            {e.titleId && e.status === "ready"
              ? <DownloadMark titleId={e.titleId} head={{
                  name: e.name || `Episode ${e.number}`, context: `${showName} · ${episodeCode(e)}`,
                  runtime: e.runtimeMinutes ? `${e.runtimeMinutes} min` : e.durationSeconds ? clock(e.durationSeconds) : null,
                  still: e.stillUrl, backdrop,
                }} />
              : <span className="kc-episode-save" />}
          </div>
        );
      })}
    </div>
  );
}

function EpisodeRow({ episode: e, rooms, offers, showImdbId, download }) {
  const [starting, setStarting] = React.useState(false);
  const online = useOnline();
  const done = e.progress && e.progress.finished;
  const fraction = done ? 1
    : e.progress && e.durationSeconds ? Math.min(1, e.progress.positionSeconds / e.durationSeconds) : 0;
  const room = e.titleId && rooms.find((r) => r.titleId === e.titleId && r.participants > 0);
  const here = !!e.titleId && e.status !== "failed";
  const play = useWatchLink(e.titleId, room && room.sessionId, !done && e.progress ? e.progress.positionSeconds : 0);
  // An episode that has to be fetched plays while it is prepared, from the same row as any other.
  const offer = !e.titleId ? episodeRelease(offers, e.season, e.number) : null;
  const watch = async () => {
    if (starting) return;
    setStarting(true);
    const started = await watchNow({ imdbId: showImdbId, torrentId: offer.torrentId, episodes: [{ season: e.season, episode: e.number }] });
    if (!started) setStarting(false);
  };
  const runtime = e.runtimeMinutes ? `${e.runtimeMinutes}m` : e.durationSeconds ? clock(e.durationSeconds) : null;
  const inner = (
    <>
      <span className="kc-episode__n">{e.number}</span>
      <span className="kc-episode__still">
        {e.stillUrl && <img src={e.stillUrl} alt="" loading="lazy" />}
        {fraction > 0 && <span className="kc-bar"><span style={{ width: `${Math.round(fraction * 100)}%` }} className={done ? "is-done" : ""} /></span>}
      </span>
      <span className="kc-episode__text">
        <span className={"kc-episode__title" + (done ? " is-done" : "")}>{e.name || `Episode ${e.number}`}</span>
        {download
          ? <span className={"kc-episode__sub kc-episode__sub--download" + (download.kind === "done" ? " is-done" : "")}>{downloadLine(download)}</span>
          : (
            <span className="kc-episode__sub">
              {e.titleId && e.status === "failed" ? "Could not be prepared"
                : here && e.status === "transcoding" ? "Still being prepared"
                : starting ? "Starting…"
                : here || offer ? (e.airDate ? `Aired ${e.airDate}` : null)
                : "Not available yet"}
            </span>
          )}
      </span>
      <span className="kc-episode__progress"><span style={{ width: `${Math.round(fraction * 100)}%` }} className={done ? "is-done" : ""} /></span>
      <span className="kc-episode__score">{e.rating ? <><Icon name="star" size={11} />{e.rating.toFixed(1)}</> : null}</span>
      <span className="kc-episode__runtime">{runtime}</span>
      <span className="kc-episode__go"><Icon name={here || offer ? (room ? "users" : done ? "check" : "play") : "cloud-off"} size={13} /></span>
    </>
  );

  // With no connection a row plays from the device when the episode is downloaded here, and is dimmed
  // and inert when not.
  if (here) return play.disabled ? <div className={"kc-episode is-absent" + (online ? "" : " is-off")}>{inner}</div> : <a className="kc-episode" {...play}>{inner}</a>;
  if (offer) return <button type="button" className={"kc-episode kc-episode--fetch" + (online ? "" : " is-off")} disabled={starting || !online} onClick={watch}>{inner}</button>;
  return <div className={"kc-episode is-absent" + (online ? "" : " is-off")}>{inner}</div>;
}
