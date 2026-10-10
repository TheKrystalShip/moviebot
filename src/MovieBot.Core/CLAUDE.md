# The two disks a film lives on (`MediaRoots`)

A film is kept on one disk and its source on another. The download is written beside the media root
and seeds there for a week; reading a finished film back is one playhead at a little over a megabyte a
second, which any disk serves. So the cold root holds the library, and the library's capacity is the
cold disk's. A transcode is written to whichever root the host prefers (`Handoff.TranscodeDisk`), or to
the other when the preferred one has no room, and a film made on the media root is moved to the cold
one when it is finished and no room is playing it. The placement and the move are the hand-off's
(`src/MovieBot.Handoff/CLAUDE.md`).

- **A title is under exactly one root, and cold is resolved first.** The only moment it is under both
  is the one between the copy landing and the original being deleted, and both are whole for it, so
  every reader is already on the copy that is staying. A film made again under an id the library
  already holds has the other root's copy cleared before the transcode starts, whichever root it is
  made on, or the previous release would outlive the new one and, if it is the cold copy, be the one
  every room opened. Neither root's copy is cleared while the cold volume is out.
- **A title being made on the cold root is served like any other.** Its directory resolves cold first,
  its manifest says it is transcoding so its playlists are `no-store` and its seeks clamp to the head,
  and it is not settled, because settling reads the media root only. It is on the library's clock only
  once its manifest is ready.
- **Which disk a film is on changes no URL and appears nowhere on the wire.**
- **A mount point is an ordinary directory when nothing is mounted on it.** A cold root that is really
  a directory on the disk being drained would be written to happily — films copied onto the disk they
  were being moved off, originals deleted, nothing throwing. `Media__ColdRoot` is used only when it
  holds a `.moviebot-cold` marker, which is made once by hand on the volume itself and is therefore
  present only when the volume is.
- **The cold disk is an upgrade, not a requirement.** With none configured, or with its volume gone,
  both services start and everything on the media root is served exactly as it is with one disk. The
  fault is reported — an error in the log, and on `/health` — rather than absorbed, and what has
  settled is missing from the library until the volume is back, which is the honest answer. Nothing is
  removed while it is gone, by either clock: a film on a volume a pass cannot see looks exactly like
  one already deleted, and acting on what it cannot see is how a library loses a film that was only
  unplugged. A film is on the library's clock only while it is under the cold root alone.
- **Nothing seek-heavy goes on the cold disk.** It is a platter: sequential streaming is well within
  budget and random IO is a cliff. Media files and nothing else: segments written in order by a
  transcode and read back by playheads, but no database, no journal and no download.
