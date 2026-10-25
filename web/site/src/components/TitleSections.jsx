import React from "react";
import { discoverFranchisePath, discoverListPath } from "../paths.js";
import { linkProps } from "../router.js";
import { CastGrid, FactGrid, Keywords, dollars, originalTitle } from "./FilmDetails.jsx";
import { TitleAwards } from "./TitleHero.jsx";
import { TitleShelves } from "../pages/DiscoverShelves.jsx";

/**
 * Named things (`{id, name}`) as links, each to every title the list query `query(id)` names, or to
 * `path(id)`; one with no id is its name alone.
 */
function Links({ items, query, path }) {
  if (!items || items.length === 0) return null;
  return items.map((x, i) => (
    <React.Fragment key={x.id || x.name}>
      {i > 0 && ", "}
      {x.id ? <a {...linkProps(path ? path(x.id) : discoverListPath(query(x.id), x.name))}>{x.name}</a> : x.name}
    </React.Fragment>
  ));
}

/**
 * The facts a title's page lists under Details, from its catalogue page (`page`): its names, when it
 * came out, its languages and countries, its studios or networks, its franchises, where it is set and
 * filmed, and its money for a film; each thing named leads to the titles that share it. `more` are the
 * page's own rows, put before the IMDb id.
 */
export function titleFacts(page, more = []) {
  if (!page) return more;
  const show = page.kind === "show";
  const companies = show ? page.networks : page.companies;
  return [
    ["Original title", originalTitle(page.title, page.originalTitle)],
    ...(show ? [] : [["Released", page.released ? new Date(page.released).getFullYear() : page.year]]),
    ["Language", page.languages && page.languages.length ? page.languages.join(", ") : null],
    ["Country", page.countries && page.countries.length ? page.countries.join(", ") : null],
    [show ? "Network" : "Studio", companies && companies.length
      ? <Links items={companies.slice(0, 3)} query={(id) => ({ company: id })} /> : null],
    ["Franchise", page.franchises && page.franchises.length ? <Links items={page.franchises} path={discoverFranchisePath} /> : null],
    ["Set in", page.facts && page.facts.setIn.length ? <Links items={page.facts.setIn.slice(0, 3)} query={(id) => ({ fact: `P840:${id}` })} /> : null],
    ["Filmed in", page.facts && page.facts.filmedIn.length ? <Links items={page.facts.filmedIn.slice(0, 3)} query={(id) => ({ fact: `P915:${id}` })} /> : null],
    ...(show ? [] : [["Budget", dollars(page.budget)], ["Box office", dollars(page.revenue)]]),
    ...more,
    ["IMDb", page.id, true],
  ];
}

/**
 * Everything a title's page draws under its hero, from its catalogue page (`page`): the cast, the
 * shelves of titles like it (`shelves`, absent from a page drawn from the device), the awards, the
 * details (`facts`, from `titleFacts`) and the keywords. Only the details are drawn with no page.
 */
export function TitleSections({ page, facts, shelves = true }) {
  return (
    <>
      {page && page.cast.length > 0 && (
        <section className="kc-section">
          <h2>Cast</h2>
          <CastGrid cast={page.cast} />
        </section>
      )}
      {page && shelves && <TitleShelves page={page} />}
      {page && <TitleAwards facts={page.facts} />}
      <section className="kc-section">
        <h2>Details</h2>
        <FactGrid facts={facts} />
      </section>
      {page && <Keywords tags={page.keywords} />}
      {page && (
        <p className="kc-attribution-note">
          Details and artwork from TMDB, IMDb and Wikidata. This product uses TMDB and the TMDB APIs but is not
          endorsed, certified, or otherwise approved by TMDB.
        </p>
      )}
    </>
  );
}
