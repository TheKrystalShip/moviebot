// The wire shapes MovieBot.Api serves. Names match the JSON exactly.

export type TitleStatus = 'transcoding' | 'ready' | 'failed';
export type TrackKind = 'feature' | 'commentary';
export type SubtitleSource = 'embedded-text' | 'sidecar' | 'bitmap';

export interface TitleSummary {
  id: string;
  title: string;
  durationSeconds: number;
  status: TitleStatus;
  headSeconds?: number;
  poster?: string;
}

export interface Rendition {
  name: string;
  bitrateKbps: number;
  /** What this rung is encoded at. Absent on a film with a single rung, which is the source's
   *  own size. */
  width?: number;
  height?: number;
  uri: string;
}

export interface VideoInfo {
  width: number;
  height: number;
  sourceCodec: string;
  sourceHdr?: string;
  renditions: Rendition[];
}

export interface AudioTrack {
  id: string;
  kind: TrackKind;
  language: string;
  label: string;
  channels: number;
  default?: boolean;
  uri: string;
}

/** Somebody watched the film with this track and confirmed it fits. The only ground truth there is. */
export interface SubtitlePin {
  pinnedBy: string;
  pinnedAt: string;
  /** How far in it had been watched. Drift only shows late, so a pin near the end says more. */
  watchedFraction?: number;
}

export interface SubtitleTrack {
  id: string;
  kind: TrackKind;
  language: string;
  label: string;
  hearingImpaired?: boolean;
  forced?: boolean;
  source: SubtitleSource;
  available: boolean;
  reason?: string;
  uri?: string;
  pin?: SubtitlePin;
  /** Only a fetched track carries these; one that came out of the film needed nothing done to it. */
  appliedShiftSeconds?: number;
  alignedFraction?: number;
  addedBy?: string;
}

/**
 * Which film this is, as the catalogue names it, as opposed to which file it came from. The
 * artwork's source address is left out: the page takes artwork from this server only.
 */
export interface FilmIdentity {
  imdbId?: string;
  name?: string;
  year?: number;
  starring?: string;
}

/**
 * What the film was made from, used to judge whether a subtitle found elsewhere was timed against
 * this exact release or against some other one. Absent while a source is still arriving.
 */
export interface SourceFingerprint {
  release: string;
  sizeBytes: number;
  movieHash?: string;
  frameRate: number;
}

/** One thing that was compared between a subtitle and the film, as the picker shows it. */
export interface SubtitleCheck {
  name: string;
  /** Nothing to compare is its own answer and never counts against a candidate. */
  result: 'unknown' | 'match' | 'mismatch';
  detail: string;
}

/** A subtitle on offer. Everything here is free to look at; only fetching one costs an allowance. */
export interface SubtitleCandidate {
  fileId: number;
  release: string;
  language: string;
  fps?: number;
  downloadCount: number;
  /** OpenSubtitles' rating out of ten; absent when nobody has voted on it. */
  rating?: number;
  hearingImpaired: boolean;
  trusted: boolean;
  score: number;
  checks: SubtitleCheck[];
}

export interface SubtitleSearch {
  candidates: SubtitleCandidate[];
  remainingDownloads?: number;
  /** Why the list is empty, when it is. An empty menu otherwise reads as a broken search. */
  explanation?: string;
}

export interface EpisodePlace {
  season: number;
  number: number;
}

/** The episodes either side of one the library holds, and what each is called where that is known. */
export interface EpisodeNeighbours {
  previous?: TitleSummary;
  next?: TitleSummary;
  name?: string;
  previousName?: string;
  nextName?: string;
  /** The episode right after this one when it is not here and the tracker offers it: what next fetches. */
  fetchable?: { season: number; number: number };
  fetchableName?: string;
}

/** One point in the film with a name, as the container already records it. */
export interface Chapter {
  startSeconds: number;
  title?: string;
}

/**
 * Every preview frame in one image. A request per frame would spend the whole hover fetching;
 * the browser holds one sheet and moves a window over it for nothing.
 */
export interface ThumbnailStrip {
  uri: string;
  intervalSeconds: number;
  columns: number;
  rows: number;
  width: number;
  height: number;
  count: number;
}

export interface Manifest {
  id: string;
  title: string;
  durationSeconds: number;
  status: TitleStatus;
  headSeconds?: number;
  error?: string;
  poster?: string;
  film?: FilmIdentity;
  /** An episode's place in its show, which `film` then names; absent for a film. */
  episode?: EpisodePlace;
  source?: SourceFingerprint;
  /** Languages the source carries that were not extracted, so an absent one is explained. */
  otherLanguages?: string[];
  chapters?: Chapter[];
  thumbnails?: ThumbnailStrip;
  /** The HLS master playlist binding audio to video, when the ingest wrote one. */
  master?: string;
  video: VideoInfo;
  audio: AudioTrack[];
  subtitles: SubtitleTrack[];
}

export interface Actor {
  userId: string;
  displayName: string;
}

export interface SessionState {
  sessionId: string;
  titleId?: string;
  paused: boolean;
  /** True as of anchorUtc. Never read this as a ticking number; derive from the anchor. */
  positionSeconds: number;
  anchorUtc: string;
  rate: number;
  updatedBy?: Actor;
  revision: number;
  /** Which run of the room the revision belongs to. A room built again counts from zero again. */
  epoch: string;
  /** How far the transcode has reached. Absent once the title is ready. */
  transcodeHead?: number;
  /** The film the room waits on while it is fetched for a Watch now. Absent once it is in the room. */
  preparing?: RoomPreparing;
}

/** A film being fetched for a room: found, arriving, or not coming. */
export interface RoomPreparing {
  imdbId: string;
  name: string;
  stage: 'finding' | 'downloading' | 'failed';
  /** The release as its resolution and source: "1080p WEB". */
  release?: string;
  /** From 0 to 1, while downloading. */
  progress?: number;
  /** Why it will not arrive: notOffered, refused, unavailable or notWatchable. */
  problem?: string;
  refusal?: string;
}

export interface SessionStatePush {
  state: SessionState;
  serverTime: string;
}

export interface SeekClamped {
  requestedSeconds: number;
  grantedSeconds: number;
  headSeconds: number;
}

/** How one person watches, kept by the API so it follows them between the site and the Activity. */
export interface ViewerPreferences {
  /** The subtitle language a film starts with when it carries one; absent for none. */
  subtitleLanguage?: string | null;
  /** The audio language a film starts with when it carries one; absent for the film's own. */
  audioLanguage?: string | null;
  /** The height a film is held at; absent to let the connection decide. */
  qualityHeight?: number | null;
}

export interface Participant {
  userId: string;
  displayName: string;
  /** Their player has been waiting for the film for longer than a stutter. */
  buffering?: boolean;
}

/** Somebody who can be asked into a room. */
export interface Person {
  userId: string;
  name: string;
  /** When any of their signed-in devices was last used. */
  lastSeenAt?: string;
}
