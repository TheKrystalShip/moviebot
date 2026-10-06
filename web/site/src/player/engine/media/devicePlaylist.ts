/**
 * The playlists a film saved on the device plays from, built from the segment index the API answered
 * with the save (`docs/api-contract.md`, "Save offline").
 *
 * A saved track is one file: the rendition's init segment at offset 0 and every segment after it,
 * joined. A segment's offset is the init segment's length plus the lengths of every segment before
 * it, so each one is a byte range of that file and the playlist says so; the loader answers a range
 * with a slice of the file.
 */

/** Where each segment of a saved track lies in its file. */
export interface SegmentIndex {
  initBytes: number;
  segmentSeconds: number[];
  segmentBytes: number[];
}

/** One byte range of a track's file: `start` inclusive, `end` exclusive. */
export interface ByteRange {
  start: number;
  end: number;
}

/** The name every saved track's segments are listed under, relative to its playlist. */
export const TrackFileName = 'track.mp4';

/** Whether an index describes a track that can be played: one length per segment, none negative. */
export function isIndex(index: SegmentIndex | null | undefined): index is SegmentIndex {
  if (index === null || index === undefined) return false;
  const { initBytes, segmentSeconds, segmentBytes } = index;
  return Number.isInteger(initBytes) && initBytes > 0
    && Array.isArray(segmentSeconds) && Array.isArray(segmentBytes)
    && segmentSeconds.length > 0 && segmentSeconds.length === segmentBytes.length
    && segmentSeconds.every((s) => Number.isFinite(s) && s > 0)
    && segmentBytes.every((b) => Number.isInteger(b) && b > 0);
}

/** Each segment's range in the file, in order. */
export function segmentRanges(index: SegmentIndex): ByteRange[] {
  const ranges: ByteRange[] = [];
  let offset = index.initBytes;
  for (const length of index.segmentBytes) {
    ranges.push({ start: offset, end: offset + length });
    offset += length;
  }
  return ranges;
}

/** The file's length the index describes: the init segment and every segment. */
export function trackBytes(index: SegmentIndex): number {
  return index.segmentBytes.reduce((sum, length) => sum + length, index.initBytes);
}

/**
 * A media playlist for one saved track, every segment a byte range of `TrackFileName` beside it.
 * The film is whole, so it is a VOD playlist with an end.
 */
export function mediaPlaylist(index: SegmentIndex): string {
  const target = Math.ceil(Math.max(...index.segmentSeconds));
  const lines = [
    '#EXTM3U',
    '#EXT-X-VERSION:7',
    `#EXT-X-TARGETDURATION:${target}`,
    '#EXT-X-MEDIA-SEQUENCE:0',
    '#EXT-X-PLAYLIST-TYPE:VOD',
    `#EXT-X-MAP:URI="${TrackFileName}",BYTERANGE="${index.initBytes}@0"`
  ];
  segmentRanges(index).forEach((range, i) => {
    lines.push(`#EXTINF:${index.segmentSeconds[i].toFixed(6)},`);
    lines.push(`#EXT-X-BYTERANGE:${range.end - range.start}@${range.start}`);
    lines.push(TrackFileName);
  });
  lines.push('#EXT-X-ENDLIST');
  return `${lines.join('\n')}\n`;
}

/** The directory a manifest-relative rendition URI names: `v1` for `v1/index.m3u8`. */
export function renditionDirectory(uri: string): string {
  const slash = uri.lastIndexOf('/');
  return slash < 0 ? '' : uri.slice(0, slash);
}

/** Where a saved track's file is listed, relative to the title's media directory. */
export function trackFileUri(uri: string): string {
  const directory = renditionDirectory(uri);
  return directory === '' ? TrackFileName : `${directory}/${TrackFileName}`;
}
