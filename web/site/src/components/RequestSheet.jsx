import React from "react";
import { Icon, Modal, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { bytes } from "./FilmDetails.jsx";
import { downloadWords } from "../pages/Discover.jsx";

const QUALITIES = [
  { value: "720p", label: "720p", note: "Recommended: a third of the space" },
  { value: "1080p", label: "1080p", note: "The sharpest the server keeps" },
];

/**
 * Downloading a show: which seasons and episodes, and at which quality, with what that costs on disk.
 *
 * What can be picked is what the tracker offers: an episode is pickable when a pack of its season or a
 * release of its own is on offer. A season's pack is preferred, fetching only the picked files out of
 * it; an episode with no pack at the quality asked comes on its own. 720p is the default for a show.
 */
export function RequestSheet({ show, onClose, onStarted }) {
  const offers = useLoad(() => api.showReleases(show.imdbId), [show.imdbId]);
  const [picks, setPicks] = React.useState(null);
  const [open, setOpen] = React.useState(() => new Set());
  const [quality, setQuality] = React.useState("720p");
  const [busy, setBusy] = React.useState(false);

  const offered = (season, episode) => {
    const so = offers.data && offers.data.seasons.find((x) => x.season === season);
    return !!so && (so.packs.length > 0 || so.episodes.some((e) => e.episode === episode));
  };
  const seasons = show.seasons.map((x) => ({
    ...x,
    pickable: x.episodes.filter((e) => !e.titleId && offered(x.number, e.number)).map((e) => e.number),
  }));

  // Everything not on the server and on offer, until somebody changes it.
  const chosen = picks || Object.fromEntries(seasons.map((x) => [x.number, new Set(x.pickable)]));
  const toggle = (season, episodes, on) => {
    const next = Object.fromEntries(Object.entries(chosen).map(([k, v]) => [k, new Set(v)]));
    const set = next[season] || (next[season] = new Set());
    for (const e of episodes) (on ? set.add(e) : set.delete(e));
    setPicks(next);
  };

  const plans = Object.fromEntries(QUALITIES.map((q) => [q.value, plan(offers.data, chosen, q.value)]));
  const current = plans[quality];
  const pickedSeasons = Object.values(chosen).filter((v) => v.size > 0).length;
  const pickedEpisodes = Object.values(chosen).reduce((n, v) => n + v.size, 0);

  const submit = async () => {
    if (current.torrents.length === 0) return;
    setBusy(true);
    let started = 0;
    try {
      for (const t of current.torrents) {
        const reply = await api.download(t.offer.torrentId, t.episodes);
        if (reply.outcome === "started") started++;
        else toast.error(downloadWords(reply));
      }
      if (started > 0) {
        toast.success(`Downloading ${pickedEpisodes} ${pickedEpisodes === 1 ? "episode" : "episodes"} of ${show.title}. The first is watchable as soon as enough of it has arrived.`);
        onStarted();
      }
    } catch (error) {
      toast.fromError(error, "The download did not start");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal onClose={onClose} canClose={!busy} scrimClassName="kc-sheet-scrim">
      <div className="kc-sheet" role="dialog" aria-label={`Download ${show.title}`}>
        <div className="kc-sheet__head">
          <span className="kc-sheet__poster">{show.posterUrl && <img src={show.posterUrl} alt="" />}</span>
          <div className="kc-sheet__title">
            <span className="kc-eyebrow kc-eyebrow--muted">Download</span>
            <span className="kc-sheet__name">{show.title} {show.firstYear && <span className="kc-quiet">{show.firstYear}</span>}</span>
            {show.creators.length > 0 && <span className="kc-quiet">Created by {show.creators.join(", ")}</span>}
          </div>
          <button type="button" className="kc-sheet__close" aria-label="Close" onClick={onClose}><Icon name="x" size={18} /></button>
        </div>

        <div className="kc-sheet__body">
          {offers.loading && !offers.data && <span className="kc-quiet">Asking the tracker…</span>}
          {offers.error && <span className="kc-quiet">{offers.error.message}</span>}
          {offers.data && offers.data.seasons.length === 0 && (
            <span className="kc-quiet">{offers.data.emptyExplanation || "The tracker has nothing of this show."}</span>
          )}

          {offers.data && offers.data.seasons.length > 0 && (
            <div className="kc-sheet__group">
              <span className="kc-sheet__label">Seasons and episodes</span>
              <div className="kc-picks">
                {seasons.filter((x) => x.pickable.length > 0).map((x) => {
                  const set = chosen[x.number] || new Set();
                  const all = set.size === x.pickable.length;
                  const some = set.size > 0 && !all;
                  const expanded = open.has(x.number);
                  return (
                    <div key={x.number} className="kc-picks__season">
                      <div className="kc-picks__row kc-picks__row--season">
                        <Check on={all} partial={some} onClick={() => toggle(x.number, x.pickable, !all)} />
                        <button type="button" className="kc-picks__name" onClick={() => toggle(x.number, x.pickable, !all)}>Season {x.number}</button>
                        <span className="kc-quiet">
                          {all ? `All ${x.pickable.length} episodes` : some ? `${set.size} of ${x.pickable.length} episodes` : `${x.pickable.length} episodes`}
                        </span>
                        <button type="button" className="kc-picks__expand"
                          onClick={() => setOpen((o) => { const n = new Set(o); if (n.has(x.number)) n.delete(x.number); else n.add(x.number); return n; })}>
                          {expanded ? "Hide episodes" : "Pick episodes"}<Icon name={expanded ? "chevron-up" : "chevron-down"} size={14} />
                        </button>
                      </div>
                      {expanded && x.episodes.filter((e) => x.pickable.includes(e.number)).map((e) => (
                        <button key={e.number} type="button" className="kc-picks__row kc-picks__row--episode"
                          onClick={() => toggle(x.number, [e.number], !set.has(e.number))}>
                          <Check on={set.has(e.number)} />
                          <span className="kc-picks__still">{e.stillUrl && <img src={e.stillUrl} alt="" loading="lazy" />}</span>
                          <span className="kc-picks__text">
                            <span className="kc-picks__ep"><span className="kc-mono kc-quiet">E{e.number}</span> {e.name || `Episode ${e.number}`}</span>
                            <span className="kc-picks__meta kc-mono">
                              {[e.runtimeMinutes ? `${e.runtimeMinutes}m` : null, e.airDate ? `aired ${e.airDate}` : null, e.rating ? `★ ${e.rating.toFixed(1)}` : null].filter(Boolean).join(" · ")}
                            </span>
                          </span>
                        </button>
                      ))}
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          <div className="kc-sheet__group">
            <span className="kc-sheet__label">Quality</span>
            <div className="kc-qualities">
              {QUALITIES.map((q) => (
                <button key={q.value} type="button" className={"kc-quality" + (quality === q.value ? " is-on" : "")} onClick={() => setQuality(q.value)}>
                  <span className="kc-quality__dot" />
                  <span className="kc-quality__text">
                    <span className="kc-quality__label">{q.label}</span>
                    <span className="kc-mono kc-quiet">{plans[q.value].bytes ? `~${bytes(plans[q.value].bytes)}` : "—"}</span>
                    <span className="kc-quiet">{q.note}</span>
                  </span>
                </button>
              ))}
            </div>
          </div>

          {current.otherQuality > 0 && (
            <span className="kc-quiet">
              {current.otherQuality} {current.otherQuality === 1 ? "episode is" : "episodes are"} not offered at {quality} and {current.otherQuality === 1 ? "comes" : "come"} at the quality the tracker has.
            </span>
          )}
          <span className="kc-quiet">The first episode can be watched while the rest arrive. Each appears on the show's page as soon as enough of it is here.</span>
        </div>

        <div className="kc-sheet__foot">
          <span className="kc-mono kc-sheet__summary">
            {pickedEpisodes
              ? `${pickedSeasons} ${pickedSeasons === 1 ? "season" : "seasons"} · ${pickedEpisodes} ${pickedEpisodes === 1 ? "episode" : "episodes"}${current.bytes ? ` · ~${bytes(current.bytes)}` : ""}`
              : "Nothing picked"}
          </span>
          <button type="button" className="kc-btn" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="button" className="kc-btn kc-btn--primary" disabled={busy || current.torrents.length === 0} onClick={submit}>
            <Icon name="download" size={16} />Download
          </button>
        </div>
      </div>
    </Modal>
  );
}

const FILM_QUALITIES = [
  { value: "1080p", label: "1080p", note: "The sharpest the server keeps", key: "hd1080" },
  { value: "720p", label: "720p", note: "Smaller, and plenty on a phone", key: "hd720" },
];

/**
 * Downloading a film: 1080p or 720p, the best release the tracker offers at each, with what it costs on
 * disk. 1080p is the default where it is offered; a quality nothing is offered at cannot be picked.
 */
export function FilmRequestSheet({ film, availability, onClose, onStarted }) {
  const offered = FILM_QUALITIES.filter((q) => availability[q.key]);
  const [quality, setQuality] = React.useState(offered.length ? offered[0].value : null);
  const [busy, setBusy] = React.useState(false);
  const chosen = quality && availability[FILM_QUALITIES.find((q) => q.value === quality).key];
  const name = film.year ? `${film.title} (${film.year})` : film.title;

  const submit = async () => {
    if (!chosen) return;
    setBusy(true);
    try {
      const reply = await api.download(chosen.torrentId);
      if (reply.outcome === "started") {
        toast.success(`Downloading ${name} at ${quality}. You'll get a push when it's ready.`);
        onStarted();
      } else {
        toast.error(downloadWords(reply));
      }
    } catch (error) {
      toast.fromError(error, "The download did not start");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal onClose={onClose} canClose={!busy} scrimClassName="kc-sheet-scrim">
      <div className="kc-sheet" role="dialog" aria-label={`Download ${name}`}>
        <div className="kc-sheet__head">
          <span className="kc-sheet__poster">{film.posterUrl && <img src={film.posterUrl} alt="" />}</span>
          <div className="kc-sheet__title">
            <span className="kc-eyebrow kc-eyebrow--muted">Download</span>
            <span className="kc-sheet__name">{film.title} {film.year && <span className="kc-quiet">{film.year}</span>}</span>
            {film.directors.length > 0 && <span className="kc-quiet">Directed by {film.directors.join(", ")}</span>}
          </div>
          <button type="button" className="kc-sheet__close" aria-label="Close" onClick={onClose}><Icon name="x" size={18} /></button>
        </div>

        <div className="kc-sheet__body">
          <div className="kc-sheet__group">
            <span className="kc-sheet__label">Quality</span>
            <div className="kc-qualities">
              {FILM_QUALITIES.map((q) => {
                const offer = availability[q.key];
                return (
                  <button key={q.value} type="button" disabled={!offer}
                    className={"kc-quality" + (quality === q.value ? " is-on" : "")} onClick={() => setQuality(q.value)}>
                    <span className="kc-quality__dot" />
                    <span className="kc-quality__text">
                      <span className="kc-quality__label">{q.label}</span>
                      <span className="kc-mono kc-quiet">{offer ? `~${bytes(offer.sizeBytes)}` : "not offered"}</span>
                      <span className="kc-quiet">{q.note}</span>
                    </span>
                  </button>
                );
              })}
            </div>
          </div>
          <span className="kc-quiet">Downloads usually finish in 10–40 minutes. You'll get a push when it's ready to watch, and it shows up under My downloads.</span>
        </div>

        <div className="kc-sheet__foot">
          <span className="kc-mono kc-sheet__summary">{chosen ? `${quality} · ~${bytes(chosen.sizeBytes)}` : "Nothing offered"}</span>
          <button type="button" className="kc-btn" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="button" className="kc-btn kc-btn--primary" disabled={busy || !chosen} onClick={submit}>
            <Icon name="download" size={16} />Download
          </button>
        </div>
      </div>
    </Modal>
  );
}

function Check({ on, partial, onClick }) {
  const box = <span className={"kc-check" + (on || partial ? " is-on" : "")}>{on ? <Icon name="check" size={13} /> : partial ? <Icon name="minus" size={13} /> : null}</span>;
  return onClick ? <button type="button" className="kc-check__hit" onClick={onClick}>{box}</button> : box;
}

/**
 * Which releases fetch the picked episodes at one quality, and roughly what they take on disk. A
 * season's pack at that quality is preferred, fetching only the picked files; an episode with none
 * comes on its own; and an episode offered only at another quality comes at that one, counted so
 * the sheet can say so. One torrent named for several seasons is one download.
 */
export function plan(offers, chosen, quality) {
  const byTorrent = new Map();
  let total = 0;
  let otherQuality = 0;
  const add = (offer, season, episode, wholeRelease) => {
    const entry = byTorrent.get(offer.torrentId) || { offer, episodes: wholeRelease ? null : [] };
    if (entry.episodes) entry.episodes.push({ season, episode });
    byTorrent.set(offer.torrentId, entry);
  };

  for (const [key, set] of Object.entries(chosen || {})) {
    const season = Number(key);
    const so = offers && offers.seasons.find((x) => x.season === season);
    if (!so || set.size === 0) continue;
    const pack = so.packs.find((p) => p.resolution === quality);
    for (const episode of set) {
      if (pack) {
        add(pack, season, episode, false);
        total += pack.sizePerEpisode || 0;
        continue;
      }
      const own = so.episodes.find((x) => x.episode === episode);
      const single = own && own.releases.find((r) => r.resolution === quality);
      if (single) {
        add(single, season, episode, true);
        total += single.sizeBytes;
        continue;
      }
      const fallback = (own && own.releases[0]) || so.packs[0];
      if (!fallback) continue;
      otherQuality++;
      if (fallback === so.packs[0] && !(own && own.releases[0])) {
        add(fallback, season, episode, false);
        total += fallback.sizePerEpisode || 0;
      } else {
        add(fallback, season, episode, true);
        total += fallback.sizeBytes;
      }
    }
  }

  return { torrents: [...byTorrent.values()], bytes: total, otherQuality };
}
