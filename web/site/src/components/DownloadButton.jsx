import React from "react";
import { Button, Dialog, Drawer, Icon, IconButton, Meter, Notice, OptionList, Select, toast, usePhone } from "@thekrystalship/krystal";
import { api } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { languageName, languageOptions } from "../languages.js";
import { keptPreferences } from "../offline/preferences.js";
import { defaultAudio, defaultRung, defaultSubtitles, evictedUnvisited, handheld, sameLanguage } from "../offline/choose.js";
import { useSave, useSaves, useSpace } from "../offline/hooks.js";
import { askToNotify } from "../offline/notice.js";
import { NoRoom, pauseSave, percentOf, removeFromDevice, restingState, resumeSave, startSave, useSaving, useSavingAll } from "../offline/saver.js";
import { offlineSupported } from "../offline/store.js";
import { askDownload, letGo, stageWords, useRefused, useWants, wantOf } from "../offline/wants.js";
import { episodeRelease } from "../watchNow.js";
import { size } from "../pages/Offline.jsx";
import { playSavedPath } from "../paths.js";
import { linkProps } from "../router.js";
import { useCan, useSession } from "../session.js";
import { ago } from "../time.js";
import { onTv } from "../tv/tv.js";
import "./download.css";

// Download on a film's page, on an episode's row and on a season: one control whose face says where
// the download stands (not downloaded, preparing on the server, under way, downloaded) and which opens
// the one sheet for all of it: the choice of quality, audio and subtitles before it, its progress with
// Pause and Resume while under way, and Play and Remove once it is here. The sheet is the drawer on a
// phone and a dialog on a wide screen. "Save" is the lists' word; this one is always Download.
//
// A title the library does not hold whole yet (one the tracker offers, or one still being made) has
// no tracks to pick from, so it is asked for by what is known before the server holds it: a quality
// with its estimated size ("~1.4GB"), an audio language and a subtitle language. The server fetches
// and prepares it, preferring a release carrying that audio, and this device downloads it as it is
// made, with the tracks in those languages where the title has them (`offline/wants.js`). Until the
// device's download begins the control says the title is being prepared.

let playable = null;

/** Whether hls.js plays here, which is how a downloaded film is played: asked once, from its own chunk. */
function hlsPlays() {
  playable ??= import("hls.js").then((m) => m.default.isSupported(), () => false);
  return playable;
}

/**
 * Whether this page offers Download: to somebody who holds `cinema:offline`, on a phone or a tablet
 * (`handheld`) whose browser has the Origin Private File System and plays through hls.js, never on a
 * computer or a television. Somebody new to the site takes Download beside Watch for the way to watch
 * a film, and a phone or tablet carried somewhere without a connection is what downloading is for.
 */
export function useOffersSaving() {
  const may = useCan("cinema:offline") && !onTv && handheld() && offlineSupported();
  const [plays, setPlays] = React.useState(false);
  React.useEffect(() => {
    if (!may) return undefined;
    let live = true;
    hlsPlays().then((ok) => { if (live) setPlays(ok); });
    return () => { live = false; };
  }, [may]);
  return may && plays;
}

const STATES = {
  queued: { icon: "clock", label: "Queued" },
  saving: { icon: "hard-drive-download", label: "Downloading" },
  paused: { icon: "pause", label: "Paused" },
  waiting: { icon: "wifi-off", label: "Waiting for a connection" },
  elsewhere: { icon: "hard-drive-download", label: "Downloading in another tab" },
  failed: { icon: "triangle-alert", label: "Not downloaded" },
  preparing: { icon: "hourglass", label: "Preparing" },
};

/**
 * Where a title's download stands on this device: `{ kind: "none" }`, `{ kind: "done", save }`,
 * `{ kind: "underway", state, received, bytes, message, save }`, from the store and the saver together,
 * or, with nothing of it here, `{ kind: "preparing", want, failed, percent, message }` while the
 * server prepares what this device asked for (`imdbId`, and `season` and `number` for an episode).
 */
function useStanding(titleId, imdbId = null, season = null, number = null) {
  const save = useSave(titleId);
  const live = useSaving(titleId);
  const online = useOnline();
  const wants = useWants();
  const refused = useRefused();
  const standing = standingOf(titleId, save.data, live, online, save.loading);
  if (standing.kind !== "none") return standing;
  const want = wantOf(wants, imdbId, season, number);
  return want ? wantStanding(want, refused) : standing;
}

/** A want as the controls draw it. */
function wantStanding(want, refused) {
  const refusal = refused[want.id];
  if (want.stage === "failed" || refusal) {
    return { kind: "preparing", want, failed: true, percent: null, message: problemWords(refusal || want) };
  }
  const percent = want.stage === "downloading" && want.progress != null ? Math.floor(want.progress * 100) : null;
  return { kind: "preparing", want, failed: false, percent, message: null };
}

function problemWords(p) {
  switch (p.problem) {
    case "notOffered": return "The tracker has nothing to download it from.";
    case "refused": return p.refusal || "The server turned the download down.";
    case "notWatchable": return "It could not be prepared.";
    case "noRoom": return p.message || "There is not enough room on this device.";
    default: return p.message || "The server could not fetch it.";
  }
}

function standingOf(titleId, held, live, online, loading = false) {
  if (live) {
    return {
      kind: "underway", state: live.state || restingState(titleId, online), message: live.message || null, save: held,
      received: live.received != null ? live.received : held ? held.receivedBytes : 0,
      bytes: live.bytes || (held ? held.meta.bytes : 0),
    };
  }
  if (held && held.complete) return { kind: "done", save: held };
  if (held) return { kind: "underway", state: restingState(titleId, online), save: held, received: held.receivedBytes, bytes: held.meta.bytes };
  return { kind: loading ? "loading" : "none" };
}

/** How far a download under way has got. */
const percent = (s) => percentOf(s.save && s.save.meta, s.received, s.bytes);

/**
 * Every download of the signed-in account on this device, by title id: `{ kind, state, percent, save }`,
 * `kind` being `done` or `underway` and `state` the saver's while under way. For a shelf, which marks
 * each card from one read.
 */
export function useDownloads() {
  const saves = useSaves();
  const live = useSavingAll();
  const online = useOnline();
  return React.useMemo(() => {
    const all = new Map();
    for (const save of saves.data || []) {
      const s = standingOf(save.titleId, save, live[save.titleId], online);
      all.set(save.titleId, { kind: s.kind, state: s.state || null, percent: s.kind === "underway" ? percent(s) : 100, save });
    }
    return all;
  }, [saves.data, live, online]);
}

/**
 * What a shelf marks its cards with: `film(titleId)` is a film card's `download`, `episodes(imdbId)`
 * a show card's `downloaded`, the number of its episodes downloaded, and `want(imdbId, season,
 * number)` what the server is preparing for this device of a film or an episode with nothing here yet.
 */
export function useDownloadMarks() {
  const downloads = useDownloads();
  const wants = useWants();
  const refused = useRefused();
  return React.useMemo(() => {
    const shows = new Map();
    for (const d of downloads.values()) {
      const imdbId = d.kind === "done" && d.save.meta.show && d.save.meta.show.imdbId;
      if (imdbId) shows.set(imdbId, (shows.get(imdbId) || 0) + 1);
    }
    return {
      film: (titleId) => downloads.get(titleId),
      episodes: (imdbId) => shows.get(imdbId) || 0,
      want: (imdbId, season = null, number = null) => {
        const want = wantOf(wants, imdbId, season, number);
        return want ? wantStanding(want, refused) : null;
      },
    };
  }, [downloads, wants, refused]);
}

/**
 * A row's line for a download from `useDownloadMarks`: "Downloaded · 480p", or what it is doing and
 * how far it has got ("Downloading · 34%", "Preparing · 12%"); null for none.
 */
export function downloadLine(download) {
  if (!download) return null;
  if (download.kind === "preparing") {
    if (download.failed) return STATES.failed.label;
    return download.percent != null ? `${STATES.preparing.label} · ${download.percent}%` : STATES.preparing.label;
  }
  if (download.kind === "done") {
    const quality = download.save.meta.quality && download.save.meta.quality.label;
    return quality ? `Downloaded · ${quality}` : "Downloaded";
  }
  const state = STATES[download.state] || STATES.queued;
  return download.state === "failed" ? state.label : `${state.label} · ${download.percent}%`;
}

/** The face of the control: its icon and words. */
function face(standing) {
  if (standing.kind === "preparing") return { icon: standing.failed ? STATES.failed.icon : STATES.preparing.icon, label: downloadLine(standing) };
  if (standing.kind === "done") return { icon: "circle-check", label: "Downloaded" };
  if (standing.kind === "underway") {
    const state = STATES[standing.state] || STATES.queued;
    return { icon: state.icon, label: standing.state === "failed" ? state.label : `${state.label} · ${percent(standing)}%` };
  }
  return { icon: "hard-drive-download", label: "Download" };
}

/**
 * Download on a film's page. `head` is what the sheet is headed with: `{ name, context, runtime,
 * still, backdrop }`, `context` the line under the name and `still` the image beside it. `titleId` is
 * the library's title, absent for a film it does not hold; `ready` whether the title is whole, which
 * is what lets its quality, audio and subtitles be picked; `imdbId` the film, by which one not whole is
 * asked for.
 */
export function DownloadButton({ titleId = null, imdbId = null, ready = false, head }) {
  const standing = useStanding(titleId, imdbId);
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  if (standing.kind === "loading") return null;
  const { icon, label } = face(standing);
  const target = { titleId, imdbId, ready, episodes: null };
  return (
    <>
      <Button variant={standing.kind !== "none" ? "accent" : "secondary"} size="xl" icon={icon}
        disabled={standing.kind === "none" && !online} onClick={() => setOpen(true)}>
        {label}
      </Button>
      {open && <DownloadSheet target={target} head={head} standing={standing} onClose={() => setOpen(false)} />}
    </>
  );
}

/**
 * Download on an episode's row: a square button at the row's end, a ring while under way. `imdbId` is
 * the show's, and `season` and `number` the episode's, by which one not whole is asked for.
 */
export function DownloadMark({ titleId = null, imdbId = null, season = null, number = null, ready = false, head }) {
  const standing = useStanding(titleId, imdbId, season, number);
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  if (standing.kind === "loading") return <span className="kc-episode-save" />;
  const { icon, label } = face(standing);
  const target = { titleId, imdbId, ready, episodes: season != null ? [{ season, episode: number }] : null };
  const ring = standing.kind === "underway"
    ? <Ring percent={percent(standing)} state={standing.state} />
    : standing.kind === "preparing" && !standing.failed ? <Ring percent={standing.percent || 0} state="preparing" /> : null;
  return (
    <span className="kc-episode-save">
      <Button variant="bare" className={"kc-dl-mark is-" + standing.kind} aria-label={`${label}: ${head.name}`} title={label}
        disabled={standing.kind === "none" && !online} onClick={() => setOpen(true)}>
        {ring || <Icon name={icon} size={18} />}
      </Button>
      {open && <DownloadSheet target={target} head={head} standing={standing} onClose={() => setOpen(false)} />}
    </span>
  );
}

/**
 * Download season: every episode of a season this device has not downloaded or asked for, that the
 * library holds or the tracker offers, asked for together at one quality. Says so once every one is
 * here or on its way.
 */
export function SeasonDownload({ imdbId, showName, season, offers, backdrop }) {
  const marks = useDownloadMarks();
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  const eligible = season.episodes.filter((e) => (e.titleId && e.status !== "failed") || episodeRelease(offers, e.season, e.number));
  if (!eligible.length) return null;
  const mark = (e) => (e.titleId && marks.film(e.titleId)) || marks.want(imdbId, e.season, e.number);
  const remaining = eligible.filter((e) => !mark(e) || (mark(e).kind === "preparing" && mark(e).failed));
  const done = eligible.every((e) => { const m = mark(e); return m && m.kind === "done"; });
  const label = done ? "Downloaded"
    : remaining.length === 0 ? "Downloading"
      : remaining.length === eligible.length ? "Download season" : `Download ${remaining.length} more`;
  const count = `${remaining.length} ${remaining.length === 1 ? "episode" : "episodes"}`;
  return (
    <>
      <Button variant="outline" size="sm" icon={done ? "circle-check" : "hard-drive-download"} className="kc-dl-season"
        disabled={remaining.length === 0 || !online} onClick={() => setOpen(true)}>
        {label}
      </Button>
      {open && (
        <WantSheet head={{ name: showName, context: `Season ${season.number} · ${count}`, still: backdrop, backdrop }}
          eyebrow="Download season" target={{ imdbId, episodes: remaining.map((e) => ({ season: e.season, episode: e.number })) }}
          onClose={() => setOpen(false)} />
      )}
    </>
  );
}

/** How far a download has got, as a ring around what it is doing. */
function Ring({ percent: p, state }) {
  const r = 10;
  const around = 2 * Math.PI * r;
  return (
    <span className="kc-dl-ring">
      <svg width="26" height="26" viewBox="0 0 26 26" aria-hidden="true">
        <circle cx="13" cy="13" r={r} className="kc-dl-ring__track" />
        <circle cx="13" cy="13" r={r} className="kc-dl-ring__done" strokeDasharray={`${(around * p) / 100} ${around}`} transform="rotate(-90 13 13)" />
      </svg>
      {state === "saving" || state === "elsewhere"
        ? <span className="kc-dl-ring__stop" />
        : <Icon name={(STATES[state] || STATES.queued).icon} size={10} />}
    </span>
  );
}

/**
 * The sheet for where a download stands. `target` is what Download asks for: `{ titleId, imdbId,
 * ready, episodes }`. A title whole in the library has its parts picked; anything else, its quality.
 */
function DownloadSheet({ target, head, standing, onClose }) {
  const [removing, setRemoving] = React.useState(false);
  const titleId = target.titleId || (standing.save && standing.save.titleId);
  if (removing) return <RemoveSheet titleId={titleId} head={head} standing={standing} onClose={onClose} onKeep={() => setRemoving(false)} />;
  if (standing.kind === "done") return <DoneSheet titleId={titleId} head={head} save={standing.save} onClose={onClose} onRemove={() => setRemoving(true)} />;
  if (standing.kind === "underway") return <UnderwaySheet titleId={titleId} head={head} standing={standing} onClose={onClose} onRemove={() => setRemoving(true)} />;
  if (standing.kind === "preparing") return <PreparingSheet head={head} standing={standing} onClose={onClose} />;
  if (target.titleId && target.ready) return <PickSheet titleId={target.titleId} head={head} onClose={onClose} />;
  return <WantSheet head={head} target={target} onClose={onClose} />;
}

/** A button in a sheet's foot: full width in the phone's drawer, its own size in the dialog. */
function FootButton(props) {
  return <Button size="lg" wide={usePhone()} {...props} />;
}

/** The qualities a title not yet whole is asked for at, by picture height. */
const HEIGHTS = [1080, 720, 480];

/** The quality picked by default: the person's Download quality, else the smallest on a phone or a tablet. */
function defaultHeight(preferences) {
  return (preferences && preferences.offlineQualityHeight) || (handheld() ? 480 : 720);
}

/**
 * Before a download of a title the library does not hold whole: its quality with roughly what each
 * takes (`GET /api/offline/estimate`), checked against the device's room where the estimate is known,
 * and the audio and subtitle languages, the person's by default. The title's own language is the
 * first audio choice and the one asked for by default when the person's is the same.
 */
function WantSheet({ head, target, eyebrow = "Download", onClose }) {
  const session = useSession();
  const sub = session && session.user.id;
  const online = useOnline();
  const narrow = usePhone();
  const preferences = useLoad(() => api.preferences().catch(() => keptPreferences(sub)), [sub]);
  const season = target.episodes ? target.episodes[0].season : null;
  const numbers = target.episodes ? target.episodes.map((e) => e.episode) : null;
  const estimate = useLoad(() => api.offlineEstimate(target.imdbId, season, numbers).catch(() => null),
    [target.imdbId, season, numbers && numbers.join()]);
  const room = useSpace();
  const [picked, setPicked] = React.useState(null);
  const [busy, setBusy] = React.useState(false);

  const original = estimate.data && estimate.data.originalLanguage;
  React.useEffect(() => {
    if (picked || preferences.loading || estimate.loading) return;
    const p = preferences.data || {};
    // The title's own language is what every release carries, so asking for it is asking for nothing.
    const audio = p.audioLanguage && !sameLanguage(p.audioLanguage, original) ? p.audioLanguage : "";
    setPicked({ height: defaultHeight(p), audio, subtitles: p.subtitleLanguage || "" });
  }, [picked, preferences.loading, preferences.data, estimate.loading, original]);

  const bytesAt = (h) => {
    const found = estimate.data && estimate.data.sizes.find((s) => s.height === h);
    return found ? found.bytes : null;
  };
  const need = picked ? bytesAt(picked.height) : null;
  const free = room.data ? room.data.free : null;
  const fits = need == null || free == null || need <= free;

  const download = async () => {
    setBusy(true);
    askToNotify();
    try {
      await askDownload({
        imdbId: target.imdbId, episodes: target.episodes, height: picked.height,
        audioLanguage: picked.audio || null, subtitleLanguage: picked.subtitles || null,
      });
      onClose();
    } catch (error) {
      setBusy(false);
      toast.fromError(error, "Could not download it");
    }
  };

  const foot = (
    <>
      <DeviceSpace space={room.data} need={need || 0} />
      {!narrow && <FootButton variant="outline" disabled={busy} onClick={onClose}>Cancel</FootButton>}
      <FootButton variant="primary" icon="hard-drive-download" busy={busy}
        disabled={!online || !picked || !fits} onClick={download}>
        {need ? `Download · ${roughly(need)}` : "Download"}
      </FootButton>
    </>
  );

  const set = (key) => (value) => setPicked((p) => ({ ...p, [key]: value }));
  return (
    <Sheet head={head} eyebrow={eyebrow} sub={joined(head.context, head.runtime)} onClose={onClose} canClose={!busy}
      className="kc-dl-pick" foot={foot}>
      {picked && (
        <>
          <Section label="Quality">
            <OptionList label="Quality" value={picked.height} variant={narrow ? "list" : "segmented"}
              options={HEIGHTS.map((h) => ({ value: h, label: `${h}p`, aside: bytesAt(h) ? roughly(bytesAt(h)) : null }))}
              onChange={set("height")} />
          </Section>
          <div className="kc-dl-pair">
            <Section label="Audio">
              <Select aria-label="Audio language" value={picked.audio} onChange={(e) => set("audio")(e.target.value)}
                options={languageOptions(picked.audio, original ? `${languageName(original)} (original)` : "Original")
                  .filter((o) => !o.value || !sameLanguage(o.value, original))} />
            </Section>
            <Section label="Subtitles">
              <Select aria-label="Subtitle language" value={picked.subtitles} onChange={(e) => set("subtitles")(e.target.value)}
                options={languageOptions(picked.subtitles, "Off")} />
            </Section>
          </div>
          {!fits && (
            <Notice tone="danger" icon="hard-drive">
              Needs {roughly(need)}; this device has {size(free)} free.
            </Notice>
          )}
        </>
      )}
    </Sheet>
  );
}

/** An estimated size, short: "~1.4GB". */
const roughly = (bytes) => "~" + size(bytes).replace(" ", "");

/**
 * What the server is preparing for this device: how far it has got, and Cancel; or why it could not,
 * with Try again and Remove.
 */
function PreparingSheet({ head, standing, onClose }) {
  const [busy, setBusy] = React.useState(false);
  const want = standing.want;
  const act = (what) => async () => {
    setBusy(true);
    try {
      await what();
      onClose();
    } catch (error) {
      setBusy(false);
      toast.fromError(error, "Could not do that");
    }
  };
  const stop = act(() => letGo(want));
  const again = act(() => askDownload({
    imdbId: want.imdbId, episodes: want.episode ? [want.episode] : null, height: want.height,
    audioLanguage: want.audioLanguage, subtitleLanguage: want.subtitleLanguage,
  }));
  const quality = want.height ? `${want.height}p` : null;

  return (
    <Sheet head={head} eyebrow={standing.failed ? STATES.failed.label : STATES.preparing.label} tone={standing.failed ? "failed" : "active"}
      sub={joined(head.context, quality)} onClose={onClose} canClose={!busy} className="kc-dl-underway"
      foot={standing.failed
        ? <>
          <FootButton variant="outline" icon="trash-2" disabled={busy} onClick={stop}>Remove</FootButton>
          <FootButton variant="primary" icon="rotate-ccw" busy={busy} onClick={again}>Try again</FootButton>
        </>
        : <FootButton variant="outline" icon="x" busy={busy} onClick={stop}>Cancel</FootButton>}>
      {standing.failed
        ? <Notice tone="danger" icon="triangle-alert">{standing.message}</Notice>
        : (
          <div className="kc-dl-progress">
            <span className="kc-dl-progress__figures">
              <span className="kc-dl-progress__percent">{standing.percent != null ? `${standing.percent}%` : "—"}</span>
              <span className="kc-dl-progress__bytes">{stageWords(want)}</span>
            </span>
            <Meter label="Prepared" max={100} value={standing.percent || 0} />
          </div>
        )}
    </Sheet>
  );
}

/**
 * The sheet's frame: on a phone the drawer, headed by the title's still, a small label, its name and
 * a line under it; on a wide screen a dialog with the backdrop as its art. `tone` colours the label.
 */
function Sheet({ head, eyebrow, tone, sub, onClose, canClose = true, foot, className = "", children }) {
  const narrow = usePhone();
  const label = `${eyebrow} ${head.name}`;
  const top = (
    <span className="kc-dl-head__text">
      <span className={"kc-dl-head__eyebrow" + (tone ? " is-" + tone : "")}>
        {tone === "done" && <Icon name="circle-check" size={12} />}{eyebrow}
      </span>
      <span className="kc-dl-head__name">{head.name}</span>
      {sub && <span className="kc-dl-head__sub">{sub}</span>}
    </span>
  );
  if (!narrow) {
    return (
      <Dialog title={top} label={label} art={head.backdrop ? `url("${head.backdrop}")` : undefined} onClose={onClose} canClose={canClose}
        className={"kc-dl-sheet kc-dl-dialog " + className} foot={foot}>
        {children}
      </Dialog>
    );
  }
  return (
    <Drawer onClose={onClose} canClose={canClose} label={label} className={"kc-dl-sheet kc-dl-drawer " + className} foot={foot} head={(
      <div className="kc-dl-head">
        {head.still && <img className={"kc-dl-head__still" + (head.portrait ? " is-portrait" : "")} src={head.still} alt="" />}
        {top}
        <IconButton variant="ghost" icon="x" label="Close" className="kc-dl-head__close" disabled={!canClose} onClick={onClose} />
      </div>
    )}>
      {children}
    </Drawer>
  );
}

const joined = (...parts) => parts.filter(Boolean).join(" · ");

/** An audio track's channels as a person names them. */
function channels(n) {
  if (n === 1) return "Mono";
  if (n === 2) return "Stereo";
  if (n === 6) return "5.1";
  if (n === 8) return "7.1";
  return null;
}

/**
 * The subtitles shown before "more languages": the person's subtitle and audio languages, the picked
 * audio's, and whatever is picked. A title with none of those shows its first two.
 */
function featuredSubtitles(subtitles, picked, audioPicked, preferences) {
  const languages = [preferences && preferences.subtitleLanguage, preferences && preferences.audioLanguage, ...audioPicked.map((a) => a.language)].filter(Boolean);
  const featured = subtitles.filter((s) => picked.includes(s.id) || languages.some((l) => sameLanguage(s.language, l)));
  return new Set((featured.length ? featured : subtitles.slice(0, 2)).map((s) => s.id));
}

/** Before a download: the title's rungs with their sizes, its audio and its text subtitles, each picked by default. */
function PickSheet({ titleId, head, onClose }) {
  const session = useSession();
  const sub = session && session.user.id;
  const online = useOnline();
  const narrow = usePhone();
  const offer = useLoad(() => api.offlineOffer(titleId), [titleId]);
  const preferences = useLoad(() => api.preferences().catch(() => keptPreferences(sub)), [sub]);
  const room = useSpace();
  const [picked, setPicked] = React.useState(null);
  const [featured, setFeatured] = React.useState(null);
  const [busy, setBusy] = React.useState(false);
  const [refused, setRefused] = React.useState(null);

  const o = offer.data;
  React.useEffect(() => {
    if (!o || preferences.loading || picked) return;
    const audio = defaultAudio(o.audio, preferences.data);
    const audioPicked = o.audio.filter((a) => audio.includes(a.rendition));
    const rung = defaultRung(o.rungs, preferences.data);
    const subtitles = defaultSubtitles(o.subtitles, audioPicked, preferences.data);
    setPicked({ rung: rung ? rung.rendition : null, audio, subtitles });
    setFeatured(featuredSubtitles(o.subtitles, subtitles, audioPicked, preferences.data));
  }, [o, preferences.loading, preferences.data, picked]);

  const rung = o && picked ? o.rungs.find((r) => r.rendition === picked.rung) : null;
  const audio = o && picked ? o.audio.filter((a) => picked.audio.includes(a.rendition)) : [];
  const subtitles = o && picked ? o.subtitles.filter((s) => picked.subtitles.includes(s.id)) : [];
  const need = (rung ? rung.bytes : 0) + audio.reduce((n, a) => n + a.bytes, 0) + subtitles.reduce((n, s) => n + s.bytes, 0);
  const space = room.data;
  const free = space ? space.free : null;
  const fits = free == null || need <= free;

  const download = async () => {
    setBusy(true);
    askToNotify();
    try {
      await startSave({ titleId, rung, audio, subtitles });
      onClose();
    } catch (error) {
      setBusy(false);
      if (error instanceof NoRoom) setRefused(error);
      else toast.fromError(error, "Could not download it");
    }
  };

  const shortBy = refused || (!fits ? { need, free } : null);
  // The featured subtitles lead, and the rest fold under "more languages".
  const ordered = o && featured ? [...o.subtitles.filter((s) => featured.has(s.id)), ...o.subtitles.filter((s) => !featured.has(s.id))] : [];
  const start = (
    <FootButton variant="primary" icon="hard-drive-download" busy={busy}
      disabled={!online || !rung || audio.length === 0 || !!shortBy} onClick={download}>
      {picked ? `Download · ${size(need)}` : "Download"}
    </FootButton>
  );
  const foot = (
    <>
      <DeviceSpace space={space} need={need} />
      {!narrow && <FootButton variant="outline" disabled={busy} onClick={onClose}>Cancel</FootButton>}
      {start}
    </>
  );

  return (
    <Sheet head={head} eyebrow="Download" sub={joined(head.context, head.runtime)} onClose={onClose} canClose={!busy}
      className="kc-dl-pick" foot={foot}>
      {offer.error && <Notice tone="danger" icon="triangle-alert">{offer.error.message}</Notice>}
      {!o && !offer.error && <span className="kc-note">Measuring…</span>}
      {o && picked && (
        <>
          <Section label="Quality">
            <OptionList label="Quality" value={picked.rung} variant={narrow ? "list" : "segmented"}
              options={o.rungs.map((r) => ({ value: r.rendition, label: r.name, aside: size(r.bytes) }))}
              onChange={(value) => setPicked((p) => ({ ...p, rung: value }))} />
          </Section>
          <div className="kc-dl-pair">
            <Section label="Audio">
              <OptionList label="Audio" multiple value={picked.audio} size={narrow ? "md" : "sm"}
                options={o.audio.map((a) => ({ value: a.rendition, label: a.label, detail: channels(a.channels), aside: size(a.bytes) }))}
                onChange={(value) => setPicked((p) => ({ ...p, audio: value }))} />
            </Section>
            {o.subtitles.length > 0 && (
              <Section label="Subtitles">
                <OptionList label="Subtitles" multiple value={picked.subtitles} size={narrow ? "md" : "sm"}
                  collapsedAfter={featured.size} moreLabel={(n) => `${n} more ${n === 1 ? "language" : "languages"}`}
                  options={ordered.map((s) => ({ value: s.id, label: s.label, detail: s.forced ? "Forced" : null, aside: size(s.bytes) }))}
                  onChange={(value) => setPicked((p) => ({ ...p, subtitles: value }))} />
              </Section>
            )}
          </div>
          {shortBy && (
            <Notice tone="danger" icon="hard-drive">
              Needs {size(shortBy.need)}; this device has {size(shortBy.free)} free.
            </Notice>
          )}
          {evictedUnvisited() && (
            <Notice tone="warn" icon="smartphone">
              Safari deletes downloads from a site left unvisited for 7 days. Add Krystal Cinema to the Home Screen to keep them.
            </Notice>
          )}
        </>
      )}
    </Sheet>
  );
}

function Section({ label, children }) {
  return (
    <div className="kc-dl-section">
      <span className="kc-dl-section__label">{label}</span>
      {children}
    </div>
  );
}

/** What the device holds and what a download would add to it, with what is free. */
function DeviceSpace({ space, need }) {
  if (!space || !space.quota) return <span className="kc-dl-space" />;
  return (
    <Meter className="kc-dl-space" label="This device" max={space.quota} start="This device" end={`${size(space.free)} free`}
      segments={[{ value: space.usage, tone: "neutral", label: "In use" }, { value: need, tone: "accent", label: "This download" }]} />
  );
}

/**
 * The files of a download in the order the worker writes them, with what of each is on the device:
 * the worker counts its progress file by file in that order, so the bytes received cover the first
 * files whole and the next in part.
 */
function filesReceived(files, received) {
  let left = received;
  return files.map((f) => {
    const got = Math.max(0, Math.min(f.bytes, left));
    left -= f.bytes;
    return { ...f, got };
  });
}

/** A download under way: how far it has got, each part of it, Cancel, and Pause or Resume. */
function UnderwaySheet({ titleId, head, standing, onClose, onRemove }) {
  const state = STATES[standing.state] || STATES.queued;
  const stopped = standing.state === "paused" || standing.state === "failed";
  const meta = standing.save && standing.save.meta;
  const p = percent(standing);
  const files = meta ? filesReceived(meta.files, standing.received) : [];
  const parts = [
    { label: "Picture", role: "video", name: meta && meta.quality && meta.quality.label },
    { label: "Sound", role: "audio" },
    { label: "Subtitles", role: "subtitle" },
  ].map((part) => {
    const of = files.filter((f) => f.role === part.role);
    const bytes = of.reduce((n, f) => n + f.bytes, 0);
    const got = of.reduce((n, f) => n + f.got, 0);
    return { ...part, files: of, bytes, got, name: part.name || of.map((f) => f.label).filter(Boolean).join(", ") };
  }).filter((part) => part.files.length > 0);

  return (
    <Sheet head={head} eyebrow={state.label} tone={standing.state === "failed" ? "failed" : "active"}
      sub={joined(head.context, meta && meta.quality && meta.quality.label)} onClose={onClose} className="kc-dl-underway"
      foot={<>
        <FootButton variant="outline" icon="trash-2" onClick={onRemove}>Cancel</FootButton>
        {stopped
          ? <FootButton variant="primary" icon="play" onClick={() => resumeSave(titleId)}>Resume</FootButton>
          : <FootButton variant="primary" icon="pause" onClick={() => pauseSave(titleId)}>Pause</FootButton>}
      </>}>
      <div className="kc-dl-progress">
        <span className="kc-dl-progress__figures">
          <span className="kc-dl-progress__percent">{p}%</span>
          <span className="kc-dl-progress__bytes">{size(standing.received)} of {size(standing.bytes)}</span>
        </span>
        <Meter label="Downloaded" max={100} value={p} />
      </div>
      {parts.length > 0 && (
        <div className="kc-dl-parts">
          {parts.map((part) => {
            const done = part.got >= part.bytes;
            const active = !done && part.got > 0;
            return (
              <div key={part.role} className={"kc-dl-part" + (done ? " is-done" : active ? " is-active" : "")}>
                <Icon name={done ? "circle-check" : active ? "hard-drive-download" : "clock"} size={18} />
                <span className="kc-dl-part__name">{part.label}{part.name && <span> · {part.name}</span>}</span>
                <span className="kc-dl-part__size">{active ? `${size(part.got)} / ${size(part.bytes)}` : size(part.bytes)}</span>
              </div>
            );
          })}
        </div>
      )}
      {standing.state === "failed" && standing.message && <Notice tone="danger" icon="triangle-alert">{standing.message}</Notice>}
    </Sheet>
  );
}

/** A download on this device: what it is, Play, and Remove. */
function DoneSheet({ titleId, head, save, onClose, onRemove }) {
  const meta = save.meta;
  const labels = (role) => meta.files.filter((f) => f.role === role).map((f) => f.label).filter(Boolean).join(", ");
  const facts = [
    ["Quality", (meta.quality && meta.quality.label) || null],
    ["Size", size(meta.bytes), true],
    ["Audio", labels("audio") || null],
    ["Subtitles", labels("subtitle") || "None"],
  ];
  return (
    <Sheet head={head} eyebrow="Downloaded" tone="done" sub={joined(head.context, head.runtime)} onClose={onClose} className="kc-dl-done"
      foot={<>
        <FootButton variant="outline" icon="trash-2" onClick={onRemove}>Remove</FootButton>
        <FootButton variant="primary" icon="play" {...linkProps(playSavedPath(titleId))}>Play</FootButton>
      </>}>
      <dl className="kc-dl-facts">
        {facts.map(([label, value, mono]) => (
          <div key={label} className="kc-dl-fact">
            <dt>{label}</dt>
            <dd className={mono ? "is-mono" : ""}>{value || "—"}</dd>
          </div>
        ))}
      </dl>
      {meta.completedAt && <span className="kc-dl-when">Downloaded {ago(meta.completedAt)}</span>}
    </Sheet>
  );
}

/** Asks before a download is taken off the device: downloading it again costs its whole size. */
function RemoveSheet({ titleId, head, standing, onClose, onKeep }) {
  const session = useSession();
  const narrow = usePhone();
  const [busy, setBusy] = React.useState(false);
  const bytes = standing.kind === "done" ? standing.save.receivedBytes : standing.received;
  const remove = async () => {
    setBusy(true);
    try {
      await removeFromDevice(session.user.id, titleId);
      toast.info(`Removed the download of ${head.name}.`);
      onClose();
    } catch (error) {
      setBusy(false);
      toast.fromError(error, "Could not remove it");
    }
  };
  const foot = (
    <>
      <FootButton variant="outline" disabled={busy} onClick={onKeep}>Keep</FootButton>
      <FootButton variant="danger" icon="trash-2" busy={busy} onClick={remove}>Remove</FootButton>
    </>
  );
  const what = <span className="kc-dl-remove__what">{head.name} · <span className="is-mono">{size(bytes || 0)}</span></span>;
  if (!narrow) {
    return (
      <Dialog title="Remove download?" icon="trash-2" onClose={onClose} canClose={!busy} className="kc-dl-sheet kc-dl-remove" foot={foot}>
        {what}
      </Dialog>
    );
  }
  return (
    <Drawer onClose={onClose} canClose={!busy} label="Remove download" role="alertdialog" className="kc-dl-sheet kc-dl-drawer kc-dl-remove" foot={foot} head={(
      <div className="kc-dl-head kc-dl-head--remove">
        <span className="kc-dl-remove__icon"><Icon name="trash-2" size={20} /></span>
        <span className="kc-dl-head__text">
          <span className="kc-dl-head__name">Remove download?</span>
          {what}
        </span>
      </div>
    )} />
  );
}
