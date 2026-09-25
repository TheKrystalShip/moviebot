# MovieBot.Handoff: a finished download into the library

The hand-off runs as its own service, and each of the three reasons is a constraint rather than a
preference: it writes into the media root, which the bot is not permitted to do; a transcode takes
minutes, so a bot restart would abandon it halfway; and it holds the GPU, which has no business inside
a gateway connection.

## Getting a film watchable

Transcoding alongside the download exists so a film can be watched while it arrives. Everything here
is about not losing that.

- **A film is announced when it becomes playable, not when the transcode ends.** The playlists grow as
  segments land, so a film is watchable seconds in against roughly ten times realtime. Waiting for the
  whole transcode holds a room for a quarter of an hour in front of a film that was already playing.
- **Playable means the manifest carries a head**, or reports itself ready. Announcing at the end of the
  *download* instead is too early by the other margin: the transcode has not started, so the manifest
  does not exist and `/watch` finds nothing.
- **A transcode starts before the download finishes**, once enough has arrived for the container's
  index to be readable. The head start only covers the opening; the reader is held behind the arrived
  bytes from then on (`src/MovieBot.Ingest/CLAUDE.md`), so too small a threshold costs a pause rather
  than a broken film.
- **Films are ingested concurrently, up to a ceiling.** A film is watchable seconds after its own
  transcode starts and not before, so one taken strictly in turn does not exist yet: not in the
  library, not by name, however much of it has arrived. They share one card and each runs slower for
  it — around twelve times realtime alone, six apiece with two — and every one of them still outruns a
  person watching several times over, which is the only rate that matters.
- **A sweep never restarts what it is already running.** Sweeps carry on while a transcode does, and an
  ingest replaces a title's directory wholesale, so a second start would delete what the first is
  writing. The in-flight set keyed by download hash is what prevents it.
- **A film owing a transcode and a film being watchable are different facts with different tags.**
  `ingest` survives until the transcode finishes; `watchable` is set seconds into it and is what the
  announcement waits for. One tag cannot answer both: clearing it to let the announcement out would
  leave nothing to say the rest of the transcode is still owed, and an interrupted one unrecoverable.
- **A poster is put on disk before the manifest names it.** A manifest is read the moment it exists,
  and a surface that fetches artwork on its own servers caches the 404 rather than trying again. The
  catalogue's poster is already a local file and waits for nothing; a source's own cover art has to be
  demuxed out of it, so for a film still downloading it is named only once it has been extracted.
- **The feature is the largest video file above a floor**, with samples and trailers excluded by name.
  Without the floor, a torrent holding only a sample yields the sample and the room watches ninety
  seconds of a film.
- **The library id comes from the same parser the search uses**, and the hand-off tags the torrent with
  it before the transcode starts, so the bot never derives it. A second name parser written here would
  not disagree loudly; it would give one film two ids and nobody would notice until the library held
  both.
- **The unit keeps the GPU devices visible.** `PrivateDevices` would hide them and drop the pipeline
  onto the CPU, where a feature costs hours instead of minutes.

## Naming a film (`FilmMetadata`)

A release name is what a film arrives as. It is not what the film is called.

- **A film is named by the catalogue wherever the catalogue has been asked.** `FilmMetadata` resolves
  an IMDb id — the tracker supplies one, and a parsed name and year are searched when it did not — and
  what comes back is the name, the year, the billing and the poster. `manifest.Title` becomes that
  name, and `manifest.Film` keeps the rest. Which cut arrived stays in `source.release`, which is the
  only place it is a fact.
- **It is resolved before the transcode, never after.** A film is watchable, and announced, within
  seconds of the first segments landing. Metadata added when the transcode finishes arrives a quarter
  of an hour after every message that would have shown it.
- **A film named wrongly is worse than a film not named.** Everything downstream believes it, so a
  search that cannot be sure returns nothing: a year given and matched by nothing finds nothing rather
  than the closest thing.
- **A source that shipped its own cover art keeps it.** It came with the release, and the catalogue's
  is only ever a stand-in for a film that has none.

## Settling onto the cold disk (`SettleWorker`, `ColdStore`)

The model of the two roots is `src/MovieBot.Core/CLAUDE.md`.

- **A film settles when its manifest says it is ready, and not before.** The subtitles, the cover art
  and the preview sheet are all written before the status turns, so a film that reports itself ready
  is a directory nothing is going to add to.
- **The move is a copy, and it is ordered so that every failure before the last step is harmless.**
  The two roots are different filesystems, so nothing about it is atomic on its own: the film is
  assembled under `.incoming` on the cold root, checked against what was read, committed to the disk
  with one `sync -f`, and renamed into place — a rename within one filesystem, which either happened
  or did not. The copy on the media root goes last. A stop anywhere before that leaves the film
  exactly where it was and the next pass clears what was half written.
- **One film at a time.** The copy saturates the disk it writes to, and a second alongside it would
  divide the same throughput while the transcode this is making room for reads the disk being
  drained.
- **Timestamps are carried across.** A poster and a sheet of scrub previews keep their names and are
  served asking to be revalidated, so a film stamped with the moment it moved would have every viewer
  fetch all of it again for nothing.

## Letting a film go (`RetentionWorker`)

Disk is finite, and a downloaded film that nobody keeps leaves after a week of seeding. The rule
itself, and the clock it runs on, are `Retention` in the acquire library; this is about who acts on it
and what is checked first. Keeping a film is `src/MovieBot.Bot/Keep/CLAUDE.md`.

- **The clock is the torrent client's seeding time, never the calendar.** The tracker credits seeding
  only while the client is running and the torrent is active, and the client's own count covers the
  same stretches and survives its restarts. A week is a week of seeding, and a machine that was off for
  a month has moved nothing closer to leaving.
- **Nothing is removed under the tracker's minimum plus a margin, whatever the window says.** The
  client's clock can only run ahead of the tracker's, by the announces that never landed, so the floor
  carries a margin and the window sits days above it.
- **The hand-off prunes, because it is the only process allowed to write the media root.** The bot and
  the API hold that root read-only, and the torrent's files go through the client rather than being
  deleted from under it, which would leave the client announcing a file it no longer has.
- **The title directory goes first, then the torrent.** A failure between the two leaves a torrent
  that the next pass finds again; the other order leaves a directory nothing points at.
- **A directory is deleted only when its manifest names the source this download arrived as.** A film
  fetched twice as two releases lands under one id, the second ingest replacing the first's directory,
  and the older torrent's turn to go must not take the newer transcode with it. A mismatch removes the
  download alone and says so.
- **A film a room holds is not pruned** (`OccupiedRooms`). The API is asked which titles the rooms
  hold, whether or not anybody is in them this second, and if it cannot answer the pass removes
  nothing. The API forgets an idle room on its own, so a room blocks a prune for at most that idle
  window.
- **A film still owing its transcode is left alone.** The ingest deletes and rewrites its directory,
  and two processes doing that to one directory is how a film ends up half written. One owed for a
  week is a fault to read about in the log, not a film to remove.
- **A title with no download behind it is on no clock.** A film put in the library by hand owes the
  tracker nothing and carries no seeding time, so retention does not apply to it and the launch says
  nothing about how long it stays.
