# Keeping a film

A settled film nobody plays or keeps leaves the library after a while; the API decides which and the
hand-off deletes them (`src/MovieBot.Handoff/CLAUDE.md`, "The library's clock").

- **A keep is the API's, by library id, and it names who set it.** It lives with the library's clock
  and not on the film's torrent, which is let go of long before the film is. Anyone holding
  `cinema:keep` may keep a film and may let it go, and the record says who did. The bot acts with the
  token it exchanged for the person who ran the command, so the API records them.
- **A film already kept stays its first keeper's.** `/keep add` on a kept film says who has it
  rather than changing hands; `/keep remove` on one nobody keeps says so.
- **The launch and the ready announcement say how long a film stays in one sentence**,
  `KeepCommand.Notice`, so the message that starts a film and the one that announced it cannot
  disagree about when it leaves. The list is the short form of the same figures.
- **A film that has not settled is on no clock, and the sentence says nothing.** It counts from
  the moment it settles on the cold disk, and before then any number would be invented. The same is
  true of a title put in the library by hand on a host with no cold disk.
