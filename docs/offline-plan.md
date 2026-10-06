# Save offline, and the site without a connection

A plan, not canon. What it settles moves into the docs beside the code it governs
(`docs/api-contract.md`, `web/site/README.md`, `web/site/src/player/CLAUDE.md`, the API's and the
hand-off's `CLAUDE.md`s) when it lands, and its section here is struck. When the last step lands, this
file is deleted.

Each step below is taken through a deep dive before it is built: the questions listed under it are
answered, by measurement where they can be, and the answers are written into the step before code is
written.

## 1. What this is for

A person keeps a film on their phone, tablet or laptop and watches it with no connection, the way
Netflix and Spotify keep downloads. The installed site opens with no connection at all, and the person
gets to what is on the device from it.

Three parts, each useful alone and built in this order:

- **A 480p rung** in every title's ladder: the copy a phone saves, and a lighter stream for a weak
  connection.
- **Save offline**: a film's tracks copied onto the device, played from there, alone.
- **The site offline**: the installed app starts, signs nobody out and draws the pages that can be
  drawn without the server, marking everything else as needing a connection.

## 2. Decisions

| Decision | Outcome |
|---|---|
| Name | **Save offline**, on the button, the action and the routes (`cinema:offline`, `/api/me/offline`). "Download" already means fetching a torrent into the library (`cinema:download`, `/api/downloads`) |
| Who may | Everyone by default. `cinema:offline` is its own action so it can be taken from somebody, and it is filed at the provider into every role that carries `cinema:watch` |
| Where | Desktop, phone and tablet browsers, best installed. Not a television: the TV design leaves it out, as it leaves out installing and pushes. Not the Discord Activity, which runs at Discord's origin |
| Protection | None beyond the account. The bytes on a device are the person's; no DRM, no encryption at rest. Removal happens only on a client that cooperates and is online (Step 5·6) |
| A small rung | The ladder gains a third rung, 854 wide, for every title wider than that, streamed like the others and offered to Save offline as the small copy (Step 1). It is named by its height like every rung, so a 16:9 film's reads 480p and a scope film's 358p |
| What is saved | One video rung, picked from those the title has with each one's size beside it, the audio tracks the person picks (their preferred language preselected), the text subtitles in their preferred languages, the poster, backdrop and the title's description. Only a title whose status is `ready` |
| Which rung by default | The smallest on a phone or tablet, the step-down elsewhere. A person's own choice is kept in their preferences and shown in Profile's Playback |
| Where the page lives | The Offline tab of Profile, `/profile/offline`, on every screen: the phone's `TabBar` has no room, and Me already leads to Saved. A wide screen's sidebar links to the same address |
| Where it is kept | The Origin Private File System, one directory per account `sub`. Never the service worker's cache: `sw.js` keeps no media and no API answer, and that rule stands |
| Playing it | Alone, on a local timeline. No room, no hub, no invite |

## 3. What stands

- A film on disk is HLS: fMP4 segments of `Ladder.SegmentSeconds`, a video rung per ladder step
  (`v0` at the source's size, `v1` at 1280 wide where the source is wider), audio renditions apart from
  the video, and subtitles as standalone WebVTT. All under `/media/{id}/**`, opened by a ticket or the
  bearer token.
- The player plays through hls.js wherever `Hls.isSupported()`, which on an iPhone is iOS 17.1 and up
  (ManagedMediaSource); older iPhones fall back to the browser's own HLS. The ticket already rides a
  custom loader (`engine/media/mediaTicket.ts`).
- The player's timeline is the room's (`engine/session/sync.ts`): the server is authoritative.
- `sw.js` keeps the shell network-first and caches `/assets/`, `/icons/` and `/splash/` on first use.
  A lazy chunk never loaded (the player is one) is not in the cache.
- `session.js` keeps the `oidc-client-ts` user in `localStorage`. A refresh that fails ends the session
  on the page, which offers the way back to the provider.
- Progress and history are recorded by the API from rooms; there is no route a client posts a position
  to.

## 4. Steps

### Step 1: the 480p rung

Useful on its own, ahead of everything else: a viewer on a weak connection streams it, and Save
offline offers it as the copy a phone wants.

1. **`Ladder`** gains a third rung, `v2`, 854 wide, joining wherever the source is wider than that,
   at its own bitrate cap. `Ladder.For` returns every rung the source's size is owed, and the ingest,
   the re-encode, the hand-off's placement and the planner all read it, so a new title is made with all
   three in its first pass.
2. **The library already held is owed the rung.** The planner already lists a rung a finished title
   lacks as a `rung-add` unit, and the re-encode already makes rungs from the title's own top rung once
   its download is gone. A `rung-add` for `v2` encodes that one rung from the top rung and adds it,
   leaving the rungs the title has untouched. It runs as backfill: only while no room is playing.
3. **The player** offers it in the quality menu like any rung, and hls.js's own choice uses it when
   the connection calls for it. The planner's wanted height already maps to the tallest rung not above
   it, so a viewer capped low is served by `v2` rather than `v1`.

Deep dive:
- The bitrate cap and constant quality for 854 wide, scored with VMAF the way `Reencoder.LibraryCq`
  was, against what a phone's screen shows.
- What the rung costs the cold disk across the library, measured, against its free space.
- How long the backfill takes across the library on the card, measured on a few titles, and whether
  `rung-add` for one rung from the top rung needs its own path through `Reencoder`, which today
  encodes every rung again.
- Whether a 480p rung changes the first pass's NVENC cost enough to matter to the planner's budget
  (its pixel area is about a fifth of 1080p).

### Step 2: the API's half of Save offline

1. **`cinema:offline`** declared in `CinemaActions.All`, `execute`, and filed at the provider beside
   `cinema:watch`.
2. **`POST /api/me/offline`** with `{titleId, rung, audio[], subtitles[], device}` answers the package:
   for each track its URL, its exact size in bytes and its segment index (each segment's duration and
   byte length), and the description and artwork to keep. It records
   `{sub, titleId, rung, device, createdAt, completedAt?}`. `GET /api/me/offline` lists the person's
   saves across their devices; `DELETE /api/me/offline/{id}` forgets one. A title not `ready` is a
   `409`.
3. **One file per track.** `GET /media/{id}/{rendition}/track.mp4` answers the init segment and every
   segment joined, a fragmented MP4 with a known `Content-Length`, ranged so a broken transfer resumes.
   A film is a handful of requests rather than thousands. It is concatenation of files already on disk:
   the API still runs no ffmpeg.
4. **A title being saved stays.** Retention does not delete it and the hand-off does not move it
   between roots while a save of it is incomplete. A save left incomplete past a deadline stops
   holding it. Once saved, the device's copy is independent of the library.
5. **Saves yield to rooms.** A per-person and a per-host limit on concurrent `track.mp4` transfers,
   and a transfer slowed while a room is playing, so a phone filling up never stalls a shared film.
6. **`POST /api/me/progress`** takes a batch of `{titleId, positionSeconds, watchedAt}` from a device
   that watched offline. The newer of it and what the API holds wins, and history is recorded the
   same way a room records it.

Deep dive:
- Sizes per rung measured on hotbox's library: the bitrates are caps under constant quality, so the
  package reports bytes on disk, never an estimate.
- A title still owed its `v2` offers the rungs it has; whether its page says the small copy is coming.
- The segment index: read from the rung's playlist and the files' sizes, or written by the ingest
  beside the playlist?
- What a transfer may cost on hotbox's uplink while a room plays, measured.

### Step 3: saving onto the device

1. **The store** (`src/offline/`): OPFS, `/<sub>/<titleId>/` holding each track's file, the
   subtitles, the artwork and a `meta.json` (the package, the description, when it was saved). Written
   from a worker through sync access handles.
2. **Before a save**: `navigator.storage.persist()`, and `navigator.storage.estimate()` against the
   package's bytes; a film that does not fit is refused before a byte moves, with how much it needs.
3. **The transfer**: one track after another, resumed by `Range` from what is on disk, paused and
   resumed by the person, surviving a reload. A Wake Lock while the page saves keeps a phone awake.
4. **Save offline** on a film's and an episode's page, for whoever holds `cinema:offline` (`useCan`):
   a choice of the title's rungs, each with its size, the default one picked (§2), then the save's
   progress once started, and Saved on this device when done, with Remove.
5. **Profile's Offline tab** (`/profile/offline`, in `PROFILE_TABS`): every film on this device for
   this account, its size and quality, Play and Remove, what is being saved with its progress, and what
   is free on the device. The wide screen's sidebar links to it.
6. **Profile's Playback tab** gains the quality Save offline picks by default, kept in the person's
   preferences.

Deep dive:
- The tab's name beside "Save" (to a list) and "Saved" (the lists), so the two are never mistaken for
  each other.
- iOS quota and eviction measured on a real iPhone, installed and not: Safari evicts a site that is not
  on the Home Screen after seven days without a visit, and the page says so to anybody who saves
  without installing.

### Step 4: playing from the device

1. **An offline loader** for hls.js answering the master, the playlists and the segments from the
   store: the playlists are built from the segment index as byte ranges, and a segment is a slice of
   the track's file. The browser's own HLS cannot be fed this way, so a device without
   `Hls.isSupported()` is not offered Save offline.
2. **A solo timeline** behind the interface `sync.ts` gives the player: play, pause and seek act at
   once, there is no clamp to a head because the film is whole, and nothing is sent anywhere. The
   room's controls that mean nothing alone (invite, the viewers, the waiting mark) are not drawn.
3. **Progress** is written to a local log per account, and sent with `POST /api/me/progress` when a
   connection is back.
4. **Preferences** (subtitle language and style, audio language, intro skipping) are kept on the device
   from the last `GET /api/me/preferences`, so the player starts the way the person watches.

Deep dive:
- Whether the solo timeline is a second implementation of the session interface or the room's own
  logic with a local authority; which the player's existing tests cover.
- Seeking a multi-gigabyte OPFS file on a phone: the cost of a slice, measured.

### Step 5: the site without a connection

1. **The whole build is precached.** The build writes the list of every file in `dist/` into the
   service worker, and its version is the build's, so installing a new worker fetches every chunk,
   the player's among them, and the fonts and artwork. Opening the app with no connection never meets
   a chunk it has not seen.
2. **Knowing it is offline** (`src/connectivity.js`): `navigator.onLine` says only that there is no
   network. A request to the API that fails on the network, or a probe of `/api/config` that does not
   answer in time, also means offline, so a captive portal, a dead uplink and a server that is down all
   read the same. A successful request means online again; while offline the probe repeats on a
   lengthening wait, and at once on the `online` event or the page coming back into view.
3. **Signing in offline.** With no connection, a session whose access token has run out is not ended:
   the stored user's `sub`, name and actions stand for the person until the connection is back, when the
   refresh grant runs and its answer decides. The page never goes to the provider while offline. A
   device with no stored session shows one page saying a first sign-in needs a connection.
4. **What is drawn offline.** A banner across every page says the app is offline and that saved films
   play. Home leads with the films on this device. The film's and episode's pages of a saved title draw
   from its `meta.json`. Profile opens on its Offline tab and shows Playback, Subtitles and Appearance
   from the device; on a phone Me stays lit in the `TabBar` and leads there. Every other place in the
   sidebar and the `TabBar` is drawn disabled; a page that needs the server, reached by an
   address or a pushed notification, shows that it needs a connection and leads to the films on the
   device. No API answer is cached to draw a page offline: what is drawn is the device's own store.
5. **What does nothing offline.** Every control that asks the server (Save, Follow, Notify, Watch on a
   film not saved, lists, the assistant) is disabled while offline. The only writes kept for later are
   progress and the forgetting of a removed save.
6. **Coming back.** The banner goes, the session renews, progress is sent, and the device's saves are
   compared with `GET /api/me/offline`: a save the API no longer lists, or an account that no longer
   holds `cinema:watch`, is removed from the device. A page that was showing its offline state loads
   again.
7. **Losing the connection on a page.** The banner comes, the controls that ask the server are
   disabled, and the page stays. A room whose film is saved offers to carry on alone from the device at
   the room's position; one whose film is not shows the room unreachable.
8. **Signing out** asks whether to remove this account's saved films from the device.

Deep dive:
- How the API's failure is told apart from offline: a `5xx` from a server that answered is not
  offline.
- Which pages' first reads fail today with no network, by walking every route with the network cut
  (§5); each is given its offline state.
- The cost of precaching the build on a phone's first visit, measured, and whether the player's
  chunk joins the precache only on an installed app.

### Step 6: following up

- **Background Fetch** on Chromium (Android, desktop), so a save continues after the app closes.
  Safari has none, and on an iPhone a save moves only while the app is open on screen.
- **Playing a saved film in a room.** The offline loader answers from the device first, so a person in
  a shared room plays the film from disk while the hub keeps the timeline, and spends no connection on
  it.

## 5. Testing

- `npm run verify:pwa` keeps asserting that the service worker stores no media and no API answer.
  Saved films are in OPFS, never in the worker's cache.
- `npm run verify:offline`, new, against the scratch site in a real Chromium: save a short title,
  cut the network (`context.setOffline(true)`), open the app cold in a second tab, reach the film from
  Home, play it to a position, and come back online to find the position on the API. The same walk with
  the network up and the API blocked stands for a server that is down. Every route opened offline is
  screenshotted and checked for its offline state.
- `npm run verify:mobile` gains the offline states of each page.
- A real iPhone and a real Android phone, by hand, installed: a save, the app killed and reopened in
  airplane mode, playing, and storage after a week. The desktop WebKit the harness drives has neither
  iOS's quota nor its suspension of a page in the background.
