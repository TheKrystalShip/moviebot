# The films saved on a device

`src/offline/` is the store Download writes and the site reads with no connection: the Origin
Private File System, one directory per account, one directory per saved title. It is never the
service worker's cache, which keeps no media and no API answer (`public/sw.js`). The saver writes it
and the pages and the player read it; this file is the contract between them.

## Layout (`layout.js`)

```
/<sub>/                    an account, by the provider's `sub`
/<sub>/<titleId>/          one saved title
/<sub>/<titleId>/meta.json what was saved, written by the saver
/<sub>/<titleId>/<name>    each file meta.json lists: tracks, subtitles, artwork
/<sub>/.shows/<imdbId>/            a show a saved episode belongs to (`shows.js`)
/<sub>/.shows/<imdbId>/show.json   what the API said of it
/<sub>/.shows/<imdbId>/<name>      each artwork file show.json lists
```

The names are encoded by `accountDirName`, `titleDirName` and `showDirName` (percent-encoding, with
`.` encoded too), so any `sub`, title id and IMDb id make a legal name; `titleIdOf` reads one back.
An encoded name never holds a bare `.`, so `.shows` is no title's. `layout.js` touches no DOM, so the
worker that writes a save names every file the way the page reads it. Nothing else is kept at the root.

## `meta.json`

```jsonc
{
  "format": 1,                       // FORMAT; a save of another format is not listed
  "titleId": "coherence-2013",
  "account": "<sub>",                // the directory it is under
  "kind": "film",                    // "film" | "episode"
  "name": "Coherence",               // a film's name; an episode's show's name
  "year": 2013,                      // or null
  "imdbId": "tt2866360",             // the film's, or the episode's show's; or null
  "show": { "imdbId": "tt…", "name": "…" },                  // an episode's show; null for a film
  "episode": { "season": 2, "number": 4, "name": "…" },     // null for a film
  "overview": "…",                   // the episode's own for an episode, the film's otherwise
  "genres": ["Drama"],
  "rating": 7.2,                     // the film's, or an episode's show's; or null
  "durationSeconds": 5340,
  "quality": { "rendition": "v1", "width": 1280, "height": 546, "label": "546p" },
  "files": [                         // every file the save holds, each written under its `name`
    { "name": "v1.mp4", "role": "video", "rendition": "v1", "bytes": 1339012345 },
    { "name": "a0.mp4", "role": "audio", "rendition": "a0", "language": "eng", "label": "English", "bytes": 163456789 },
    { "name": "s3.vtt", "role": "subtitle", "id": "s3", "language": "eng", "label": "English", "bytes": 81234 },
    { "name": "poster.jpg", "role": "poster", "type": "image/jpeg", "bytes": 81234 },
    { "name": "backdrop.jpg", "role": "backdrop", "type": "image/jpeg", "bytes": 234567 }
  ],
  "bytes": 1502865913,               // every file in `files`, exactly
  "saveId": "5c0f3b2a9e7d4c18",      // the API's record (`package.save.id`)
  "startedAt": "2026-10-06T19:00:00Z",
  "completedAt": null,               // set by the saver once every file holds its `bytes`
  "package": { }                     // the OfflinePackage POST /api/me/offline answered, whole
}
```

The fields above `package` are what a page draws from, so nothing reading a save parses the package.
`package` is kept whole for the player: the segment index (`tracks[].initBytes`, `segmentSeconds`,
`segmentBytes`), the trimmed `manifest`, each track's `etag` for resuming, and the URLs are there and
nowhere else. Its shape is `docs/api-contract.md`'s ("Save offline"). A `role` is one of `ROLES`:
`video`, `audio`, `subtitle`, `poster`, `backdrop`, `still`, `thumbnails`.

A save is under way until `completedAt` is set; how far it has got is what is on the device of each
listed file, so the saver never writes progress into `meta.json`.

## Reading (`store.js`, `hooks.js`)

| Call | Answers |
|---|---|
| `offlineSupported()` | whether this browser has the Origin Private File System |
| `listSaves(sub)` | every save of the account, `{ titleId, meta, receivedBytes, complete }`, the newest started first |
| `readSave(sub, titleId)` | one save, or null |
| `saveFile(sub, titleId, name)` | a listed file as a `File` (a `Blob`, sliceable), or null |
| `artworkUrl(save, role)` | an object URL for `poster`, `backdrop` or `still`, or null; the caller revokes it |
| `space()` | `{ usage, quota, free }` from `navigator.storage.estimate()`, or null |
| `removeSave(sub, titleId)` | removes a save no worker is writing, and queues its `saveId` to be forgotten at the API (a page removes through `removeFromDevice` in `saver.js`); the last episode of a show to go takes the kept show with it |
| `readShow(sub, imdbId)`, `listShows(sub)` | a kept show, `{ imdbId, keptAt, show, files }`, or null; every kept show of the account |
| `showFile(sub, imdbId, entry)` | a kept show's artwork file as a `Blob` of its listed `type`, or null |
| `writeShow(sub, kept, artwork)`, `removeShow(sub, imdbId)` | writes a kept show through `show.worker.js` (`shows.js` calls it), and removes one |
| `removeAccount(sub)` | removes every save of the account, queueing each `saveId` |
| `pendingForgets(sub)`, `forgotten(sub, id)` | the queue of the API's records to forget, and taking one off once `DELETE /api/me/offline/{id}` answered |
| `savesChanged()`, `onSavesChanged(fn)` | says the saves changed, here and in this origin's other tabs (a `BroadcastChannel`), and listens for it |

`hooks.js` wraps them for a page, read again on every change: `useSaves()`, `useSave(titleId)`,
`useSpace()` and `useArtwork(save, role)`, which revokes its object URL when the page lets go of it.
Each reads for the signed-in account (`session.user.id`).

A page opened with no connection is the same page drawn from here, so `hooks.js` also answers what is
downloaded in the shapes the API answers (`library.js` does the shaping), from finished saves only,
with artwork as object URLs it revokes once the page lets go or a newer set is made:

| Hook | Answers, as the API would |
|---|---|
| `useDeviceLibrary(enabled)` | `{ films, shows, titles, continuing }`: the films as `GET /api/titles` lists them, the shows with a downloaded episode as `GET /api/shows` does (one each, `downloaded` how many), every downloaded title by id, and `GET /api/me/progress`'s entries for those this person is part way through, the newest first |
| `useLibrary(kind, fromDevice)` | `useLoad`'s `{ data, error, loading, reload }` for the library's films or shows: the API's, or with `fromDevice` the device's |
| `useDeviceTitle(titleId, enabled)` | a downloaded title as `GET /api/titles/{id}` (`manifest`), its `GET /api/films/{imdbId}` (`details`) and its listing entry (`summary`); `manifest.data` null when it is not downloaded |
| `useKeptShow(imdbId, enabled)` | `GET /api/shows/{imdbId}` from the kept show, its artwork the device's, nothing leading to the catalogue, and `resume` only when that episode is downloaded; from the downloaded episodes alone when no show is kept; null when neither is here |

What the device does not hold is not made up: a film's cast and the server's own facts
are absent from a page drawn from here, and a section with nothing in it is not drawn.

## Shows kept on the device (`shows.js`)

A show's page with no connection draws every season and episode, so a show is kept beside its
downloaded episodes: `show.json` is `{ imdbId, keptAt, show, files }`, `show` the
`GET /api/shows/{imdbId}` answer whole, and `files` its artwork, `[{ name, url, type }]` — `poster`,
`backdrop` and each episode's still as `s<season>e<number>`, fetched from the open artwork routes. The
artwork is written first and `show.json` last, so a reader never meets a listing of a file not yet
there. `keepShow(sub, imdbId, answered)` writes it, fetching the answer when not handed one and only
the artwork whose address changed; writes of one show run one after another. It is called when an
episode's download starts (`startSave`), again by the show's page whenever it reads the show from the
API while it is kept (`refreshShow`), and by `sync.js` for any downloaded episode whose show is not
kept (`keepMissingShows`). `writeShow` posts the files to `show.worker.js`, which writes them through
sync access handles, as the saver's worker does, so every browser with the file system keeps a show.
A show's page drawn from here with no show kept shows its downloaded episodes alone.

## Writing: the saver (`saver.js`, `saver.worker.js`)

`saver.worker.js` writes through sync access handles, which exist only in a dedicated worker, using
`saveDir(sub, titleId, { create: true })` for the directory. Its order is fixed, so a reader never
meets a half-made save: the subtitles and artwork are fetched into memory first (they are small), so
`meta.json` is written first with `completedAt: null` and every file with its exact `bytes`; then each
file under its listed `name`; then `meta.json` again with `completedAt` set. A subtitle's and an
artwork's entry also carries the `url` it came from and whether it needs the bearer token (`auth`, the
preview sheet's), so a save interrupted before they were written fetches them again. Artwork is named
by its role and the type it arrived as (`poster.jpg`); one that cannot be fetched is left out of the
listing. It posts `"changed"` on the `cinema-offline` channel after each write a reader should see,
which reaches every page, the one that started the save among them.

A track resumes from its length on disk, by `Range` with `If-Range` naming the package's `etag`. An
answer that is not that range (a whole `200`, a `206` from elsewhere) or a `409` means the save is
stale: the page asks for the package again as it was chosen, once, and the worker writes it as new,
starting again every track whose `etag` changed and removing any file the new listing does not name.
A `429` waits for `Retry-After`; a broken connection is retried three times, then the save waits for
the connection to come back. Each request asks the page for the bearer token, so a transfer that
outlives a token renews it.

`saver.js` is the page's half: `startSave` (asks `navigator.storage.persist()`, refuses with `NoRoom`
before anything is fetched when `estimate()` leaves too little, then posts the save and queues it),
`pauseSave`, `resumeSave`, `removeFromDevice` and `removeAllFromDevice` (which stop a transfer first),
`resumeUnfinished`, and `useSaving(titleId)` / `useSavingAll()`, what each save under way is doing:
`{ state, received, bytes, message }`, `state` one of `queued`, `saving`, `paused`, `waiting`,
`elsewhere`, `failed`. `restingState` is what a save under way that no tab writes is: `paused` when the
person paused it, else `queued`, or `waiting` with no connection. One save is written at a time, one
track after another. `received` counts the files in the order `meta.json` lists them, each whole
before the next, so the download sheet reads how far each file has got from that one number. A Wake Lock keeps the screen on while the page is in view and a save is written.
Pauses are kept per account in localStorage (`cinema.offline.paused.<sub>`), so a reload resumes every
unfinished save but those.

**While a file is being written, reading it from the page answers an empty file** (Chromium answers
the length it had when its writer opened it). `receivedBytes` is therefore short while a track is
written, and a page drawing progress reads `useSaving` first and the store second.

A save is written only by the worker holding its lock (`cinema-offline-save:<sub>/<titleId>`, Web
Locks), so two tabs never write one save. A tab that finds it held waits for the lock and carries the
save on when the other lets go. Tabs say what their saves are doing on `cinema-offline-saving`, so
every tab draws the same progress, and a pause or a removal in one stops the transfer wherever it runs.
Removing takes the lock, so it waits for the writer to let go; `removeSave` alone is for a save no
worker is writing.

A save the API answers `POST /api/me/offline/{id}/complete` for is one whose `completedAt` is set here.
The page posts it when the worker finishes, and `sync.js` posts it again for any it could not.

## What is picked (`choose.js`)

The download sheet's defaults: the rung is the person's `offlineQualityHeight` matched to the widest rung no
wider than that height at 16:9 (`rungFor`), else the smallest on a phone or a tablet (a coarse pointer
and no fine one) and the step-down anywhere else; the audio in the person's language, else the
title's default; the text subtitles in the person's subtitle language and the forced ones in each
picked audio language. `deviceName()` is what this device is called on the API's records: the browser,
the system and a mark kept in localStorage, so saving a title again from here replaces this device's
save and never another's.

## Keeping in step with the API (`sync.js`)

`useOfflineSync` (in `App.jsx`) runs `reconcile` when a page loads with a connection and whenever the
connection comes back. It forgets at the API the saves removed with no connection (`pendingForgets`),
removes from the device every save whose `saveId` the API no longer lists (one started after the list
was asked for is left alone), removes every save of an account that no longer holds `cinema:watch`,
tells the API of a save finished while it could not be told, resumes every unfinished save that is
not paused, and keeps every show a saved episode belongs to that is not kept. A save with no `saveId`
is not the API's to judge and is left as it is.

## Playing a saved film

`playSavedPath(titleId, at)` in `src/paths.js` is the address: a room address of its own with
`?title=<titleId>&offline=1` (and `&at=<seconds>` to start somewhere). `App.jsx` opens it with the
room's player, `SiteRoom`, passing `offline`, at any time, connection or none; a room address without
the flag, opened with no connection, says it is out of reach. The player, given `offline`, reads the
save from here for the signed-in account and plays it alone: no hub, no ticket, a local timeline. A
room whose film is saved whole here plays it from the device too. What the player reads of a save
(`src/player/deviceCopy.js`) is a save whose `completedAt` is set: `package.manifest`, and for each
of `package.tracks` the file listed with that `rendition` and a `video` or `audio` role, which must
hold exactly the track's `bytes`; a `subtitle` file by its `id`, the `thumbnails` file and the
`poster`. How it plays them is `src/player/CLAUDE.md`'s.

## Progress on the device (`progress.js`)

A film played alone writes where it got to into a log per account in localStorage
(`cinema.progress.<sub>`), one entry a title, `{ titleId, positionSeconds, watchedAt }`, the latest
replacing the one before. `sendProgress()` sends the signed-in account's log with
`POST /api/me/progress` and takes off each entry the API answered for, unless it was written again
meanwhile; `App.jsx` calls it whenever somebody is signed in and the server can be reached, and the
player when a film played alone is left. `resumeAt(sub, titleId, duration)` is where a saved film
starts when nothing names a position: the newer of the log and `GET /api/me/progress` (asked only
while the server can be reached), or the beginning once nine tenths of it was watched.

Every `GET /api/me/progress` answer is also kept per account (`cinema.progress.kept.<sub>`) by
`api.js`. `progressOnDevice(sub, durationOf)` is where the person got to in each title as this device
knows it, the newer of that answer and the log, leaving out what was watched past nine tenths; it is
what a page drawn from here resumes from and lists under Continue watching. Signing out with the
saves removed forgets the kept answer, and the log stays until the API has it.

## Preferences on the device (`preferences.js`)

Every answer of `GET` and `PUT /api/me/preferences` is kept per account in localStorage
(`cinema.preferences.<sub>`) by `api.js`. With no connection `keptPreferences(sub)` is how the person
watches: Profile's Playback and Subtitles show it, and the player starts by it. It is the API's answer
as it was; nothing writes it but an answer.

## Signing out

Signing out with saves on the device asks whether to remove them (`leave.js`,
`components/SignOutDialog.jsx`). Either way a transfer under way stops. Kept, they stay under the account's `sub` for its next sign-in on
this device; removed, the account's directory and its kept preferences go, and the API's records are
forgotten while the token still works.
