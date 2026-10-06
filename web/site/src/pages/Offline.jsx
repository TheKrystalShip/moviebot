import React from "react";
import { Button, Dialog, Notice, ShowcaseCard, toast } from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { probe, useOnline } from "../connectivity.js";
import { useCrumb } from "../crumbs.js";
import { useArtwork, useSave, useSaves, useSpace } from "../offline/hooks.js";
import { pauseSave, removeFromDevice, restingState, resumeSave, useSaving, useSavingAll } from "../offline/saver.js";
import { episodeCode } from "../api.js";
import { moviePath, playSavedPath, profilePath, showPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { useSession } from "../session.js";
import { ago, clock } from "../time.js";
import { Empty, PageHead } from "../components/Film.jsx";
import { FactGrid, Genres } from "../components/FilmDetails.jsx";

// What the site draws from the device: the films saved on it, each one's page, and what a page that
// needs the server says while there is no connection. Everything here reads the device's own store
// (`offline/`), never an answer of the API's.

const UNITS = ["B", "KB", "MB", "GB", "TB"];

/** A size in bytes the way a person reads it: 1.4 GB. */
export function size(bytes) {
  let n = Math.max(0, bytes || 0);
  let unit = 0;
  while (n >= 1000 && unit < UNITS.length - 1) {
    n /= 1000;
    unit += 1;
  }
  return `${n >= 10 || unit === 0 ? Math.round(n) : n.toFixed(1)} ${UNITS[unit]}`;
}

/** What a save is called: a film by its name and year, an episode by its show and place. */
export function saveName(meta) {
  if (meta.episode && meta.show) return `${meta.show.name} · ${episodeCode(meta.episode)}`;
  return meta.year ? `${meta.name} (${meta.year})` : meta.name;
}

/** Where a save's page is: its show's for an episode, the film's own otherwise. */
const savePath = (meta) => (meta.show && meta.show.imdbId ? showPath(meta.show.imdbId) : moviePath(meta.titleId));

/** How far a save has got, from what the saver says while it writes, else from what is on the device. */
const fraction = (save, live) => {
  const received = live && live.received != null ? live.received : save.receivedBytes;
  return save.meta.bytes > 0 ? Math.min(1, received / save.meta.bytes) : 0;
};

/** What a save under way is doing, in a word. */
const UNDERWAY = {
  queued: { icon: "clock", label: "Queued" },
  saving: { icon: "download", label: "Saving" },
  elsewhere: { icon: "download", label: "Saving" },
  paused: { icon: "pause", label: "Paused" },
  waiting: { icon: "wifi-off", label: "Waiting" },
  failed: { icon: "triangle-alert", label: "Not saved" },
};

/** A save under way as the saver says, or, when no tab is writing it, as it rests. */
function useUnderway(titleId) {
  const live = useSaving(titleId);
  const online = useOnline();
  return live && live.state ? live : { ...(live || {}), state: restingState(titleId, online) };
}
const underway = (live) => UNDERWAY[live.state] || UNDERWAY.queued;
const stopped = (live) => live.state === "paused" || live.state === "failed";

/** The banner across every page while there is no connection. */
export function OfflineBanner() {
  return (
    <Notice tone="warn" icon="wifi-off" className="kc-offline-banner">
      Offline. Films saved on this device play.
    </Notice>
  );
}

/** A page that needs the server, reached with no connection: it says so and leads to the films here. */
export function NeedsConnection() {
  return (
    <div className="kc-page">
      <Empty icon="wifi-off" title="Needs a connection">
        <Button size="lg" icon="hard-drive-download" {...linkProps(profilePath("offline"))}>On this device</Button>
      </Empty>
    </div>
  );
}

/** A device that holds no session, with no connection to sign in over. */
export function FirstSignIn() {
  const [busy, setBusy] = React.useState(false);
  const retry = () => {
    setBusy(true);
    probe().finally(() => setBusy(false));
  };
  return (
    <div className="kc-signin">
      <div className="kc-signin__card">
        <img src={markUrl} width="48" height="48" alt="" />
        <h1>Krystal Cinema</h1>
        <span className="kc-note">Signing in needs a connection.</span>
        <Button variant="primary" size="xl" wide icon="refresh-cw" busy={busy} onClick={retry}>Try again</Button>
      </div>
    </div>
  );
}

/** Home with no connection: the films on this device. */
export function OfflineHome() {
  const saves = useSaves();
  return (
    <div className="kc-page">
      <PageHead title="On this device" />
      <SaveGrid saves={saves} />
    </div>
  );
}

/** Profile's Offline tab: every save of this account on the device, under way or done, and the room left. */
export function OfflineTab() {
  const saves = useSaves();
  const room = useSpace();
  const live = useSavingAll();
  const used = (saves.data || []).reduce((sum, s) => sum + fraction(s, live[s.titleId]) * s.meta.bytes, 0);
  return (
    <section className="kc-shelf">
      {room.data && (
        <div className="kc-shelf__head kc-offline-space">
          <span className="kc-note">{size(used)} saved · {size(room.data.free)} free</span>
        </div>
      )}
      <SaveGrid saves={saves} />
    </section>
  );
}

function SaveGrid({ saves }) {
  if (saves.error) return <Empty icon="triangle-alert" title="This device's saves could not be read" />;
  if (!saves.data) return null;
  if (saves.data.length === 0) return <Empty icon="hard-drive-download" title="Nothing saved on this device" />;
  return (
    <div className="kc-grid">
      {saves.data.map((save) => <SaveCard key={save.titleId} save={save} />)}
    </div>
  );
}

/**
 * One save: its poster, quality, size and length, how far it has got with Pause or Resume while under
 * way, Play once done, and Remove.
 */
function SaveCard({ save }) {
  const poster = useArtwork(save, "poster");
  const live = useUnderway(save.titleId);
  const [removing, setRemoving] = React.useState(false);
  const { meta } = save;
  const done = save.complete;
  const state = underway(live);
  const shown = fraction(save, live);
  const pausing = done ? [] : stopped(live)
    ? [{ icon: "play", label: `Resume saving ${saveName(meta)}`, onClick: () => resumeSave(save.titleId) }]
    : [{ icon: "pause", label: `Pause saving ${saveName(meta)}`, onClick: () => pauseSave(save.titleId) }];
  return (
    <>
      <ShowcaseCard
        className="kc-card"
        name={meta.episode && meta.show ? saveName(meta) : meta.name}
        art={poster ? `url("${poster}")` : null}
        onPick={() => navigate(savePath(meta))}
        progress={done ? null : shown}
        specs={[
          { icon: "gauge", value: (meta.quality && meta.quality.label) || null, label: "Quality" },
          { icon: "hard-drive", value: size(meta.bytes), label: "Size" },
          { icon: "clock", value: meta.durationSeconds ? clock(meta.durationSeconds) : null, label: "Length" },
        ]}
        status={done
          ? { icon: "check", label: `Saved ${ago(meta.completedAt)}`, title: new Date(meta.completedAt).toLocaleString() }
          : {
            tone: stopped(live) ? undefined : "active", icon: state.icon,
            label: live && live.state === "failed" ? state.label : `${state.label} · ${Math.floor(100 * shown)}%`,
            title: (live && live.message) || undefined,
          }}
        action={done ? { label: "Play", onClick: () => navigate(playSavedPath(meta.titleId)) } : { label: "View" }}
        tools={[
          ...pausing,
          { icon: "trash-2", label: `Remove ${saveName(meta)} from this device`, tone: "danger", onClick: () => setRemoving(true) },
        ]} />
      {removing && <RemoveDialog save={save} onClose={() => setRemoving(false)} />}
    </>
  );
}

/** Asks before a save is removed from the device: a film taken off costs its whole size to save again. */
function RemoveDialog({ save, onClose, onRemoved }) {
  const session = useSession();
  const [busy, setBusy] = React.useState(false);
  const remove = async () => {
    setBusy(true);
    try {
      await removeFromDevice(session.user.id, save.titleId);
      toast.info(`Removed ${saveName(save.meta)} from this device.`);
      onClose();
      if (onRemoved) onRemoved();
    } catch (error) {
      setBusy(false);
      toast.fromError(error, "Could not remove it");
    }
  };
  return (
    <Dialog title={`Remove ${saveName(save.meta)}?`} icon="trash-2" onClose={onClose} canClose={!busy}
      foot={<>
        <Button variant="outline" size="lg" disabled={busy} onClick={onClose}>Keep</Button>
        <Button variant="danger" size="lg" icon="trash-2" busy={busy} onClick={remove}>Remove</Button>
      </>}>
      <span className="kc-note">{size(save.receivedBytes)}</span>
    </Dialog>
  );
}

/** A saved film's or episode's page, drawn from its `meta.json`; a title not saved here needs a connection. */
export function SavedTitle({ titleId }) {
  const save = useSave(titleId);
  const meta = save.data && save.data.meta;
  useCrumb(meta ? (meta.episode ? saveName(meta) : meta.name) : null);
  if (save.loading && !save.data) return <div className="kc-page" />;
  if (!save.data) return <NeedsConnection />;
  return <SavedPage save={save.data} />;
}

/** A show's page with no connection: the episodes of it saved here, or that it needs a connection. */
export function SavedShow({ imdbId }) {
  const saves = useSaves();
  const episodes = (saves.data || []).filter((s) => s.meta.show && s.meta.show.imdbId === imdbId)
    .sort((a, b) => (a.meta.episode.season - b.meta.episode.season) || (a.meta.episode.number - b.meta.episode.number));
  const show = episodes.length > 0 ? episodes[0].meta.show : null;
  useCrumb(show ? show.name : null);
  if (!saves.data) return <div className="kc-page" />;
  if (!show) return <NeedsConnection />;
  return (
    <div className="kc-page">
      <PageHead title={show.name} />
      <div className="kc-grid">
        {episodes.map((save) => <SaveCard key={save.titleId} save={save} />)}
      </div>
    </div>
  );
}

function SavedPage({ save }) {
  const { meta } = save;
  const poster = useArtwork(save, "poster");
  const backdrop = useArtwork(save, "backdrop");
  const live = useUnderway(save.titleId);
  const [removing, setRemoving] = React.useState(false);
  const art = backdrop || poster;
  const done = save.complete;

  return (
    <div className="kc-page kc-title">
      <section className="kc-tt">
        {art && <div className="kc-tt__art" style={{ backgroundImage: `url("${art}")` }} />}
        <div className="kc-tt__shade" />
        <div className="kc-tt__grid">
          <div className="kc-poster-art kc-tt__poster">
            {poster ? <img src={poster} alt="" /> : <span className="kc-poster-art__name">{meta.name}</span>}
          </div>
          <div className="kc-tt__body">
            <span className="kc-eyebrow kc-eyebrow--muted">{meta.episode ? episodeCode(meta.episode) : "Movie"}</span>
            <h1>{meta.episode && meta.episode.name ? meta.episode.name : meta.name}</h1>
            <div className="kc-tt__meta">
              {meta.show && <a {...linkProps(showPath(meta.show.imdbId))}>{meta.show.name}</a>}
              {meta.year && <span>{meta.year}</span>}
              {meta.durationSeconds > 0 && <span>{clock(meta.durationSeconds)}</span>}
            </div>
            {meta.genres && meta.genres.length > 0 && <div className="kc-tt__score-row"><Genres names={meta.genres} /></div>}
            {meta.overview && <p className="kc-tt__overview">{meta.overview}</p>}
            <div className="kc-actions">
              {done
                ? <Button variant="primary" size="xl" icon="play" {...linkProps(playSavedPath(meta.titleId))}>Play</Button>
                : <span className="kc-pill kc-pill--info">{underway(live).label.toLowerCase()} · {Math.floor(100 * fraction(save, live))}%</span>}
              <Button size="xl" icon="trash-2" onClick={() => setRemoving(true)}>Remove</Button>
            </div>
          </div>
        </div>
      </section>
      <section className="kc-section">
        <h2>On this device</h2>
        <FactGrid facts={[
          ["Quality", meta.quality && meta.quality.label],
          ["Size", size(meta.bytes)],
          ["Saved", meta.completedAt ? ago(meta.completedAt) : null],
          ["IMDb", meta.imdbId, true],
        ]} />
      </section>
      {removing && <RemoveDialog save={save} onClose={() => setRemoving(false)}
        onRemoved={() => navigate(profilePath("offline"), { replace: true })} />}
    </div>
  );
}
