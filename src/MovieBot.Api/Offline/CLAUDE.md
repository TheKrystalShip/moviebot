# Save offline (`Offline/`)

A person copies a film onto their device and plays it there alone, whether the library holds it
whole, is still making it, or has yet to fetch it. The wire is `docs/api-contract.md`, "Save
offline"; these are the rules it rests on.

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
- **A resume never joins two films.** The track's strong `ETag` hashes every file's name and length
  and carries how many files that is, a segment is never rewritten under its own name, and a `Range`
  whose `If-Range` names another `ETag` is answered with the whole track. `TrackIndex.Continues`
  accepts a validator whose count of files hashes the same over this track's first files: the bytes
  that validator stood for are how this track begins.
- **A title still being made is saved as it grows.** The encoder only ever adds a segment to a
  playlist once it is whole, so a playlist not yet ended is a track as far as it has got: the offer,
  the package and the track route read it so (`growing`), and a device holding a stretch of it
  resumes by the validator that stretch arrived under. A finished title's track is only ever its
  whole rendition. A title that failed is not saved.
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
- **A title the library does not hold is wanted, then saved** (`OfflineWanting`). A want names a
  film, or an episode by its show and place, a device, a height and the audio and subtitle languages
  asked for, and is kept beside the saves. A release says nothing of its audio but
  its name, and one made from the original carries the original's language unsaid, so an audio
  language that is not the title's own prefers a release whose name carries it
  (`Acquisition/ReleaseLanguages`), and the device picks the track in it where the title has one.
  The library copy is everybody's, so that preference decides what everyone later watches too. Asking records it and answers at once; the release is picked and the download
  started after the answer, as Watch now does, a film's at 1080p and a season's episodes out of its
  pack at 720p, one download per release, and a film already arriving is waited on rather than
  fetched again. Fetching spends the disk and the tracker, so asking takes `cinema:download` as well
  as `cinema:offline`, and nobody is told the download can be watched, because the device is what the
  person waits on. Where a want stands is read from the library and the torrent client each time it
  is asked and never kept, so a restart loses nothing. The device saves the title once it is ready,
  which lets the device's wants for it go; a want also goes when the person lets it go, and is not
  waited on past `Offline:WantDays`. A want holds nothing: the download is the library's like any
  other.
- **A save can take a subtitle language as well as subtitle ids.** Its text subtitles in that
  language are added each time the package is asked for, so one that appears later (a growing
  title's are extracted once its source is whole) rides on a later stretch. A title carrying none
  has one fetched from the subtitle index (`Subtitles/SubtitleFetcher`) once its source is known,
  the package waiting on the index at most 20 seconds; it is looked for once per save and language,
  since each fetch spends the day's allowance.
- **Before a title exists, its size is estimated, and said so** (`OfflineEstimates`). The estimate
  is the runtime times the median bytes a second the library's finished titles take at each height,
  over the rung a device takes at that height and the default audio, measured from a sample of
  titles in the background a minute after start and every six hours, since measuring stats every
  segment. It is the only size here that is not bytes on disk, and a page shows it as roughly.
- **Progress from a device is recorded where it is newer** (`WatchRecord.RecordIfNewer`): the
  person's entry and the film's last play each move only forward, a `watchedAt` ahead of the server's
  clock is taken as now, and a title the library no longer holds is skipped, as a room's stretch is.
