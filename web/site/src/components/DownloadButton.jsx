import React from "react";
import { Button, Dialog, Icon, Meter, Notice, OptionList, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { keptPreferences } from "../offline/preferences.js";
import { defaultAudio, defaultRung, defaultSubtitles, evictedUnvisited, sameLanguage } from "../offline/choose.js";
import { useSave, useSaves, useSpace } from "../offline/hooks.js";
import { NoRoom, pauseSave, removeFromDevice, restingState, resumeSave, startSave, useSaving, useSavingAll } from "../offline/saver.js";
import { offlineSupported } from "../offline/store.js";
import { size } from "../pages/Offline.jsx";
import { playSavedPath } from "../paths.js";
import { linkProps } from "../router.js";
import { useCan, useSession } from "../session.js";
import { ago } from "../time.js";
import { onTv } from "../tv/tv.js";
import { Drawer } from "./Drawer.jsx";
import { useNarrow } from "./SaveButton.jsx";
import "./download.css";

// Download on a film's page and on an episode's row: one control whose face says where the download
// stands (not downloaded, under way, downloaded) and which opens the one sheet for all of it: the
// choice of quality, audio and subtitles before it, its progress with Pause and Resume while under
// way, and Play and Remove once it is here. The sheet is the drawer on a phone and a dialog on a wide
// screen. "Save" is the lists' word; this one is always Download.

let playable = null;

/** Whether hls.js plays here, which is how a downloaded film is played: asked once, from its own chunk. */
function hlsPlays() {
  playable ??= import("hls.js").then((m) => m.default.isSupported(), () => false);
  return playable;
}

/**
 * Whether this page offers Download: to somebody who holds `cinema:offline`, in a browser that has
 * the Origin Private File System and plays through hls.js, never on a television.
 */
export function useOffersSaving() {
  const may = useCan("cinema:offline") && !onTv && offlineSupported();
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
};

/**
 * Where a title's download stands on this device: `{ kind: "none" }`, `{ kind: "done", save }`, or
 * `{ kind: "underway", state, received, bytes, message, save }`, from the store and the saver together.
 */
function useStanding(titleId) {
  const save = useSave(titleId);
  const live = useSaving(titleId);
  const online = useOnline();
  return standingOf(titleId, save.data, live, online, save.loading);
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

const percent = (s) => (s.bytes > 0 ? Math.floor((100 * Math.min(s.received, s.bytes)) / s.bytes) : 0);

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
 * a show card's `downloaded`, the number of its episodes downloaded.
 */
export function useDownloadMarks() {
  const downloads = useDownloads();
  return React.useMemo(() => {
    const shows = new Map();
    for (const d of downloads.values()) {
      const imdbId = d.kind === "done" && d.save.meta.show && d.save.meta.show.imdbId;
      if (imdbId) shows.set(imdbId, (shows.get(imdbId) || 0) + 1);
    }
    return { film: (titleId) => downloads.get(titleId), episodes: (imdbId) => shows.get(imdbId) || 0 };
  }, [downloads]);
}

/**
 * A row's line for a download from `useDownloads`: "Downloaded · 480p", or what it is doing and how
 * far it has got ("Downloading · 34%"); null for none.
 */
export function downloadLine(download) {
  if (!download) return null;
  if (download.kind === "done") {
    const quality = download.save.meta.quality && download.save.meta.quality.label;
    return quality ? `Downloaded · ${quality}` : "Downloaded";
  }
  const state = STATES[download.state] || STATES.queued;
  return download.state === "failed" ? state.label : `${state.label} · ${download.percent}%`;
}

/** The face of the control: its icon and words. */
function face(standing) {
  if (standing.kind === "done") return { icon: "circle-check", label: "Downloaded" };
  if (standing.kind === "underway") {
    const state = STATES[standing.state] || STATES.queued;
    return { icon: state.icon, label: standing.state === "failed" ? state.label : `${state.label} · ${percent(standing)}%` };
  }
  return { icon: "hard-drive-download", label: "Download" };
}

/**
 * Download on a film's page. `head` is what the sheet is headed with: `{ name, context, runtime,
 * still, backdrop }`, `context` the line under the name and `still` the image beside it.
 */
export function DownloadButton({ titleId, head }) {
  const standing = useStanding(titleId);
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  if (standing.kind === "loading") return null;
  const { icon, label } = face(standing);
  return (
    <>
      <Button variant="outline" size="xl" icon={icon} className={"kc-dl-button" + (standing.kind !== "none" ? " is-held" : "")}
        disabled={standing.kind === "none" && !online} onClick={() => setOpen(true)}>
        {label}
      </Button>
      {open && <DownloadSheet titleId={titleId} head={head} standing={standing} onClose={() => setOpen(false)} />}
    </>
  );
}

/** Download on an episode's row: a square button at the row's end, a ring while under way. */
export function DownloadMark({ titleId, head }) {
  const standing = useStanding(titleId);
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  if (standing.kind === "loading") return <span className="kc-episode-save" />;
  const { icon, label } = face(standing);
  const p = standing.kind === "underway" ? percent(standing) : 0;
  return (
    <span className="kc-episode-save">
      <button type="button" className={"kc-dl-mark is-" + standing.kind} aria-label={`${label}: ${head.name}`} title={label}
        disabled={standing.kind === "none" && !online} onClick={() => setOpen(true)}>
        {standing.kind === "underway" ? <Ring percent={p} state={standing.state} /> : <Icon name={icon} size={18} />}
      </button>
      {open && <DownloadSheet titleId={titleId} head={head} standing={standing} onClose={() => setOpen(false)} />}
    </span>
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

function DownloadSheet({ titleId, head, standing, onClose }) {
  const [removing, setRemoving] = React.useState(false);
  if (removing) return <RemoveSheet titleId={titleId} head={head} standing={standing} onClose={onClose} onKeep={() => setRemoving(false)} />;
  if (standing.kind === "done") return <DoneSheet titleId={titleId} head={head} save={standing.save} onClose={onClose} onRemove={() => setRemoving(true)} />;
  if (standing.kind === "underway") return <UnderwaySheet titleId={titleId} head={head} standing={standing} onClose={onClose} onRemove={() => setRemoving(true)} />;
  return <PickSheet titleId={titleId} head={head} onClose={onClose} />;
}

/**
 * The sheet's frame: on a phone the drawer, headed by the title's still, a small label, its name and
 * a line under it; on a wide screen a dialog with the backdrop as its art. `tone` colours the label.
 */
function Sheet({ head, eyebrow, tone, sub, onClose, canClose = true, foot, className = "", children }) {
  const narrow = useNarrow();
  const label = `${eyebrow} ${head.name}`;
  const close = () => { if (canClose) onClose(); };
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
    <Drawer onClose={close} label={label} className={"kc-dl-sheet kc-dl-drawer " + className}>
      <div className="kc-dl-head">
        {head.still && <img className={"kc-dl-head__still" + (head.portrait ? " is-portrait" : "")} src={head.still} alt="" />}
        {top}
        <button type="button" className="kc-dl-head__close" aria-label="Close" disabled={!canClose} onClick={onClose}>
          <Icon name="x" size={18} />
        </button>
      </div>
      <div className="kc-dl-body">{children}</div>
      {foot && <div className="kc-dl-foot">{foot}</div>}
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
  const narrow = useNarrow();
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
    <Button variant="primary" size="lg" icon="hard-drive-download" busy={busy} className="kc-dl-start"
      disabled={!online || !rung || audio.length === 0 || !!shortBy} onClick={download}>
      {picked ? `Download · ${size(need)}` : "Download"}
    </Button>
  );
  const foot = (
    <>
      <DeviceSpace space={space} need={need} />
      {!narrow && <Button variant="outline" size="lg" disabled={busy} onClick={onClose}>Cancel</Button>}
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
        <Button variant="outline" size="lg" icon="trash-2" onClick={onRemove}>Cancel</Button>
        {stopped
          ? <Button variant="primary" size="lg" icon="play" onClick={() => resumeSave(titleId)}>Resume</Button>
          : <Button variant="primary" size="lg" icon="pause" onClick={() => pauseSave(titleId)}>Pause</Button>}
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
        <Button variant="outline" size="lg" icon="trash-2" onClick={onRemove}>Remove</Button>
        <Button variant="primary" size="lg" icon="play" {...linkProps(playSavedPath(titleId))}>Play</Button>
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
  const narrow = useNarrow();
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
      <Button variant="outline" size="lg" disabled={busy} onClick={onKeep}>Keep</Button>
      <Button variant="danger" size="lg" icon="trash-2" busy={busy} onClick={remove}>Remove</Button>
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
    <Drawer onClose={() => { if (!busy) onClose(); }} label="Remove download" role="alertdialog" className="kc-dl-sheet kc-dl-drawer kc-dl-remove">
      <div className="kc-dl-head kc-dl-head--remove">
        <span className="kc-dl-remove__icon"><Icon name="trash-2" size={20} /></span>
        <span className="kc-dl-head__text">
          <span className="kc-dl-head__name">Remove download?</span>
          {what}
        </span>
      </div>
      <div className="kc-dl-foot">{foot}</div>
    </Drawer>
  );
}
