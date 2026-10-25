import { Avatar, TvHeading, TvRow } from "@thekrystalship/krystal";
import { discoverPersonPath } from "../paths.js";
import { linkProps } from "../router.js";

/**
 * The people in a film or a show as a row of round portraits, each with the part they play. OK on one
 * the catalogue knows opens Discover on everything they are credited on; every one takes focus,
 * which is also how the remote reaches the row and the details below it.
 */
export function TvCast({ cast }) {
  if (!cast || cast.length === 0) return null;
  return (
    <TvRow className="kc-tv-cast" trackClassName="kc-tv-cast__track" title="Cast">
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
    </TvRow>
  );
}

function Face({ person }) {
  return <Avatar src={person.portrait} name={person.name} tone="muted" size="css" imagePosition="center 20%" className="kc-tv-person__face" />;
}

/** Label over value, four to a line; a fact with no value is left out. `mono` sets a code in a code face. */
export function TvFacts({ facts }) {
  const shown = facts.filter(([, value]) => value !== null && value !== undefined && value !== "");
  if (shown.length === 0) return null;
  return (
    <section className="kc-tv-facts">
      <TvHeading title="Details" />
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

/** The sources' credit, in the words TMDB's terms ask for wherever its words and artwork are shown. */
export function TmdbCredit() {
  return (
    <p className="kc-tv-tmdb">
      Details and artwork from TMDB, IMDb and Wikidata. This product uses TMDB and the TMDB APIs but is not endorsed,
      certified, or otherwise approved by TMDB.
    </p>
  );
}
