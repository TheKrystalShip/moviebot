// What is on the device, in the shapes the API answers the library in, so a page drawn with no
// connection is the same page drawing less: a finished save of a film is a title as `GET /api/titles`
// lists it, a show with a finished save of an episode is a show as `GET /api/shows` lists it, and a
// kept show is `GET /api/shows/{imdbId}` with every episode not on the device unplayable. Artwork is
// the device's own, by the object URLs the caller passes in.

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
    details: {
      title: meta.name, overview: meta.overview, genres: meta.genres || [], rating: meta.rating ?? null,
      posterUrl: art.poster || null, backdropUrl: art.backdrop || null,
      directors: [], writers: [], cast: [], languages: [], countries: [],
    },
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
    details: { genres: meta.genres || [], posterUrl: art.poster || null, backdropUrl: art.still || art.backdrop || null },
    durationSeconds: meta.durationSeconds,
  };
}

/**
 * A show the API would list, from its finished episode saves and, when it is kept, what the API last
 * said of it. Where the person picks up is an episode on the device, and Watch starts the first one
 * that is.
 */
export function deviceShowSummary(imdbId, episodes, kept, poster, progress) {
  const show = kept && kept.show;
  const meta = episodes[0].meta;
  const order = [...episodes].sort((a, b) => (a.meta.episode.season - b.meta.episode.season) || (a.meta.episode.number - b.meta.episode.number));
  const resumed = episodes
    .filter((s) => progress.has(s.titleId))
    .sort((a, b) => Date.parse(progress.get(b.titleId).watchedAt || 0) - Date.parse(progress.get(a.titleId).watchedAt || 0))[0];
  const p = resumed && progress.get(resumed.titleId);
  return {
    imdbId,
    title: show ? show.title : meta.show.name,
    firstYear: show ? show.firstYear : meta.year,
    lastYear: show ? show.lastYear : null,
    status: show ? show.status : null,
    details: {
      genres: show ? show.genres : meta.genres || [],
      rating: show ? show.rating : meta.rating ?? null,
      posterUrl: poster || null,
    },
    seasons: show ? show.seasons.map((x) => x.number) : [...new Set(order.map((s) => s.meta.episode.season))],
    episodes: show ? show.seasons.reduce((n, x) => n + x.episodes.length, 0) : episodes.length,
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
 * A show with no kept answer, as much of `GET /api/shows/{imdbId}` as its saved episodes tell: its
 * name, and a season for each with those episodes in it.
 */
export function showFromSaves(imdbId, saves) {
  const episodes = saves.filter((s) => s.meta.show && s.meta.show.imdbId === imdbId && s.meta.episode);
  if (episodes.length === 0) return null;
  const meta = episodes[0].meta;
  const seasons = new Map();
  for (const s of episodes) {
    const e = s.meta.episode;
    if (!seasons.has(e.season)) seasons.set(e.season, { number: e.season, name: null, airDate: null, overview: null, episodes: [] });
    seasons.get(e.season).episodes.push({
      season: e.season, number: e.number, name: e.name, overview: s.meta.overview, airDate: null, runtimeMinutes: null,
      rating: null, votes: 0, stillUrl: null, titleId: s.titleId, status: "ready", durationSeconds: s.meta.durationSeconds,
      progress: null, lastPlayed: null,
    });
  }
  return {
    imdbId, title: meta.show.name, firstYear: meta.year, lastYear: null, status: null, inProduction: false, certification: null,
    genres: meta.genres || [], overview: null, rating: null, votes: 0, creators: [], personIds: {}, cast: [], languages: [],
    countries: [], networks: [], posterUrl: null, backdropUrl: null, resume: null, followable: false, follow: null, nextEpisode: null,
    seasons: [...seasons.values()].sort((a, b) => a.number - b.number)
      .map((x) => ({ ...x, episodes: x.episodes.sort((a, b) => a.number - b.number) })),
  };
}

/**
 * A kept show as `GET /api/shows/{imdbId}` answered it, drawn from the device: artwork by `art` (object
 * URLs by file name), nothing that leads to the catalogue, and where the person picks up only when that
 * episode is on the device.
 */
export function deviceShow(kept, art, onDevice) {
  const s = kept.show;
  const urlOf = (name) => art.get(name) || null;
  const resume = s.resume && onDevice.has(s.resume.titleId) ? s.resume : null;
  return {
    ...s,
    posterUrl: urlOf("poster"),
    backdropUrl: urlOf("backdrop"),
    personIds: {},
    cast: (s.cast || []).map((c) => ({ ...c, personId: null, portraitUrl: null })),
    resume,
    seasons: s.seasons.map((x) => ({
      ...x,
      episodes: x.episodes.map((e) => ({ ...e, stillUrl: urlOf(`s${e.season}e${e.number}`) })),
    })),
  };
}
