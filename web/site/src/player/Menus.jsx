import React from "react";
import { Button, Drawer, Icon, IconButton, Menu, MenuGroup, MenuItem, MenuRadioGroup, MenuRadioItem, Popover } from "@thekrystalship/krystal";
import { api } from "./engine/api.ts";
import { useCan } from "../session.js";
import { environment } from "./engine/environment.ts";
import { formatTime } from "./engine/format.ts";
import { SubtitleStyleEditor } from "./SubtitleStyleEditor.jsx";
import { EpisodeRow } from "../components/EpisodeRow.jsx";
import { useDownloadMarks, useOffersSaving } from "../components/DownloadButton.jsx";

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
  if (view.quality !== null) {
    return view.playingHeight !== null && view.playingHeight < view.quality
      ? `Up to ${view.quality}p · ${view.playingHeight}p`
      : `${view.quality}p`;
  }
  return view.playingHeight !== null ? `Auto · ${view.playingHeight}p` : "Auto";
}

/** Whether there is anything to choose between. One rung is no choice at all. */
export const hasQualityChoice = (view) => view.qualityLevels.length > 1;

export const MenuTitles = {
  subs: "Subtitles", audio: "Audio track", quality: "Quality", chapters: "Chapters", episodes: "Episodes",
  listen: "Audio & Subtitles", settings: "Settings",
};

/** Whether the film has a season to move around in: an episode, in a room, for somebody who may move it. */
export const hasEpisodes = (view) => Boolean(view.manifest && view.manifest.episode && view.manifest.film
  && view.manifest.film.imdbId && !view.solo && !view.viewOnly);

/** What the episode menu's button says: the episode playing. */
export const episodeValue = (view) => `S${view.manifest.episode.season} · E${view.manifest.episode.number}`;

/**
 * One button of the control row that opens a menu, naming the choice it is set to, or with no `value`
 * the menu itself, as a phone's row does.
 */
export function MenuButton({ kind, icon, value = null, open, compact, onClick }) {
  return (
    <Button variant="bare" className={"mp-menu-button" + (open ? " is-open" : "") + (compact ? " is-icon" : "")}
      aria-haspopup="true" aria-expanded={open} aria-label={value === null ? MenuTitles[kind] : `${MenuTitles[kind]}: ${value}`}
      title={MenuTitles[kind]} onClick={onClick}>
      <Icon name={icon} size={18} />
      {!compact && <span className="mp-menu-button__value">{value === null ? MenuTitles[kind] : value}</span>}
    </Button>
  );
}

/**
 * A row of a list that has no menu around it (a sheet) is reached by Tab and by a remote's arrows, which
 * leave out what sits at `tabindex=-1`. Every row of the player's menus is a `mp-menu__item`.
 */
export const reach = { tabIndex: 0, className: "mp-menu__item" };

/**
 * One menu over the film: a panel hanging above the control bar where the frame is wide, a sheet from
 * the bottom edge where it is not. Every menu but two holds this viewer's own choice; the chapters
 * and the episodes move the whole room, like the scrub bar.
 *
 * The panel is drawn inside `container`, the player's frame, so it shows while the frame is full
 * screen, and hangs from `anchorRef`. On a television it is a column down the frame's right edge.
 *
 * A phone's remote draws the same menu over the page rather than inside a frame (`contained` false) and
 * names the screen it is aimed at (`aimedAt`).
 *
 * A phone's player has two menus of its own, as the big streaming apps have: Audio & Subtitles
 * (`listen`), the audio tracks over the subtitles, and Settings, whose `settings` entries each open
 * their own menu through `onPick`.
 */
export function PlayerMenu({ kind, view, room, sheet, position, styling, onStyling, onClose, contained = true, aimedAt = null, settings = [], onPick, anchorRef, container }) {
  const editing = (kind === "subs" || kind === "listen") && styling;
  const body = editing ? <SubtitleStyleEditor style={view.preferences.subtitleStyle} onChange={(style) => room.setSubtitleStyle(style)} />
    : kind === "listen" ? (
      <>
        {view.manifest.audio.length > 1 && (
          <MenuGroup label={MenuTitles.audio}>
            <AudioOptions view={view} room={room} onClose={onClose} />
          </MenuGroup>
        )}
        <MenuGroup label={view.manifest.audio.length > 1 ? MenuTitles.subs : undefined}>
          <SubtitleOptions view={view} room={room} onStyle={() => onStyling(true)} />
        </MenuGroup>
      </>
    )
    : kind === "settings" ? <SettingsOptions entries={settings} onPick={onPick} />
    : kind === "subs" ? <SubtitleOptions view={view} room={room} onStyle={() => onStyling(true)} />
      : kind === "audio" ? <AudioOptions view={view} room={room} onClose={onClose} />
        : kind === "chapters" ? <ChapterOptions view={view} room={room} position={position} onClose={onClose} />
          : kind === "episodes" ? <EpisodeOptions view={view} room={room} onClose={onClose} />
            : <QualityOptions view={view} room={room} onClose={onClose} />;
  const title = editing ? "Subtitle style" : MenuTitles[kind];
  const back = editing ? { label: "Back to subtitles", onClick: () => onStyling(false) }
    : aimedAt ? { label: "Back to the remote", onClick: onClose } : null;
  const listed = !editing && kind !== "episodes";
  if (sheet) {
    return (
      <Drawer contained={contained} theme={contained ? undefined : "dark"} role="menu" title={title} back={back} onClose={onClose}
        action={aimedAt && <span className="mp-menu__aim"><Icon name="tv" size={13} />{aimedAt}</span>}
        className={"mp-menu" + (editing ? " is-styling" : "") + (contained ? "" : " mp-aimed")}>
        {listed ? <div className="mp-menu__list">{body}</div> : body}
      </Drawer>
    );
  }
  const panel = {
    open: true, anchorRef, container, placement: "top-end", onOpenChange: (open) => { if (!open) onClose(); },
    className: "mp-menu mp-panel" + (kind === "subs" || kind === "listen" ? " mp-panel--wide" : kind === "episodes" ? " mp-panel--episodes" : ""),
    ...(environment().tv ? { style: TvColumn } : {}),
  };
  const head = (
    <>
      {back && <IconButton variant="ghost" size="xs" icon="arrow-left" label={back.label} className="mp-menu__back" onClick={back.onClick} />}
      {title}
    </>
  );
  return listed
    ? <Menu {...panel} label={title} header={head}>{body}</Menu>
    : <Popover {...panel} title={head}>{body}</Popover>;
}

/** Where a television draws the panel: the frame's right edge, top to bottom. */
const TvColumn = { position: "absolute", top: 0, right: 0, bottom: 0 };

/**
 * The film's chapters, the one playing marked; picking one takes the whole room there. A guest reads
 * them and picks nothing.
 */
function ChapterOptions({ view, room, position, onClose }) {
  const chapters = view.manifest.chapters || [];
  let current = -1;
  chapters.forEach((c, i) => { if (c.startSeconds <= position + 0.5) current = i; });
  return (
    <MenuRadioGroup value={current}>
      {chapters.map((c, i) => (
        <MenuRadioItem key={i} value={i} sub={formatTime(c.startSeconds)} readOnly={view.viewOnly} {...reach}
          onSelect={() => { room.seek(c.startSeconds); onClose(); }}>
          {c.title || `Chapter ${i + 1}`}
        </MenuRadioItem>
      ))}
    </MenuRadioGroup>
  );
}

/** Each show's seasons as last read, so the menu draws at once while it is read again. */
const seasonsRead = new Map();

/**
 * Every episode of the season the room holds, as the show's page lists it (`EpisodeRow`), the one the
 * room holds marked, as its state says, and the list opened on it. Choosing an episode asks the room
 * for it, and the server decides how it gets there, as it does for next; an episode the library lacks
 * can be chosen by a viewer who may download. The season is read again whenever the menu opens, so
 * what the library gained since is there.
 */
function EpisodeOptions({ view, room, onClose }) {
  const manifest = view.manifest;
  const imdbId = manifest.film.imdbId;
  const seasonNumber = manifest.episode.season;
  const [show, setShow] = React.useState(() => seasonsRead.get(imdbId) || null);
  const [failure, setFailure] = React.useState(null);
  const list = React.useRef(null);
  const marks = useDownloadMarks();
  const saving = useOffersSaving();
  const mayFetch = useCan("cinema:download");

  React.useEffect(() => {
    let live = true;
    api.show(imdbId).then((read) => {
      seasonsRead.set(imdbId, read);
      if (live) setShow(read);
    }).catch(() => { if (live && !seasonsRead.has(imdbId)) setFailure("The season could not be read."); });
    return () => { live = false; };
  }, [imdbId]);

  const season = show && show.seasons.find((s) => s.number === seasonNumber);
  const episodes = season ? season.episodes : [];
  const state = view.state || {};
  const playing = (e) => (state.titleId ? e.titleId === state.titleId
    : Boolean(state.preparing && state.preparing.season === e.season && state.preparing.episode === e.number));

  // The list opens on the episode playing, however far down the season it is.
  const drawn = episodes.length > 0;
  React.useLayoutEffect(() => {
    const on = drawn && list.current && list.current.querySelector(".kc-episode.is-current");
    if (on) on.scrollIntoView({ block: "center" });
  }, [drawn]);

  const pick = (e) => {
    if (playing(e)) return onClose;
    if ((e.titleId && e.status !== "failed") || mayFetch) return () => { room.playEpisode(e.season, e.number); onClose(); };
    return null;
  };

  return (
    <div className="mp-episodes" ref={list}>
      {failure && <div className="mp-menu__message">{failure}</div>}
      {!failure && show === null && <div className="mp-menu__message"><Icon name="loader-circle" size={14} className="mp-spin" />Looking</div>}
      {episodes.map((e) => (
        <EpisodeRow key={e.number} episode={e} current={playing(e)} onPick={pick(e)}
          download={saving ? (e.titleId && marks.film(e.titleId)) || marks.want(imdbId, e.season, e.number) : null} />
      ))}
    </div>
  );
}

/** Settings: each entry the name of a menu and what it is set to, opening that menu. */
function SettingsOptions({ entries, onPick }) {
  return entries.map(({ kind, icon, value }) => (
    <MenuItem key={kind} icon={icon} aside={value} closeOnSelect={false} {...reach} onSelect={() => onPick(kind)}>{MenuTitles[kind]}</MenuItem>
  ));
}

function AudioOptions({ view, room, onClose }) {
  return (
    <MenuRadioGroup value={view.audioId}>
      {view.manifest.audio.map((track) => (
        <MenuRadioItem key={track.id} value={track.id} {...reach}
          sub={[track.kind === "commentary" ? "Commentary" : null, channelLayout(track.channels)].filter(Boolean).join(" · ")}
          onSelect={() => { room.selectAudio(track.id); onClose(); }}>
          {track.label}
        </MenuRadioItem>
      ))}
    </MenuRadioGroup>
  );
}

function QualityOptions({ view, room, onClose }) {
  const pick = (height) => { room.selectQuality(height); onClose(); };
  return (
    <MenuRadioGroup value={view.quality}>
      <MenuRadioItem value={null} sub={view.playingHeight !== null ? `Adaptive · ${view.playingHeight}p` : "Adaptive"} {...reach}
        onSelect={() => pick(null)}>Auto</MenuRadioItem>
      {view.qualityLevels.map((level) => (
        <MenuRadioItem key={level.height} value={level.height} sub={mbps(level.bitrateKbps)} {...reach}
          onSelect={() => pick(level.height)}>{`${level.height}p`}</MenuRadioItem>
      ))}
    </MenuRadioGroup>
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
  return "built in";
}

/**
 * The film's subtitles, then what OpenSubtitles offers for it.
 *
 * What the room already has is drawn at once. The index is asked whenever the menu opens, which
 * costs nothing; fetching one spends one of the day's downloads and adds it for the whole room, and
 * the person who fetched it is switched to it. The film's own list is read again on opening too,
 * because what it gains is pushed as it lands and this is the guarantee behind the push.
 */
function SubtitleOptions({ view, room, onStyle }) {
  const manifest = view.manifest;
  const [found, setFound] = React.useState(null);
  const [failure, setFailure] = React.useState(null);
  const [fetching, setFetching] = React.useState(null);
  const film = manifest.id;

  // A film played alone has the subtitles saved with it, and a guest the ones the room has; fetching
  // more is for somebody in the room with an account.
  const alone = view.solo || view.viewOnly;
  React.useEffect(() => {
    if (alone) return undefined;
    let live = true;
    setFound(null);
    setFailure(null);
    room.refreshManifest().catch(() => {});
    room.searchSubtitles()
      .then((answer) => { if (live) setFound(answer); })
      .catch((error) => { if (live) setFailure(error instanceof Error ? error.message : "OpenSubtitles did not answer."); });
    return () => { live = false; };
  }, [film, room, alone]);

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
      <MenuRadioGroup value={view.subtitleId}>
        <MenuRadioItem value={null} closeOnSelect={false} {...reach} onSelect={() => room.selectSubtitle(null)}>Off</MenuRadioItem>
        {manifest.subtitles.map((track) => (
          <MenuRadioItem key={track.id} value={track.id} sub={trackSource(track)} closeOnSelect={false} {...reach}
            disabled={!track.available || track.uri === undefined} onSelect={() => room.selectSubtitle(track.id)}>
            {track.label}
          </MenuRadioItem>
        ))}
      </MenuRadioGroup>
      <MenuItem icon="type" aside={<Icon name="chevron-right" size={15} />} closeOnSelect={false} {...reach} onSelect={onStyle}>Style</MenuItem>
      {!alone && (
        <MenuGroup label="OpenSubtitles">
          {failure && <div className="mp-menu__message">{failure}</div>}
          {!failure && found === null && <div className="mp-menu__message"><Icon name="loader-circle" size={14} className="mp-spin" />Looking</div>}
          {!failure && found && offered.length === 0 && <div className="mp-menu__message">{found.explanation || "Nothing more to offer for this film."}</div>}
          {offered.map((candidate) => (
            <MenuItem key={candidate.fileId} icon="download" disabled={fetching !== null} closeOnSelect={false} {...reach}
              aside={fetching === candidate.fileId ? <span className="mp-candidate__state"><Icon name="loader-circle" size={15} className="mp-spin" />downloading</span> : undefined}
              onSelect={() => void take(candidate)}
              sub={(
                <>
                  <span className="mp-candidate__release">{candidate.release || "Release not stated"}</span>
                  <span className={"mp-candidate__stats" + (fit(candidate) ? " is-" + fit(candidate).tone : "")}>
                    {[
                      typeof candidate.rating === "number" ? `★ ${candidate.rating.toFixed(1)}` : null,
                      `${candidate.downloadCount.toLocaleString()} downloads`,
                      fit(candidate) && fit(candidate).text,
                    ].filter(Boolean).join(" · ")}
                  </span>
                </>
              )}>
              {languageName(candidate.language)}
            </MenuItem>
          ))}
        </MenuGroup>
      )}
    </>
  );
}
