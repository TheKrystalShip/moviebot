# Keeping a film

A downloaded film nobody keeps leaves after a week of seeding; the hand-off does the pruning
(`src/MovieBot.Handoff/CLAUDE.md`).

- **A keep is a tag on the torrent, and it names who set it.** Every process that decides a download's
  fate already reads its tags. Anyone may keep a film and anyone may let it go, and the row says who
  did, which beats deciding who is allowed to.
- **The launch and the ready announcement say how long a film stays in one sentence**,
  `KeepCommand.Notice`, so the message that starts a film and the one that announced it cannot
  disagree about when it leaves. The list is the short form of the same figures.
- A title put in the library by hand is on no clock, and the launch says nothing about how long it
  stays.
