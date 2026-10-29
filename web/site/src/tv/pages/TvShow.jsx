import React from "react";
import { Button, TvCard, TvRow } from "@thekrystalship/krystal";
import { certificateOf, episodeCode, libraryApi, roomsApi } from "../../api/index.js";
import { showAnswers } from "../../offline/shows.js";
import { useLoad } from "../../data.js";
import { showPath } from "../../paths.js";
import { navigate } from "../../router.js";
import { useCan } from "../../session.js";
import { airDay, clock, timeLeft } from "../../time.js";
import { Failed, watchLink } from "../../components/Film.jsx";
import { FollowButton } from "../../components/FollowButton.jsx";
import { WantButton } from "../../components/WantButton.jsx";
import { VoteButtons } from "../../components/VoteButtons.jsx";
import { episodeRelease, watchNow, watchOpening, openingEpisode } from "../../watchNow.js";
import { SpotFacts, SpotStage, useSpotlight } from "../spot.jsx";
import { TmdbCredit, TvCast, TvFacts } from "../TvCredits.jsx";
import { TvAbout, TvGroup, TvHonours, TvNextEpisode, TvTitleHero } from "../TvHero.jsx";
import { TrailerButton } from "../../components/TitleHero.jsx";
import { TvTitleShelves } from "./TvDiscoverShelves.jsx";

/**
 * A show on a television: its artwork, name, facts and story, and Resume (or Watch), Follow and Want;
 * then its seasons and the open season's episodes as stills. With an episode focused the top of the
 * page is about that episode — its place, name, length, air date, story and how far somebody got —
 * and OK plays it, arriving while it plays when the server does not hold it yet. Below, the cast and
 * the details. The address says which season is open, as it does on every surface.
 */
export function TvShow({ imdbId, season, inLibrary }) {
  const show = useLoad(() => showAnswers(imdbId), [imdbId]);
  const rooms = useLoad(() => roomsApi.list(), [imdbId]);
  const downloading = useCan("cinema:download");
  const offers = useLoad(() => (downloading ? libraryApi.showReleases(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId, downloading]);
  const { store, pageRef, regionRef, focusOn } = useSpotlight();
  const [starting, setStarting] = React.useState(false);

  const seasonsHere = show.data ? show.data.mine.seasons : null;
  const episodesHere = seasonsHere ? seasonsHere.some((x) => x.episodes.some((e) => e.titleId)) : null;
  const hasSeason = seasonsHere ? seasonsHere.some((x) => x.number === season) : null;
  const shelved = show.data ? show.data.page.category : null;
  React.useEffect(() => {
    if (episodesHere === null) return;
    navigate(showPath(imdbId, { held: episodesHere, season: hasSeason ? season : undefined, category: shelved }), { replace: true });
  }, [imdbId, season, inLibrary, episodesHere, hasSeason, shelved]);

  if (!show.data && !show.error) return <div className="kc-tv-page" />;
  if (!show.data) return <div className="kc-tv-page kc-tv-pad"><Failed error={show.error} onRetry={show.reload} /></div>;

  // The show as one: its catalogue page, with what this person has of it over it.
  const s = { ...show.data.page, ...show.data.mine };
  const certificate = certificateOf(s);
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
  const years = !s.year ? null : s.endYear === s.year ? String(s.year) : `${s.year}–${s.endYear || ""}`;
  const nextEpisode = play && seasons.find((x) => x.number === play.season)?.episodes.find((e) => e.number === play.episode);

  const watchFirst = async () => {
    setStarting(true);
    await watchOpening(s);
    setStarting(false);
  };

  const episodeSpot = (e) => {
    const done = e.progress && e.progress.finished;
    const length = e.runtime ? `${e.runtime}m` : e.durationSeconds ? clock(e.durationSeconds) : null;
    const fraction = done ? 1 : e.progress && e.durationSeconds ? Math.min(1, e.progress.positionSeconds / e.durationSeconds) : 0;
    return {
      key: `${e.season}:${e.number}`,
      eyebrow: `${s.title} · Season ${e.season} · Episode ${e.number}`,
      title: e.name || `Episode ${e.number}`,
      facts: { length, year: e.airDate ? airDay(e.airDate) : null, cert: certificate },
      overview: e.overview,
      progress: fraction > 0 && !done ? { fraction, left: timeLeft(e.durationSeconds - e.progress.positionSeconds) } : null,
    };
  };

  const facts = [
    ["Aired", years],
    ["Status", s.status],
    ["Network", s.networks.map((n) => n.name).join(", ") || null],
    ["Language", s.languages.length ? s.languages.join(", ") : null],
    ["Country", s.countries.length ? s.countries.join(", ") : null],
    ["Rating", s.rating && s.rating.value ? `${s.rating.value.toFixed(1)} from ${s.rating.votes.toLocaleString("en-GB")}` : null],
    ["Created by", [...new Set(s.crew.filter((c) => c.job === "Creator").map((c) => c.name))].join(", ") || null],
    ["IMDb", s.imdbId, true],
  ];

  return (
    <div className="kc-tv-page kc-tvs" ref={pageRef}>
      <SpotStage store={store} art={s.backdrop || s.poster} idleSize="page" />

      <TvTitleHero kind="tvs" eyebrow="TV series" title={s.title} trailer={s.trailers[0]}
        facts={<SpotFacts facts={{
          rating: s.rating && s.rating.value, ratedBy: s.rating && s.rating.by, year: years, cert: certificate,
          length: `${seasons.length} ${seasons.length === 1 ? "season" : "seasons"}`, genres: s.genres,
        }} />}>
        {play && nextEpisode && (
          <TvNextEpisode episode={nextEpisode} at={resume && resume.positionSeconds > 0 ? resume.positionSeconds : 0}
            label={`${resume && resume.positionSeconds > 0 ? "Continue" : "Up next"} · ${episodeCode({ season: play.season, number: play.episode })}`}
            meta={resume && resume.positionSeconds > 0 ? `${clock(resume.durationSeconds - resume.positionSeconds)} remaining` : null} />
        )}
        <TvHonours facts={s.facts} />
        <TvAbout overview={s.overview || s.plot} credits={s} />
        <TvGroup>
          <div className="k-tv-spot__actions">
            {play && (
              <Button variant="primary" size="lg" icon={room ? "users" : "play"} data-tv-initial=""
                {...watchLink(play.titleId, room && room.sessionId, play.positionSeconds)}>
                {room ? `Join the room (${room.participants})`
                  : `${resume && resume.positionSeconds > 0 ? "Resume" : "Watch"} ${episodeCode({ season: play.season, number: play.episode })}`}
              </Button>
            )}
            {!play && opening && (
              <Button variant="primary" size="lg" icon="play" busy={starting} data-tv-initial="" onClick={watchFirst}>Watch</Button>
            )}
            <TrailerButton size="lg" />
            {(s.followable || s.follow) && <FollowButton imdbId={s.imdbId} title={s.title} follow={s.follow} onChange={show.reload} />}
            {s.imdbId && <WantButton imdbId={s.imdbId} kind="show" title={s.title} />}
            {s.imdbId && <VoteButtons kind="title" id={s.imdbId} name={s.title} className="kc-tv-vote" />}
          </div>
        </TvGroup>
      </TvTitleHero>

      {current && (
        <div ref={regionRef}>
          <TvRow className="kc-tvs__seasons" trackClassName="kc-tvs__episodes" lead={(
            <div className="kc-tvs__pills">
              {seasons.map((x) => (
                <Button variant="bare" key={x.number} className={"kc-tvs__pill" + (x.number === current.number ? " is-on" : "")}
                  aria-current={x.number === current.number ? "true" : undefined}
                  onClick={() => navigate(showPath(imdbId, { held: inLibrary, season: x.number, category: shelved }), { replace: true })}
                  {...focusOn(0, { key: "season:" + x.number, eyebrow: s.title, title: x.name || `Season ${x.number}`,
                    facts: { length: `${x.episodes.length} episodes`, year: x.airDate ? new Date(x.airDate).getFullYear() : null }, overview: x.overview })}>
                  Season {x.number}
                </Button>
              ))}
            </div>
          )}>
            {current.episodes.map((e) => (
              <Episode key={e.number} episode={e} rooms={rooms.data || []} offers={offers.data} showImdbId={s.imdbId}
                focus={focusOn(1, episodeSpot(e))} />
            ))}
          </TvRow>
        </div>
      )}

      <TvCast cast={s.cast} />
      <TvTitleShelves page={s} />
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
  const length = e.runtime ? `${e.runtime}m` : e.durationSeconds ? clock(e.durationSeconds) : null;
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
      <TvCard shape="wide" art={e.still} label={`${e.number}. ${name}`} progress={fraction > 0 ? fraction : null}
        {...link} {...(here || offer ? focus : {})} />
      <span className="kc-tvs__name">{e.number} · {name}</span>
      {length && <span className="kc-tvs__length kc-mono">{length}</span>}
    </div>
  );
}
