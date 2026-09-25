# The two disks a film lives on (`MediaRoots`)

A film is made on one disk and kept on another. The transcode writes it in pieces for as long as it
runs, and the download it was read from seeds beside it for a week; between them that is what fills a
disk. Reading a finished film back is one playhead at a little over a megabyte a second, which any
disk serves. So the media root holds the work in progress and the cold root holds the library, and the
library's capacity is the cold disk's. The move between them is the hand-off's
(`src/MovieBot.Handoff/CLAUDE.md`).

- **A title is under exactly one root, and cold is resolved first.** The only moment it is under both
  is the one between the copy landing and the original being deleted, and both are whole for it, so
  every reader is already on the copy that is staying. A film made again under an id the library
  already holds has its settled copy cleared before the transcode starts, or the previous release
  would outlive the new one and be the one every room opened.
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
  pruned while it is gone: a film on a volume this pass cannot see looks exactly like one already
  deleted, and removing its torrent would leave the directory to come back with the disk with nothing
  pointing at it.
- **Nothing seek-heavy goes on the cold disk.** It is a platter: sequential streaming is well within
  budget and random IO is a cliff. Large finished media files, and nothing else — no database, no
  journal, no scratch space.
