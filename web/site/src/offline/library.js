// What is on the device, in the shapes the API answers, so a page drawn with no connection is the same
// page drawing less: a finished save of a film is a title as `GET /api/titles` lists it, a show with a
// finished save of an episode is a show as `GET /api/shows` lists it, and a kept show is its catalogue
// page with `GET /api/me/shows/{imdbId}` beside it, every episode not on the device unplayable.
// Artwork is the device's own, by the object URLs the caller passes in, and nothing leads to the
// catalogue, which needs a connection.

/** What Wikidata says of a title drawn from the device: nothing, since nothing of it leads anywhere. */
const NO_FACTS = { setIn: [], filmedIn: [], basedOn: [], awards: [], nominations: 0, subjects: [] };

/**
 * The card a save keeps (the show's for an episode), with the device's artwork; one saved with no card
 * is drawn from what the save names.
 */
export function cardOf(meta, art) {
  const imdbId = meta.show ? meta.show.imdbId : meta.imdbId;
  const card = meta.card || {
    id: imdbId, kind: meta.kind === "episode" ? "show" : "film", title: meta.show ? meta.show.name : meta.name,
    year: meta.year, genres: [], rating: null, category: meta.category || "general", madeBy: [], starring: [],
  };
  return { ...card, poster: art.poster || null, backdrop: art.backdrop || null, you: null };
}

/**
 * A title's catalogue page as far as a card tells it, for a page drawn from the device: its card, with
 * those who made it as its crew and nothing that leads to the catalogue.
 */
export function pageOf(card) {
  return {
    ...card,
    overview: card.plot || null, ratings: { imdb: null, tmdb: null }, certificates: {}, countries: [], languages: [],
    companies: [], networks: [], keywords: [], cast: [], trailers: [], facts: NO_FACTS, franchises: [], seasons: [],
    crew: (card.madeBy || []).map((name) => ({ personId: null, name, job: card.kind === "show" ? "Creator" : "Director" })),
  };
}

/** A film the API would list, from a finished save of it. `art` holds its poster and backdrop. */
export function deviceFilm(save, art, progress) {
  const { meta } = save;
  const p = progress.get(save.titleId);
  return {
    id: save.titleId,
    title: meta.name,
    kind: "film",
    status: "ready",
    film: { name: meta.name, year: meta.year, imdbId: meta.imdbId },
    card: cardOf(meta, art),
    durationSeconds: meta.durationSeconds,
    addedAt: meta.completedAt,
    savedAt: meta.completedAt,
    progress: p ? { positionSeconds: p.positionSeconds, finished: false } : null,
  };
}

/** An episode the API would list, from a finished save of it, for Continue watching. */
export function deviceEpisode(save, art) {
  const { meta } = save;
  return {
    id: save.titleId,
    title: meta.name,
    kind: "show",
    status: "ready",
    film: { name: meta.show ? meta.show.name : meta.name, year: meta.year, imdbId: meta.imdbId },
    episode: meta.episode,
    card: cardOf(meta, { poster: art.poster, backdrop: art.still || art.backdrop }),
    durationSeconds: meta.durationSeconds,
  };
}

/** The kept show's two answers, when it is kept in their shape: `{ page, mine }`. */
const keptAnswers = (kept) => (kept && kept.show && kept.show.page && kept.show.mine ? kept.show : null);

/**
 * A show the API would list, from its finished episode saves and, when it is kept, what the API last
 * said of it. Where the person picks up is an episode on the device, and Watch starts the first one
 * that is.
 */
export function deviceShowSummary(imdbId, episodes, kept, poster, progress) {
  const answers = keptAnswers(kept);
  const order = [...episodes].sort((a, b) => (a.meta.episode.season - b.meta.episode.season) || (a.meta.episode.number - b.meta.episode.number));
  const resumed = episodes
    .filter((s) => progress.has(s.titleId))
    .sort((a, b) => Date.parse(progress.get(b.titleId).watchedAt || 0) - Date.parse(progress.get(a.titleId).watchedAt || 0))[0];
  const p = resumed && progress.get(resumed.titleId);
  const card = answers ? { ...answers.page, poster: poster || null, backdrop: null, you: null } : cardOf(episodes[0].meta, { poster });
  const seasons = [...new Set(order.map((s) => s.meta.episode.season))];
  return {
    imdbId,
    title: card.title,
    card,
    seasons: answers ? answers.mine.seasons.map((x) => x.number) : seasons,
    episodes: answers ? answers.mine.seasons.reduce((n, x) => n + x.episodes.length, 0) : episodes.length,
    seasonProgress: seasons.map((n) => ({ season: n, episodes: order.filter((s) => s.meta.episode.season === n).length, watched: 0 })),
    addedAt: episodes.map((s) => s.meta.completedAt).sort().pop(),
    downloaded: episodes.length,
    resume: p ? {
      titleId: resumed.titleId, season: resumed.meta.episode.season, episode: resumed.meta.episode.number,
      positionSeconds: p.positionSeconds, durationSeconds: resumed.meta.durationSeconds,
    } : null,
    firstTitleId: order[0].titleId,
  };
}

/**
 * A show with no kept answers, as much of its page and `GET /api/me/shows/{imdbId}` as its saved
 * episodes tell: its card, and a season for each with those episodes in it.
 */
export function showFromSaves(imdbId, saves) {
  const episodes = saves.filter((s) => s.meta.show && s.meta.show.imdbId === imdbId && s.meta.episode);
  if (episodes.length === 0) return null;
  const seasons = new Map();
  for (const s of episodes) {
    const e = s.meta.episode;
    if (!seasons.has(e.season)) seasons.set(e.season, { number: e.season, name: null, airDate: null, overview: null, poster: null, episodes: [] });
    seasons.get(e.season).episodes.push({
      season: e.season, number: e.number, name: e.name, overview: s.meta.overview, airDate: null, runtime: null,
      still: null, titleId: s.titleId, status: "ready", durationSeconds: s.meta.durationSeconds, progress: null, lastPlayed: null,
    });
  }
  return {
    page: pageOf(cardOf(episodes[0].meta, {})),
    mine: {
      imdbId, resume: null, followable: false, follow: null, nextEpisode: null,
      seasons: [...seasons.values()].sort((a, b) => a.number - b.number)
        .map((x) => ({ ...x, episodes: x.episodes.sort((a, b) => a.number - b.number) })),
    },
  };
}

/**
 * A kept show as the API answered it, `{ page, mine }`, drawn from the device: artwork by `art` (object
 * URLs by file name), nothing that leads to the catalogue, and where the person picks up only when that
 * episode is on the device. A show kept in no shape this reads is drawn from its saves.
 */
export function deviceShow(kept, art, onDevice, saves) {
  const answers = keptAnswers(kept);
  if (!answers) return showFromSaves(kept.imdbId, saves);
  const { page, mine } = answers;
  const urlOf = (name) => art.get(name) || null;
  return {
    page: {
      ...pageOf(page),
      ...page,
      poster: urlOf("poster"), backdrop: urlOf("backdrop"), logo: null, you: null,
      cast: page.cast.map((c) => ({ ...c, personId: null, portrait: null })),
      crew: page.crew.map((c) => ({ ...c, personId: null })),
      keywords: [], franchises: [], companies: [], networks: page.networks.map((n) => ({ ...n, id: null })),
      facts: NO_FACTS, similar: [], follows: [], followedBy: [],
    },
    mine: {
      ...mine,
      resume: mine.resume && onDevice.has(mine.resume.titleId) ? mine.resume : null,
      seasons: mine.seasons.map((x) => ({
        ...x,
        poster: null,
        episodes: x.episodes.map((e) => ({ ...e, still: urlOf(`s${e.season}e${e.number}`) })),
      })),
    },
  };
}
