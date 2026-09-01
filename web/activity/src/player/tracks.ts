import type { AudioTrack, Manifest, SubtitleTrack, TrackKind } from '../types';

export interface TrackEntry {
  /** The manifest track id, or null for the entry that turns subtitles off. */
  id: string | null;
  label: string;
  /** Why an entry cannot be chosen, shown beside it. */
  detail?: string;
  available: boolean;
}

export interface TrackGroup {
  title: string;
  entries: TrackEntry[];
}

const GroupTitles: Record<TrackKind, string> = {
  feature: 'Feature',
  commentary: 'Commentary'
};

const Reasons: Record<string, string> = {
  'needs-ocr': 'picture-based, needs OCR'
};

function reasonText(track: SubtitleTrack): string | undefined {
  if (track.available) return undefined;
  const reason = track.reason ?? 'unavailable';
  return Reasons[reason] ?? reason;
}

function group<T extends { kind: TrackKind }>(tracks: T[], kind: TrackKind): T[] {
  return tracks.filter((track) => track.kind === kind);
}

function grouped<T extends { kind: TrackKind }>(tracks: T[], toEntry: (track: T) => TrackEntry): TrackGroup[] {
  return (['feature', 'commentary'] as TrackKind[])
    .map((kind) => ({ title: GroupTitles[kind], entries: group(tracks, kind).map(toEntry) }))
    .filter((g) => g.entries.length > 0);
}

function audioEntry(track: AudioTrack): TrackEntry {
  return { id: track.id, label: track.label, available: true };
}

function subtitleEntry(track: SubtitleTrack): TrackEntry {
  return {
    id: track.id,
    label: track.forced ? `${track.label} (forced)` : track.label,
    detail: reasonText(track),
    available: track.available
  };
}

export function audioGroups(manifest: Manifest): TrackGroup[] {
  return grouped(manifest.audio, audioEntry);
}

/**
 * A track that cannot be played is listed rather than dropped: a language simply missing from
 * the menu reads as a bug, while one shown with its reason reads as an answer.
 */
export function subtitleGroups(manifest: Manifest): TrackGroup[] {
  return [
    { title: '', entries: [{ id: null, label: 'Off', available: true }] },
    ...grouped(manifest.subtitles, subtitleEntry)
  ];
}

export function defaultAudioId(manifest: Manifest): string | undefined {
  return (manifest.audio.find((track) => track.default) ?? manifest.audio[0])?.id;
}

export function findSubtitle(manifest: Manifest, id: string | null): SubtitleTrack | undefined {
  return id === null ? undefined : manifest.subtitles.find((track) => track.id === id);
}
