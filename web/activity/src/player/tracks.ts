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

export function audioGroups(manifest: Manifest): TrackGroup[] {
  return grouped(manifest.audio, audioEntry);
}

export function defaultAudioId(manifest: Manifest): string | undefined {
  return (manifest.audio.find((track) => track.default) ?? manifest.audio[0])?.id;
}

export function findSubtitle(manifest: Manifest, id: string | null): SubtitleTrack | undefined {
  return id === null ? undefined : manifest.subtitles.find((track) => track.id === id);
}
