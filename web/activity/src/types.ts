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

export interface Manifest {
  id: string;
  title: string;
  durationSeconds: number;
  status: TitleStatus;
  headSeconds?: number;
  error?: string;
  poster?: string;
  source?: SourceFingerprint;
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
  /** How far the transcode has reached. Absent once the title is ready. */
  transcodeHead?: number;
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

export interface Participant {
  userId: string;
  displayName: string;
}
