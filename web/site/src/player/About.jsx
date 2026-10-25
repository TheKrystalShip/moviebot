import React from "react";
import { Button, Drawer, Icon } from "@thekrystalship/krystal";
import { api } from "./engine/api.ts";
import { environment } from "./engine/environment.ts";
import { CastGrid, Credits, FactGrid, FilmScore, Genres, Tagline, originalTitle } from "../components/FilmDetails.jsx";
import { clock } from "../time.js";

/** How long the room stays paused before the paused card says what is playing. */
export const PauseCardMs = 4000;

/**
 * What the player tells about what the room holds: the title's catalogue page through the API when it
 * answers, else what the manifest carries. Every manifest names the film or show, its year and its
 * stars; a saved copy's also keeps the title's card and the episode's own name and story. A guest's
 * link opens the room's film and nothing else, so a guest is told what the manifest says.
 *
 * A show's page and what the person has of it are read once for all its episodes; the episode is found
 * in the second by its place.
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
    Promise.all([api.title(imdbId), show ? api.show(imdbId).catch(() => null) : null])
      .then(([page, mine]) => { if (live) setRead({ imdbId, page, mine }); }, () => {});
    return () => { live = false; };
  }, [asks, imdbId, show]);
  const described = asks && read && read.imdbId === imdbId ? read : null;
  return React.useMemo(() => (manifest ? aboutOf(manifest, described) : null), [manifest, described]);
}

/** An image the API keeps, by the address it names it with. */
const served = (path) => (path ? environment().apiUrl(path) : null);

/** In the player a name leads nowhere: following it would leave the room. */
const unlinked = (people) => people.map((person) => ({ ...person, personId: null }));

/** The certificate for the viewer's own country, read from the browser's languages; none when it names none. */
function certificateOf(page) {
  const languages = navigator.languages || [navigator.language];
  const region = languages.map((l) => (l || "").split("-")[1]).find((r) => r && r.length === 2);
  return region && page.certificates ? page.certificates[region.toUpperCase()] || null : null;
}

function aboutOf(manifest, read) {
  const film = manifest.film || {};
  const d = read ? read.page : null;
  const card = d || manifest.card || null;
  const place = manifest.episode || null;
  const show = Boolean(place);
  const listed = (read && read.mine ? read.mine.seasons.flatMap((s) => s.episodes) : [])
    .find((e) => place && e.season === place.season && e.number === place.number) || null;
  const stars = d && d.cast.length > 0 ? d.cast.slice(0, 3).map((c) => c.name)
    : card && card.starring.length > 0 ? card.starring.slice(0, 3)
      : film.starring ? film.starring.split(", ") : [];
  const year = (card && card.year) || film.year || null;
  const years = show && card && card.year ? `${card.year}–${card.endYear && card.endYear !== card.year ? card.endYear : ""}` : year;
  const rating = card && card.rating && card.rating.value ? card.rating : null;

  return {
    show,
    described: Boolean(d),
    name: (card && card.title) || film.name || manifest.title,
    years,
    certification: d ? certificateOf(d) : null,
    rating: rating && rating.value,
    votes: rating ? rating.votes : 0,
    ratedBy: rating && rating.by,
    genres: (card && card.genres) || [],
    tagline: (d && d.tagline) || null,
    overview: (d && d.overview) || (card && card.plot) || null,
    stars,
    credits: d
      ? { crew: unlinked(d.crew), cast: unlinked(d.cast) }
      : { crew: ((card && card.madeBy) || []).map((name) => ({ name, job: show ? "Creator" : "Director" })), cast: stars.map((name) => ({ name })) },
    cast: d ? unlinked(d.cast) : [],
    backdrop: served(card && (card.backdrop || card.poster)),
    length: clock(manifest.durationSeconds),
    episode: place && {
      code: `S${place.season} · E${place.number}`,
      name: (listed && listed.name) || place.name || null,
      overview: (listed && listed.overview) || place.overview || null,
      airDate: (listed && listed.airDate) || null,
      still: served(listed && listed.still),
    },
    facts: d ? [
      ["Original title", originalTitle(d.title, d.originalTitle)],
      ...(show
        ? [["Status", d.status], ["Network", d.networks.map((n) => n.name).join(", ") || null]]
        : [["Released", d.released ? new Date(d.released).getFullYear() : d.year]]),
      ["Language", d.languages.join(", ") || null],
      ["Country", d.countries.join(", ") || null],
      ["IMDb", d.id, true],
    ] : [],
  };
}

/**
 * The (i) in the top bar: what is playing, in a pane down the right edge on a wide frame and a sheet
 * from the bottom on a compact one. It is this viewer's alone and moves nobody, so the film plays on
 * beside it, and nothing in it leads out of the room.
 */
export function AboutButton({ open, onToggle }) {
  return (
    <Button variant="bare" className={"mp-round mp-top__about" + (open ? " is-open" : "")} aria-expanded={open}
      aria-label="About" title="About (i)" onClick={onToggle}>
      <Icon name="info" size={18} />
    </Button>
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
        <Button variant="bare" className="mp-round mp-round--small" aria-label="Close" onClick={onClose}><Icon name="x" size={16} /></Button>
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
            {a.rating && <FilmScore rating={a.rating} votes={a.votes} by={a.ratedBy} compact />}
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
      {a.described && (
        <p className="kc-attribution-note">
          Details and artwork from TMDB, IMDb and Wikidata. This product uses TMDB and the TMDB APIs but is not
          endorsed, certified, or otherwise approved by TMDB.
        </p>
      )}
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
          {!e && about.rating && <FilmScore rating={about.rating} by={about.ratedBy} compact />}
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
