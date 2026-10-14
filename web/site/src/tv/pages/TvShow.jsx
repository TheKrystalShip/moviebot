import React from "react";
import { Button, TvCard, TvSpotlight } from "@thekrystalship/krystal";
import { api, episodeCode } from "../../api.js";
import { useLoad } from "../../data.js";
import { showPath } from "../../paths.js";
import { navigate } from "../../router.js";
import { useCan } from "../../session.js";
import { airDay, clock, timeLeft } from "../../time.js";
import { Failed, watchLink } from "../../components/Film.jsx";
import { FollowButton } from "../../components/FollowButton.jsx";
import { SaveButton } from "../../components/SaveButton.jsx";
import { episodeRelease, watchNow, watchOpening, openingEpisode } from "../../watchNow.js";
import { SpotFacts, SpotProgress, SpotStage, useSpotlight } from "../spot.jsx";
import { TmdbCredit, TvCast, TvFacts } from "../TvCredits.jsx";

/**
 * A show on a television: its artwork, name, facts and story, and Resume (or Watch), Follow and Save;
 * then its seasons and the open season's episodes as stills. With an episode focused the top of the
 * page is about that episode — its place, name, length, air date, story and how far somebody got —
 * and OK plays it, arriving while it plays when the server does not hold it yet. Below, the cast and
 * the details. The address says which season is open, as it does on every surface.
 */
export function TvShow({ imdbId, season, inLibrary }) {
  const show = useLoad(() => api.show(imdbId), [imdbId]);
  const rooms = useLoad(() => api.rooms(), [imdbId]);
  const downloading = useCan("cinema:download");
  const offers = useLoad(() => (downloading ? api.showReleases(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId, downloading]);
  const { store, pageRef, regionRef, focusOn } = useSpotlight();
  const [starting, setStarting] = React.useState(false);

  const seasonsHere = show.data ? show.data.seasons : null;
  const episodesHere = seasonsHere ? seasonsHere.some((x) => x.episodes.some((e) => e.titleId)) : null;
  const hasSeason = seasonsHere ? seasonsHere.some((x) => x.number === season) : null;
  const shelved = show.data ? show.data.category : null;
  React.useEffect(() => {
    if (episodesHere === null) return;
    navigate(showPath(imdbId, { held: episodesHere, season: hasSeason ? season : undefined, category: shelved }), { replace: true });
  }, [imdbId, season, inLibrary, episodesHere, hasSeason, shelved]);

  if (!show.data && !show.error) return <div className="kc-tv-page" />;
  if (!show.data) return <div className="kc-tv-page kc-tv-pad"><Failed error={show.error} onRetry={show.reload} /></div>;

  const s = show.data;
  const seasons = s.seasons.filter((x) => x.number >= 1 || s.seasons.length === 1);
  const resume = s.resume;
  const held = (n) => seasons.find((x) => x.number === n)?.episodes.some((e) => e.titleId);
  const current = seasons.find((x) => x.number === season)
    || seasons.find((x) => resume && x.number === resume.season)
    || seasons.find((x) => held(x.number))
    || seasons[0];
  const first = seasons.flatMap((x) => x.episodes).find((e) => e.titleId);
  const play = resume || (first && { titleId: first.titleId, season: first.season, episode: first.number, positionSeconds: 0 });
  const room = play && (rooms.data || []).find((r) => r.titleId === play.titleId && r.participants > 0);
  const opening = openingEpisode(s);
  const years = s.firstYear ? `${s.firstYear}–${s.inProduction ? "" : s.lastYear || ""}` : null;
  const resumeEpisode = resume && seasons.find((x) => x.number === resume.season)?.episodes.find((e) => e.number === resume.episode);

  const watchFirst = async () => {
    setStarting(true);
    await watchOpening(s);
    setStarting(false);
  };

  const episodeSpot = (e) => {
    const done = e.progress && e.progress.finished;
    const length = e.runtimeMinutes ? `${e.runtimeMinutes}m` : e.durationSeconds ? clock(e.durationSeconds) : null;
    const fraction = done ? 1 : e.progress && e.durationSeconds ? Math.min(1, e.progress.positionSeconds / e.durationSeconds) : 0;
    return {
      key: `${e.season}:${e.number}`,
      eyebrow: `${s.title} · Season ${e.season} · Episode ${e.number}`,
      title: e.name || `Episode ${e.number}`,
      facts: { length, year: e.airDate ? airDay(e.airDate) : null, cert: s.certification },
      overview: e.overview,
      progress: fraction > 0 && !done ? { fraction, left: timeLeft(e.durationSeconds - e.progress.positionSeconds) } : null,
    };
  };

  const facts = [
    ["Aired", years],
    ["Status", s.status],
    ["Network", s.networks.join(", ") || null],
    ["Language", s.languages.length ? s.languages.join(", ") : null],
    ["Country", s.countries.length ? s.countries.join(", ") : null],
    ["Rating", s.rating ? `${s.rating.toFixed(1)} from ${s.votes.toLocaleString("en-GB")}` : null],
    ["Created by", s.creators.join(", ")],
    ["IMDb", s.imdbId, true],
  ];

  return (
    <div className="kc-tv-page kc-tvs" ref={pageRef}>
      <SpotStage store={store} art={s.backdropUrl || s.posterUrl} idleSize="page" />

      <section className="kc-tvs__head" data-tv-top="">
        <TvSpotlight size="lg" eyebrow="TV series" title={s.title}
          meta={<SpotFacts facts={{
            rating: s.rating, year: years, cert: s.certification,
            length: `${seasons.length} ${seasons.length === 1 ? "season" : "seasons"}`, genres: s.genres,
          }} />}>
          {s.overview && <p className="k-tv-spot__overview">{s.overview}</p>}
          {s.creators.length > 0 && <span className="kc-tv-credit">Created by <b>{s.creators.join(", ")}</b></span>}
          <div className="k-tv-spot__actions">
            {play && (
              <Button variant="primary" size="lg" icon={room ? "users" : "play"} data-tv-initial=""
                {...watchLink(play.titleId, room && room.sessionId, play.positionSeconds)}>
                {room ? `Join the room (${room.participants})` : resume ? `Resume ${episodeCode({ season: resume.season, number: resume.episode })}` : "Watch"}
              </Button>
            )}
            {!play && opening && (
              <Button variant="primary" size="lg" icon="play" busy={starting} data-tv-initial="" onClick={watchFirst}>Watch</Button>
            )}
            {(s.followable || s.follow) && <FollowButton imdbId={s.imdbId} title={s.title} follow={s.follow} onChange={show.reload} />}
            {s.imdbId && <SaveButton imdbId={s.imdbId} kind="show" title={s.title} />}
          </div>
          {resume && resume.positionSeconds > 0 && (
            <SpotProgress fraction={resume.positionSeconds / resume.durationSeconds}
              left={`${resumeEpisode && resumeEpisode.name ? resumeEpisode.name + " - " : ""}(${clock(resume.durationSeconds - resume.positionSeconds)} remaining)`} />
          )}
        </TvSpotlight>
      </section>

      {current && (
        <section className="kc-tvs__seasons" data-tv-row="" ref={regionRef}>
          <div className="kc-tvs__pills">
            {seasons.map((x) => (
              <button key={x.number} type="button" className={"k-tv-pill kc-tvs__pill" + (x.number === current.number ? " is-on" : "")}
                aria-current={x.number === current.number ? "true" : undefined}
                onClick={() => navigate(showPath(imdbId, { held: inLibrary, season: x.number, category: shelved }), { replace: true })}
                {...focusOn(0, { key: "season:" + x.number, eyebrow: s.title, title: x.name || `Season ${x.number}`,
                  facts: { length: `${x.episodes.length} episodes`, year: x.airDate ? new Date(x.airDate).getFullYear() : null }, overview: x.overview })}>
                Season {x.number}
              </button>
            ))}
          </div>
          <div className="k-tv-row__track kc-tvs__episodes">
            {current.episodes.map((e) => (
              <Episode key={e.number} episode={e} rooms={rooms.data || []} offers={offers.data} showImdbId={s.imdbId}
                focus={focusOn(1, episodeSpot(e))} />
            ))}
          </div>
        </section>
      )}

      <TvCast cast={s.cast} />
      <TvFacts facts={facts} />
      <TmdbCredit />
    </div>
  );
}

/**
 * One episode: its still, with how far somebody got, and its number, name and length under it. OK
 * plays it, in the room already watching it if there is one, or while it arrives when the server does
 * not hold it and the tracker offers it. One that can be neither is drawn and cannot be focused.
 */
function Episode({ episode: e, rooms, offers, showImdbId, focus }) {
  const [starting, setStarting] = React.useState(false);
  const done = e.progress && e.progress.finished;
  const fraction = done ? 1 : e.progress && e.durationSeconds ? Math.min(1, e.progress.positionSeconds / e.durationSeconds) : 0;
  const room = e.titleId && rooms.find((r) => r.titleId === e.titleId && r.participants > 0);
  const here = !!e.titleId && e.status !== "failed";
  const offer = !e.titleId ? episodeRelease(offers, e.season, e.number) : null;
  const length = e.runtimeMinutes ? `${e.runtimeMinutes}m` : e.durationSeconds ? clock(e.durationSeconds) : null;
  const name = e.name || `Episode ${e.number}`;

  const watch = async (event) => {
    event.preventDefault();
    if (starting) return;
    setStarting(true);
    const started = await watchNow({ imdbId: showImdbId, torrentId: offer.torrentId, episodes: [{ season: e.season, episode: e.number }] });
    if (!started) setStarting(false);
  };
  const link = here
    ? watchLink(e.titleId, room && room.sessionId, !done && e.progress ? e.progress.positionSeconds : 0)
    : offer ? { href: "#", onClick: watch } : {};

  return (
    <div className={"kc-tvs__episode" + (here || offer ? "" : " is-absent")}>
      <TvCard shape="wide" art={e.stillUrl} label={`${e.number}. ${name}`} progress={fraction > 0 ? fraction : null}
        {...link} {...(here || offer ? focus : {})} />
      <span className="kc-tvs__name">{e.number} · {name}</span>
      {length && <span className="kc-tvs__length kc-mono">{length}</span>}
    </div>
  );
}
