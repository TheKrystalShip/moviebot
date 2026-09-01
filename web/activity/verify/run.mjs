import { resolve } from 'node:path';
import { BASE, launch } from './harness.mjs';
import { headSuite } from './head.mjs';
import { playerSuite } from './player.mjs';
import { roomSuite } from './room.mjs';

const manifestPath = process.env.MOVIEBOT_MANIFEST
  ?? resolve(import.meta.dirname, '../../../media/clip/manifest.json');

console.log(`MovieBot player checks against ${BASE}`);

const browser = await launch();
let failed = 0;
try {
  for (const suite of [
    () => playerSuite(browser),
    () => roomSuite(browser),
    () => headSuite(browser, manifestPath)
  ]) {
    failed += (await suite()).failed;
  }
} finally {
  await browser.close();
}

console.log(failed === 0 ? '\nAll checks passed' : `\n${failed} checks failed`);
process.exit(failed === 0 ? 0 : 1);
