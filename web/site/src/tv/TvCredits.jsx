import React from "react";
import { initials } from "../components/FilmDetails.jsx";
import { discoverPersonPath } from "../paths.js";
import { linkProps } from "../router.js";

/**
 * The people in a film or a show as a row of round portraits, each with the part they play. OK on one
 * the film database knows opens Discover on everything they are credited on; every one takes focus,
 * which is also how the remote reaches the row and the details below it.
 */
export function TvCast({ cast }) {
  if (!cast || cast.length === 0) return null;
  return (
    <section className="kc-tv-cast" data-tv-row="">
      <h2 className="tv-row__head">Cast</h2>
      <div className="kc-tv-cast__track tv-row__track">
        {cast.map((c) => {
          const body = (
            <>
              <Face person={c} />
              <span className="kc-tv-person__name">{c.name}</span>
              {c.character && <span className="kc-tv-person__part">{c.character}</span>}
            </>
          );
          const key = c.name + (c.character || "");
          const label = c.character ? `${c.name}, ${c.character}` : c.name;
          return c.personId
            ? <a key={key} className="kc-tv-person" aria-label={label} {...linkProps(discoverPersonPath(c.personId))}>{body}</a>
            : <div key={key} className="kc-tv-person" tabIndex={0} role="group" aria-label={label}>{body}</div>;
        })}
      </div>
    </section>
  );
}

function Face({ person }) {
  const [failed, setFailed] = React.useState(false);
  return (
    <span className="kc-tv-person__face">
      {person.portraitUrl && !failed
        ? <img src={person.portraitUrl} alt="" loading="lazy" onError={() => setFailed(true)} />
        : initials(person.name)}
    </span>
  );
}

/** Label over value, four to a line; a fact with no value is left out. `mono` sets a code in a code face. */
export function TvFacts({ facts }) {
  const shown = facts.filter(([, value]) => value !== null && value !== undefined && value !== "");
  if (shown.length === 0) return null;
  return (
    <section className="kc-tv-facts">
      <h2 className="tv-row__head">Details</h2>
      <div className="kc-tv-facts__grid">
        {shown.map(([label, value, mono]) => (
          <div key={label} className="kc-tv-facts__cell">
            <span className="kc-tv-facts__label">{label}</span>
            <span className={"kc-tv-facts__value" + (mono ? " kc-mono" : "")}>{value}</span>
          </div>
        ))}
      </div>
    </section>
  );
}

/** The film database's credit, which its terms ask for wherever its words and artwork are shown. */
export function TmdbCredit() {
  return <p className="kc-tv-tmdb">Film details and artwork from TMDB.</p>;
}
