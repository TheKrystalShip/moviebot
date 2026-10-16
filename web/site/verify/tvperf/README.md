# Krystal Cinema on a television, timed

How the site feels driven by a remote: how long a press goes unanswered, how long the screen stands
still, and how many times a page rearranges itself before it settles. `verify:tv` says every control
can be reached; this says what reaching it is like.

The same walks run on two targets:

- **The lab** (`--target lab`, the default): Chromium on this machine against the scratch site, drawn
  the way the reference television draws and slowed to its pace. Each step is the median of several
  passes, compared with `baseline.json`, and a regression that persists through a second look fails
  the run (see "How the lab decides"). It is what a release passes.
- **The television** (`--target tv`): the reference television itself over adb, on its own site, woken
  and held awake for the run (`../awake.mjs`). It is run when something feels slow, and before a release of the TV app, and it is what the lab is
  calibrated against. It is reported, never gated.

## Running it

From `web/site`, with the scratch site up (`verify/mobile/scratch.sh`, `verify/mobile/README.md`):

```bash
npm run verify:tv-perf                                   # the lab, every walk, against the baseline
npm run verify:tv-perf -- --walks bar,film --runs 1      # narrower and quicker, for working on a page
npm run verify:tv-perf -- --warmup 0 --confirm 0         # no warm-up pass, no second look
npm run verify:tv-perf -- --video                        # with the screen recorded
npm run verify:tv-perf -- --save-baseline                # this run becomes the baseline
```

The lab stages the scratch site itself (`stage.mjs`): the viewer partway through films, two others
watching in rooms, lists. A walk starts from its page loaded with the HTTP cache emptied, so a load is
measured cold on both targets.

On the television:

```bash
npm run verify:tv-perf -- --target tv --serial <tv-host>:5555 --video
```

It needs the debug build of `tv/` installed (`tv/build.sh --debug --install`), and its own session:
the first time, the run stops on the sign-in screen and the code there is approved like any
television's. It opens the debug app on `--origin` (the production site by default), drives its
WebView over the DevTools protocol, and leaves the television on the release app.

Output goes to `~/.cache/moviebot-verify/tvperf/<target>-<time>/`: `report.json` with every run's
measures, the three slowest frames of each step with the scripts that filled them, each step's offset
into its video, and the videos. `run.mjs` holds the options and exit codes.

## What is measured

`probe.js` is installed in the page before the site's scripts and records every animation frame: the
focused element and its rectangle, and what is drawn under a grid of 6 by 4 points across the screen.
The grid is what sees a page rearranging: a skeleton replaced, a spotlight drawn over half a page,
rows swapping, the screen going blank. The browser's layout shifts miss most of that, since a node
replaced is not a node moved. Alongside: long animation frames, key events, layout shifts and every
fetch. A key is timed from the event's own time inside the page, so adb's latency is not counted.
`measure.mjs` turns a step's record into:

| | | budget |
|---|---|---|
| `answer` | key to the next frame drawn after it was handled; for a load, the first contentful paint | 100 ms |
| `focus` | until focus moves; reported, not held against the baseline | |
| `ready` | until the last change of any kind | |
| `relay` | how many times the page took a new layout; frames of one slide or scroll count once | 2 |
| `blank` | time the screen was blank after something was drawn | 0 |
| `freeze` | the longest gap between two frames | 100 ms |
| `block` | long animation frames' blocking time, summed | |
| `cls` | layout shifts, summed, including those right after the key | 0.1 |
| `req`, `kb` | what was fetched | |

A step over a budget is marked `*`; `--strict` fails on it. A step past its baseline by more than
`Slack` in `run.mjs`, after the second look, is marked `!` and fails the run.

## How the lab decides

One pass is noisy: the lab shares the machine with whatever else runs on it, a single pass of a step
swings by half its value or more (a load's blocking time between 360 and 1340 ms, a key's freeze between
100 and 500 ms), and a few measures are two-valued (`film/5:back` reads 3 requests or 17 for the same
bytes; a Back key's answer reads 16 ms or one whole slow frame, 208, about half the time each). The
machine's own speed also moves by a third or more over a minute. So the gate never judges a pass:

1. **A warm-up pass** of every walk runs first and is discarded. The first pass in a fresh browser sees
   a cold site (a load that fetches two requests, a first scroll that fetches the whole page's pictures),
   and counts for nothing, including a step that does not land where it should.
2. **Three measured passes** (`--runs`); a step's value is the median of them. For the two-valued
   measures, `answer` and `req`, it is the lower quartile (`LowerQuartile` in `measure.mjs`): noise only
   adds to them, so a coin landing on the high value half the time does not decide, while a cost that every
   pass carries still lifts the lower quartile.
3. **A second look.** A step past its limit, `base × factor + add` of `Slack` in `run.mjs`, makes its walk a
   suspect. Each suspect walk is measured four more passes (`--confirm`) and the medians over all seven
   decide. Noise passes the second look because a median of seven does not follow two outliers; a real
   regression fails it because every pass carries it. A run with no suspect costs nothing extra.

The limit of a measure is the baseline plus its slack (`Slack` in `run.mjs`), and the slack is read off
the spread of repeated passes. A pass of a load's blocking time spreads over 30 to 55 percent of its
median, so the median of seven moves by well under a fifth, and the limit, 1.5 times the baseline plus
150 ms, sits several of those above it. A key the baseline answers inside a frame (32 ms or less) is
held to 240 ms at least, one whole slow frame at this slowing, because the same key reads 16 or 208
from one pass to the next depending on which task runs first. What still fails: work added to
the main thread by half again, a relayout, a request or a hundred kilobytes that was not there, a blank
screen. The gate cannot see a regression smaller than its slack; the television run is what measures
the finer ones. `report.json` keeps the machine's one-minute load at the start and the end of the run,
the pass count of each step and every sample, so a failure can be read against how busy the machine was.

## Calibrating

The lab's CPU slowdown (`Lab.cpuSlowdown` in `run.mjs`) is the factor at which the lab's totals over
the walks land on the television's: blocking, freeze, answer and ready each within about a fifth.
Against the reference television that factor is 20. CPU throttling slows the page's main thread and
nothing else, so drawing and decoding stay at this machine's speed; the factor makes up for them, which
is why it is fitted to the television rather than taken from benchmarks. To fit it again, after the
reference television changes or the site's work moves between main thread and drawing:

```bash
npm run verify:tv-perf -- --target tv --serial <tv> --out /tmp/tv
for c in 12 20 30; do npm run verify:tv-perf -- --runs 1 --cpu $c --out /tmp/lab-$c; done
```

and compare the steps the two runs share (`report.json`, `steps.<step>.runs`). A new factor goes in
`Lab` with a baseline saved at it.

## The baseline

`baseline.json` is the lab's medians at a known build. It is saved again whenever a change moves the
numbers on purpose, and after a change that makes a page faster above all, so the gate holds the
gain. It is saved from a longer run than the gate's, `--runs 7 --save-baseline`, so its medians are
steadier than any gate run's, and on a machine as idle as it gets: a baseline taken while the machine
is busy raises every limit with it. A failing run's `report.json` names the slowest frames and their scripts; `--video` shows what
the screen did.

## Adding to it

- **A walk**: an entry in `walks.mjs`, its keys naming nothing a library holds so the same walk runs on
  the television's own site. A step that opens a page names the path it lands on.
- **A measure**: the recorder in `probe.js`, the reduction in `measure.mjs`, its slack in `run.mjs`.
