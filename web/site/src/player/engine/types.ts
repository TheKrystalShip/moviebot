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
  /** How far this rung has reached while the title transcodes. Absent once ready, and on a
   *  manifest that records one head, which says the rung is at `headSeconds`. */
  headSeconds?: number;
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

/** One episode of a show as its description lists it, joined with the library and the viewer's progress. */
export interface ShowEpisode {
  season: number;
  number: number;
  name?: string;
  airDate?: string;
  runtimeMinutes?: number;
  rating?: number;
  /** The episode's still, served from here. */
  stillUrl?: string;
  /** The library's title for the episode; absent when the library does not hold it. */
  titleId?: string;
  status?: TitleStatus;
  durationSeconds?: number;
  progress?: { positionSeconds: number; finished: boolean };
}

/** A show's seasons, each with every episode the film database or the library knows of. */
export interface ShowSeasons {
  imdbId: string;
  title: string;
  seasons: { number: number; name?: string; episodes: ShowEpisode[] }[];
}

/** A release the tracker offers, as far as choosing one to fetch needs it. */
export interface ReleaseOffer {
  torrentId: number;
  resolution?: string;
}

/** What the tracker offers for a show, season by season: packs, and single episodes, each best first. */
export interface ShowOffers {
  seasons: { season: number; packs: ReleaseOffer[]; episodes: { episode: number; releases: ReleaseOffer[] }[] }[];
}

/** One point in the film with a name, as the container already records it. */
export interface Chapter {
  startSeconds: number;
  title?: string;
}

/** A stretch of an episode that can be skipped: its opening credits, or a recap of the last one. */
export interface Marker {
  kind: 'intro' | 'recap';
  startSeconds: number;
  endSeconds: number;
  /** What the ingest found it from: the container's chapters, or the audio itself. */
  from: 'chapter' | 'audio';
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
  /** The shortest playlist: how far every rung and the audio have reached. */
  headSeconds?: number;
  /** How far the audio has reached, where the ingest tracks it apart from the rungs. */
  audioHeadSeconds?: number;
  error?: string;
  poster?: string;
  film?: FilmIdentity;
  /** An episode's place in its show, which `film` then names; absent for a film. */
  episode?: EpisodePlace;
  source?: SourceFingerprint;
  /** Languages the source carries that were not extracted, so an absent one is explained. */
  otherLanguages?: string[];
  chapters?: Chapter[];
  /** Skippable stretches; absent for a title with none. */
  markers?: Marker[];
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
  /** How far every rung of the transcode has reached. Absent once the title is ready. */
  transcodeHead?: number;
  /** How far each rung has reached, where the manifest tracks them apart. Absent otherwise. */
  rungHeads?: RungHead[];
  /** How far the audio has reached, where the manifest tracks it apart. Absent otherwise. */
  audioHead?: number;
  /** The film the room waits on while it is fetched for a Watch now. Absent once it is in the room. */
  preparing?: RoomPreparing;
}

/** One rung's reach, by the height the master playlist advertises it at. */
export interface RungHead {
  height: number;
  seconds: number;
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
  /** Skip an intro or a recap on its own when this viewer is alone in the room; absent for off. */
  skipIntros?: boolean;
  /** How subtitles are drawn; absent, or a field of it absent, for the player's own look. */
  subtitleStyle?: SubtitleStyle | null;
}

/** A person's subtitle look. The API keeps each field in range; `subtitleStyle.js` holds the defaults. */
export interface SubtitleStyle {
  /** Percent of the player's own size, 50 to 200. */
  size?: number | null;
  font?: 'sans' | 'serif' | 'mono' | 'casual' | null;
  bold?: boolean | null;
  /** `#rrggbb`. */
  textColor?: string | null;
  edge?: 'none' | 'outline' | 'shadow' | 'raised' | 'depressed' | null;
  edgeColor?: string | null;
  backgroundColor?: string | null;
  /** 0, no box at all, to 1. */
  backgroundOpacity?: number | null;
  /** Dialogue's distance above the bottom of the frame, percent of its height, 0 to 30. */
  position?: number | null;
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
