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
npm run verify:mobile  # every page on iPhones and Galaxies, against a scratch site (verify/mobile/)
npm run verify:pwa     # installable, and the service worker keeps only what it may (verify/pwa.mjs)
```

The dev server proxies `/api`, `/media`, `/films`, `/shows`, `/popular` and `/hub` to the API (`MOVIEBOT_API`, default
`http://127.0.0.1:8099`) and `/assistant` to moviebot-assistant (`MOVIEBOT_ASSISTANT`, default
`http://127.0.0.1:8191`), which is what the vhost does in production.

## Pages

| Path | What it is |
|---|---|
| `/` | A film to watch, then a row each for where the person left off, the rooms watching something now, what was recently added, what leaves next, and the shelf as Movies and TV Shows, each krystal-ui's scroll-snapped `Rail`. The Movies and TV Shows rows end in a tile that opens Discover |
| `/movies`, `/tv` | Every film, or every show (one card each, however many of its episodes are here), with krystal-ui's `Toolbar`: a search by name, one Filters popover (state, stay, genre) with the active ones as chips, and a sort with its direction. On a phone a Movies \| TV shows switch under the heading moves between the two |
| `/title/<id>` | One film: what it is, who played it last, how long it stays, keep or let go, save, and watch or resume. An episode's id forwards to its show's page at its season |
| `/show/<imdbId>?season=` | One show, in the library or not: what it is, its seasons as chips with how long each stays, each season's episodes with their stills and how far this person got, resume, keep a season, and the request sheet that picks seasons and episodes to download at 720p (the default) or 1080p. Every episode the tracker has plays from its row while it arrives, each season can be downloaded whole, first episode first, and with nothing of it here Watch plays its first episode. A followable show has Follow beside its actions; once followed the button is a menu holding whether new episodes are downloaded automatically, and Unfollow, and the next episode's day is written beside it. Save is beside them |
| `/film/<imdbId>` | A film that is not here: what it is, and Watch (now, while it arrives) and Download (1080p, the default, or 720p, with the size of each) when the tracker offers it, the download's progress while it is on its way, or Notify when it is not out digitally yet, and Save. A film in the library forwards to its `/title/` |
| `/discover?q=` | Finding anything to watch, here or not. A large field and, with nothing typed, the films and shows popular this week (`/api/popular`), each marked when the library holds it and otherwise leading to its `/film/` or `/show/` page. With a query, the library first, then the catalogue's films and shows, each leading to its page, then the tracker's releases, asked for when that row is opened and opened by itself when the catalogue found nothing. `/`, Ctrl+K or Cmd+K anywhere outside a text field opens it with the field focused, and a fine pointer focuses the field on arrival |
| `/lists` | The signed-in person's lists, Watch later first, then the rest most recently changed first, each its newest posters, name and size, and whose it is when it is somebody else's; and making a new one. On a phone a Movies \| TV shows \| Lists switch under the heading moves between the three |
| `/lists/<id>` | One list (`later` is the person's own Watch later): its titles newest saved first under Today, Yesterday, This week and each month, with krystal-ui's `Toolbar` to search titles and notes, narrow by kind, library and watched, and sort. Each row says when it was saved, by whom on a shared list, its note, and whether it is here or watched; an owner or editor edits the note and takes it off. Its owner renames it, deletes it, and puts people on it as editors or viewers; a member leaves it |
| `/notify` | The films the signed-in person is waiting on |
| `/leaving` | Every settled film and how long it stays, kept or not |
| `/profile?tab=` | The signed-in person: History (with a way to remove a film), Requests, Following (each followed show with its next episode, whether new episodes are downloaded, and Unfollow), Notifications, Playback (subtitle and audio language, quality, skipping intros and recaps automatically, how subtitles look with a preview of them, theme) and Sessions (the devices signed in, signing one or all others out), and signing out |
| `/about` | What the site is, and where its film details come from |
| `/signin` | The door, and where Discord sends the browser back to |
| `/r/<room>?title=<id>` | A room: the player, full screen, with a way back to the page it was opened from. A room waiting on a Watch now shows the film being found, arriving and starting, and plays it on its own |
| `/embed/` | The Discord Activity: the same player with no site around it |

A page is a path, so it can be linked to and reloaded: the API answers any path that is not its own
with this site's index.

Save, on a film's and a show's page, puts it on the person's Watch later in one press. Once it is on
any list the button reads Saved and opens the lists the person can add to, each switched on where it
holds the title, and New list, which makes one and saves to it: a popover beside the button on a wide
screen, the drawer on a phone.

Wide screens get around by the sidebar, which has Discover under Home. A phone has krystal-ui's
`TabBar` along its bottom edge instead, and no drawer: Home (lit on `/` and `/leaving`), Library
(`/movies`, lit on `/tv`, `/lists` and a title too), Discover (lit on a `/film/` page too), Assistant, which opens the dock above the bar rather than a page, and Me
(`/profile`, lit on `/notify` too). The tab of the page already open scrolls it back to its top. The
pages no tab names are linked from the footer every page ends with.

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

`npm run verify:mobile` looks at every page and every tapped-open state on a set of iPhones (WebKit)
and Galaxies (Chromium), against a scratch site with a library and a person of its own, and leaves a
screenshot per view per phone, a contact sheet per view and a report of what reads as broken on a
phone. How to run it, what it measures and how to add a page or a phone: `verify/mobile/README.md`.

## Installing

The site installs as an app, Krystal Cinema, from Android Chrome's "Install app" and from Safari's
"Add to Home Screen", and opens standalone.

- **`public/manifest.webmanifest`** is what Chrome reads: name, standalone display, the canvas colour
  and a 192, a 512 and a maskable icon, without which it offers no install. It names no
  `orientation`, because any value there overrides the phone's rotation lock in the installed app,
  and a film is watched sideways by turning the phone.
- **`index.html`** carries what Safari reads instead: the home-screen icons and title, standalone
  display, the black status bar the page starts below, and a launch screen per iPhone and iPad size.
- **The artwork** under `public/icons/` and `public/splash/` is written by `scripts/make-icons.mjs`
  (needs `rsvg-convert`; no build runs it): the Movies entry's clapperboard on the accent's tile. Its
  launch-screen list and the `index.html` tags mirror each other.
- **`public/sw.js`** is the service worker. It allowlists: page loads, network first with the
  index kept as the offline shell, and the files under `/assets/`, `/icons/` and `/splash/`, cache
  first. Anything else, the API, a film's media and the hub among it, goes to the network untouched,
  so a cached answer can never outlive a session or hand one person's data to another. It is
  registered by `src/registerSW.js` in a production build only, and never in the Activity, which runs
  inside Discord at Discord's origin. It also shows the API's pushes: the sentence as the API wrote
  it, opening the page it names in a window of the site already open, or a new one.

## Notifications

The bell lists what the API told this person (`src/notifications.js`, read every minute and whenever
the page comes back into view): in the sidebar above the person on a wide screen, at the top right of
the front page on a phone. Profile's Notifications tab turns pushes on for this browser, through
krystal-ui's `pushBrowser` against `/api/push/key` and `/api/me/push`, and holds a switch
for each kind that is pushed, new episodes of followed shows among them. A browser can be pushed to only once it has a service worker, which is a
production build; on an iPhone, only once the site is on the Home Screen, and the tab says so. The
player's invite panel lists everybody who can be asked into the room and says, per person, whether
the invite went out as a push or as a Discord message, never that it was seen.

Installing needs HTTPS, which localhost is exempt from. `npm run verify:pwa [origin]` asks a real
Chromium whether the site is installable, decodes every icon and launch screen at the size it is
named for, and drives the API, media and hub routes through the controlled page to show none of
them was kept; it defaults to the scratch site and takes a deployed origin as readily.

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
- **TMDB is credited.** Its terms are free personal use with attribution: `/about` carries its logo
  and notice ("This product uses the TMDB API but is not endorsed or certified by TMDB.") and says
  what comes from it. The logo is TMDB's own, from its
  attribution page, in `src/assets/tmdb.svg`.
- **One module attaches the token.** `src/api.js` is the only place a bearer is added to a call to
  the API; the assistant's client is given a way to read the token, never the token itself. A 401
  signs the browser out.
- **Every act is the person's own.** The API takes who asked from the token, so the site sends no
  name of its own and cannot act for anybody else.
