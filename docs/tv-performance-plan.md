# Krystal Cinema on a television: performance

A plan, not canon. It holds where the television performance work stands, the numbers it is measured
against, what has been tried and must not be tried again, and what comes next. What a step settles
moves into the docs beside the code it governs when it lands; when the last step lands, this file is
deleted.

## 1. How it is measured

- **The workbench is `npm run verify:tv-sweep`** (`web/site/verify/tvsweep/README.md`): every key
  press of a walk timed on the reference television, traced when asked, with ablations by `--css`,
  `--reduced`, `--eager` and `--shrink`. `--scratch` runs it against the scratch site through
  `adb reverse`, so a local build (or a patched `node_modules/@thekrystalship/krystal-ui`) is measured
  on the television before anything is published or deployed.
- **The release gate is `npm run verify:tv-perf`** in the lab, against `verify/tvperf/baseline.json`.
  It catches regressions in load time, layout and downloads, which the workbench does not; it cannot
  see GPU cost. Run it before every deploy.
- **The television stays awake by itself**: each harness wakes it and holds it awake for the run
  (`verify/awake.mjs`).
- **Two runs of each**, baseline and change, before believing a difference: the same walk on the same
  build varies by a few frames.
- Raw `results.json` of the runs below are kept on the development machine in
  `~/.cache/moviebot-verify/tvsweep/baseline-2026-10-12/` (walks named `disc` and `disc-open` there
  are `discover` and `discover-open` in `walks.mjs`). Compare with `verify/tvsweep/report.mjs`.

## 2. Where it stands (moviebot 3.26.4, krystal-ui 0.26.5, 2026-10-12)

Live on the reference television, production site, two untraced runs each:

| walk | frames over 25 ms | over 50 ms | worst stall | other |
|---|---|---|---|---|
| `home-rows` + `bar` + `deep-bar` (26 moves) | 22 / 26 | 12 / 12 | 80–100 ms crossing the bar | |
| `discover` (19 moves) | 67 / 61 | 35 / 24 | 140 / 100 ms | |
| `discover-open` | | | | Discover cold 856 / 588 ms to focus, again 257 / 309 ms |
| `switch` + `film` (six page opens) | | | | 2.9 s to focus in all |

On the scratch site, through `adb reverse`, same build (`discover`, 19 moves): 59 / 51 frames over
25 ms, 23 / 16 over 50 ms; with the collection tiles hidden altogether 34 and 9, which is what the
tiles still cost.

## 3. What is known

**The television.** The panel is 3840×2160 at 50 Hz (20 ms a frame), on a Mali-G52 with about 110 MB
of free memory. The app's window and buffers are 1920×1080, but Amazon's TV WebView rasterizes the page
at devicePixelRatio 4: four times the pixels the window can show, every frame.

**What made it stutter, measured, and what was done:**

1. Focus's drop shadow (`--shadow-lift`) and the shadow's transition on every focused card, tile,
   button and bar place, painted again at 4K on every frame of the lift. Focus is lit by the glow alone,
   drawn at once (krystal-ui 0.26.2, moviebot 3.26.2): row moves 99–103 → 31–41 frames over 25 ms.
2. Moving between a page and the bar: the whole page faded as one 4K layer, and every card with
   `content-visibility: auto` was styled and laid out again crossing the screen's edge. The page steps
   back under a canvas veil instead, every card is rendered and asks for its picture a quarter screen
   ahead (krystal-ui 0.26.4, moviebot 3.26.3): worst bar stall 220 → 80–100 ms.
3. Home's rows were drawn at the top and pushed down when the top's slides arrived (CLS 0.417), and
   latched their pictures from where they first stood. Home holds its top's place while slides are
   described (moviebot 3.26.3): CLS 0, Home's downloads back to their baseline.
4. Discover's tile posters drawn with rounded corners at 4K: the most expensive part of the collection
   tiles. Square-cornered on a television (krystal-ui 0.26.5, moviebot 3.26.4): Discover's worst stall
   237–253 → 100–140 ms.

**Not the problem** (measured): the navigator's choice of where focus goes (focus moves in 13–40 ms),
smooth scrolling on Home, DOM size (Home is about 420 elements), moving along the bar or along a row,
the shelf rows' own posters on Discover.

**Tried, and not to try again:**

- Rendering at 1080p from the app. The app sees a 1080p display at density 2; the doubling is the
  WebView's own, from the display mode, in native code. A half-size view scaled up twice gives DPR 8;
  page scale and density overrides cancel the same way; setting `PhysicalDisplayAndroid.sForcedDIPScale`
  by reflection and recomputing the display changes nothing; `/data/local/tmp/webview-command-line` is
  not read on a production build; DevTools' `setDeviceMetricsOverride` is ignored; Amazon's per-app
  engine options come from its own service and allow no scale switch. Only another engine (GeckoView)
  would render at 1080p, at about 70 MB of app, unknown performance on this GPU, and no DevTools
  protocol for the tooling.
- Lighter backdrop crossfades (no `will-change`, no fade, both): within noise. Hiding the backdrop
  outright saves about 10 frames over 20 moves.
- Eager images: no gain. `loading="lazy"` stays.
- On Discover's tiles: no shadow, no rotation of the fan, no grayscale filter on a strip, no clip on the
  tile, keeping the radii but dropping `overflow: hidden`, a tighter image lookahead. Each within
  noise on the count of frames over 25 ms (rotation and the tile hints below are worth a second look
  only together with something else).
- Removing `content-visibility` without loading pictures by nearness: every lazy poster loads at once
  (52 images, 5.4 MB on Home). The lab gate catches this; deploy only after it passes.

## 4. What comes next

In order of what the measurements promise. Every number is the scratch site's `discover` walk unless
it says otherwise; the baseline it is against is 51–59 frames over 25 ms and 16–23 over 50 ms.

1. **Discover's tiles, the rest of their cost** (tiles hidden: 34 and 9). Each of these measured once,
   on top of the square posters, and needs a second run before it is believed:
   - the tile as a size container (`container-type: inline-size`, its text in `cqi`): switched off,
     62 and 20 with the worst stall 87 ms. A television's tiles are one fixed size, so their sizes can
     be set outright there and the container dropped without any visible change;
   - the fan's rotated posters: unrotated, 58 and 18. A change to how the fan looks, the user's call;
   - smaller tile posters: served 185 px wide, 57–63 against 66–68 before the square posters. Needs the
     API to serve a smaller poster for tiles; a 185 px poster is what a tile shows on the 1080p window.
2. **Instant scrolling on a television**: 48 and 22 on Discover (54 and 25 before the square
   posters); no gain on Home. A change to how moving feels, the user's call.
3. **Opening Discover**: 0.6–1.0 s to focus cold. Most of a page open is its own first style and
   layout (about 0.1 ms an element on this television) and its first raster; building fewer rows at
   once, the rows near the screen first, is the structural lever, for every page.
4. **Smaller trims seen on the way**, each 40–80 ms a page open: a page being shown restyles all of it
   (its inherited `visibility` and `inert` change), and focus entering a row restyles every card in it.
