import type { Manifest } from '../types';
import type { QualityLevel } from './stream';

/**
 * The rungs of a film's ladder as the player offers them, tallest first: what the stream lists once
 * its master playlist is parsed, read from the manifest alone. A rendition states its own size where
 * it has one, and the source's otherwise (`masterPlaylist.ts`).
 */
export function qualityLevelsOf(manifest: Manifest): QualityLevel[] {
  return manifest.video.renditions
    .map((rendition) => ({
      height: rendition.width && rendition.height ? rendition.height : manifest.video.height,
      bitrateKbps: Math.round(rendition.bitrateKbps)
    }))
    .sort((a, b) => b.height - a.height);
}
