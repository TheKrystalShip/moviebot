import React from "react";
import { Avatar, Button, Icon, Pill, Skeleton } from "@thekrystalship/krystal";
import { discoverPersonPath } from "../paths.js";
import { linkProps } from "../router.js";

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

/** The line the film database sells a title with, set apart from the overview under it. */
export function Tagline({ text }) {
  return text ? <p className="kc-tt__tagline">{text}</p> : null;
}

/** The film database's keywords for a title (`{id, name}`), as a section of quiet chips; none, no section. */
export function Keywords({ tags }) {
  if (!tags || tags.length === 0) return null;
  return (
    <section className="kc-section">
      <h2>Keywords</h2>
      <div className="kc-keywords">{tags.map((k) => <Pill key={k.id} tone="muted">{k.name}</Pill>)}</div>
    </section>
  );
}

/** The title in its own language, when it is not the one the page is headed with. */
export function originalTitle(title, original) {
  return original && original !== title ? original : null;
}

export function Genres({ names }) {
  if (!names || names.length === 0) return null;
  return <div className="kc-genres">{names.map((g) => <span key={g} className="kc-genre">{g}</span>)}</div>;
}

/**
 * Director, writers and the top of the billing, as the design lays them out; a show names its
 * creators in the director's place. Each person the film database has an id for leads to their work
 * in Discover.
 */
export function Credits({ film }) {
  const ids = film.personIds || {};
  const named = (names) => (names || []).map((name) => ({ name, personId: ids[name] }));
  const creators = named(film.creators);
  const directors = named(film.directors);
  const rows = [
    [creators.length > 1 ? "Creators" : "Creator", creators, ", "],
    [directors.length > 1 ? "Directors" : "Director", directors, ", "],
    ["Writers", named(film.writers), ", "],
    ["Stars", film.cast.slice(0, 4), " · "],
  ].filter(([, people]) => people.length > 0);
  if (rows.length === 0) return null;
  return (
    <div className="kc-credits">
      {rows.map(([label, people, between]) => (
        <React.Fragment key={label}>
          <span className="kc-credits__label">{label}</span>
          <span className="kc-credits__value">
            {people.map((p, i) => (
              <React.Fragment key={p.name}>
                {i > 0 && between}
                {p.personId ? <a className="kc-credits__person" {...linkProps(discoverPersonPath(p.personId))}>{p.name}</a> : p.name}
              </React.Fragment>
            ))}
          </span>
        </React.Fragment>
      ))}
    </div>
  );
}

function Portrait({ person }) {
  return <Avatar src={person.portraitUrl} name={person.name} tone="muted" size="css" className="kc-cast__face" />;
}

/**
 * A person as a card: their portrait (initials where there is none), their name with an optional tag
 * beside it, and a line under it, which `dot` leads with a mark in a tone (`ok`, `warn`, `muted`,
 * `none`). `face` stands in for the portrait. With `href` the card opens it, and with `onClick` it is
 * a button; `pressed` makes that button a two-state one, the card tinted while it is on. `end` sits
 * at the card's far end.
 */
export function PersonCard({ person, line, tag, dot, face, href, onClick, pressed, end, className = "" }) {
  const body = (
    <>
      {face || <Portrait person={person} />}
      <div className="kc-cast__text">
        <span className="kc-cast__name">
          <span className="kc-cast__name-text">{person.name}</span>
          {tag && <Pill size="sm" tone="accent">{tag}</Pill>}
        </span>
        {line && (
          <span className="kc-cast__role">
            {dot && <span className={"kc-cast__dot kc-cast__dot--" + dot} aria-hidden="true" />}
            {line}
          </span>
        )}
      </div>
      {end}
    </>
  );
  const classes = "kc-cast__card" + (className ? " " + className : "");
  if (href) return <a className={classes + " kc-cast__card--link"} {...linkProps(href)}>{body}</a>;
  if (onClick) return <Button variant="bare" pressed={pressed} className={classes + " kc-cast__card--link kc-cast__card--button"} onClick={onClick}>{body}</Button>;
  return <div className={classes}>{body}</div>;
}

/** The billing, each person the film database has an id for leading to their work in Discover. */
export function CastGrid({ cast }) {
  return (
    <div className="kc-cast">
      {cast.map((c) => (
        <PersonCard key={c.name + (c.character || "")} person={c} line={c.character}
          href={c.personId ? discoverPersonPath(c.personId) : undefined} />
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

/**
 * A film's or show's page while its description is read: the same frame, poster, heading, lines of
 * text and cast, as blocks where the words and artwork will be, so the page fills in where it stands.
 * `kind` is the eyebrow, which is known before anything is read.
 */
export function TitleSkeleton({ kind }) {
  return (
    <div className="kc-page kc-title">
      <section className="kc-tt">
        <div className="kc-tt__grid">
          <span className="k-skeleton kc-tt__poster" />
          <div className="kc-tt__body">
            <span className="kc-eyebrow kc-eyebrow--muted">{kind}</span>
            <Skeleton w="62%" h={34} r={8} />
            <div className="kc-tt__meta">
              <Skeleton w={40} h={14} /><Skeleton w={34} h={14} /><Skeleton w={64} h={14} />
            </div>
            <div className="kc-tt__score-row">
              <Skeleton w={180} h={18} /><Skeleton w={64} h={24} r={999} /><Skeleton w={72} h={24} r={999} />
            </div>
            <div className="kc-tt__overview">
              <Skeleton h={14} /><Skeleton h={14} style={{ marginTop: 9 }} /><Skeleton w="70%" h={14} style={{ marginTop: 9 }} />
            </div>
            <div className="kc-credits">
              {[48, 64, 40].map((w) => (
                <React.Fragment key={w}>
                  <Skeleton w={w} h={13} /><Skeleton w={`${w + 20}%`} h={13} />
                </React.Fragment>
              ))}
            </div>
            <div className="kc-actions">
              <Skeleton w={132} h={48} r={8} /><Skeleton w={112} h={48} r={8} />
            </div>
          </div>
        </div>
      </section>
      <section className="kc-section">
        <Skeleton w={64} h={18} />
        <div className="kc-cast">
          {Array.from({ length: 8 }, (_, i) => (
            <div key={i} className="kc-cast__card">
              <Skeleton w={44} h={44} r={999} />
              <div className="kc-cast__text" style={{ flex: 1, gap: 6 }}>
                <Skeleton w="70%" h={13} /><Skeleton w="45%" h={11} />
              </div>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
