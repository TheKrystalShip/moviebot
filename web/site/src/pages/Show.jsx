import React from "react";
import { Button, Icon, IconButton, Menu, MenuItem, usePhone } from "@thekrystalship/krystal";
import { certificateOf, episodeCode, libraryApi, roomsApi } from "../api/index.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { ANIME, showPath } from "../paths.js";
import { navigate } from "../router.js";
import { useCan, useSession } from "../session.js";
import { ago, airDay, clock, since, timeLeft } from "../time.js";
import { Empty, Failed, WatchButton, useWatchLink } from "../components/Film.jsx";
import { TitleSkeleton } from "../components/FilmDetails.jsx";
import { TitleSections, titleFacts } from "../components/TitleSections.jsx";
import { HeroGroup, kindWord, TitleAbout, TitleFacts, TitleHero, TitleHonours, TrailerButton } from "../components/TitleHero.jsx";
import { FollowButton } from "../components/FollowButton.jsx";
import { WantButton } from "../components/WantButton.jsx";
import { PlanButton } from "../components/PlanButton.jsx";
import { Wanters } from "../components/Wanters.jsx";
import { DownloadMark, SeasonDownload, useDownloadMark, useDownloadMarks, useOffersSaving } from "../components/DownloadButton.jsx";
import { EpisodeRow } from "../components/EpisodeRow.jsx";
import { useWatchedChange } from "../components/WatchedButton.jsx";
import { episodeRelease, openingEpisode, openingRequest, watchNow, watchOpening } from "../watchNow.js";
import { WatchOnTvActions, useDriving } from "../components/WatchOnTv.jsx";
import { watchOnTv } from "../remoteWatch.js";
import { setRemoteMode } from "../remote.js";
import { useKeptShow } from "../offline/hooks.js";
import { refreshShow, showAnswers } from "../offline/shows.js";
import { Unreachable } from "./Offline.jsx";

/**
 * One show: what it is, its seasons with their episodes, where this person picks up, and watching any
 * episode, whether it plays now or once it is prepared. Works the same for a show the library holds
 * nothing of.
 *
 * What the show is comes from its catalogue page; its episodes as the releases number them, which
 * play now and how far this person got in each, from `GET /api/me/shows/{imdbId}`. The page reads both
 * together (`showAnswers`).
 *
 * The address says which season is open (`season`), where the library stands (`inLibrary`: a shelf's
 * address or Discover's) and which shelf (`category`: Anime's, or TV Shows'). All three are the page's
 * to correct once it knows the show: a show with an episode here belongs at the address of the shelf
 * the API puts it on, one without at Discover's, and a season the show does not have is dropped, each
 * by replacing the address.
 *
 * `fromDevice` is the page opened with no connection, drawn from the show kept on the device for its
 * downloaded episodes (`offline/shows.js`); the episodes not downloaded are drawn and cannot be played.
 * Read from the API while the device keeps the show, the page writes it there again.
 */
export function Show({ imdbId, season, inLibrary, category = null, fromDevice = false }) {
  const fetched = useLoad(() => (fromDevice ? Promise.resolve(null) : showAnswers(imdbId)), [imdbId]);
  const kept = useKeptShow(imdbId, fromDevice);
  const show = fromDevice ? kept : fetched;
  const library = useLoad(() => (fromDevice ? Promise.resolve(null)
    : libraryApi.shows().then((all) => all.find((s) => s.imdbId === imdbId) || null).catch(() => null)), [imdbId]);
  const rooms = useLoad(() => (fromDevice ? Promise.resolve([]) : roomsApi.list()), [imdbId]);
  const downloading = useCan("cinema:download") && !fromDevice;
  // What the tracker offers, so every episode it holds can be played; kept a few minutes by the API,
  // and asked by somebody who may fetch an episode.
  const offers = useLoad(() => (downloading ? libraryApi.showReleases(imdbId).catch(() => null) : Promise.resolve(null)), [imdbId, downloading]);
  const [starting, setStarting] = React.useState(false);
  const session = useSession();
  const me = session && session.user;
  const online = useOnline();
  const saving = useOffersSaving();
  const driving = useDriving();
  const sub = me && me.id;
  React.useEffect(() => {
    if (!fromDevice && fetched.data) void refreshShow(sub, fetched.data);
  }, [fromDevice, sub, fetched.data]);

  useCrumb(show.data ? show.data.page.title : show.error && show.error.status === 404 ? "No such show" : null);

  const seasonsHere = show.data ? show.data.mine.seasons : null;
  const episodesHere = seasonsHere ? seasonsHere.some((x) => x.episodes.some((e) => e.titleId)) : null;
  const hasSeason = seasonsHere ? seasonsHere.some((x) => x.number === season) : null;
  const shelved = show.data ? (show.data.page.category === ANIME ? ANIME : null) : category;
  React.useEffect(() => {
    if (episodesHere === null) return;
    navigate(showPath(imdbId, { held: episodesHere, season: hasSeason ? season : undefined, category: shelved }), { replace: true });
  }, [imdbId, season, inLibrary, episodesHere, hasSeason, shelved]);
  if (episodesHere !== null
    && (episodesHere !== inLibrary || (season != null && !hasSeason) || (episodesHere && shelved !== category))) return <div className="kc-page" />;

  if (fromDevice && !show.data) return show.loading ? <div className="kc-page" /> : <Unreachable place={inLibrary ? null : "discover"} />;
  if (show.error && show.error.status === 404) {
    return <div className="kc-page"><Empty icon="tv" title="No such show">The catalogue has no show under this id.</Empty></div>;
  }
  if (!show.data && !show.error) return <TitleSkeleton kind="TV series" />;
  if (!show.data) return <div className="kc-page"><Failed error={show.error} onRetry={show.reload} /></div>;

  // The show as one: its catalogue page, with what this person has of it over it.
  const s = { ...show.data.page, ...show.data.mine };
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
  const watchFirstOnTv = async () => {
    setStarting(true);
    const request = await openingRequest(s);
    if (request) await watchOnTv({ request, name: `${s.title} · ${episodeCode({ season: opening.season, number: opening.number })}`, art: s.backdrop || s.poster });
    setStarting(false);
  };
  const years = !s.year ? null
    : s.endYear === s.year ? String(s.year)
      : `${s.year}–${s.endYear || ""}`;
  const episodeCount = seasons.reduce((n, x) => n + x.episodes.length, 0);
  const certificate = certificateOf(s);

  const facts = titleFacts(s, [
    ["Aired", years],
    ["Status", s.status],
    ["Episodes", episodeCount || null],
    ["Added", library.data && library.data.addedAt ? since(library.data.addedAt) : null],
  ]);

  return (
    <div className="kc-page kc-title">
      <TitleHero art={s.backdrop || s.poster} eyebrow={kindWord("show", shelved === ANIME)} title={s.title}
        trailer={s.trailers[0]}
        facts={<TitleFacts rating={s.rating && s.rating.value} votes={s.rating && s.rating.votes} ratedBy={s.rating && s.rating.by}
          kind="show" category={shelved}
          parts={[years, certificate && { cert: certificate }, `${seasons.length} ${seasons.length === 1 ? "season" : "seasons"}`]}
          genres={s.genres} />}>
        {play && <NextEpisode play={play} resume={resume} seasons={seasons} room={room} />}
        <TitleHonours facts={s.facts} />
        <TitleAbout overview={s.overview || s.plot} credits={s} />
        <HeroGroup>
          <div className="kc-actions">
            {play && driving && (
              <WatchOnTvActions titleId={play.titleId} sessionId={room && room.sessionId} at={play.positionSeconds}
                target={{ name: `${s.title} · ${episodeCode({ season: play.season, number: play.episode })}`, art: s.backdrop || s.poster }} />
            )}
            {play && !driving && (
              <WatchButton variant="primary" size="xl" icon={room ? "users" : "play"}
                titleId={play.titleId} sessionId={room && room.sessionId} at={play.positionSeconds}>
                {room ? `Join the room (${room.participants})`
                  : `${resume && resume.positionSeconds > 0 ? "Resume" : "Watch"} ${episodeCode({ season: play.season, number: play.episode })}`}
              </WatchButton>
            )}
            {!play && opening && driving && (
              <>
                <Button variant="primary" size="xl" icon="tv" disabled={starting || !online} onClick={watchFirstOnTv}>Watch on TV</Button>
                <Button variant="secondary" size="xl" icon="smartphone" disabled={starting || !online}
                  onClick={() => { setRemoteMode(false); void watchFirst(); }}>Watch</Button>
              </>
            )}
            {!play && opening && !driving && (
              <Button variant="primary" size="xl" icon="play" disabled={starting || !online} onClick={watchFirst}>Watch</Button>
            )}
            <TrailerButton />
            {(s.followable || s.follow) && <FollowButton imdbId={s.imdbId} title={s.title} follow={s.follow} onChange={show.reload} />}
            {s.imdbId && <WantButton imdbId={s.imdbId} kind="show" title={s.title} />}
            {s.imdbId && <PlanButton imdbId={s.imdbId} kind="show" />}
          </div>
          {s.imdbId && <Wanters imdbId={s.imdbId} />}
          {s.nextEpisode && (
            <span className="kc-mono kc-quiet">
              Next episode: {episodeCode({ season: s.nextEpisode.season, number: s.nextEpisode.episode })} · {airDay(s.nextEpisode.airDate)}
            </span>
          )}
        </HeroGroup>
      </TitleHero>

      {seasons.length > 0 && (
        <section className="kc-section">
          <div className="kc-seasons">
            {seasons.map((x) => (
              <Button variant="bare" key={x.number}
                className={"kc-season-chip" + (x.number === current.number ? " is-on" : "")}
                onClick={() => navigate(showPath(imdbId, { held: inLibrary, season: x.number, category: shelved }), { replace: true })}>
                Season {x.number}
              </Button>
            ))}
          </div>

          <SeasonPanel season={current} me={me} rooms={rooms.data || []}
            offers={offers.data} showImdbId={s.imdbId} showName={s.title} backdrop={s.backdrop || s.poster} saving={saving}
            fetching={saving && downloading} onWatched={show.reload} />
        </section>
      )}

      <TitleSections page={s} facts={facts} shelves={!fromDevice} />
    </div>
  );
}

/**
 * The episode to watch next, leading a show's hero: its still with how far into it this person is, the
 * episode by its number, its name, its story and its length, and the whole of it a way into it. Where
 * this person is part way through an episode it is Continue, else Up next.
 */
function NextEpisode({ play, resume, seasons, room }) {
  const e = seasons.find((x) => x.number === play.season)?.episodes.find((x) => x.number === play.episode);
  const at = resume && resume.positionSeconds > 0 ? resume.positionSeconds : 0;
  const link = useWatchLink(play.titleId, room && room.sessionId, at);
  if (!e) return null;
  const length = e.runtime ? e.runtime * 60 : e.durationSeconds || (resume && resume.durationSeconds) || 0;
  const meta = [length ? clock(length) : null, at && length ? timeLeft(length - at) : null].filter(Boolean).join(" · ");
  const body = (
    <>
      <span className="kc-next__still" style={e.still ? { backgroundImage: `url("${e.still}")` } : undefined}>
        {at > 0 && length > 0 && <span className="kc-next__progress" style={{ width: `${Math.min(100, (at / length) * 100)}%` }} />}
      </span>
      <span className="kc-next__text">
        <span className="kc-next__label">{at > 0 ? "Continue" : "Up next"} · {episodeCode({ season: e.season, number: e.number })}</span>
        <span className="kc-next__name">{e.name || `Episode ${e.number}`}</span>
        {e.overview && <span className="kc-next__story">{e.overview}</span>}
        {meta && <span className="kc-next__meta">{meta}</span>}
      </span>
    </>
  );
  return link.disabled
    ? <div className="kc-next">{body}</div>
    : <a className="kc-next kc-next--link" {...link}>{body}</a>;
}

/**
 * One season: when it was last played, and its episodes with how far this person got in each. With
 * Download offered (`saving`), every row has its place for it at its end, so the rows line up, and a
 * row whose episode is downloaded, downloading or being prepared says so under its name. An episode
 * whole in the library can be downloaded by anybody who may download; one still being made or only
 * on the tracker, and the season together, by somebody who may also fetch onto the server (`fetching`).
 * Every aired episode's row ends in whether this person watched it, here or anywhere, and the season's
 * head marks the whole season so; `onWatched` follows a change, to read the show again. On a phone the
 * row keeps its own play mark and ends in one menu holding Watched and Download, and the season's two
 * buttons share one row.
 */
function SeasonPanel({ season, me, rooms, offers, showImdbId, showName, backdrop, saving, fetching, onWatched }) {
  const played = season.episodes.map((e) => e.lastPlayed).filter(Boolean).sort((a, b) => b.at.localeCompare(a.at))[0];
  const playedBy = played && (me && played.byId === me.id ? "you" : played.byName);
  const aired = season.airDate ? new Date(season.airDate).getFullYear() : null;
  const marks = useDownloadMarks();
  const phone = usePhone();
  const downloadable = (e) => (e.titleId && e.status === "ready")
    || (fetching && ((e.titleId && e.status === "transcoding") || (!e.titleId && !!episodeRelease(offers, e.season, e.number))));

  return (
    <div className="kc-season">
      <div className="kc-season__head">
        <div className="kc-season__text">
          <span className="kc-season__title">Season {season.number} · {season.episodes.length} episodes{aired ? ` · ${aired}` : ""}</span>
          <span className="kc-season__note">
            {played ? `Last played ${ago(played.at)} by ${playedBy}` : "Not played yet"}
          </span>
        </div>
        <div className="kc-season__actions">
          <SeasonWatched imdbId={showImdbId} season={season} onChange={onWatched} grow={phone} />
          {fetching && <SeasonDownload imdbId={showImdbId} showName={showName} season={season} offers={offers} backdrop={backdrop} grow={phone} />}
        </div>
      </div>

      {season.episodes.map((e) => {
        const row = <SeasonEpisode key={e.number} episode={e} rooms={rooms} offers={offers} showImdbId={showImdbId}
          download={saving ? (e.titleId && marks.film(e.titleId)) || marks.want(showImdbId, e.season, e.number) : null} />;
        const seenable = hasAired(e);
        const downloading = saving && (downloadable(e) || !!marks.want(showImdbId, e.season, e.number));
        const head = downloading ? {
          name: e.name || `Episode ${e.number}`, context: `${showName} · ${episodeCode(e)}`,
          runtime: e.runtime ? clock(e.runtime * 60) : e.durationSeconds ? clock(e.durationSeconds) : null,
          still: e.still, backdrop,
        } : null;
        const target = { titleId: e.titleId || null, imdbId: showImdbId, season: e.season, number: e.number, ready: e.status === "ready", head };

        if (phone) {
          return (
            <div key={e.number} className="kc-episode-line">
              {row}
              {downloading
                ? <EpisodeMenuWithDownload imdbId={showImdbId} episode={e} seenable={seenable} target={target} onWatched={onWatched} />
                : seenable ? <EpisodeMenu imdbId={showImdbId} episode={e} seenable onWatched={onWatched} />
                  : <span className="kc-episode-save" />}
            </div>
          );
        }

        const seen = seenable
          ? <EpisodeWatched imdbId={showImdbId} episode={e} onChange={onWatched} />
          : <span className="kc-episode-save" />;
        if (!saving) return <div key={e.number} className="kc-episode-line">{row}{seen}</div>;
        return (
          <div key={e.number} className="kc-episode-line">
            {row}
            {seen}
            {downloading ? <DownloadMark {...target} /> : <span className="kc-episode-save" />}
          </div>
        );
      })}
    </div>
  );
}

/** Whether an episode can have been watched: the library holds it, or it has aired. */
function hasAired(e) {
  return Boolean(e.titleId) || (e.airDate && e.airDate <= new Date().toISOString().slice(0, 10));
}

/**
 * The season watched, or not, at its head: every aired episode of it marked watched with one press, and
 * unmarked with the next once every one is.
 */
function SeasonWatched({ imdbId, season, onChange, grow = false }) {
  const online = useOnline();
  const all = season.episodes.filter(hasAired);
  const { shown, busy, change } = useWatchedChange(all.length > 0 && all.every((e) => e.watched), onChange);
  if (all.length === 0) return null;
  return (
    <Button variant={shown ? "accent" : "outline"} size="sm" icon="circle-check" aria-pressed={shown} disabled={busy || !online} grow={grow}
      onClick={() => change({ imdbId, kind: "show", season: season.number, watched: !shown }, "Could not change that")}>
      {shown ? "Season watched" : "Mark season watched"}
    </Button>
  );
}

/** At an episode's row's end: whether this person watched it, one press to say otherwise. */
function EpisodeWatched({ imdbId, episode: e, onChange }) {
  const online = useOnline();
  const { shown, busy, change } = useWatchedChange(Boolean(e.watched), onChange);
  const label = shown ? "Watched" : "Mark watched";
  return (
    <span className="kc-episode-save">
      <Button variant="bare" className={"kc-seen-mark" + (shown ? " is-on" : "")} aria-pressed={shown}
        aria-label={`${label}: ${e.name || `Episode ${e.number}`}`} title={label} disabled={busy || !online}
        onClick={() => change({ imdbId, kind: "show", season: e.season, episode: e.number, watched: !shown }, "Could not change that")}>
        <Icon name="circle-check" size={18} />
      </Button>
    </span>
  );
}

/**
 * A phone's episode menu, at its row's end: Watched, and Download where the episode can be downloaded
 * (`download`, from `useDownloadMark`), whose sheet is drawn beside the menu so it outlives the menu
 * closing.
 */
function EpisodeMenu({ imdbId, episode: e, seenable, download = null, onWatched }) {
  const online = useOnline();
  const { shown, busy, change } = useWatchedChange(Boolean(e.watched), onWatched);
  const name = e.name || `Episode ${e.number}`;
  return (
    <span className="kc-episode-save">
      <Menu label={name} placement="bottom-end"
        trigger={<IconButton variant="ghost" icon="ellipsis-vertical" label={`More: ${name}`} />}>
        {seenable && (
          <MenuItem icon="circle-check" disabled={busy || !online}
            onSelect={() => change({ imdbId, kind: "show", season: e.season, episode: e.number, watched: !shown }, "Could not change that")}>
            {shown ? "Mark unwatched" : "Mark watched"}
          </MenuItem>
        )}
        {download && (
          <MenuItem icon={download.icon} disabled={download.disabled} onSelect={download.open}>{download.label}</MenuItem>
        )}
      </Menu>
    </span>
  );
}

/** A phone's episode menu for an episode that can be downloaded: where its download stands, and its sheet. */
function EpisodeMenuWithDownload({ imdbId, episode, seenable, target, onWatched }) {
  const download = useDownloadMark(target);
  return (
    <>
      <EpisodeMenu imdbId={imdbId} episode={episode} seenable={seenable} download={download} onWatched={onWatched} />
      {download && download.sheet}
    </>
  );
}

/** An episode's row on the page: a link to watch it, or a press that fetches it while it plays. */
function SeasonEpisode({ episode: e, rooms, offers, showImdbId, download }) {
  const [starting, setStarting] = React.useState(false);
  const online = useOnline();
  const done = e.progress && e.progress.finished;
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
  // With no connection a row plays from the device when the episode is downloaded here, and is dimmed
  // and inert when not.
  return <EpisodeRow episode={e} download={download} room={!!room} starting={starting} off={!online}
    link={here && !play.disabled ? play : null} onPick={!here && offer ? watch : null} />;
}
