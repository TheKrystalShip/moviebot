# Telling a person something

The routes and the kinds are in `docs/api-contract.md` ("Notifications"); these are the rules they
rest on. `Notifier` is the one way anything here tells anybody anything.

- **Every notification is kept, and the person's switches hold back only the push.**
  The bell is where a person finds what they missed, so what they chose not to be interrupted by still
  lands in it. An invite is addressed to one person on purpose and is pushed whatever they chose.
- **A thing is said once.** A notification carries a key naming what it is about, and a person holding
  one with the same key is not told again. That is what lets every watcher ask on every pass and
  survive a restart without remembering anything of its own. The key outlives nothing the bell does
  not keep: a person keeps their newest 50, none older than 30 days.
- **Taken by a push service is all that is known.** `Delivery` counts the devices a push service
  accepted the push for, and nothing anywhere says a phone showed it. A browser the service says is
  gone (404/410) is forgotten at once; any other refusal is logged and the row is kept.
- **The key pair is made once and never again.** Its public half is inside every subscription a browser
  makes, so a new pair silently orphans every registered device. It is kept in the database, which is
  readable by this service's user alone.
- **A subscription is a capability to put text on somebody's phone.** Only the person a token names
  registers, lists or removes their own browsers; there is no route that touches anybody else's. A
  browser belongs to whoever registered it last.
- **The text is one sentence that suits every surface.** It names the film and the person, mentions
  nobody and names no command, so the bell and the push show it unchanged; the link is the site's own
  path.
- **Who is owed news of a download is written on its torrent** (`TorrentTags.Tell` beside the
  requester's account). `ReadyNotices` tells them once it can be watched or could not be made
  watchable, and takes the tag off; a download that never carried them is news to
  nobody, which is also why the first start of this service announces nothing it finds already done.
- **A download fetched for people's wants tells all of them.** The prefetcher (`Acquisition/CLAUDE.md`,
  "Prefetch") tags the torrent `want:<imdbId>` and the first wanter as its requester; `ReadyNotices`
  tells that person and every other person whose want on the title can be fetched, each once by the
  `ready` notification's key.
- **A new episode of a followed show is told once, and not twice to whoever is fetching it.**
  `FollowWatcher` tells every follower the episode is out (`episode`, keyed by show and episode), once
  it is due and the tracker offers it. The follower a download is started as carries the torrent's
  tags like any requester, so `ReadyNotices` tells them when it can be watched and they are not also
  told the episode is out; every other follower, with or without `autoDownload`, gets the `episode`
  notice, and so does everyone when the download is refused. A follower who switched `episode` off
  still has it in the bell.
- **Titles to watch together are told as one digest a day.** `TogetherDigest` names, in one sentence, the
  ready titles a person wants that somebody else visibly wants too (the same matching as `GET
  /api/me/together`), keyed by person and day, and says nothing when it would repeat the last digest word
  for word. The `together` kind is always in the bell and pushed only to a person who switched it on,
  unlike every other switch, which starts on.
- **A plan is said to the people in it, whatever their switches say.** The `plan` kind is addressed to
  the maker and the people invited on purpose, as an invite is, and `Allows` is true for it. Each event
  (`invited`, `suggested`, `fixed`, `reminder`, `cancelled`) is keyed by the plan, the event and the
  time it names, so each is said once to each person however often `PlanService` and `PlanKeeper` ask;
  a pick is keyed afresh each time the maker makes one, so moving back to an earlier time is said too.
  The one who made, suggested, picked or cancelled is not told of their own act. The reminder opens the
  plan's room, and so does `invited` for a person brought in once the room is open; an `invited` sent
  by somebody other than the maker names them. Times in the text are the host's local zone.
- **Nothing is said about the library itself.** A film settling, moving between disks or leaving is
  the server's business; a person hears about what they asked for and what they follow.
