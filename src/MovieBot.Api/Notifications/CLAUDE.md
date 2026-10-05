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
  makes, so a new pair silently orphans every registered device. `push-keys.json` is readable by this
  service's user alone, and a file that is there but unreadable stops pushes rather than being
  replaced.
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
- **A new episode of a followed show is told once, and not twice to whoever is fetching it.**
  `FollowWatcher` tells every follower the episode is out (`episode`, keyed by show and episode), once
  it is due and the tracker offers it. The follower a download is started as carries the torrent's
  tags like any requester, so `ReadyNotices` tells them when it can be watched and they are not also
  told the episode is out; every other follower, with or without `autoDownload`, gets the `episode`
  notice, and so does everyone when the download is refused. A follower who switched `episode` off
  still has it in the bell.
- **What leaves is the library clock's to say.** `LeavingNotices` reads `LibraryClock.Stays()`, so a
  title it says leaves is the title the sweep deletes, and tells everybody the watch record says
  watched any of the unit once per date it is due.
