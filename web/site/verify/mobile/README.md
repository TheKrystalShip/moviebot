# Krystal Cinema on phones

Every page of the site, and every state of a page a person reaches by tapping something, looked at on
a set of iPhones and Galaxies, measured for what reads as broken on a phone, and photographed. One run
leaves a screenshot per view per phone, one tall picture per page, a contact sheet per view with every
phone side by side, and `report.json`.

An iPhone is drawn by WebKit, Safari's engine, and a Galaxy by Chromium, which is what each one's
browser is built on. Playwright's device descriptors give each its viewport, pixel ratio, touch and
user agent.

## Running it

Three terminals, from `web/site`:

```bash
MOVIEBOT_VERIFY_TMDB_TOKEN_FILE=/path/to/token verify/mobile/scratch.sh   # the scratch site
verify/mobile/webkit.sh                                                     # Safari's engine
npm run verify:mobile                                                       # the run
```

`scratch.sh` builds the site's production build and the API, and serves them on `127.0.0.1:8299`
over a library of its own (below); `--no-build` serves what was built last. A change to the site is
`npm run build` while it runs, since the API reads the site from disk. `webkit.sh` runs WebKit in
Playwright's Docker image, which carries the system libraries it needs; without it the iPhones are
skipped and the run says so.

```bash
npm run verify:mobile -- --devices iphone-se,galaxy-s24 --views movies,movies-sort
npm run verify:mobile -- --devices all
npm run verify:mobile -- --out <a run's directory> --sheets-only     # draw that run's sheets again
```

A run's output is under `~/.cache/moviebot-verify/runs/<time>/`: `<device>/<view>.png` (the screen),
`<device>/<view>-full.png` (the whole page), `sheets/<view>.png`, and `report.json`. It exits 1 when
any view has a fault.

## The scratch site

The checkout's `media/` is hard-linked into a library of the harness's own, so nothing a run does
reaches a library people use: the first few films go on a cold root, where they are on the library's
clock, and the rest on the media root. A hard link costs nothing and the API replaces a manifest
rather than editing it, so the source library is never written to; it does mean the harness directory
and `media/` share a filesystem. The state directory is new on every start, its watch record begun some
days ago so that a film nobody has played is close to leaving. The film database is asked through a
token read from a file, and what it says is kept between runs.

`stage.mjs` then puts in what a person would see: the viewer partway through three films, a film they
keep, two others watching in rooms of their own, one of them a film about to leave, a Watch later
with a note on one film, a list of the viewer's shared with Ben, and one of Ana's the viewer may add
to. It does it through the API and the room hub, never by writing a file. The viewer is signed in with a member's
token signed by the scratch API's own key, which is what signing in on the site hands back.

## What is measured

`inspect.mjs`, in the page. The site scrolls inside its page column rather than the document, so both
are measured, and every element's own edges.

| | |
|---|---|
| **fault** | the page or its column scrolls sideways; an element runs past the screen; text is cut off without an ellipsis; text or an icon runs into other text (a value too wide for its cell spills over the next one without being clipped, which no overflow measure sees); the page raised an error; a tap opened nothing |
| **warning** | a control smaller than 32px either way; a control mostly, or text partly, under a floating button |
| **note** | text shortened with an ellipsis, which is usually intended |

What it measures is what it can see, so the contact sheets are the other half of a run: a layout can
be measurably sound and still look wrong.

## Adding to it

- **A page or a state**: a line in `views.mjs` — where it goes, given what staging made, what to tap
  once there and what that tap opens, and whether a tall picture of it makes sense. A view that
  changes something, as playing a film does, acts as a person of its own so the others see the
  library as staged.
- **A phone**: a line in `devices.mjs`, by Playwright's descriptor name. `DEFAULT_DEVICES` is the set a
  plain run uses.
- **Something the library should hold**: `stage.mjs`.
