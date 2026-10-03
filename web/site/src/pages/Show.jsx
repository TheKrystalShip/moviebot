import React from "react";
import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api, episodeCode } from "../api.js";
import { SOON_DAYS, leaveRule, stayWords, useLoad } from "../data.js";
import { goBack } from "../router.js";
import { useSession } from "../session.js";
import { ago, airDay, clock, since, timeLeft } from "../time.js";
import { Empty, Failed, watchLink } from "../components/Film.jsx";
import { CastGrid, Credits, FactGrid, FilmScore, Genres, bytes } from "../components/FilmDetails.jsx";
import { FollowButton } from "../components/FollowButton.jsx";
import { RequestSheet, plan } from "../components/RequestSheet.jsx";
import { watchNow } from "../watchNow.js";
import { downloadWords } from "./Search.jsx";

/**
 * One show: what it is, its seasons with their episodes, where this person picks up, how long each
 * season stays, and getting more of it. Works for a show the library does not hold yet, which is
 * where its episodes are picked to download.
 *
 * What the show is comes from the film database through the API; which episodes are on this server,
 * how far this person got in each and how long each season stays comes from the library.
 */
export function Show({ imdbId, query }) {
  const show = useLoad(() => api.show(imdbId), [imdbId]);
  const library = useLoad(() => api.shows().then((all) => all.find((s) => s.imdbId === imdbId) || null).catch(() => null), [imdbId]);
  const rooms = useLoad(() => api.rooms(), [imdbId]);
  // What the tracker offers, so every episode it holds can be played; kept a few minutes by the API.
  const offers = useLoad(() => api.showReleases(imdbId).catch(() => null), [imdbId]);
  const [season, setSeason] = React.useState(Number(query.get("season")) || null);
  const [requesting, setRequesting] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [starting, setStarting] = React.useState(false);
  const session = useSession();
  const me = session && session.user;

  if (show.error && show.error.status === 404) {
    return <div className="kc-page"><Empty icon="tv" title="No such show">The film database has no show under this id.</Empty></div>;
  }
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
  const episodesHere = seasons.reduce((n, x) => n + x.episodes.filter((e) => e.titleId).length, 0);
  const missing = seasons.some((x) => x.episodes.some((e) => !e.titleId));
  const first = seasons.flatMap((x) => x.episodes).find((e) => e.titleId);
  const play = resume || (first && { titleId: first.titleId, season: first.season, episode: first.number, positionSeconds: 0 });
  const room = play && (rooms.data || []).find((r) => r.titleId === play.titleId && r.participants > 0);
  // With nothing of the show here, Watch starts its first episode while it arrives.
  const opening = seasons.filter((x) => x.number >= 1 && x.episodes.length > 0).map((x) => x.episodes[0])[0] || null;

  const watchFirst = async () => {
    setStarting(true);
    try {
      const offers = await api.showReleases(s.imdbId);
      const picked = plan(offers, { [opening.season]: new Set([opening.number]) }, "720p").torrents[0];
      if (!picked) {
        toast.info(`The tracker has nothing of ${s.title} Season ${opening.season} to watch yet.`);
        return;
      }
      await watchNow({ imdbId: s.imdbId, torrentId: picked.offer.torrentId, episodes: [{ season: opening.season, episode: opening.number }] });
    } catch (error) {
      toast.fromError(error, "That could not be started");
    } finally {
      setStarting(false);
    }
  };
  const years = s.firstYear ? `${s.firstYear}–${s.inProduction ? "" : s.lastYear || ""}` : null;
  const episodeCount = seasons.reduce((n, x) => n + x.episodes.length, 0);

  const keep = async (stay, keeping) => {
    setBusy(true);
    try {
      if (keeping) await api.keep(stay.titleId);
      else await api.unkeep(stay.titleId);
      toast.success(keeping ? `${s.title} Season ${stay.season} is kept, so it stays.` : `${s.title} Season ${stay.season} is back on the clock.`);
      show.reload();
    } catch (error) {
      toast.fromError(error, "That did not change anything");
    } finally {
      setBusy(false);
    }
  };

  const facts = [
    ["Aired", years],
    ["Status", s.status],
    ["Network", s.networks.join(", ") || null],
    ["Language", s.languages.length ? s.languages.join(", ") : null],
    ["Country", s.countries.length ? s.countries.join(", ") : null],
    ["On this server", episodesHere ? [`${episodesHere} of ${episodeCount} episodes`, library.data && library.data.sizeBytes ? bytes(library.data.sizeBytes) : null].filter(Boolean).join(" · ") : null, true],
    ["Added", library.data && library.data.addedAt ? since(library.data.addedAt) : null],
    ["IMDb", s.imdbId, true],
  ];

  return (
    <div className="kc-page kc-title">
      <button type="button" className="kc-back" onClick={() => goBack("/tv")}>
        <Icon name="arrow-left" size={14} />Back
      </button>

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
            <Credits film={{ creators: s.creators, cast: s.cast }} />

            <div className="kc-actions">
              {play && (
                <a className="kc-btn kc-btn--primary kc-btn--lg" {...watchLink(play.titleId, room && room.sessionId, play.positionSeconds)}>
                  <Icon name={room ? "users" : "play"} size={18} />
                  {room ? `Join the room (${room.participants})` : resume ? "Resume" : "Watch"}
                </a>
              )}
              {!play && opening && (
                <button type="button" className="kc-btn kc-btn--primary kc-btn--lg" disabled={starting} onClick={watchFirst}>
                  <Icon name="play" size={18} />Watch
                </button>
              )}
              {missing && (
                <button type="button" className="kc-btn kc-btn--lg" onClick={() => setRequesting(true)}>
                  <Icon name="download" size={17} />{play ? "Get episodes" : "Download"}
                </button>
              )}
              {(s.followable || s.follow) && <FollowButton imdbId={s.imdbId} title={s.title} follow={s.follow} onChange={show.reload} />}
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
            {seasons.map((x) => {
              const stay = x.stay && x.stay.settled ? x.stay : null;
              const leaving = stay && !stay.keeperId && stay.daysLeft != null && stay.daysLeft <= SOON_DAYS;
              return (
                <button key={x.number} type="button"
                  className={"kc-season-chip" + (x.number === current.number ? " is-on" : "")}
                  onClick={() => setSeason(x.number)}>
                  Season {x.number}
                  {stay && stay.keeperId && <span className="kc-season-chip__flag is-kept"><Icon name="pin" size={12} />Kept</span>}
                  {leaving && <span className={"kc-season-chip__flag" + (stay.daysLeft <= 1 ? " is-danger" : "")}><Icon name="hourglass" size={12} />{stayWords(stay, me)}</span>}
                </button>
              );
            })}
          </div>

          <SeasonPanel season={current} me={me} busy={busy} onKeep={keep} rooms={rooms.data || []}
            offers={offers.data} showImdbId={s.imdbId} showTitle={s.title} onFetched={show.reload} />
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

      {requesting && (
        <RequestSheet show={s} onClose={() => setRequesting(false)} onStarted={() => { setRequesting(false); show.reload(); }} />
      )}
    </div>
  );
}

/** One season: its stay and its keep, and its episodes with how far this person got in each. */
function SeasonPanel({ season, me, busy, onKeep, rooms, offers, showImdbId, showTitle, onFetched }) {
  const [fetching, setFetching] = React.useState(false);
  const stay = season.stay && season.stay.settled ? season.stay : null;
  const fetchable = fetchableIn(season, offers);

  // The whole of the season not here, at 720p, first episode first: the season's pack where there
  // is one, the first picked at the torrent client's highest priority and the second at its next.
  const fetchSeason = async () => {
    setFetching(true);
    try {
      const torrents = plan(offers, { [season.number]: new Set(fetchable) }, "720p").torrents;
      let started = 0;
      for (const t of torrents) {
        const reply = await api.download(t.offer.torrentId, t.episodes);
        if (reply.outcome === "started") started++;
        else toast.error(downloadWords(reply));
      }
      if (started > 0) {
        toast.success(`Downloading ${showTitle} Season ${season.number}, episode ${fetchable[0]} first. Each can be watched as soon as enough of it has arrived.`);
        onFetched();
      }
    } catch (error) {
      toast.fromError(error, "The download did not start");
    } finally {
      setFetching(false);
    }
  };
  const played = season.episodes.map((e) => e.lastPlayed).filter(Boolean).sort((a, b) => b.at.localeCompare(a.at))[0];
  const playedBy = played && (me && played.byId === me.id ? "you" : played.byName);
  const aired = season.airDate ? new Date(season.airDate).getFullYear() : null;
  const leaving = stay && !stay.keeperId && stay.daysLeft != null && stay.daysLeft <= SOON_DAYS;

  return (
    <div className="kc-season">
      <div className="kc-season__head">
        <div className="kc-season__text">
          <span className="kc-season__title">Season {season.number} · {season.episodes.length} episodes{aired ? ` · ${aired}` : ""}</span>
          <span className="kc-season__note">
            {stay && stay.keeperId ? "Stays for everyone until someone un-keeps it"
              : played ? `Last played ${ago(played.at)} by ${playedBy}`
              : stay ? leaveRule(stay) : "Not played yet"}
          </span>
        </div>
        {stay && <span className={"kc-pill" + (leaving ? " kc-pill--warn" : "")}>{stayWords(stay, me)}</span>}
        {stay && (stay.keeperId
          ? <button type="button" className="kc-btn" disabled={busy} onClick={() => onKeep(stay, false)}><Icon name="pin-off" size={15} />Un-keep season</button>
          : <button type="button" className="kc-btn" disabled={busy} onClick={() => onKeep(stay, true)}><Icon name="pin" size={15} />Keep season</button>)}
        {fetchable.length > 0 && (
          <button type="button" className="kc-btn" disabled={fetching} onClick={fetchSeason}>
            <Icon name="download" size={15} />{fetchable.length === season.episodes.length ? "Download season" : `Download the other ${fetchable.length}`}
          </button>
        )}
      </div>

      {season.episodes.map((e) => <EpisodeRow key={e.number} episode={e} rooms={rooms} offers={offers} showImdbId={showImdbId} />)}
    </div>
  );
}

/** The release an episode is fetched from at 720p, the show's default, or null when nothing offered holds it. */
function offerFor(offers, season, number) {
  return (offers && plan(offers, { [season]: new Set([number]) }, "720p").torrents[0]) || null;
}

/** Every episode of a season not here that the tracker offers, by number. */
function fetchableIn(season, offers) {
  return season.episodes.filter((e) => !e.titleId && offerFor(offers, season.number, e.number)).map((e) => e.number);
}

function EpisodeRow({ episode: e, rooms, offers, showImdbId }) {
  const [starting, setStarting] = React.useState(false);
  const done = e.progress && e.progress.finished;
  const fraction = done ? 1
    : e.progress && e.durationSeconds ? Math.min(1, e.progress.positionSeconds / e.durationSeconds) : 0;
  const room = e.titleId && rooms.find((r) => r.titleId === e.titleId && r.participants > 0);
  const here = !!e.titleId && e.status !== "failed";
  // An episode not here that the tracker offers plays while it arrives, as any other does.
  const offer = !e.titleId ? offerFor(offers, e.season, e.number) : null;
  const watch = async () => {
    if (starting) return;
    setStarting(true);
    const started = await watchNow({ imdbId: showImdbId, torrentId: offer.offer.torrentId, episodes: [{ season: e.season, episode: e.number }] });
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
        <span className="kc-episode__sub">
          {here ? (e.status === "transcoding" ? "Still being prepared" : e.airDate ? `Aired ${e.airDate}` : "On this server")
            : e.titleId ? "Could not be prepared"
            : offer ? (starting ? "Starting…" : "Plays while it downloads")
            : offers ? "Not on the tracker" : "Not on this server"}
        </span>
      </span>
      <span className="kc-episode__progress"><span style={{ width: `${Math.round(fraction * 100)}%` }} className={done ? "is-done" : ""} /></span>
      <span className="kc-episode__score">{e.rating ? <><Icon name="star" size={11} />{e.rating.toFixed(1)}</> : null}</span>
      <span className="kc-episode__runtime">{runtime}</span>
      <span className="kc-episode__go"><Icon name={here || offer ? (room ? "users" : done ? "check" : "play") : "cloud-off"} size={13} /></span>
    </>
  );

  if (here) return <a className="kc-episode" {...watchLink(e.titleId, room && room.sessionId, !done && e.progress ? e.progress.positionSeconds : 0)}>{inner}</a>;
  if (offer) return <button type="button" className="kc-episode kc-episode--fetch" disabled={starting} onClick={watch}>{inner}</button>;
  return <div className="kc-episode is-absent">{inner}</div>;
}
