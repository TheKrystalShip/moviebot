# Save offline (`Offline/`)

A person copies a film onto their device and plays it there alone. The wire is
`docs/api-contract.md`, "Save offline"; these are the rules it rests on.

- **A track is the files already on disk, joined.** `TrackIndex` reads a rendition's playlist for
  its init segment and its segments in order and measures each file; `TrackResult` writes them out
  one after another. The API runs no ffmpeg for it and writes nothing. The playlist is the authority
  for which files make the track, because their names are not one pattern: a rung encoded again names
  its segments `q23-seg…`, and older titles cut audio at a length of its own.
- **Every size is bytes on disk.** A rung's bitrate is a cap under constant quality and overstates it
  two to three times, so the offer and the package carry each track's measured length, and the
  segment index (`segmentSeconds`, `segmentBytes`) is what a device builds its playlists from.
- **A track is measured once.** `TrackIndexes` keeps each one keyed by its playlist's path, length and
  write time: measuring is a stat of thousands of segments on a disk that may be a platter, and a
  resumed transfer asks for the same track again. A rendition encoded again rewrites its playlist.
- **A resume never joins two films.** The track's strong `ETag` hashes every file's name and length,
  a segment is never rewritten under its own name, and a `Range` whose `If-Range` names another
  `ETag` is answered with the whole track.
- **A title moving between disks under a transfer is followed.** A file not where the transfer found
  the title is looked for in the title's directory as it is now; both copies are whole while they
  overlap, so the bytes are the same.
- **A track opens to the bearer of somebody holding `cinema:offline` who saved the title, and never
  to a ticket.** The limits are per person and a ticket names nobody, and a save is what holds the
  title in place for the transfer.
- **A save still being made holds its title** (`OfflineSaves`, `ISaveHolds`). Until the device says
  it holds every byte, the save is forgotten, or `Offline:HoldHours` after it was made, the library's
  clock does not let the title (or an episode's season) go, and `/api/library/saving` tells the
  hand-off, which then neither settles, re-encodes, removes, nor frees the source of it. A save past
  its hold is still listed and holds nothing. Saving the same title on the same device again is that
  save made again: one record, its hold started over.
- **Transfers yield to rooms** (`Transfers`). A person runs at most `Offline:MaxTransfersPerPerson`
  tracks at once and the server `Offline:MaxTransfers`; past either a request is a `429` with
  `Retry-After`. A `HEAD` moves nothing and is never counted. While a room has somebody in it, each
  transfer is paced to `Offline:MegabitsPerTransferWhileWatched`, checked between every 64 KiB it
  writes and measured from the moment the pace last changed, so a room starting mid-transfer slows it
  within a second and one ending lets it run without repaying the past. A track goes out `no-store`,
  so a browser keeps no second copy, and `X-Accel-Buffering: no`, so a proxy cannot read it off the
  disk faster than the pace. Each transfer's bytes, time and rate are logged when it ends.
- **The records are the person's own.** Listing, completing and forgetting a save take the self
  actions, so a device can still learn its saves are gone from an account that may no longer save.
  Forgetting a save that is not there is the same end state and answers `204`.
- **Progress from a device is recorded where it is newer** (`WatchRecord.RecordIfNewer`): the
  person's entry and the film's last play each move only forward, a `watchedAt` ahead of the server's
  clock is taken as now, and a title the library no longer holds is skipped, as a room's stretch is.
