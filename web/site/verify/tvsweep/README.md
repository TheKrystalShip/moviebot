# Krystal Cinema on a television, key by key

`npm run verify:tv-sweep` times every key press of a walk on the reference television itself: how long
focus takes to move, how many frames go over the panel's budget while the screen settles, and with
`--trace` where the time went. It is the tool for finding why something stutters and for proving that
a change helps. `verify:tv-perf` (`../tvperf/`) is the release gate; this is the workbench.

It measures the television, never a lab: the costs that make the television stutter are its GPU and
its image decoding at the panel's resolution, which no CPU throttling on this machine models.

## What it needs

- The reference television on adb (`cinema-reference-tv` in the operator's notes): the debug build of
  `tv/` installed (`tv/build.sh --debug --install`), signed in on the production site.
- The television awake: `verify/tvsweep/awake.sh <host:port> [hours]` in the background holds it awake
  without pressing anything and restores its own settings when stopped. A step with no frames was
  taken with the panel asleep and is reported `INVALID`.
- For `--scratch`, the scratch site (`verify/mobile/scratch.sh`): the television reaches it through
  `adb reverse` with a session planted the way the lab plants one, so a local build is measured on the
  television before anything is published or deployed. The scratch library is not production's, so
  compare scratch runs with scratch runs.

## Running it

```bash
npm run verify:tv-sweep -- --serial <tv>:5555 --only discover              # one walk, untraced
npm run verify:tv-sweep -- --serial <tv>:5555 --only discover --scratch    # the local build
npm run verify:tv-sweep -- --serial <tv>:5555 --only discover --trace --profile
npm run verify:tv-sweep -- --serial <tv>:5555 --only discover --css ".collection-tile{visibility:hidden}"
node verify/tvsweep/report.mjs <results dir> <results dir> ...            # runs side by side
node verify/tvsweep/inspect.mjs tasks <results dir>/<step>.json            # one traced step
```

The walks are in `walks.mjs`; without `--only`, every walk not marked `optional` runs. Output goes to
`~/.cache/moviebot-verify/tvsweep/<time>/`: `results.json`, and with `--trace` one trace per step.

## Reading the numbers

The panel draws at 50 Hz, so a frame is 20 ms. `>25ms` counts the gaps between frames that missed
one, `>50ms` the ones a person sees as a hitch, and `longest` is the worst stall. Run to run, the same
walk on the same build varies by a few frames: run a baseline twice, and a change twice, before
believing a difference. A change worth shipping moves the numbers beyond that spread on both runs.

Tracing roughly doubles what the television's CPU spends. Use a traced run for where time goes
(`trace` in results.json, and `inspect.mjs`), an untraced one for how long things take.

An ablation answers "what does this cost": switch one thing off (`--css`, `--reduced`, `--eager`,
`--shrink`) and compare with the baseline. Its upper bound is hiding the suspect altogether.

## Television facts it rests on

- The panel is 3840×2160 and draws at 50 Hz. The app's window and its buffers are 1920×1080, and the
  WebView draws the 960×540 page at devicePixelRatio 4, so every frame is rasterized at four times the
  pixels the window holds. That factor is the WebView's own: the app sees a 1080p display at density
  2, and the doubling is applied from the display mode inside the WebView's native code. Nothing the
  app sets changes it (a view's size or scale, page scale, density, the WebView's forced scale field),
  DevTools' metrics override is ignored, and the WebView's command-line file is not read on a
  production build.
- Raster happens on the WebView's GPU thread (`Chrome_InProcGpuThread`), so it shows in `gpu`, and
  image decoding on worker threads, which `decode` sums.
- `prefers-reduced-motion` emulated over DevTools is honoured, and makes the navigator's scrolling
  instant.
