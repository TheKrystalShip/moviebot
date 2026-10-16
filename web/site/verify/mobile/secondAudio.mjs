// Gives one film in the scratch library a second audio track, so the player's audio menu has a choice.
// The track repeats the film's first one under another label. The film is a hard-linked copy of the
// source library's, so its manifest and master are replaced with new files, never written through.
//
//   node secondAudio.mjs <film directory>
import { cpSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const film = process.argv[2];
if (!film) {
  console.error('usage: secondAudio.mjs <film directory>');
  process.exit(1);
}

const manifestPath = join(film, 'manifest.json');
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
const first = manifest.audio[0];
if (manifest.audio.length > 1) process.exit(0);

cpSync(join(film, 'a0'), join(film, 'a1'), { recursive: true, force: true });
const second = { ...first, id: 'a1', language: 'fra', label: 'Français', default: false, uri: 'a1/index.m3u8' };
manifest.audio.push(second);
rmSync(manifestPath);
writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));

const masterPath = join(film, 'master.m3u8');
const master = readFileSync(masterPath, 'utf8').split('\n');
const at = master.findIndex((l) => l.startsWith('#EXT-X-MEDIA:TYPE=AUDIO'));
master.splice(at + 1, 0,
  `#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio",NAME="${second.label}",LANGUAGE="${second.language}",DEFAULT=NO,AUTOSELECT=NO,CHANNELS="${second.channels}",URI="${second.uri}"`);
rmSync(masterPath);
writeFileSync(masterPath, master.join('\n'));
