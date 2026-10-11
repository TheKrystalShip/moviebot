import type { AudioTrack, Manifest, SubtitleTrack, TrackKind, ViewerPreferences } from '../types';

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

/**
 * A language's primary code in its canonical spelling. A film's tracks carry the three-letter codes
 * its container wrote (`eng`, `ger`) and a person's preference the two-letter ones, and the browser
 * already knows which is which.
 */
function primaryLanguage(code: string): string {
  try {
    return (Intl.getCanonicalLocales(code)[0] ?? code).split('-')[0].toLowerCase();
  } catch {
    return code.split('-')[0].toLowerCase();
  }
}

/** Whether a track's language is the one asked for: `eng` is `en`, and `es` matches `es-419`. */
function sameLanguage(track: string, wanted: string): boolean {
  return primaryLanguage(track) === primaryLanguage(wanted);
}

/**
 * The audio and subtitle track a film starts on for one person. A choice made for the film stands, where
 * the film as this viewer holds it has the track: a copy saved on a device carries only the tracks picked
 * when it was saved. Otherwise the person's languages decide, where the film carries them, and the film's
 * own default after that. Among tracks in the person's language, the ones saved on the device come first:
 * they cost no connection. The player and the phone's remote both start from here, so a film whose tracks
 * nobody chose reads the same in both.
 */
export function startingTracks(
  manifest: Manifest,
  held: { audioTrackId?: string; subtitleTrackId?: string | null },
  preferences: Pick<ViewerPreferences, 'audioLanguage' | 'subtitleLanguage'>,
  device?: Manifest
): { audioId: string | null; subtitleId: string | null } {
  const audioHeld = manifest.audio.some((t) => t.id === held.audioTrackId) ? held.audioTrackId : undefined;
  const subtitleHeld = held.subtitleTrackId === null || manifest.subtitles.some((t) => t.id === held.subtitleTrackId)
    ? held.subtitleTrackId
    : undefined;

  const { audioLanguage, subtitleLanguage } = preferences;
  const onDevice = (ids: { id: string }[] | undefined) => (track: { id: string }) => ids?.some((t) => t.id === track.id) ?? false;
  const first = <T extends { id: string }>(tracks: T[], saved: (t: T) => boolean, wanted: (t: T) => boolean) =>
    tracks.find((t) => wanted(t) && saved(t)) ?? tracks.find(wanted);
  const audioId = audioHeld
    ?? (audioLanguage
      ? first(manifest.audio, onDevice(device?.audio), (t) => t.kind === 'feature' && sameLanguage(t.language, audioLanguage))?.id
      : undefined)
    ?? defaultAudioId(manifest) ?? null;
  const subtitleId = subtitleHeld !== undefined
    ? subtitleHeld
    : (subtitleLanguage
      ? first(manifest.subtitles, onDevice(device?.subtitles),
        (t) => t.available && t.uri !== undefined && !t.forced && sameLanguage(t.language, subtitleLanguage))?.id ?? null
      : null);
  return { audioId, subtitleId };
}
