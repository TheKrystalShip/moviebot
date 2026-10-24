// A filter over an answer the page holds whole: the library's films, shows and anime, Want to watch
// and History. Each title is read for the traits a section picks by, and the options each section
// holds are counted over the whole answer, so they are exact. The sections,
// their ids and their option values are the ones the API's facets name (`docs/api-contract.md`,
// Filters and facets), so a page treats an answer it holds and one the API pages the same way.
//
// A section is answered as any of its picked options and the sections combine as all of them; a
// section's options are counted with every other section's picks applied and without its own, so a
// second option of the same section can still be picked.

/** The decades a decade's options are named by: its first year. */
const decadeOf = (year) => (year ? Math.floor(year / 10) * 10 : null);

/**
 * The film database's genres that are other genres in Cinema's one vocabulary across films and shows,
 * by the names they are here: a show's list joins genres a film's keeps apart. The API's
 * `Films/CinemaGenres.cs` holds the same table, and its tests hold the two equal.
 */
export const JOINED_GENRES = {
  "Action & Adventure": ["Action", "Adventure"],
  "Sci-Fi & Fantasy": ["Science Fiction", "Fantasy"],
  "War & Politics": ["War"],
};

/** The genres of a title's database genres in Cinema's vocabulary, each once. */
export function cinemaGenres(names) {
  return [...new Set((names || []).flatMap((name) => JOINED_GENRES[name] || [name]))];
}

/** A film's length by its minutes, as the API names lengths; a show has none. */
export function lengthOf(minutes) {
  if (!minutes) return null;
  return minutes < 90 ? "short" : minutes <= 150 ? "standard" : "long";
}

/** The rating floors a filter offers, highest first. */
export const RATINGS = [8, 7, 6];

/**
 * A title's traits, from any of the shapes the API lists a title in: `kind` (`film` or `show`),
 * `anime`, `genres`, `year`, `length`, `rating` and `language`; a trait the answer does not carry is
 * null, and such a title is in none of that section's options.
 */
export function traitsOf(title) {
  const details = title.details || null;
  const kind = title.kind === "show" || title.episode || title.seasons ? "show" : "film";
  const minutes = title.runtimeMinutes ?? (details && details.runtimeMinutes)
    ?? (kind === "film" && title.durationSeconds ? Math.round(title.durationSeconds / 60) : null);
  return {
    kind,
    anime: title.category === "anime",
    genres: cinemaGenres(title.genres || (details && details.genres)),
    year: title.year ?? title.firstYear ?? (title.film && title.film.year) ?? null,
    length: kind === "film" ? lengthOf(minutes) : null,
    rating: title.rating ?? (details && details.rating) ?? null,
    language: title.originalLanguage ?? (details && details.originalLanguage) ?? null,
  };
}

/** Whether a title's traits hold one option of a section. */
function holds(traits, section, value) {
  switch (section) {
    case "kind": return value === "anime" ? traits.anime : traits.kind === value;
    case "genre": return traits.genres.includes(value);
    case "decade": return decadeOf(traits.year) === Number(value);
    case "length": return traits.length === value;
    case "rating": return traits.rating !== null && traits.rating >= Number(value);
    case "language": return traits.language === value;
    default: return false;
  }
}

/** The values picked in a section, as a list: a `one` or `optional` section's single value is a list of one. */
const picked = (picks, section) => {
  const value = picks[section];
  const values = Array.isArray(value) ? value : value === null || value === undefined || value === "" ? [] : [value];
  return section === "genre" ? cinemaGenres(values) : values;
};

/** Whether a title's traits answer every section's picks other than `except`. */
function answers(traits, picks, except = null) {
  return Object.keys(picks).every((section) => {
    if (section === except) return true;
    const values = picked(picks, section);
    return values.length === 0 || values.some((value) => holds(traits, section, value));
  });
}

/** The titles of a whole answer that answer the picks (`{kind, genre, decade, length, rating, language}`). */
export function filterWhole(titles, picks) {
  return titles.filter((title) => answers(traitsOf(title), picks));
}

/**
 * The options each section's titles hold, as the API's facets name them, each with how many titles
 * hold it: `[{id, options: [{value, count}]}]`. `fixed` names the sections the page's scope fixes,
 * which are not reported.
 */
export function wholeFacets(titles, picks = {}, fixed = []) {
  const read = titles.map(traitsOf);
  const count = (section, value) => read.filter((t) => answers(t, picks, section) && holds(t, section, value)).length;
  const options = (section, values) => values
    .map((value) => ({ value: String(value), count: count(section, value) }))
    .filter((option) => option.count > 0);

  const genres = [...new Set(read.flatMap((t) => t.genres))].sort((a, b) => a.localeCompare(b));
  const decades = [...new Set(read.map((t) => decadeOf(t.year)).filter(Boolean))].sort((a, b) => b - a);
  const languages = [...new Set(read.map((t) => t.language).filter(Boolean))].sort();
  return [
    { id: "kind", options: options("kind", ["film", "show", "anime"]) },
    { id: "genre", options: options("genre", genres) },
    { id: "decade", options: options("decade", decades) },
    { id: "length", options: options("length", ["short", "standard", "long"]) },
    { id: "rating", options: options("rating", RATINGS) },
    { id: "language", options: options("language", languages) },
  ].filter((section) => !fixed.includes(section.id));
}
