/** A media playlist as the device keeping a film ahead reads it (`ahead.ts`). */

export interface PlaylistSegment {
  /** The segment's path under the title's media. */
  uri: string;
  start: number;
  end: number;
}

export interface ParsedPlaylist {
  /** The initialisation segment's path under the title's media, or null for a playlist that names none. */
  init: string | null;
  segments: PlaylistSegment[];
}

/** The segments of a media playlist at `playlist` (a path under the title's media), with where each lies in the film. */
export function parsePlaylist(text: string, playlist: string): ParsedPlaylist {
  const base = playlist.includes('/') ? playlist.slice(0, playlist.lastIndexOf('/') + 1) : '';
  const local = (uri: string) => (/^[a-z]+:|^\/|\?/i.test(uri) ? null : base + uri);
  let init: string | null = null;
  const segments: PlaylistSegment[] = [];
  let duration: number | null = null;
  let at = 0;
  for (const raw of text.split('\n')) {
    const line = raw.trim();
    if (line.startsWith('#EXT-X-MAP:')) {
      const uri = /URI="([^"]+)"/.exec(line)?.[1];
      init = uri === undefined ? null : local(uri);
    } else if (line.startsWith('#EXTINF:')) {
      const seconds = Number.parseFloat(line.slice('#EXTINF:'.length));
      duration = Number.isFinite(seconds) && seconds > 0 ? seconds : null;
    } else if (line !== '' && !line.startsWith('#') && duration !== null) {
      const uri = local(line);
      if (uri !== null) segments.push({ uri, start: at, end: at + duration });
      at += duration;
      duration = null;
    }
  }
  return { init, segments };
}
