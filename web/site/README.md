# Krystal Cinema

MovieBot's site and its player: the library in a browser for members of the server MovieBot plays
films in, and the Discord Activity, which is the same player. It is served by `MovieBot.Api` from the
root of its `wwwroot` and built on `@thekrystalship/krystal-ui`, the design system the KGSM Control
Panel is built on too. The design is the Krystal Cinema project (desktop, mobile and embed pages); the
API is the authority for everything it draws.

```bash
npm install        # @thekrystalship/krystal-ui comes from GitHub Packages, which wants a token
npm run dev        # http://localhost:5175
npm run lint
npm run check      # the player's engine, types only
npm run build      # -> dist/; scripts/build-player.sh installs it into the API's wwwroot
npm run verify     # the player in a real browser, against a running API and dev server
```

The dev server proxies `/api`, `/media` and `/hub` to the API (`MOVIEBOT_API`, default
`http://127.0.0.1:8099`) and `/assistant` to moviebot-assistant (`MOVIEBOT_ASSISTANT`, default
`http://127.0.0.1:8191`), which is what the vhost does in production.

## Pages

| Path | What it is |
|---|---|
| `/` | A film to watch, then a row each for where the person left off, the rooms watching something now, what was recently added, what leaves next, what is on its way, and the shelf as Movies and TV Shows, each krystal-ui's scroll-snapped `Rail` |
| `/library` | Every film, narrowed by state and ordered by name, year or length |
| `/title/<id>` | One film: what it is, who played it last, how long it stays, keep or let go, and watch or resume |
| `/search?q=` | The library first, then the tracker's releases to fetch, and a film with no release yet to wait on |
| `/notify` | The films the signed-in person is waiting on |
| `/leaving` | Every settled film and how long it stays, kept or not |
| `/downloads` | What is downloading and what has arrived, read again every few seconds |
| `/profile?tab=` | The signed-in person: History (with a way to remove a film), Requests, Playback (subtitle and audio language, quality, theme) and Sessions (the devices signed in, signing one or all others out), and signing out |
| `/about` | What the site is, and where its film details come from |
| `/signin` | The door, and where Discord sends the browser back to |
| `/r/<room>?title=<id>` | A room: the player, full screen, with a way back to the page it was opened from |
| `/embed/` | The Discord Activity: the same player with no site around it |

A page is a path, so it can be linked to and reloaded: the API answers any path that is not its own
with this site's index.

## Signing in

Discord is the only door. Signing in asks Discord for `identify guilds.members.read`; the API redeems
the code, asks Discord whether the person is a member of the server, and issues its own token only to
a member. That token is the bearer on every call to the API and to the assistant, and is kept in this
browser under `cinema:session` until it expires. The redirect, `<origin>/signin`, is registered with
the Discord application.

## Watching

Watching opens `/r/<room>?title=<id>`: a room of its own for whoever opens it, or the room somebody is
already watching in. The room is the address, so the link is what Invite hands to somebody else, and a
voice channel's room is open on the site at `/r/<channel id>`. `title` is put on once, by whoever
arrives while the room is showing nothing; somebody joining a room already holding it says nothing,
because loading it again would send everyone back to the beginning.

Discord launches the Activity at the root of its URL mapping. A load carrying Discord's launch
parameters is the Activity's, so the index rewrites its address to `/embed/` before the page reads it,
and the player signs the viewer in through the Embedded App SDK rather than the site's session.

How the player behaves, and why: `src/player/CLAUDE.md`.

### How the timeline holds together

`src/player/engine/session/sync.ts` is the whole of it.

- **Revisions gate everything.** A push whose revision is not greater than the last applied is
  discarded, which makes reordered and duplicated pushes harmless.
- **Position is derived, never read.** `paused ? positionSeconds : positionSeconds + (serverNow -
  anchorUtc) * rate`, against a clock offset learned from round trips to `ServerTime`; the sample with
  the shortest round trip wins.
- **Echoes are recognised, not counted.** Applying remote state raises the same events that publish
  intent, and an event that tells the room what the room just said is dropped by comparing against
  what was applied.
- **Seeks are never clamped here.** The scrub bar asks for what was let go on and does not move the
  playhead; the server grants a position and answers `SeekClamped` to that viewer alone.
- **A viewer that falls behind fixes itself.** Every two seconds the client compares where the room is
  against its playhead: over two seconds of drift it seeks, between half a second and two it trims
  `playbackRate` to 1.02 or 0.98 until inside a quarter second. Nothing about a stalled viewer moves
  the room; the room is only told the viewer is waiting.

### A viewer who cannot play the film

The player asks `MediaSource.isTypeSupported` for what the ingest writes — H.264 High at level 4.2 and
stereo AAC-LC — before fetching anything. Discord's desktop client decodes H.264 only on the graphics
card, so with hardware acceleration off it answers no, and the player says so, naming the setting. A
fatal media error is recovered from twice, the second time swapping the audio codec, and then the
player stops and says why for as long as that holds: recovering without a limit resets the element
about ten times a second and fetches the film's opening each time.

### Verifying

`npm run verify` drives a real browser against a running API and this dev server: two and three
viewers in a room through play, pause, seek and the waiting mark, the join gate, the menus, a person's
preferences reaching a fresh player, each frame size, and a film still transcoding (its manifest is
edited in place and put back). It needs Chrome or Chromium **with H.264 and AAC** (`CHROME_PATH`, or
Playwright's own download), the API (`MOVIEBOT_API`, `MOVIEBOT_SERVICE_KEY`, `MOVIEBOT_MEDIA` for its
media root) and a film (`MOVIEBOT_TITLE`) with more than one audio track and a subtitle track. A jsdom
mount has no media pipeline, so it agrees with itself while the film never decodes a frame; a real
browser is the only thing that catches what matters here.

## The assistant

The dock holds krystal-ui's `Chat`, talking to moviebot-assistant at `/assistant` as the signed-in
person. A film the assistant puts on or finds comes back as the tool's card and is drawn with a way
to watch it; a proposal (a download, a wish, a keep, subtitles) is confirmed in the chat by the person
who asked for it.

## Rules

- **The design is the UI and the API is the authority for its data.** Nothing is drawn from a
  fixture: what the design shows, the API serves, and a row the API has nothing for is left out rather
  than filled in.
- **Everything is fetched from this origin.** Film details and their artwork come from TMDB through
  the API, which keeps them and serves its own copies under `/films/`; no page reaches TMDB, IMDb or
  any other outside host itself.
- **TMDB is credited wherever its data is shown.** Its terms are free personal use with attribution:
  every page ends with its logo and notice ("This product uses the TMDB API but is not endorsed or
  certified by TMDB."), and `/about` says what comes from it. The logo is TMDB's own, from its
  attribution page, in `src/assets/tmdb.svg`.
- **One module attaches the token.** `src/api.js` is the only place a bearer is added to a call to
  the API; the assistant's client is given a way to read the token, never the token itself. A 401
  signs the browser out.
- **Every act is the person's own.** The API takes who asked from the token, so the site sends no
  name of its own and cannot act for anybody else.
