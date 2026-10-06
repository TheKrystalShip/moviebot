import React from "react";
import { Button, Checkbox, Dialog, Field, IconButton, Notice, Select, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { keptPreferences } from "../offline/preferences.js";
import { defaultAudio, defaultRung, defaultSubtitles, evictedUnvisited } from "../offline/choose.js";
import { useSave, useSpace } from "../offline/hooks.js";
import { NoRoom, pauseSave, removeFromDevice, restingState, resumeSave, startSave, useSaving } from "../offline/saver.js";
import { offlineSupported } from "../offline/store.js";
import { size } from "../pages/Offline.jsx";
import { playSavedPath } from "../paths.js";
import { linkProps } from "../router.js";
import { useCan, useSession } from "../session.js";
import { ago } from "../time.js";
import { onTv } from "../tv/tv.js";

// Save offline on a film's page and on an episode's row: one control whose face says where the save
// stands (not saved, under way, on this device) and which opens the one sheet for all of it: the
// picker before a save, its progress with Pause and Resume while under way, and Play and Remove once
// it is here. "Save" alone is the lists' word, so this one always says where the film goes.

let playable = null;

/** Whether hls.js plays here, which is how a saved film is played: asked once, from its own chunk. */
function hlsPlays() {
  playable ??= import("hls.js").then((m) => m.default.isSupported(), () => false);
  return playable;
}

/**
 * Whether this page offers Save offline: to somebody who holds `cinema:offline`, in a browser that has
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
  saving: { icon: "hard-drive-download", label: "Saving" },
  paused: { icon: "pause", label: "Paused" },
  waiting: { icon: "wifi-off", label: "Waiting for a connection" },
  elsewhere: { icon: "hard-drive-download", label: "Saving in another tab" },
  failed: { icon: "triangle-alert", label: "Not saved" },
};

/**
 * Where a title's save stands on this device: `{ kind: "none" }`, `{ kind: "done", save }`, or
 * `{ kind: "underway", state, received, bytes, message, save }`, from the store and the saver together.
 */
function useStanding(titleId) {
  const save = useSave(titleId);
  const live = useSaving(titleId);
  const online = useOnline();
  const held = save.data;
  if (live) {
    return {
      kind: "underway", state: live.state || restingState(titleId, online), message: live.message || null, save: held,
      received: live.received != null ? live.received : held ? held.receivedBytes : 0,
      bytes: live.bytes || (held ? held.meta.bytes : 0),
    };
  }
  if (held && held.complete) return { kind: "done", save: held };
  if (held) return { kind: "underway", state: restingState(titleId, online), save: held, received: held.receivedBytes, bytes: held.meta.bytes };
  return { kind: save.loading ? "loading" : "none" };
}

const percent = (s) => (s.bytes > 0 ? Math.floor((100 * Math.min(s.received, s.bytes)) / s.bytes) : 0);

/** The face of the control: its icon and words. */
function face(standing) {
  if (standing.kind === "done") return { icon: "circle-check", label: "On this device" };
  if (standing.kind === "underway") {
    const state = STATES[standing.state] || STATES.queued;
    return { icon: state.icon, label: standing.state === "failed" ? state.label : `${state.label} · ${percent(standing)}%` };
  }
  return { icon: "hard-drive-download", label: "Save offline" };
}

/** Save offline on a film's page. `name` is what the film is called. */
export function SaveOffline({ titleId, name }) {
  const standing = useStanding(titleId);
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  if (standing.kind === "loading") return null;
  const { icon, label } = face(standing);
  return (
    <>
      <Button size="xl" icon={icon} pressed={standing.kind === "done" ? true : undefined}
        disabled={standing.kind === "none" && !online} onClick={() => setOpen(true)}>
        {label}
      </Button>
      {open && <SaveOfflineSheet titleId={titleId} name={name} standing={standing} onClose={() => setOpen(false)} />}
    </>
  );
}

/** Save offline on an episode's row: a round button beside the row, saying the same in its label. */
export function SaveOfflineMark({ titleId, name }) {
  const standing = useStanding(titleId);
  const online = useOnline();
  const [open, setOpen] = React.useState(false);
  if (standing.kind === "loading") return <span className="kc-episode-save" />;
  const { icon, label } = face(standing);
  return (
    <span className="kc-episode-save">
      <IconButton variant="ghost" icon={icon} label={`${label}: ${name}`} title={label}
        tone={standing.kind === "done" ? "success" : undefined} pressed={standing.kind === "done" ? true : undefined}
        disabled={standing.kind === "none" && !online} onClick={() => setOpen(true)} />
      {open && <SaveOfflineSheet titleId={titleId} name={name} standing={standing} onClose={() => setOpen(false)} />}
    </span>
  );
}

function SaveOfflineSheet({ titleId, name, standing, onClose }) {
  const [removing, setRemoving] = React.useState(false);
  if (removing) return <RemoveSheet titleId={titleId} name={name} standing={standing} onClose={onClose} onKeep={() => setRemoving(false)} />;
  if (standing.kind === "done") return <DoneSheet titleId={titleId} name={name} save={standing.save} onClose={onClose} onRemove={() => setRemoving(true)} />;
  if (standing.kind === "underway") return <UnderwaySheet titleId={titleId} name={name} standing={standing} onClose={onClose} onRemove={() => setRemoving(true)} />;
  return <PickSheet titleId={titleId} name={name} onClose={onClose} />;
}

/** Before a save: the title's rungs with their sizes, its audio and its text subtitles, each picked by default. */
function PickSheet({ titleId, name, onClose }) {
  const session = useSession();
  const sub = session && session.user.id;
  const online = useOnline();
  const offer = useLoad(() => api.offlineOffer(titleId), [titleId]);
  const preferences = useLoad(() => api.preferences().catch(() => keptPreferences(sub)), [sub]);
  const room = useSpace();
  const [picked, setPicked] = React.useState(null);
  const [busy, setBusy] = React.useState(false);
  const [refused, setRefused] = React.useState(null);

  const o = offer.data;
  React.useEffect(() => {
    if (!o || preferences.loading || picked) return;
    const audio = defaultAudio(o.audio, preferences.data);
    const audioPicked = o.audio.filter((a) => audio.includes(a.rendition));
    const rung = defaultRung(o.rungs, preferences.data);
    setPicked({
      rung: rung ? rung.rendition : null,
      audio,
      subtitles: defaultSubtitles(o.subtitles, audioPicked, preferences.data),
    });
  }, [o, preferences.loading, preferences.data, picked]);

  const rung = o && picked ? o.rungs.find((r) => r.rendition === picked.rung) : null;
  const audio = o && picked ? o.audio.filter((a) => picked.audio.includes(a.rendition)) : [];
  const subtitles = o && picked ? o.subtitles.filter((s) => picked.subtitles.includes(s.id)) : [];
  const need = (rung ? rung.bytes : 0) + audio.reduce((n, a) => n + a.bytes, 0) + subtitles.reduce((n, s) => n + s.bytes, 0);
  const free = room.data ? room.data.free : null;
  const fits = free == null || need <= free;

  const toggle = (key, id, on) => setPicked((p) => ({ ...p, [key]: on ? [...p[key], id] : p[key].filter((x) => x !== id) }));
  const save = async () => {
    setBusy(true);
    try {
      await startSave({ titleId, rung, audio, subtitles });
      onClose();
    } catch (error) {
      setBusy(false);
      if (error instanceof NoRoom) setRefused(error);
      else toast.fromError(error, "Could not save it");
    }
  };

  const shortBy = refused || (!fits ? { need, free } : null);
  return (
    <Dialog title="Save offline" sub={name} icon="hard-drive-download" onClose={onClose} canClose={!busy}
      className="kc-save-sheet"
      foot={<>
        <Button variant="outline" size="lg" disabled={busy} onClick={onClose}>Cancel</Button>
        <Button variant="primary" size="lg" icon="hard-drive-download" busy={busy}
          disabled={!online || !rung || audio.length === 0 || !!shortBy} onClick={save}>Save offline</Button>
      </>}>
      {offer.error && <Notice tone="danger" icon="triangle-alert">{offer.error.message}</Notice>}
      {!o && !offer.error && <span className="kc-note">Measuring…</span>}
      {o && picked && (
        <div className="kc-save-pick">
          <Field label="Quality">
            <Select value={picked.rung || ""} aria-label="Quality"
              options={o.rungs.map((r) => ({ value: r.rendition, label: `${r.name} · ${size(r.bytes)}` }))}
              onChange={(e) => setPicked((p) => ({ ...p, rung: e.target.value }))} />
          </Field>
          <Field label="Audio" group>
            <div className="kc-save-pick__list">
              {o.audio.map((a) => (
                <Checkbox key={a.rendition} checked={picked.audio.includes(a.rendition)} onChange={(on) => toggle("audio", a.rendition, on)}>
                  <span className="kc-save-pick__name">{a.label}</span>
                  <span className="kc-save-pick__size">{size(a.bytes)}</span>
                </Checkbox>
              ))}
            </div>
          </Field>
          {o.subtitles.length > 0 && (
            <Field label="Subtitles" group>
              <div className="kc-save-pick__list kc-save-pick__list--long">
                {o.subtitles.map((s) => (
                  <Checkbox key={s.id} checked={picked.subtitles.includes(s.id)} onChange={(on) => toggle("subtitles", s.id, on)}>
                    <span className="kc-save-pick__name">{s.label}{s.forced ? " (forced)" : ""}</span>
                    <span className="kc-save-pick__size">{size(s.bytes)}</span>
                  </Checkbox>
                ))}
              </div>
            </Field>
          )}
          <span className="kc-note kc-save-pick__total">
            {size(need)}{free != null ? ` · ${size(free)} free on this device` : ""}
          </span>
          {shortBy && (
            <Notice tone="danger" icon="hard-drive">
              Needs {size(shortBy.need)}; this device has {size(shortBy.free)} free.
            </Notice>
          )}
          {evictedUnvisited() && (
            <Notice tone="warn" icon="smartphone">
              Safari deletes saved films from a site left unvisited for 7 days. Add Krystal Cinema to the Home Screen to keep them.
            </Notice>
          )}
        </div>
      )}
    </Dialog>
  );
}

/** A save under way: how far it has got, Pause or Resume, and Remove. */
function UnderwaySheet({ titleId, name, standing, onClose, onRemove }) {
  const state = STATES[standing.state] || STATES.queued;
  const stopped = standing.state === "paused" || standing.state === "failed";
  const meta = standing.save && standing.save.meta;
  return (
    <Dialog title={name} icon={state.icon} onClose={onClose}
      foot={<>
        <Button variant="outline" size="lg" icon="trash-2" onClick={onRemove}>Remove</Button>
        {stopped
          ? <Button variant="primary" size="lg" icon="play" onClick={() => resumeSave(titleId)}>Resume</Button>
          : <Button variant="primary" size="lg" icon="pause" onClick={() => pauseSave(titleId)}>Pause</Button>}
      </>}>
      <div className="kc-save-progress" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent(standing)}>
        <span style={{ width: `${percent(standing)}%` }} />
      </div>
      <span className="kc-note">
        {state.label} · {percent(standing)}% · {size(standing.received)} of {size(standing.bytes)}
        {meta && meta.quality && meta.quality.label ? ` · ${meta.quality.label}` : ""}
      </span>
      {standing.state === "failed" && standing.message && <span className="kc-note kc-note--danger">{standing.message}</span>}
    </Dialog>
  );
}

/** A save on this device: what it is, Play, and Remove. */
function DoneSheet({ titleId, name, save, onClose, onRemove }) {
  const meta = save.meta;
  return (
    <Dialog title={name} icon="circle-check" onClose={onClose}
      foot={<>
        <Button variant="outline" size="lg" icon="trash-2" onClick={onRemove}>Remove</Button>
        <Button variant="primary" size="lg" icon="play" {...linkProps(playSavedPath(titleId))}>Play</Button>
      </>}>
      <span className="kc-note">
        On this device · {(meta.quality && meta.quality.label) || ""} · {size(meta.bytes)} · saved {ago(meta.completedAt)}
      </span>
    </Dialog>
  );
}

/** Asks before a save is taken off the device: saving it again costs its whole size. */
function RemoveSheet({ titleId, name, standing, onClose, onKeep }) {
  const session = useSession();
  const [busy, setBusy] = React.useState(false);
  const bytes = standing.kind === "done" ? standing.save.receivedBytes : standing.received;
  const remove = async () => {
    setBusy(true);
    try {
      await removeFromDevice(session.user.id, titleId);
      toast.info(`Removed ${name} from this device.`);
      onClose();
    } catch (error) {
      setBusy(false);
      toast.fromError(error, "Could not remove it");
    }
  };
  return (
    <Dialog title={`Remove ${name}?`} icon="trash-2" onClose={onClose} canClose={!busy}
      foot={<>
        <Button variant="outline" size="lg" disabled={busy} onClick={onKeep}>Keep</Button>
        <Button variant="danger" size="lg" icon="trash-2" busy={busy} onClick={remove}>Remove</Button>
      </>}>
      <span className="kc-note">{size(bytes || 0)}</span>
    </Dialog>
  );
}
