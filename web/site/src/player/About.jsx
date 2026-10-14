import React from "react";
import { Icon } from "@thekrystalship/krystal";
import { api } from "./engine/api.ts";
import { environment } from "./engine/environment.ts";
import { Drawer } from "../components/Drawer.jsx";
import { CastGrid, Credits, FactGrid, FilmScore, Genres, Tagline, originalTitle } from "../components/FilmDetails.jsx";
import { clock } from "../time.js";

/** How long the room stays paused before the paused card says what is playing. */
export const PauseCardMs = 4000;

/**
 * What the player tells about what the room holds: the film database's description through the API
 * when it answers, else what the manifest carries. Every manifest names the film or show, its year and
 * its stars; a saved copy's also keeps the description's facts and the episode's own name and story. A
 * guest's link opens the room's film and nothing else, so a guest is told what the manifest says.
 *
 * A show is read once for all its episodes; the episode is found in it by its place.
 */
export function useAbout(view) {
  const manifest = view.manifest;
  const imdbId = (manifest && manifest.film && manifest.film.imdbId) || null;
  const show = Boolean(manifest && manifest.episode);
  const asks = imdbId !== null && !view.viewOnly;
  const [read, setRead] = React.useState(null);
  React.useEffect(() => {
    if (!asks) return undefined;
    let live = true;
    (show ? api.show(imdbId) : api.film(imdbId)).then((d) => { if (live) setRead({ imdbId, d }); }, () => {});
    return () => { live = false; };
  }, [asks, imdbId, show]);
  const described = asks && read && read.imdbId === imdbId ? read.d : null;
  return React.useMemo(() => (manifest ? aboutOf(manifest, described) : null), [manifest, described]);
}

/** An image the API keeps, by the address it names it with. */
const served = (path) => (path ? environment().apiUrl(path) : null);

/** In the player a name leads nowhere: following it would leave the room. */
const unlinked = (cast) => cast.map(({ personId: _, ...person }) => person);

function aboutOf(manifest, d) {
  const film = manifest.film || {};
  const facts = manifest.details || null;
  const place = manifest.episode || null;
  const show = Boolean(place);
  const listed = (d && d.seasons ? d.seasons.flatMap((s) => s.episodes) : [])
    .find((e) => place && e.season === place.season && e.number === place.number) || null;
  const stars = d && d.cast.length > 0 ? d.cast.slice(0, 3).map((c) => c.name)
    : film.starring ? film.starring.split(", ") : [];
  const years = show
    ? (d && d.firstYear ? `${d.firstYear}–${d.inProduction ? "" : d.lastYear || ""}` : film.year || null)
    : film.year || (d && d.year) || null;

  return {
    show,
    described: Boolean(d),
    name: (d && d.title) || film.name || manifest.title,
    years,
    certification: (d && d.certification) || (facts && facts.certification) || null,
    rating: (d && d.rating) || (facts && facts.rating) || null,
    votes: (d && d.votes) || (facts && facts.votes) || 0,
    genres: (d && d.genres) || (facts && facts.genres) || [],
    tagline: (d && d.tagline) || null,
    overview: (d && d.overview) || (facts && facts.overview) || null,
    stars,
    credits: d
      ? { creators: d.creators, directors: d.directors, writers: d.writers, cast: unlinked(d.cast) }
      : { directors: (facts && facts.directors) || [], cast: stars.map((name) => ({ name })) },
    cast: d ? unlinked(d.cast) : [],
    backdrop: served((d && (d.backdropUrl || d.posterUrl)) || (facts && (facts.backdropUrl || facts.posterUrl))),
    length: clock(manifest.durationSeconds),
    episode: place && {
      code: `S${place.season} · E${place.number}`,
      name: (listed && listed.name) || place.name || null,
      overview: (listed && listed.overview) || place.overview || null,
      airDate: (listed && listed.airDate) || null,
      rating: (listed && listed.rating) || null,
      still: served(listed && listed.stillUrl),
    },
    facts: d ? (show
      ? [
        ["Original title", originalTitle(d.title, d.originalTitle)],
        ["Status", d.status],
        ["Network", d.networks.join(", ") || null],
        ["Language", d.languages.join(", ") || null],
        ["Country", d.countries.join(", ") || null],
        ["IMDb", d.imdbId, true],
      ]
      : [
        ["Original title", originalTitle(d.title, d.originalTitle)],
        ["Released", d.releaseDate ? new Date(d.releaseDate).getFullYear() : d.year],
        ["Language", d.languages.join(", ") || null],
        ["Country", d.countries.join(", ") || null],
        ["IMDb", d.imdbId, true],
      ]) : [],
  };
}

/**
 * The (i) in the top bar: what is playing, in a pane down the right edge on a wide frame and a sheet
 * from the bottom on a compact one. It is this viewer's alone and moves nobody, so the film plays on
 * beside it, and nothing in it leads out of the room.
 */
export function AboutButton({ open, onToggle }) {
  return (
    <button type="button" className={"mp-round mp-top__about" + (open ? " is-open" : "")} aria-expanded={open}
      aria-label="About" title="About (i)" onClick={onToggle}>
      <Icon name="info" size={18} />
    </button>
  );
}

export function AboutPanel({ about, sheet, onClose }) {
  if (sheet) {
    return (
      <Drawer contained title="About" onClose={onClose} className="mp-about mp-about--sheet">
        <AboutBody about={about} />
      </Drawer>
    );
  }
  return (
    <aside className="mp-about mp-about--pane" aria-label="About">
      <div className="mp-about__bar">
        <span className="mp-about__label">About</span>
        <button type="button" className="mp-round mp-round--small" aria-label="Close" onClick={onClose}><Icon name="x" size={16} /></button>
      </div>
      <div className="mp-about__scroll"><AboutBody about={about} /></div>
    </aside>
  );
}

/**
 * The film's page without what a page does: the same heading, score, story, credits, cast and facts.
 * An episode leads with itself, its still, name and story, and its show follows.
 */
function AboutBody({ about: a }) {
  const e = a.episode;
  const lead = (e && e.still) || a.backdrop;
  return (
    <div className="mp-about__body">
      {lead && <div className={"mp-about__art" + (e && e.still ? " is-still" : "")} style={{ backgroundImage: `url("${lead}")` }} />}
      {e && (
        <section className="mp-about__head">
          <span className="kc-eyebrow kc-eyebrow--muted">{a.name} · {e.code}</span>
          <h2 className="mp-about__name">{e.name || e.code}</h2>
          <div className="kc-tt__meta">
            {e.airDate && <span>{new Date(e.airDate).getFullYear()}</span>}
            <span>{a.length}</span>
            {e.rating && <FilmScore rating={e.rating} compact />}
          </div>
          {e.overview && <p className="mp-about__overview">{e.overview}</p>}
        </section>
      )}
      <section className={"mp-about__head" + (e ? " mp-about__head--show" : "")}>
        <span className="kc-eyebrow kc-eyebrow--muted">{a.show ? "TV series" : "Movie"}</span>
        <h2 className="mp-about__name">{a.name}</h2>
        <div className="kc-tt__meta">
          {a.years && <span>{a.years}</span>}
          {a.certification && <span className="kc-cert">{a.certification}</span>}
          {!a.show && <span>{a.length}</span>}
        </div>
        {(a.rating || a.genres.length > 0) && (
          <div className="kc-tt__score-row">
            {a.rating && <FilmScore rating={a.rating} votes={a.votes} compact />}
            <Genres names={a.genres} />
          </div>
        )}
        <Tagline text={a.tagline} />
        {a.overview && <p className="mp-about__overview">{a.overview}</p>}
        <Credits film={a.credits} />
      </section>
      {a.cast.length > 0 && (
        <section className="mp-about__section">
          <h3 className="mp-about__label">Cast</h3>
          <CastGrid cast={a.cast} />
        </section>
      )}
      {a.facts.length > 0 && (
        <section className="mp-about__section">
          <h3 className="mp-about__label">Details</h3>
          <FactGrid facts={a.facts} />
        </section>
      )}
      {a.described && <p className="kc-attribution-note">{a.show ? "Show" : "Film"} details and artwork from TMDB.</p>}
    </div>
  );
}

/**
 * What is playing, over the left of the film once the room has stayed paused a moment: an episode's
 * show, place and name, a film's name, the facts, the story and who is in it. It answers nothing: a
 * press goes through to the film beneath it. It stands in for the Paused a guest is otherwise shown.
 */
export function PauseCard({ about }) {
  const e = about.episode;
  const story = (e && e.overview) || about.overview;
  return (
    <div className="mp-pausecard" role="status">
      <div className="mp-pausecard__text">
        <span className="mp-pausecard__eyebrow">Paused</span>
        <span className="mp-pausecard__name">{about.name}</span>
        {e && <span className="mp-pausecard__episode">{[e.code, e.name].filter(Boolean).join(" · ")}</span>}
        <div className="mp-pausecard__meta">
          {!e && about.years && <span>{about.years}</span>}
          {about.certification && <span className="kc-cert">{about.certification}</span>}
          <span>{about.length}</span>
          {(e ? e.rating : about.rating) && <FilmScore rating={e ? e.rating : about.rating} compact />}
        </div>
        {story && <p className="mp-pausecard__story">{story}</p>}
        {about.stars.length > 0 && <span className="mp-pausecard__stars">Starring {about.stars.join(", ")}</span>}
      </div>
    </div>
  );
}

/** Whether the room has stayed paused for the card's moment. */
export function usePausedLong(paused) {
  const [long, setLong] = React.useState(false);
  React.useEffect(() => {
    setLong(false);
    if (!paused) return undefined;
    const timer = window.setTimeout(() => setLong(true), PauseCardMs);
    return () => window.clearTimeout(timer);
  }, [paused]);
  return long;
}
