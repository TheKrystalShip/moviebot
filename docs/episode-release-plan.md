# Episodes a room moves past, and episodes that fail while arriving

A plan, not canon. What it settles moves into the docs beside the code it governs
(`src/MovieBot.Api/Acquisition/CLAUDE.md`, `src/MovieBot.Handoff/CLAUDE.md`, and for the acquire
library's half `../moviebot-acquire/CLAUDE.md`) when it lands, and its section here is struck. When the
last step lands, this file is deleted.

## 1. What this is for

A room waiting on an episode that is still arriving can be moved on with Next, and each step is a
Watch now of the next episode into the same season torrent. Every file a step turns on stays on, at
the client's highest priority, after the room has left it. Pressed quickly through a season, the
torrent ends up fetching every episode stepped through at one priority, in the torrent's own file
order, which is not episode order; the episode the room is on arrives last among equals, and the
hand-off's wait for its source header runs out.

The hand-off then remembers each such episode as failed for the life of the process, marks the
torrent `ingest-failed` once every picked episode is in that set, and nothing clears the tag. Asking
again re-adds `ingest`, finds nothing owed, and fails at once; a room waiting on any episode of the
torrent not yet made is told it could not be made watchable, because the loader checks the torrent's
failure before the episode's own title.

Two things are wanted: a file the room moved past is switched off when nothing else wants it, and a
failure while a source is still arriving is not a verdict on it.

## 2. Where the code stands

- `AcquisitionService.StartPickedAsync` (moviebot-acquire): picking from a torrent the client already
  holds turns on only the newly picked files and sets the first of them to priority 7. Nothing lowers
  or switches off a file afterwards.
- `WatchNowLoader.TendAsync` (`src/MovieBot.Api/Acquisition/WatchNow.cs`) already notices a room that
  holds something other than what its `watch:<room>:sXXeYY` tag names, and clears the tag. That is
  the moment the room moved on.
- Nothing records why else a file is on. `FollowWatcher`, `OfflineWanting`, `NextEpisodes`, the bot
  and a whole-torrent download pick files with no per-episode tag.
- `HandoffWorker._failedEpisodes` holds a failed episode for the life of the process.
  `SweepShowAsync` calls `FailAsync` once nothing picked is owed and nothing is ready, which tags
  `ingest-failed` and clears `ingest`. `ingest-failed` is cleared nowhere.
- An episode starts at `Handoff:StartAtProgress` (0.05) of its file, and `FfProbe.WaitForHeaderAsync`
  gives up after 120 s; `PlaceAndIngestEpisodeAsync` counts that as a failure.

## 3. Steps

### A1. Each pick says why it is on

`moviebot-acquire` `TorrentTags` gains `keep:sXXeYY`, and a bare `keep` for a whole torrent.
`Downloads.StartAsync` writes `keep:` for every pick that is not a room's Watch now: a follow, a
device's want, the next episode fetched ahead, the bot. A whole-torrent start writes `keep`. A room's
pick keeps writing only `watch:<room>:sXXeYY`.

### A2. Switching episodes off

`AcquisitionService` gains a public method that sets the files of given episodes to priority 0,
matched by path through `EpisodeFiles` as picking matches them, skipping a file already complete.
Version bump in moviebot-acquire and the pin in moviebot.

### A3. The release rule

In `TendAsync`, when the room moved on from episode X (not when it loaded it), X's file is switched
off only when all of these hold:

1. no other `watch:*:X` tag is on the torrent;
2. no `keep:X` and no bare `keep`;
3. the hand-off has not started X: no `library:<id>` tag for X (the hand-off writes it before the
   transcode) and no title for X in the library;
4. the file is not complete.

Rule 3 is what keeps a running transcode's source from being cut off under it. A file switched off is
not owed, so the hand-off drops it on its own.

### A4. The one being watched comes first

In `StartPickedAsync`, on a torrent already held, a new pick taking priority 7 lowers every other
picked, incomplete file to normal, so one file at a time is at the top whatever the torrent's file
order.

### A5. Torrents that already exist

They carry no `keep` tags. A3 acts only on a torrent carrying a marker tag every new pick writes, so an
older torrent still arriving is left as it is.

### A6. Next and Previous, one step at a time

The player's Next and Previous (`web/site/src/player/Player.jsx`, the control bar and the Next
episode overlay) are disabled from the press until the room's state changes. `RoomEpisodes` takes one
step per room at a time and drops a step that arrives while another is in progress, so every viewer
in the room and every client, the TV app included, is held to it. Stepping slowly still fetches each
episode stepped to; A3 is what makes that cheap.

### B1. A header still arriving is not a failure

`SweepShowAsync` starts an episode on the contiguous bytes arrived from the file's own start
(`AcquisitionService.ReadableBytesAsync`) reaching a byte floor, in place of a fraction of the file.
In `PlaceAndIngestEpisodeAsync`, a header wait that runs out on an incomplete file leaves the episode
owed and out of `_failedEpisodes`.

To measure before choosing the floor: E01 of the torrent that showed this was past 5 % and its header
still had not arrived.

### B2. Failures expire

A failure is kept with the file's progress when it happened. An episode that failed on an incomplete
file is tried once more when the file completes; one that failed on a complete file stays failed for
the life of the process.

### B3. `ingest-failed` is cleared

The hand-off clears it when it marks any episode playable, and when it sees a torrent carrying both
`ingest` and `ingest-failed`, which is somebody asking again; it then forgets that torrent's expired
failures.

### B4. A room waiting on an episode reads the episode first

In `TendAsync`, for a `watch:` tag naming an episode, whether that episode can play is checked before
the torrent's `ingest-failed`, and the room is told of a failure only when the episode's own title
failed, or the torrent failed and the episode cannot play.

## 4. Verification

- `tests/MovieBot.Tests/RoomEpisodesTests.cs`: Next pressed several times while waiting leaves only
  the last file on; a step during a step is dropped.
- The release rule: another room on X, `keep:X`, X already started, X complete; each keeps the file.
- The hand-off: a header timeout on an incomplete file stays owed; a failure retried once the file
  completes; `ingest-failed` cleared on the next playable episode.
- On hotbox, a disposable show torrent: Next pressed three times while waiting, then the file
  priorities read from qBittorrent.
