import React from "react";
import { Icon } from "@thekrystalship/krystal-ui";
import { formatTime } from "./engine/format.ts";

const languageNames = (() => {
  try { return new Intl.DisplayNames(undefined, { type: "language" }); } catch { return null; }
})();

/** A language code as a person reads it: `es` is Spanish, `pt-BR` is Brazilian Portuguese. */
export function languageName(code) {
  if (!code) return "Unknown language";
  try { return (languageNames && languageNames.of(code)) || code; } catch { return code; }
}

/** How many speakers a track is mixed for, in the words a receiver would use. */
function channelLayout(channels) {
  if (channels === 1) return "mono";
  if (channels === 2) return "stereo";
  if (channels === 6) return "5.1 surround";
  if (channels === 8) return "7.1 surround";
  return `${channels} channels`;
}

const mbps = (kbps) => `${(kbps / 1000).toFixed(kbps < 10000 ? 1 : 0)} Mbps`;

/** What the button for each menu says it is set to. */
export function audioValue(view) {
  const track = view.manifest && view.manifest.audio.find((t) => t.id === view.audioId);
  return track ? track.label : "";
}

export function subtitleValue(view) {
  const track = view.manifest && view.subtitleId !== null && view.manifest.subtitles.find((t) => t.id === view.subtitleId);
  return track ? track.label : "Off";
}

export function qualityValue(view) {
  if (view.quality !== null) return `${view.quality}p`;
  return view.playingHeight !== null ? `Auto · ${view.playingHeight}p` : "Auto";
}

/** Whether there is anything to choose between. One rung is no choice at all. */
export const hasQualityChoice = (view) => view.qualityLevels.length > 1;

export const MenuTitles = { subs: "Subtitles", audio: "Audio track", quality: "Quality", chapters: "Chapters" };

/**
 * One menu over the film: a popover above the control bar where the frame is wide, a sheet from
 * the bottom edge where it is not. Every menu but one holds this viewer's own choice, and the header
 * says so; the chapters move the whole room, like the scrub bar, and say that instead.
 */
export function Menu({ kind, view, room, sheet, position, onClose }) {
  const body = kind === "subs" ? <SubtitleOptions view={view} room={room} />
    : kind === "audio" ? <AudioOptions view={view} room={room} onClose={onClose} />
      : kind === "chapters" ? <ChapterOptions view={view} room={room} position={position} onClose={onClose} />
        : <QualityOptions view={view} room={room} onClose={onClose} />;
  return (
    <>
      {sheet && <div className="mp-sheet-scrim" onClick={onClose} />}
      <div className={(sheet ? "mp-sheet" : "mp-popover") + (kind === "subs" ? " mp-popover--wide" : "") + " mp-menu"} role="menu" aria-label={MenuTitles[kind]}>
        {sheet && <div className="mp-sheet__grabber" />}
        <div className="mp-menu__head">{MenuTitles[kind]}<span className="mp-menu__aside">{kind === "chapters" ? "for everyone" : "just for you"}</span></div>
        {body}
      </div>
    </>
  );
}

/** The film's chapters, the one playing marked; picking one takes the whole room there. */
function ChapterOptions({ view, room, position, onClose }) {
  const chapters = view.manifest.chapters || [];
  let current = -1;
  chapters.forEach((c, i) => { if (c.startSeconds <= position + 0.5) current = i; });
  return (
    <div className="mp-menu__list">
      {chapters.map((c, i) => (
        <Option key={i} on={i === current} label={c.title || `Chapter ${i + 1}`} sub={formatTime(c.startSeconds)}
          onPick={() => { room.seek(c.startSeconds); onClose(); }} />
      ))}
    </div>
  );
}

function Option({ on, label, sub, disabled, onPick, children }) {
  return (
    <button type="button" role="menuitemradio" aria-checked={on} disabled={disabled} title={label}
      className={"mp-option" + (on ? " is-on" : "")} onClick={onPick}>
      <span className="mp-option__check">{on && <Icon name="check" size={15} />}</span>
      <span className="mp-option__text">
        <span className="mp-option__label">{label}</span>
        {sub && <span className="mp-option__sub">{sub}</span>}
      </span>
      {children}
    </button>
  );
}

function AudioOptions({ view, room, onClose }) {
  return (
    <div className="mp-menu__list">
      {view.manifest.audio.map((track) => (
        <Option key={track.id} on={track.id === view.audioId} label={track.label}
          sub={[track.kind === "commentary" ? "Commentary" : null, channelLayout(track.channels)].filter(Boolean).join(" · ")}
          onPick={() => { room.selectAudio(track.id); onClose(); }} />
      ))}
    </div>
  );
}

function QualityOptions({ view, room, onClose }) {
  const pick = (height) => { room.selectQuality(height); onClose(); };
  return (
    <div className="mp-menu__list">
      <Option on={view.quality === null} label="Auto"
        sub={view.playingHeight !== null ? `Adaptive · ${view.playingHeight}p` : "Adaptive"} onPick={() => pick(null)} />
      {view.qualityLevels.map((level) => (
        <Option key={level.height} on={view.quality === level.height} label={`${level.height}p`} sub={mbps(level.bitrateKbps)}
          onPick={() => pick(level.height)} />
      ))}
    </div>
  );
}

/**
 * How an offered subtitle fits this copy of the film, when the checks say anything. Most uploads
 * declare nothing useful, and nothing is the ordinary answer.
 */
function fit(candidate) {
  if (candidate.checks.some((c) => c.name === "This file" && c.result === "match")) return { text: "timed for this copy", tone: "good" };
  const wrong = candidate.checks.find((c) => c.result === "mismatch");
  return wrong ? { text: wrong.detail, tone: wrong.name === "Frame rate" ? "bad" : "warn" } : null;
}

/** Where a track the room holds came from. */
function trackSource(track) {
  if (!track.available) return track.reason || "unavailable";
  if (track.source === "sidecar") return track.addedBy ? `OpenSubtitles · added by ${track.addedBy}` : "OpenSubtitles";
  return "in the film";
}

/**
 * The film's subtitles, then what OpenSubtitles offers for it.
 *
 * What the room already has is drawn at once. The index is asked whenever the menu opens, which
 * costs nothing; fetching one spends one of the day's downloads and adds it for the whole room, and
 * the person who fetched it is switched to it. The film's own list is read again on opening too,
 * because what it gains is pushed as it lands and this is the guarantee behind the push.
 */
function SubtitleOptions({ view, room }) {
  const manifest = view.manifest;
  const [found, setFound] = React.useState(null);
  const [failure, setFailure] = React.useState(null);
  const [fetching, setFetching] = React.useState(null);
  const film = manifest.id;

  React.useEffect(() => {
    let live = true;
    setFound(null);
    setFailure(null);
    room.refreshManifest().catch(() => {});
    room.searchSubtitles()
      .then((answer) => { if (live) setFound(answer); })
      .catch((error) => { if (live) setFailure(error instanceof Error ? error.message : "OpenSubtitles did not answer."); });
    return () => { live = false; };
  }, [film, room]);

  const take = async (candidate) => {
    setFetching(candidate.fileId);
    setFailure(null);
    try {
      const track = await room.fetchSubtitle(candidate.fileId);
      room.selectSubtitle(track.id);
    } catch (error) {
      setFailure(error instanceof Error ? error.message : "That subtitle could not be fetched.");
    } finally {
      setFetching(null);
    }
  };

  const held = new Set(manifest.subtitles.map((t) => t.id));
  const offered = found ? found.candidates.filter((c) => !held.has(`os${c.fileId}`)) : [];

  return (
    <>
      <div className="mp-menu__list">
        <Option on={view.subtitleId === null} label="Off" onPick={() => room.selectSubtitle(null)} />
        {manifest.subtitles.map((track) => (
          <Option key={track.id} on={track.id === view.subtitleId} label={track.label} sub={trackSource(track)}
            disabled={!track.available || track.uri === undefined} onPick={() => room.selectSubtitle(track.id)} />
        ))}
      </div>
      <div className="mp-menu__section">OpenSubtitles</div>
      <div className="mp-menu__list">
        {failure && <div className="mp-menu__message">{failure}</div>}
        {!failure && found === null && <div className="mp-menu__message"><Icon name="loader-circle" size={14} className="mp-spin" />Looking</div>}
        {!failure && found && offered.length === 0 && <div className="mp-menu__message">{found.explanation || "Nothing more to offer for this film."}</div>}
        {offered.map((candidate) => (
          <button key={candidate.fileId} type="button" className="mp-option mp-option--candidate" disabled={fetching !== null}
            onClick={() => void take(candidate)}>
            <span className="mp-option__check" />
            <span className="mp-option__text">
              <span className="mp-option__label">{languageName(candidate.language)}</span>
              <span className="mp-option__release">{candidate.release || "Release not stated"}</span>
              <span className={"mp-option__stats" + (fit(candidate) ? " is-" + fit(candidate).tone : "")}>
                {[
                  typeof candidate.rating === "number" ? `★ ${candidate.rating.toFixed(1)}` : null,
                  `${candidate.downloadCount.toLocaleString()} downloads`,
                  fit(candidate) && fit(candidate).text,
                ].filter(Boolean).join(" · ")}
              </span>
            </span>
            {fetching === candidate.fileId
              ? <span className="mp-option__state is-loading"><Icon name="loader-circle" size={15} className="mp-spin" />downloading</span>
              : <span className="mp-option__state"><Icon name="download" size={15} /></span>}
          </button>
        ))}
      </div>
    </>
  );
}
