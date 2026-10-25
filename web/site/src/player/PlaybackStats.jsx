import React from "react";
import { Button, Icon } from "@thekrystalship/krystal";
import { HistoryMs } from "./engine/media/playbackStats.ts";

/** How often the panel reads the collector again while it is open. */
const RefreshMs = 1000;
/** How long Send's answer stays on the button. */
const SentShownMs = 2500;

const unknown = "unknown";
const ms = (value) => (value === null ? unknown : `${Math.round(value)} ms`);
const signedMs = (value) => (value === null ? unknown : `${value > 0 ? "+" : value < 0 ? "−" : ""}${Math.abs(Math.round(value))} ms`);
const seconds = (value, places = 1) => (value === null ? unknown : `${value.toFixed(places)} s`);
const mbit = (bps) => (bps === null ? unknown : `${(bps / 1_000_000).toFixed(1)} Mbit/s`);
const bytes = (count) => (count >= 1_000_000 ? `${(count / 1_000_000).toFixed(1)} MB` : `${Math.round(count / 1000)} kB`);
const count = (value) => (value === null ? unknown : value.toLocaleString("en-GB"));
const height = (value) => (value === null ? unknown : `${value}p`);

/**
 * Whether the room is ahead of this player or behind it, in words, from the room's position minus the
 * playhead: positive is this player behind.
 */
function behindRoom(drift) {
  if (drift === null) return unknown;
  if (Math.abs(drift) < 0.005) return "level";
  return `${Math.abs(drift).toFixed(2)} s ${drift > 0 ? "behind" : "ahead"}`;
}

/**
 * The stats panel: what this player measures about its own playback, over the top left of the film,
 * read from the collector once a second (`engine/media/playbackStats.ts`). Its frame callback and its
 * audio context run only while it is drawn. Copy puts the snapshot on the clipboard; Send writes it to
 * the server's log, which a film played alone has no server for.
 */
export function PlaybackStats({ room, solo, onClose }) {
  const [snapshot, setSnapshot] = React.useState(() => room.statsSnapshot());
  const [sent, setSent] = React.useState(null);
  const [copied, setCopied] = React.useState(false);

  React.useEffect(() => {
    room.stats.watch(true);
    const timer = window.setInterval(() => setSnapshot(room.statsSnapshot()), RefreshMs);
    // An audio context made without a press reports nothing until one comes, which a reload brings
    // the panel back without.
    const wake = () => room.stats.wake();
    window.addEventListener("pointerdown", wake);
    window.addEventListener("keydown", wake);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener("pointerdown", wake);
      window.removeEventListener("keydown", wake);
      room.stats.watch(false);
    };
  }, [room]);

  React.useEffect(() => {
    if (sent === null) return undefined;
    const timer = window.setTimeout(() => setSent(null), SentShownMs);
    return () => window.clearTimeout(timer);
  }, [sent]);
  React.useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), SentShownMs);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const send = () => {
    setSent("sending");
    room.sendStats().then(setSent, () => setSent("failed"));
  };
  const copy = () => {
    navigator.clipboard.writeText(JSON.stringify(room.statsSnapshot(), null, 2)).then(() => setCopied(true), () => {});
  };

  const { sync, room: here, network, media } = snapshot;
  const fetch = network.lastFetch;
  const audio = media.audio;
  return (
    <section className="mp-stats" aria-label="Playback stats">
      <header className="mp-stats__head">
        <span className="mp-stats__title"><Icon name="activity" size={15} />Playback stats</span>
        <Button variant="bare" className="mp-stats__close" aria-label="Close playback stats" onClick={onClose}>
          <Icon name="x" size={16} />
        </Button>
      </header>

      <Group name="Sound and picture">
        <Row label="Picture ahead of sound" value={signedMs(sync.pictureOffsetMs)} />
        <Row label="Output latency" value={sync.outputLatencyMs === null ? unknown : `${ms(sync.outputLatencyMs)}, default output`} />
        <Row label="Sound behind picture" value={signedMs(sync.soundBehindMs)} strong />
      </Group>

      {!solo && (
        <Group name="This player and the room" lead={<DriftGraph samples={snapshot.history.samples} marks={snapshot.history.marks} />}>
          <Row label="Room" value={behindRoom(here.driftSeconds)} />
          <Row label="Rate" value={`${here.rate.toFixed(2)}×${here.roomRate === null ? "" : `, room ${here.roomRate.toFixed(2)}×`}`} />
          <Row label="Server clock" value={here.clockOffsetMs === null ? unknown
            : `${signedMs(here.clockOffsetMs)}, round trip ${ms(here.roundTripMs)}, ${here.clockSamples} ${here.clockSamples === 1 ? "sample" : "samples"}`} />
          <Row label="Connection" value={`${here.connection}, ${here.reconnects} reconnects, hidden ${here.hidden}×`} />
        </Group>
      )}

      <Group name="Network">
        <Row label="Throughput" value={`${mbit(network.estimateBps)} estimated, ${mbit(network.measuredBps)} measured`} />
        <Row label="Ahead" value={`picture ${seconds(network.aheadVideoSeconds)}, sound ${seconds(network.aheadAudioSeconds)}`} />
        <Row label="Last fetch" value={fetch === null ? unknown
          : `${fetch.kind} ${bytes(fetch.bytes)}, first byte ${ms(fetch.firstByteMs)}, done ${ms(fetch.totalMs)}`} />
        <Row label="Failed fetches" value={`${network.failed}, ${network.fatal} fatal${network.lastError ? `, last ${network.lastError}` : ""}`} />
      </Group>

      <Group name="Playback">
        <Row label="Picture" value={`${height(media.playingHeight)}, wanted ${height(media.wantedHeight)}`
          + `${media.width === null ? "" : `, ${media.width}×${media.height}`}${media.videoCodec ? `, ${media.videoCodec}` : ""}`} />
        <Row label="Sound" value={audio === null ? unknown
          : `${audio.label}${audio.channels ? `, ${audio.channels} ch` : ""}${media.audioCodec ? `, ${media.audioCodec}` : ""}`} />
        <Row label="Frames dropped" value={media.framesDropped === null ? unknown : `${count(media.framesDropped)} of ${count(media.framesShown)}`} />
        <Row label="Stalls" value={`${media.stalls}, ${seconds(media.stalledMs / 1000)} in all, longest ${seconds(media.longestStallMs / 1000)}`} />
      </Group>

      <dl className="mp-stats__ids">
        <dt>Playback id</dt><dd>{snapshot.playback ?? unknown}</dd>
        <dt>Title</dt><dd>{snapshot.titleId ?? unknown}</dd>
        <dt>Build</dt><dd>{snapshot.build}</dd>
        <dt>Front door</dt><dd>{snapshot.frontDoor}, buffer ahead {snapshot.bufferAhead}</dd>
        <dt>Browser</dt><dd>{snapshot.userAgent}</dd>
      </dl>

      <footer className="mp-stats__actions">
        <Button variant="bare" className="mp-stats__action" onClick={copy}>
          <Icon name={copied ? "check" : "copy"} size={14} />{copied ? "Copied" : "Copy"}
        </Button>
        {!solo && (
          <Button variant="bare" className="mp-stats__action" disabled={sent === "sending"} onClick={send}>
            <Icon name={sent === "sent" ? "check" : "send"} size={14} />
            {sent === "sent" ? "Sent" : sent === "wait" ? "Sent moments ago" : sent === "failed" ? "Not sent" : "Send"}
          </Button>
        )}
      </footer>
    </section>
  );
}

/** A named block of rows, with what is drawn above them (`lead`), if anything. */
function Group({ name, lead = null, children }) {
  return (
    <div className="mp-stats__group">
      <h3 className="mp-stats__name">{name}</h3>
      {lead}
      <dl className="mp-stats__rows">{children}</dl>
    </div>
  );
}

function Row({ label, value, strong = false }) {
  return (
    <>
      <dt>{label}</dt>
      <dd className={strong ? "is-strong" : undefined}>{value}</dd>
    </>
  );
}

/** Seconds of drift the graph shows at least either side of level: the hard seek's line and a little more. */
const GraphFloor = 2.5;
const GraphWidth = 300;
const GraphHeight = 84;

/** What each mark on the time axis is called when pointed at. */
const MarkNames = {
  nudge: "Nudge", settled: "Settled", seek: "Seek", hidden: "Hidden", shown: "Shown",
  reconnecting: "Reconnecting", reconnected: "Reconnected", stall: "Stall", rung: "Rung", error: "Error",
};

/**
 * The room's position minus the playhead over the last five minutes, at each of the sync controller's
 * checks: above the middle line this player is behind. The band is where drift is left alone, the
 * dashed lines are where it is corrected with a seek, and each mark below is something that happened.
 */
function DriftGraph({ samples, marks }) {
  const span = HistoryMs / 1000;
  const largest = samples.reduce((most, [, drift]) => Math.max(most, Math.abs(drift)), 0);
  const reach = Math.max(GraphFloor, Math.ceil(largest));
  const plotHeight = GraphHeight - 12;
  const x = (t) => ((t + span) / span) * GraphWidth;
  const y = (drift) => plotHeight / 2 - (drift / reach) * (plotHeight / 2);
  const line = samples.map(([t, drift]) => `${x(t).toFixed(1)},${y(drift).toFixed(1)}`).join(" ");
  return (
    <figure className="mp-stats__graph">
      <svg viewBox={`0 0 ${GraphWidth} ${GraphHeight}`} preserveAspectRatio="none" role="img"
        aria-label={`Drift from the room over the last ${span / 60} minutes`}>
        <rect className="mp-stats__band" x="0" width={GraphWidth} y={y(0.5)} height={y(-0.5) - y(0.5)} />
        <line className="mp-stats__level" x1="0" x2={GraphWidth} y1={y(0)} y2={y(0)} />
        <line className="mp-stats__limit" x1="0" x2={GraphWidth} y1={y(2)} y2={y(2)} />
        <line className="mp-stats__limit" x1="0" x2={GraphWidth} y1={y(-2)} y2={y(-2)} />
        {samples.length > 1 && <polyline className="mp-stats__line" points={line} />}
        {marks.map(([t, kind, detail], i) => (
          <line key={i} className={`mp-stats__mark is-${kind}`} x1={x(t)} x2={x(t)} y1={plotHeight + 2} y2={GraphHeight}>
            <title>{MarkNames[kind]}{detail ? ` ${detail}` : ""}, {Math.round(-t)} s ago</title>
          </line>
        ))}
      </svg>
      <figcaption className="mp-stats__axis">
        <span>−{span / 60} min</span><span>±{reach} s</span><span>now</span>
      </figcaption>
    </figure>
  );
}
