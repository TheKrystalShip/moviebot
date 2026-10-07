// The saves on this device, for a page: read when it mounts and again whenever they change, here or
// in another tab.

import React from "react";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { useSession } from "../session.js";
import { artworkUrl, listSaves, listShows, onSavesChanged, readSave, readShow, showFile, space } from "./store.js";
import { deviceEpisode, deviceFilm, deviceShow, deviceShowSummary, showFromSaves } from "./library.js";
import { progressOnDevice } from "./progress.js";

/** Reads `load` now and on every change to the saves: `{ data, error, loading }`. */
function useStoreRead(load, deps) {
  const [state, setState] = React.useState({ data: null, error: null, loading: true });
  const [tick, setTick] = React.useState(0);

  React.useEffect(() => onSavesChanged(() => setTick((t) => t + 1)), []);
  React.useEffect(() => {
    let live = true;
    load().then(
      (data) => { if (live) setState({ data, error: null, loading: false }); },
      (error) => { if (live) setState((prev) => ({ data: prev.data, error, loading: false })); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);

  return state;
}

/** The signed-in account's saves on this device, finished and under way. */
export function useSaves() {
  const session = useSession();
  const sub = session && session.user.id;
  return useStoreRead(() => listSaves(sub), [sub]);
}

/** One title's save for the signed-in account, or null when this device holds none or no title is named. */
export function useSave(titleId) {
  const session = useSession();
  const sub = session && session.user.id;
  return useStoreRead(() => (titleId ? readSave(sub, titleId) : Promise.resolve(null)), [sub, titleId]);
}

/**
 * Object URLs made by `make` for as long as the caller holds what they were made from: a new set
 * replaces the last once it is made, so artwork never blinks out while it is made again. `make`
 * answers `{ urls, value }`, `urls` every object URL in `value`.
 */
function useObjectUrls(source, make) {
  const [made, setMade] = React.useState(null);
  const held = React.useRef([]);
  React.useEffect(() => {
    if (!source) return undefined;
    let live = true;
    make(source).then(({ urls, value }) => {
      if (!live) {
        urls.forEach((u) => URL.revokeObjectURL(u));
        return;
      }
      const before = held.current;
      held.current = urls;
      setMade({ source, value });
      before.forEach((u) => URL.revokeObjectURL(u));
    }, () => {});
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [source]);
  React.useEffect(() => () => held.current.forEach((u) => URL.revokeObjectURL(u)), []);
  return made;
}

async function artworkOf(saves, sub, kept) {
  const urls = [];
  const keep = (url) => {
    if (url) urls.push(url);
    return url;
  };
  const films = new Map();
  for (const save of saves) {
    const art = {};
    for (const role of ["poster", "backdrop", "still"]) art[role] = keep(await artworkUrl(save, role).catch(() => null));
    films.set(save.titleId, art);
  }
  const shows = new Map();
  for (const show of kept) {
    const art = new Map();
    for (const entry of show.files.filter((f) => !/^s\d+e\d+$/.test(f.name))) {
      const blob = await showFile(sub, show.imdbId, entry).catch(() => null);
      if (blob) art.set(entry.name, keep(URL.createObjectURL(blob)));
    }
    shows.set(show.imdbId, art);
  }
  return { urls, value: { films, shows } };
}

/**
 * The signed-in account's library as the device holds it, in the API's shapes (`library.js`): the
 * films and the shows with a finished save, and what is under way in them, the newest watched first.
 * `enabled` false reads nothing.
 */
export function useDeviceLibrary(enabled = true) {
  const session = useSession();
  const sub = enabled && session ? session.user.id : null;
  const read = useStoreRead(async () => {
    if (!sub) return null;
    const [saves, shows] = await Promise.all([listSaves(sub), listShows(sub)]);
    return { sub, saves: saves.filter((s) => s.complete), shows };
  }, [sub]);
  const made = useObjectUrls(read.data, (data) => artworkOf(data.saves, data.sub, data.shows));
  if (!sub) return { data: null, loading: false };
  if (!made || made.source !== read.data) return { data: null, loading: true };

  const { saves, shows: kept } = read.data;
  const { films: art, shows: showArt } = made.value;
  const byId = new Map(saves.map((s) => [s.titleId, s]));
  const progress = progressOnDevice(sub, (id) => (byId.has(id) ? byId.get(id).meta.durationSeconds : null));
  const films = saves.filter((s) => s.meta.kind !== "episode").map((s) => deviceFilm(s, art.get(s.titleId), progress));
  const episodes = saves.filter((s) => s.meta.kind === "episode" && s.meta.show && s.meta.show.imdbId);
  const keptById = new Map(kept.map((k) => [k.imdbId, k]));
  const grouped = new Map();
  for (const s of episodes) grouped.set(s.meta.show.imdbId, [...(grouped.get(s.meta.show.imdbId) || []), s]);
  const shows = [...grouped].map(([imdbId, list]) => {
    const own = showArt.get(imdbId);
    const poster = (own && own.get("poster")) || art.get(list[0].titleId).poster;
    return deviceShowSummary(imdbId, list, keptById.get(imdbId), poster, progress);
  });
  const titles = new Map([
    ...films.map((f) => [f.id, f]),
    ...episodes.map((s) => [s.titleId, deviceEpisode(s, art.get(s.titleId))]),
  ]);
  const continuing = [...progress]
    .filter(([id]) => titles.has(id))
    .sort((a, b) => Date.parse(b[1].watchedAt || 0) - Date.parse(a[1].watchedAt || 0))
    .map(([titleId, p]) => {
      const duration = titles.get(titleId).durationSeconds || 0;
      return {
        titleId, positionSeconds: p.positionSeconds, durationSeconds: duration,
        remainingSeconds: Math.max(0, duration - p.positionSeconds), lastWatchedAt: p.watchedAt,
      };
    });
  return { data: { films, shows, titles, continuing }, loading: false };
}

/**
 * A show's page as the device holds it, in the shape `GET /api/shows/{imdbId}` answers (`deviceShow`):
 * from the kept show, else from its saved episodes alone; null when the device has neither.
 */
export function useKeptShow(imdbId, enabled = true) {
  const session = useSession();
  const sub = enabled && session ? session.user.id : null;
  const read = useStoreRead(async () => {
    if (!sub) return null;
    const [kept, saves] = await Promise.all([readShow(sub, imdbId), listSaves(sub)]);
    return { sub, kept, saves };
  }, [sub, imdbId]);
  const made = useObjectUrls(read.data, async ({ sub: account, kept }) => {
    const urls = [];
    const art = new Map();
    for (const entry of kept ? kept.files : []) {
      const blob = await showFile(account, imdbId, entry).catch(() => null);
      if (blob) {
        const url = URL.createObjectURL(blob);
        urls.push(url);
        art.set(entry.name, url);
      }
    }
    return { urls, value: art };
  });
  if (!sub) return { data: null, error: null, loading: false };
  if (!made || made.source !== read.data) return { data: null, error: null, loading: true };
  const { kept, saves } = read.data;
  const onDevice = new Set(saves.filter((s) => s.complete).map((s) => s.titleId));
  const show = kept ? deviceShow(kept, made.value, onDevice) : showFromSaves(imdbId, saves);
  return { data: show, error: null, loading: false };
}

/**
 * A title's page as its finished download on this device draws it, in the shapes the API answers:
 * `manifest` as `GET /api/titles/{id}`, `details` as `GET /api/films/{imdbId}` and `summary` as its
 * entry in `GET /api/titles`, each `{ data, error, loading }`. `manifest.data` is null when the title
 * is not downloaded here.
 */
export function useDeviceTitle(titleId, enabled = true) {
  const session = useSession();
  const sub = enabled && session ? session.user.id : null;
  const save = useStoreRead(() => (sub ? readSave(sub, titleId) : Promise.resolve(null)), [sub, titleId]);
  const done = save.data && save.data.complete ? save.data : null;
  const poster = useArtwork(done, "poster");
  const backdrop = useArtwork(done, "backdrop");
  const shape = (data) => ({ data, error: null, loading: save.loading, reload: () => {} });
  if (!done) return { manifest: shape(null), details: shape(null), summary: null };
  const progress = progressOnDevice(sub, () => done.meta.durationSeconds);
  const film = deviceFilm(done, { poster, backdrop }, progress);
  const { meta } = done;
  const manifest = {
    ...film, poster: null, episode: meta.episode,
    film: { ...film.film, imdbId: meta.imdbId },
    video: meta.quality && meta.quality.width ? { width: meta.quality.width, height: meta.quality.height } : null,
  };
  return { manifest: shape(manifest), details: shape(film.details), summary: { progress: film.progress, addedAt: null } };
}

/**
 * The library's films (`film`, as `GET /api/titles` lists them) or shows (`show`, as `GET /api/shows`
 * does), `useLoad`'s way: the API's, or with `fromDevice` the ones this device holds.
 */
export function useLibrary(kind, fromDevice) {
  const listed = useLoad(() => (fromDevice ? Promise.resolve(null) : kind === "show" ? api.shows() : api.titles()), [kind, fromDevice]);
  const device = useDeviceLibrary(fromDevice);
  if (!fromDevice) return listed;
  const data = device.data ? (kind === "show" ? device.data.shows : device.data.films) : null;
  return { data, error: null, loading: device.loading, reload: listed.reload };
}

/** What is left for saves on this device: `{ usage, quota, free }`, or null. */
export function useSpace() {
  return useStoreRead(() => space(), []);
}

/** A save's artwork, `poster` or `backdrop`, as an object URL for as long as the caller is drawn. */
export function useArtwork(save, role) {
  const [url, setUrl] = React.useState(null);
  const key = save ? save.meta.account + "/" + save.titleId + "/" + role : null;
  React.useEffect(() => {
    if (!save) return undefined;
    let live = true;
    let made = null;
    artworkUrl(save, role).then((u) => {
      made = u;
      if (live) setUrl(u);
      else if (u) URL.revokeObjectURL(u);
    }, () => {});
    return () => {
      live = false;
      if (made) URL.revokeObjectURL(made);
      setUrl(null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  return url;
}
