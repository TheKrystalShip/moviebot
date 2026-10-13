# Where focus goes on a television

`npm run verify:tv-focus` presses the remote's keys on the reference television and records every
focus jump: where focus was, everything it passed through, and where it came to rest. It compares the
resting place with where the walk says it should go. It is the tool for finding where the navigator
(`src/tv/navigator.js`) takes focus somewhere a person would not expect, and for proving a fix.

## What it needs

The same television as `verify:tv-sweep` (`../tvsweep/README.md`): the debug build of `tv/` installed
and signed in on the production site, and on adb; the harness wakes it and holds it awake for the run
(`verify/awake.mjs`). The keys go in over adb as the remote's own key
events, so Back and Menu take the wrapper app's path into the page. Each one takes most of a second to
arrive, and the harness waits for it before it measures anything.

## Running it

```bash
npm run verify:tv-focus -- --serial <tv>:5555                          # every walk not marked optional
npm run verify:tv-focus -- --serial <tv>:5555 --only profile-back --shots
npm run verify:tv-focus -- --serial <tv>:5555 --cdp-keys               # keys typed over DevTools
npm run verify:tv-focus -- --serial <tv>:5555 --scratch --only player  # the player, on the scratch site
```

`--scratch` drives the scratch site (`../mobile/scratch.sh`, running) instead of production: the
television reaches it through `adb reverse`, signed in with a planted session. The player walk needs it,
since it plays a film, and it measures a local build before anything is deployed. In the player each
step also records the film: paused or playing, where it is, whether the controls are up and whether
the scrub bar is seeking, and whether the film jumped rather than played on, which a walk can expect.

Each step prints one line: `ok` when focus came to rest where expected, `MISS` when it did not, `FLAG`
when there was no expectation but focus ended on nothing, ended against the arrow (judged by where
the elements sit on the page, not on the screen, so a column scrolling under the move is not counted),
or moved more than once for one press (`via`). A blank verdict is a step recorded but not judged.

Output goes to `~/.cache/moviebot-verify/tvfocus/<time>/`: `results.json`, and with `--shots` a picture
of each step with where focus was outlined in red, where it went in green, and where it should have
gone, when that is elsewhere, in yellow. The exit code is 1 when a step missed or was flagged.

## Writing a walk

The walks are in `walks.mjs`; the header there says what a step can say. An element is named by where
it is (`bar/Movies`, `hero/Watch`, `row/<row title>/<n>`, `side/<label>`, `dialog/<label>`,
`main/<label>`), the names `focus.mjs` documents. Write a new walk without expectations, run it, read
where focus went, and write down where it should have gone. The rows are production's library, so a
walk names a row it does not rely on with `*`.
