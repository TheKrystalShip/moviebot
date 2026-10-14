// make-icons.mjs — author Krystal Cinema's install artwork.
//
//   node scripts/make-icons.mjs        (needs rsvg-convert; not part of any build)
//
// An authoring tool, run by hand when the artwork changes. Its output is committed under public/, and
// nothing in `npm run build` invokes it: a build must not depend on a system package that only this
// one task needs.
//
// The mark is the Movies entry's lucide `clapperboard` in --krystal-teal on a --krystal-teal-dim
// tile over --canvas, the family the Control Panel's assistant icon belongs to, so Cinema installs as
// an app of its own beside the Control Panel rather than as a second copy of the ship's mark. The
// tile fills the frame square, since a launcher applies its own mask. The colours are copied from
// the design system's tokens (@thekrystalship/krystal, styles/tokens.css); an SVG cannot read a
// CSS custom property, so when one changes there it has to be changed here.

import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const OUT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "public");

const CANVAS = "#0B0F14";       // --canvas, and the manifest's background_color
const TEAL = "#40A0C0";         // --krystal-teal
const DIM = 0.14;               // --krystal-teal-dim is that colour at 14%, translucent, so the
                                // fill is drawn as the site draws it: over the canvas.

const GLYPH = 26 / 56;          // the glyph's share of the frame
const STROKE = 1.7;             // in the 24-unit lucide viewBox

// lucide `clapperboard`, verbatim from lucide-react's 24×24 viewBox, copied rather than imported
// because this script draws SVG, not React. It is centred on (12,12).
const CLAPPERBOARD = `
    <path d="M20.2 6 3 11l-.9-2.4c-.3-1.1.3-2.2 1.3-2.5l13.5-4c1.1-.3 2.2.3 2.5 1.3Z"/>
    <path d="m6.2 5.3 3.1 3.9"/><path d="m12.4 3.4 3.1 4"/>
    <path d="M3 11h18v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z"/>`;

// The mark at any size: the tile edge to edge, the glyph centred at its share of the frame.
const mark = (px) => {
  const s = (px * GLYPH) / 24;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${px}" height="${px}"`
    + ` viewBox="0 0 ${px} ${px}">`
    + `<rect width="${px}" height="${px}" fill="${CANVAS}"/>`
    + `<rect width="${px}" height="${px}" fill="${TEAL}" fill-opacity="${DIM}"/>`
    + `<g transform="translate(${px / 2},${px / 2}) scale(${s}) translate(-12,-12)"`
    + ` fill="none" stroke="${TEAL}" stroke-width="${STROKE}"`
    + ` stroke-linecap="round" stroke-linejoin="round">${CLAPPERBOARD}</g></svg>`;
};

const MARK = mark(512);
const TMP = resolve(OUT, ".mark.svg");

function render(source, w, h, out) {
  writeFileSync(TMP, source);
  mkdirSync(dirname(out), { recursive: true });
  execFileSync("rsvg-convert", ["-w", String(w), "-h", String(h), TMP, "-o", out]);
}

const icon = (name) => resolve(OUT, "icons", name);

// The favicon is the vector itself, and the 32px PNG is for the browsers that take no SVG. index.html
// draws the same vector inline as the splash, which has to be drawn before any file can arrive, so a
// change to the mark is copied there by hand.
mkdirSync(resolve(OUT, "icons"), { recursive: true });
writeFileSync(icon("icon.svg"), MARK + "\n");
render(MARK, 32, 32, icon("icon-32.png"));

// `any` icons, and Safari's home-screen icons.
for (const px of [192, 512]) render(MARK, px, px, icon(`icon-${px}.png`));
for (const px of [152, 167, 180]) render(MARK, px, px, icon(`apple-touch-icon-${px}.png`));

// `maskable`: Android crops it to the launcher's shape. The tile is a flat fill, so it survives any
// crop, and the glyph sits well inside the 80% safe circle, so the maskable icon is the same drawing.
render(MARK, 512, 512, icon("icon-maskable-512.png"));

// iOS launch images: the canvas with the mark centred at a constant logical 90pt, so it is the same
// physical size on every device. Each entry is a device's portrait size in points and its pixel
// ratio; they mirror the <link rel="apple-touch-startup-image"> media queries in index.html one for
// one.
const SPLASH = [
  [320, 568, 2], [375, 667, 2], [414, 896, 2], [375, 812, 3], [414, 896, 3], [390, 844, 3],
  [428, 926, 3], [393, 852, 3], [430, 932, 3], [402, 874, 3], [420, 912, 3], [440, 956, 3],
  [744, 1133, 2], [768, 1024, 2], [810, 1080, 2], [820, 1180, 2], [834, 1194, 2], [834, 1210, 2],
  [1024, 1366, 2], [1032, 1376, 2],
];
const markPng = readFileSync(icon("icon-512.png")).toString("base64");
for (const [pw, ph, dpr] of SPLASH) {
  const [w, h, s] = [pw * dpr, ph * dpr, 90 * dpr];
  render(
    `<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"`
    + ` width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">`
    + `<rect width="${w}" height="${h}" fill="${CANVAS}"/>`
    + `<image x="${(w - s) / 2}" y="${(h - s) / 2}" width="${s}" height="${s}"`
    + ` xlink:href="data:image/png;base64,${markPng}"/></svg>`,
    w, h, resolve(OUT, "splash", `splash-${w}x${h}.png`));
}

rmSync(TMP, { force: true });
console.log(`wrote the icons and ${SPLASH.length} launch images to public/`);
