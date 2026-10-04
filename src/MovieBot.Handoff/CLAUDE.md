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
  writing. The in-flight set, keyed by download hash for a film and by hash and file for an
  episode, is what prevents it.
- **A transcode chooses its disk when it starts, against a reservation** (`TranscodePlacement`).
  The size is the ladder's bitrates and each audio track's, times the running time from a probe, plus
  an overhead for framing, playlists and artwork (`TranscodeEstimate`). The media root takes it when
  it fits what that volume has left after the downloads still arriving and the other transcodes still
  writing; otherwise the cold root, when its volume is present and has the room; otherwise the film
  is not started. It stays owed, is asked about again each poll, and the reason is logged once when it
  begins to wait. A transcode that began and then ran out of room would stop partway with a manifest
  nobody can finish, in front of a room.
- **The reservation is one account, kept in the acquire library's `DiskBudget`.** A running transcode
  holds a claim in the `TranscodeLedger`: files in the dotted `.transcode-reservations` directory
  under the media root, which this process writes and the API and the bot read, because they start the
  downloads. It is under the media root because this unit can always write there and nowhere under the
  downloads, and the dot keeps `MediaRoots.Ids`, settling, backfill and the library's listing from
  taking it for a title. A ledger that cannot be written or read never stops a download or a
  transcode: the failure is a logged warning and the claims count as none. `DiskBudget` subtracts a claim's unwritten
  remainder (its size less what is already in the title's directory) from a download's headroom, and
  the placement subtracts the downloads still arriving from the room a transcode is judged against,
  so neither side counts the free space as its own. A claim is released when its transcode ends and
  every claim is cleared when this process starts, which is what removes one a dead process left.
  Placement is decided and claimed under one lock, so two films that each fit are not both placed on a
  disk that holds one.
- **A transcode on the cold root keeps every invariant of the two roots**
  (`src/MovieBot.Core/CLAUDE.md`). Before it starts, under the title's lock, the media root's copy is
  cleared (`ColdStore.ClearWorking`), as a transcode on the media root clears the settled one; and
  nothing is cleared while the cold volume is out, in which case the film stays owed. A film made on
  the cold root is never settled, and the library's clock reads it only once it is ready.
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

## A show's torrent: one title per episode

A release carrying a season marker is a show's, and its picked files are ingested apart, each as a
title of its own (`<show>-s02e04`, `kind: "show"`, the episode's place in the manifest).

- **Which file is which episode is `EpisodeFiles`, the one reading the download picked them with.**
  Only files the client is fetching are ingested; a file switched off is not owed.
- **What is owed is read from the library every pass, never remembered.** An episode is done when its
  title is ready, so a pass after a restart, or after more episodes are picked from the same pack,
  finds exactly what is left. The torrent's `ingest` tag clears once nothing picked is owed.
- **Episodes go in order, each once enough of its own file has arrived**, under the same ceiling as
  films. The download is sequential across files, so the first episode arrives first and is
  watchable while the rest download. Each file's own completeness is what a transcode reading it
  waits on; the torrent as a whole finishes only with its last episode.
- **Every episode made is tagged on the torrent**, so a room holding any one of them holds the
  torrent against the tracker's clock. `watchable` goes on with the first.
- **An episode that fails is not started again by this process**, and the torrent is marked failed
  only when no episode of it could be made.
- **The show is named by the id the tracker filed it under, never by a search.** A show's releases
  carry no year to tell two shows of one name apart, so an episode without an id is titled from its
  release and names no show.

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

## What an episode lets a viewer skip (`MarkerPass`, `FingerprintStore`)

Once an episode is ready, its intro and its recap are written into `manifest.markers`. The methods
and their thresholds are `src/MovieBot.Ingest/CLAUDE.md`; this is what the hand-off does around them.
Only episodes are marked: a film's chapters are not read for an intro, because a film's opening is
not a title sequence a viewer wants to skip.

- **It runs after the transcode and is never what an episode waits on.** The episode is watchable
  while it transcodes and ready before this starts. The pass uses CPU only, runs `fpcalc` at a lower
  priority, and reports its own failures as warnings: an episode it could not mark has empty markers
  and is otherwise whole. A host without `fpcalc` still gets the chapter markers.
- **A chapter that names the stretch is taken before the audio is consulted**, per kind: an episode
  with an `Intro` chapter and no recap chapter has the first exact and the second searched for.
- **The episode's own subtitles are read for the guard on title sequences.** They are written before
  the status turns ready, including for a source that arrived while it transcoded, so they are on
  disk when the pass runs. A sequence a partner is given is guarded against the partner's subtitles,
  not the new episode's.
- **Fingerprints outlive the titles.** A recap is found in episodes that came before, which the
  library's clock may have removed by then, so each episode's whole-audio fingerprint is kept in
  `<media root>/.fingerprints/<show>/s05e02.fp`, about a hundred kilobytes each, written by this
  process alone. The directory is dotted so every listing of titles skips it (`MediaRoots.Ids`,
  settling, backfill, the API's library), it stays on the hot root so a missing cold volume never
  hides it, and nothing removes it with a title. The show is the IMDb id the episodes carry, or the id
  stem before `-s02e04` when nothing identified it; an episode ingested again replaces its own file.
  A lost fingerprint costs a recap that is not found, never a wrong one.
- **A title sequence found from another episode is given to that episode too**, when it has none.
  The second episode of a season marks the first. The partner's marker is written under the partner's
  own lock after the new episode's lock is released, because two episodes finishing together would
  otherwise each hold one lock and wait for the other.
- **Settling and the pass share a per-title lock (`TitleLocks`).** A manifest rewritten in the middle
  of the copy to the cold root is missing from the copy, and the disk it was written to is then
  deleted. Each takes the title's lock for all of what it does and looks the directory up again once
  it holds it. The manifest is read again right before it is written and only `markers` is replaced.
- **`moviebot-handoff --backfill-markers` marks what is already in the library.** It fingerprints
  every ready episode first and compares second, so each is compared with all the others and the first
  episode of a season can be given its sequence by the second. Fingerprints already kept are reused. It
  reads both roots and refuses to run while the cold volume is out, like `--backfill`.

## Settling onto the cold disk (`SettleWorker`, `ColdStore`)

The model of the two roots is `src/MovieBot.Core/CLAUDE.md`.

- **A film settles when its manifest says it is ready, and not before.** The subtitles, the cover art
  and the preview sheet are all written before the status turns, so a film that reports itself ready
  is a directory nothing is going to add to. Only the media root is read for films owed a move: a
  film made on the cold root is already where it is kept.
- **A film settles only when no room is playing it** (`SettleGate`). A room is playing a title when
  somebody is connected to it, running or paused (`RoomSummary.Participants`); a room merely holding
  the title does not count. Both copies are whole while they overlap, so a copy under a viewer would
  break nothing, but it saturates the disk the viewer is about to read from, and waiting costs
  nothing: a finished film does not grow, and it settles on a later pass once nobody is playing it.
  If the API cannot say, nothing settles that pass.
- **Nothing settles onto the cold root while a transcode writes to it.** Each film checks the ledger
  before its copy, so a transcode that begins between two copies holds the next one back, and a
  title a transcode is making is never owed a move, whichever root it is on.
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

## Letting a film go: two clocks

Disk is finite, and a film is let go of twice, on two unrelated clocks. The tracker's clock frees
the **source**: the torrent and the files it downloaded. The library's clock removes a **transcoded
film** that has settled on the cold disk. Neither knows about the other, and each removes only its
own half. Keeping a film is `src/MovieBot.Bot/Keep/CLAUDE.md`.

### The tracker's clock (`RetentionWorker`)

The rule itself, and the clock it runs on, are `Retention` in the acquire library; this is about who
acts on it and what is checked first.

- **The clock is the torrent client's seeding time, never the calendar.** The tracker credits seeding
  only while the client is running and the torrent is active, and the client's own count covers the
  same stretches and survives its restarts. A week is a week of seeding, and a machine that was off for
  a month has moved nothing closer to leaving.
- **Nothing is removed under the tracker's minimum plus a margin, whatever the window says.** The
  client's clock can only run ahead of the tracker's, by the announces that never landed, so the floor
  carries a margin and the window sits days above it.
- **Only the torrent and its files go, through the client.** Deleting them from under it would leave
  the client announcing a file it no longer has. The title directory, the transcode, is not this
  clock's: it is what the library keeps.
- **A film a room holds is not removed** (`OccupiedRooms`). The API is asked which titles the rooms
  hold, whether or not anybody is in them this second, and if it cannot answer the pass removes
  nothing. The API forgets an idle room on its own, so a room blocks a removal for at most that idle
  window.
- **A film still owing its transcode is left alone.** The transcode is reading the source. One owed
  for a week is a fault to read about in the log, not a source to remove.
- **Nothing is removed while the cold volume is out.** A pass that cannot see the library acts on
  nothing it cannot ask about.
- **A kept film's source goes like any other.** A keep pins the film in the library and has no say
  over a torrent the tracker has been paid its seeding on.

### The library's clock (`LibraryWorker`)

A settled film, one that is ready and whose directory is on the cold root alone, leaves once it has gone
`Library:LeaveAfterDays` without a play, unless somebody keeps it. The rule is the API's alone
(`LibraryClock`), because the plays and the keeps are its records; the hand-off asks `GET
/api/library/due` and deletes what comes back, being the only process allowed to write the media
roots.

- **It deletes only on the cold root, and only a directory directly under it.** An id comes over the
  wire, so one that could name a path outside the root, or a link, is not a film and is skipped.
- **A film a room holds is not removed**, checked again here at the moment of deleting rather than
  trusted from the API's answer a moment ago.
- **A film that is also under the media root is not removed.** It is mid-move or being made again, so
  it has not settled, and the cold copy is what its readers are on.
- **If the API cannot answer, nothing is deleted.** An empty list is a fact and a missing one is not;
  a film deleted on a guess cannot be taken back. The same holds for the rooms, and for the cold
  volume being out.
