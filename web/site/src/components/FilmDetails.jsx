import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";

/** A count of votes the way the design prints one: 8,740 is 8.7K. */
export function votes(n) {
  if (!n) return "0";
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1).replace(/\.0$/, "")}M`;
  if (n >= 1000) return `${(n / 1000).toFixed(n >= 100_000 ? 0 : 1).replace(/\.0$/, "")}K`;
  return String(n);
}

export function bytes(n) {
  if (!n) return null;
  const gb = n / 1024 ** 3;
  return gb >= 1 ? `${gb.toFixed(1)} GB` : `${Math.round(n / 1024 ** 2)} MB`;
}

/** How long ago, in days, as a person says it. */
export function since(iso) {
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
  if (days <= 0) return "Today";
  if (days === 1) return "Yesterday";
  return `${days} days ago`;
}

export function initials(name) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0].toUpperCase()).join("");
}

/**
 * The poster the API keeps for a film by its IMDb id, for a film that is not in the library. Nothing
 * kept is an empty frame rather than a broken image.
 */
export function KeptPoster({ imdbId, className = "kc-row__thumb" }) {
  const [failed, setFailed] = React.useState(false);
  if (!imdbId || failed) return <span className={className} />;
  return <img className={className} src={`/films/${encodeURIComponent(imdbId)}/poster.jpg`} alt="" loading="lazy" onError={() => setFailed(true)} />;
}

/** The film database's rating, and that it is the database's. */
export function FilmScore({ rating, votes: count, compact = false }) {
  if (!rating) return null;
  return (
    <span className={"kc-score" + (compact ? " kc-score--compact" : "")} title={`${rating.toFixed(1)} out of 10 from ${count} votes on TMDB`}>
      <Icon name="star" size={compact ? 12 : 16} className="kc-score__star" />
      <span className="kc-score__value">{rating.toFixed(1)}</span>
      {!compact && <span className="kc-score__of">/10 · {votes(count)} votes · TMDB</span>}
    </span>
  );
}

export function Genres({ names }) {
  if (!names || names.length === 0) return null;
  return <div className="kc-genres">{names.map((g) => <span key={g} className="kc-genre">{g}</span>)}</div>;
}

/** Director, writers and the top of the billing, as the design lays them out. */
export function Credits({ film }) {
  const rows = [
    [film.directors.length > 1 ? "Directors" : "Director", film.directors.join(", ")],
    ["Writers", film.writers.join(", ")],
    ["Stars", film.cast.slice(0, 4).map((c) => c.name).join(" · ")],
  ].filter(([, v]) => v);
  if (rows.length === 0) return null;
  return (
    <div className="kc-credits">
      {rows.map(([label, value]) => (
        <React.Fragment key={label}>
          <span className="kc-credits__label">{label}</span>
          <span className="kc-credits__value">{value}</span>
        </React.Fragment>
      ))}
    </div>
  );
}

function Portrait({ person }) {
  const [failed, setFailed] = React.useState(false);
  return (
    <span className="kc-cast__face">
      {person.portraitUrl && !failed
        ? <img src={person.portraitUrl} alt="" loading="lazy" onError={() => setFailed(true)} />
        : initials(person.name)}
    </span>
  );
}

export function CastGrid({ cast }) {
  return (
    <div className="kc-cast">
      {cast.map((c) => (
        <div key={c.name + (c.character || "")} className="kc-cast__card">
          <Portrait person={c} />
          <div className="kc-cast__text">
            <span className="kc-cast__name">{c.name}</span>
            {c.character && <span className="kc-cast__role">{c.character}</span>}
          </div>
        </div>
      ))}
    </div>
  );
}

/** Label and value in a grid of cells; a fact with no value is left out. `mono` sets a code in a code face. */
export function FactGrid({ facts }) {
  const shown = facts.filter(([, value]) => value !== null && value !== undefined && value !== "");
  if (shown.length === 0) return null;
  return (
    <div className="kc-factgrid">
      {shown.map(([label, value, mono]) => (
        <div key={label} className="kc-factgrid__cell">
          <span className="kc-factgrid__label">{label}</span>
          <span className={"kc-factgrid__value" + (mono ? " kc-mono" : "")}>{value}</span>
        </div>
      ))}
    </div>
  );
}
