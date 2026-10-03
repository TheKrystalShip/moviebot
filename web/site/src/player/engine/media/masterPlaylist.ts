import { environment } from '../environment';
import type { Manifest } from '../types';

/**
 * Ingest writes one media playlist per rendition and no master, so the player composes one.
 *
 * The master is what binds the audio renditions to the video: without it hls.js loads a
 * video-only playlist and the film plays silently with no track to switch to. It is handed over
 * as a blob, which has no base to resolve against, so every URI in it is absolute.
 *
 * CODECS is deliberately absent. hls.js reads the real codecs out of the fMP4 initialisation
 * segments, and a declared string that disagrees with them costs a source buffer.
 */
const AudioGroupId = 'audio';

export interface MasterPlaylist {
  playlist: string;
  /** The rendition NAME written for each manifest audio track id, which is how a chosen track is found again. */
  audioNames: Record<string, string>;
}

function quoted(value: string): string {
  return value.replace(/"/g, "'");
}

export function buildMasterPlaylist(manifest: Manifest): MasterPlaylist {
  const media = (relative: string) => environment().mediaUrl(manifest.id, relative);

  const defaultAudio = manifest.audio.find((track) => track.default) ?? manifest.audio[0];
  const audioNames: Record<string, string> = {};
  const taken = new Set<string>();

  const lines = ['#EXTM3U', '#EXT-X-VERSION:7', '#EXT-X-INDEPENDENT-SEGMENTS'];

  for (const track of manifest.audio) {
    // NAME identifies a rendition inside its group, so a repeated label needs breaking apart.
    let name = quoted(track.label);
    while (taken.has(name)) name = `${name} (${track.id})`;
    taken.add(name);
    audioNames[track.id] = name;

    const isDefault = track === defaultAudio;
    lines.push(
      `#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="${AudioGroupId}",NAME="${name}",` +
        `LANGUAGE="${quoted(track.language)}",` +
        `DEFAULT=${isDefault ? 'YES' : 'NO'},AUTOSELECT=${isDefault ? 'YES' : 'NO'},` +
        `CHANNELS="${track.channels}",URI="${media(track.uri)}"`
    );
  }

  const audioAttribute = manifest.audio.length > 0 ? `,AUDIO="${AudioGroupId}"` : '';

  for (const rendition of manifest.video.renditions) {
    // The rung's own size where it states one, since a player sizes its buffer from what it is
    // told. A film with a single rung states none, and that rung is the source's own size.
    const width = rendition.width && rendition.height ? rendition.width : manifest.video.width;
    const height = rendition.width && rendition.height ? rendition.height : manifest.video.height;

    lines.push(
      `#EXT-X-STREAM-INF:BANDWIDTH=${Math.round(rendition.bitrateKbps * 1000)},` +
        `RESOLUTION=${width}x${height}${audioAttribute}`
    );
    lines.push(media(rendition.uri));
  }

  return { playlist: `${lines.join('\n')}\n`, audioNames };
}

export function masterPlaylistUrl(master: MasterPlaylist): string {
  const blob = new Blob([master.playlist], { type: 'application/vnd.apple.mpegurl' });
  return URL.createObjectURL(blob);
}
