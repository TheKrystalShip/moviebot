# The player

A plain web page that plays a film from `MovieBot.Api` on the room's shared timeline. video.js is
the surface, hls.js feeds it, and `@microsoft/signalr` carries play, pause and seek.

```bash
npm install
npm run dev        # http://localhost:5173, talking to http://127.0.0.1:8099
npm run build      # -> dist/
npm run check      # types only
npm run verify     # the browser checks, against a running API and page
```

The API it talks to:

```bash
Media__Root=/absolute/path/to/media dotnet run --project ../../src/MovieBot.Api -c Release
```

## Where it is served from

Everything that differs between front doors is behind `Environment` in `src/environment.ts`:
the API base, how a manifest-relative media path becomes a URL, the hub address, which session
this is, and who the viewer is. Nothing else in the app reads `location` or builds a URL, so a
front door that rewrites paths and supplies an identity is another implementation of that
interface and no other change.

The browser implementation resolves the API base from `?api=`, then `VITE_API_BASE`, then the
page's own origin — with the dev server's port 5173 pointing at `127.0.0.1:8099`, since the dev
server serves the page and not the API.

## The launch link

```
https://<player-origin>/?session=<sessionId>&title=<titleId>
```

`session` is the room to join, and is opaque: never parsed, never validated. A page opened
without one names its own and writes it into the address bar, so whoever opened it has a link to
hand to somebody else.

`title` is optional. When it names a title the player loads it — once, by whoever arrives while
the room is showing something else. A viewer joining a room that already holds that title says
nothing, because loading it again would send everyone back to the beginning. Without a `title`
the library is shown and the person picks.

The link carries **no identity**. A first-time viewer is asked what to call them, and the name
and a stable id are kept in this browser under `moviebot.identity.v1`. The link is unguessable
and nothing more than that.

## Shared, and not shared

Which title, paused, position, rate and who changed it come from the hub. **Volume, subtitle
selection and audio track are this browser's own**, kept in `localStorage` under
`moviebot.prefs.v1` and never sent: shared volume is a way for one person to deafen everyone
else, and one person needing subtitles should not put them on five other screens. Track choices
are stored per title, because a track id means nothing outside the film it came from.

## How the timeline holds together

`src/session/sync.ts` is the whole of it.

- **Revisions gate everything.** A push whose revision is not greater than the last applied is
  discarded, which makes reordered and duplicated pushes harmless.
- **Position is derived, never read.** `paused ? positionSeconds : positionSeconds + (serverNow -
  anchorUtc) * rate`, against a clock offset learned from round trips to `ServerTime` — the
  sample with the shortest round trip wins.
- **Echoes are recognised, not counted.** Applying remote state raises the same events that
  publish intent, and an event that tells the room what the room just said is dropped. The test
  compares against what was applied: a seek interrupted by a second seek raises no `seeked` at
  all, so a count waiting for one goes on to swallow the next thing the person does.
- **Seeks are never clamped here.** The scrub bar asks for what was clicked and does not move the
  playhead; the server grants a position and answers `SeekClamped` to that viewer alone, which
  the page shows as a notice naming the head and where it landed.
- **Seeks publish on `seeked`, never `seeking`.**
- **A viewer that falls behind fixes itself.** Every two seconds the client compares where the
  room is against where its playhead is: over two seconds of drift it seeks, between half a
  second and two it trims `playbackRate` to 1.02 or 0.98 until inside a quarter second. Nothing
  about a stalled viewer reaches the room.

## The scrub bar

`src/player/scrubBar.ts`, drawn at the film's true duration from the manifest with the
transcoded region shaded behind the played region. video.js only knows what the playlist
advertises, and an EVENT playlist advertises what has been written, so its own bar shrinks a
three-hour film to whatever the transcode has reached. The head arrives on every state push and
is polled from the manifest between them, since it grows while nobody is acting.

## Menus

Built from the manifest rather than from what the media element exposes, split into Feature and
Commentary, and labelled from the manifest's `label`. A track with `available: false` is listed
disabled with its reason: a language simply absent from the menu reads as a bug.

The master playlist is what binds the audio renditions to the video; without one the film plays
silently with no track to switch to. A manifest that names a `master` is loaded from it. A
manifest that names none gets one composed by `src/player/masterPlaylist.ts` and handed to hls.js
as a blob, which has no base to resolve against, so every URI in that one is absolute. Renditions
are listed in manifest order either way, which is how a chosen track is found again.

Subtitles are whole WebVTT files rather than an HLS rendition, so the chosen one — and only the
chosen one — is fetched and attached as a track.

## Verifying

`npm run verify` drives a real browser against a running API and a running page: it plays the
film, reads the menus, drives two viewers through play, pause and seek from either side, knocks
one out of step to watch it recover, and edits the fixture's manifest to a transcoding state to
exercise the shaded region and a refused seek. It needs Chrome or Chromium **with H.264 and AAC**
— `CHROME_PATH` names one, and Playwright's own download is used when it is there. `MOVIEBOT_WEB`
points it at a page other than `http://localhost:5173`, and may carry `?api=` of its own.

A headless browser is the only thing that catches what matters here: a jsdom mount has no media
pipeline, so it agrees with itself while the film never decodes a frame.

## Layout

```
src/environment.ts        the front-door seam: URLs, session, identity
src/api.ts                the library and manifest endpoints
src/prefs.ts              per-viewer state in localStorage
src/types.ts              the API's wire shapes
src/session/clock.ts      the server clock offset
src/session/hub.ts        the SignalR connection, and position derivation
src/session/sync.ts       revisions, echoes, drift
src/player/               video.js and hls.js, the scrub bar, the menus
src/ui/                   the page around the player
verify/                   the browser checks
```
