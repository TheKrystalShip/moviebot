# Fetching films

The API is the one writer of downloads and wishes; the bot and the assistant are clients of
these routes and present what they answer (`docs/api-contract.md`, "Fetching films"). The tracker,
the title index, the torrent client, the disk budget and the tag vocabulary are moviebot-acquire's,
and this directory is where the API calls them.

- **Spending anything takes an action, and the person is the token's.** Watching does not let anybody
  fill the disk or spend the tracker's ratio: a download takes `cinema:download` and a wish
  `cinema:wish`, whichever client carries the token — the site's, the Activity's or one the bot
  exchanged for the Discord user it acts for. No request names who asked; the tags, the wish and the
  log record the token's person, and the client acting for them where there is one.
- **A download names a release this API offered.** `OfferedReleases` remembers what a search or a
  picker answered, and a pick is resolved against it. An id nothing offered is refused before the
  torrent client is asked, so a client cannot fetch whatever torrent id it likes.
- **An outcome is a word and the facts, never a sentence.** A Discord channel mentions people and
  names slash commands, a web page does neither, so the words are each client's.
- **A service that did not answer is a 502, never an empty answer.** No downloads and a torrent client
  that could not be asked are different facts.
- **A release naming another language's dub comes after one that names none** (`ReleaseLanguages`).
  Its default audio is usually the dub, with the title's original language second or absent, so at
  each step of a pick (the quality asked for, then any) a clean release is taken first, a single
  episode before a foreign season pack, and the dub only when nothing else is there. Only tokens after
  the title and not marking subtitles count, and an unknown original language makes nothing a dub. A
  person asking for a dub still gets a release naming it first.

## Watch now (`WatchNow.cs`)

- **The answer comes before the release is picked.** Finding a release is the one slow step, so the
  room is told it is `finding` and the person goes to it; they watch the rest happen there rather than
  waiting on a button.
- **The torrent is the record of which room waits on it** (`watch:<room>`). `WatchNowLoader` reads the
  torrent client every three seconds and holds nothing, so a restart picks every waiting room back up;
  a room's `preparing` is dropped on restore for the same reason and read again on the next pass.
- **A site room is loaded by the API, a Discord room by the bot.** A site room has no channel to
  announce in, so the API does what the bot's download watcher does for `room:`: loads the film the
  moment the hand-off marks it watchable, as the person who pressed it, and starts it.
- **What is waited on names nobody.** A preparing state is the server reporting on a download; with
  an actor, every player would announce it as somebody moving the room.
- **The cap counts what is being watched while it arrives**: every download still arriving that a room
  waits on, or that an occupied room is playing. Each is a download and a live transcode at once.
- **Only a release from a digital source up is offered or picked**, the same floor that decides when a
  wish is out: a camcorder recording is not what Watch now means.
- **A room watching a show waits on one episode, not on its torrent.** A season is one torrent and
  another episode of it may be watchable long before the one pressed, so the tag names the episode
  and the room is loaded once that episode's own title can play.
- **An episode a room moves past is let go of when only that room asked for it.** Every pick of a
  show's episode says on the torrent why it is fetched: a room's `watch:<room>:sXXeYY`, or
  `keep:sXXeYY` for any other reason (a follow, a device, the next episode ahead, a download asked
  for outright), and a torrent fetched whole carries a bare `keep`. When the loader sees that a room
  now holds something else than the episode it was waiting on, it clears the room's tag and leaves
  `passed:sXXeYY`; the hand-off, which knows whether the episode's transcode has begun, decides
  whether the file is switched off (`src/MovieBot.Handoff/CLAUDE.md`). A room left holding nothing
  was not moved past anything, and leaves no `passed:`.
- **A room is told when what it waits on is late, and when it will not come** (`WatchNowLoader`).
  Late is measured from what arrives (`ArrivalWatch`): none of it for a minute is `stalled`, since
  the torrent client's own stalled state flips on a download that is fine. Late promises everybody in
  the room with an account news of it (`tell:sXXeYY:<account>`, `tell:film:<account>`), because the
  room tells them they will have it. A download that failed, left the client (no torrent names the
  room on two passes running), or no longer fetches the episode's file is `failed` with
  `notArriving`. A download found stopped on two passes running is started again: nothing here
  stops a download for longer than a pick takes, so it was stopped by hand, and a room is waiting.
- **A room's wait on an episode reads the episode before the torrent.** An episode that can play is
  loaded whatever else the torrent says; `ingest-failed` is about the torrent, and fails a room only
  while what it waits on cannot play.
- **One step is taken in a room at a time** (`Sessions/RoomEpisodes`). A step that arrives while
  another is under way is dropped: every press in a room moves the same room, and each press of an
  episode that is not here starts a download.
- **The next episode is fetched ahead, one and no further** (`Shows/NextEpisodes`), at the quality of
  the one being watched and out of the season's pack, so a season stays one torrent. More than one
  ahead is disk spent for a season's stay on episodes nobody may watch.
- **The tracker is asked about a show once per ten minutes** (`Shows/ShowOfferCache`): the show page,
  every episode on it, Watch now and the prefetcher all read the same answer, and the tracker caps
  how often a member may call it.
- **The tracker is asked about a film once per ten minutes** (`FilmOfferCache`): a film page's
  availability and the check a want makes read the same answer, which costs up to two seconds asked
  live. The wish sweep asks the tracker itself, since noticing a release as it appears is its purpose,
  and keeps what it heard there; Watch now asks afresh, since it picks the release it downloads.

## Prefetch (`Wants/WantFetcher.cs`)

What people want to watch is fetched ahead of them when it costs nobody anything. A pass runs every
`Wants:PassSeconds` and starts at most one download. `Wants:Prefetch` false leaves wants unfetched,
and a pass fetches only the titles of fixed plans.

- **Every condition is measured, and one that cannot be read starts nothing.** No occupied room is
  playing, no Watch now download is arriving, the prefetcher has fewer than `Wants:Concurrent` of its own
  arriving (counted from the `want:` tag on the torrents), the disk budget reads Ok, and the release
  fits the headroom the budget reports, which already keeps the transcode reserve. A download already
  running carries on when somebody starts watching; the transcode planner puts rooms first.
- **Only a want whose token held `cinema:download` is fetched for** (`StoredWant.MayFetch`). Wants that
  keep to themselves still count towards how many people want a title.
- **A fixed plan's title is a candidate in its own right** (`WantFetchRules.WithPlans`). A plan creates
  no want, so the maker's `cinema:download`, captured on the plan when it is made (`plans.may_fetch`),
  stands in for it: a title only a plan names is fetched for the maker, and a title also wanted carries
  the plan's time and gains the maker as a fetcher. A show plan fetches the episode it starts at, not the
  wanters' next. `WantCandidate.PlannedAt` is the earliest time a plan fixed for the title begins.
- **The order is a fixed plan first, the earliest time first, then the most wanters, then the oldest
  want** (`WantFetchRules.Order`).
- **A title is a candidate when the library does not hold it, the torrent client is not fetching it and
  the tracker offers a digital release.** A film is picked as Watch now picks it (source floor,
  `ReleaseLanguages`) at the best quality any wanter watches at, read from the width of the latest
  title each watched. A show fetches the earliest next episode among its wanters out of the season's
  pack, so a season stays one torrent, and is asked of the tracker through `ShowOfferCache`. A title the
  tracker has nothing for is not asked again for `Wants:AskMinutes`, and a plan reads it as `waiting`
  (`WantFetcher.AskedEmpty`).
- **The torrent carries `want:<imdbId>`, and the first wanter is its requester.** `ReadyNotices` reads
  the tag and tells every person whose want on the title can be fetched, so each is told once by the
  notification's key, and a person already in a room playing the title is not.

## The library's clock

How long a settled film stays in the library is this API's to say (`Library/LibraryClock`), and the
hand-off, the one process that writes the media roots, deletes what it names. The route is in
`docs/api-contract.md`; these are the rules it rests on.

- **The library is the server's and nobody else's.** Whether a film is on disk, how long it stays and
  when it goes are never shown to a person and never theirs to change: a film that has left is
  fetched again the way any film is, so there is nothing for anybody to watch over or protect.
- **A show's season stays and leaves as one** (`TitleLibrary.UnitOf`): a film is its own unit and an
  episode's is its season, because a season is one torrent and is fetched as one. Its clock runs from
  the latest play of any episode and the latest episode to settle, a room on one episode holds the
  season, and every settled episode in it falls due at the same moment.
- **The rule is written once.** `LibraryClock` answers a title's date and what is due from the same
  computation. The hand-off applies the answer and decides nothing of its own.
- **Only a settled film is on the clock**: its directory is on the cold root and not also on the media
  root. The count runs from the latest of the last play (`IPlayHistory`), the moment it settled, and
  the moment plays began to be recorded (`IPlayHistory.RecordingSince`): a film that settles long
  after it was last watched is not due the instant it lands, and a film with no recorded play is one
  nobody has played since recording began, which is all that can be known about it.
- **A title somebody wants and has not watched is never due** (`Wants/WantHolds`): a want younger than
  `Wants:HoldDays` from a person who has not finished the film, or every aired episode of the show, keeps
  the title, and an episode's season with it. An older want holds nothing. The title of a plan with a
  time fixed is held until that time has passed.
- **A film that has not settled has no date.** A date for a film that is not on the clock would be
  invented.
- **A film a room holds is never due**, whether or not anybody is in the room this second.
- **`/api/library/due` answers the service key alone.** It names what will be deleted, and only the
  hand-off has any business asking.

## Wishes

A wish is a film somebody wants that cannot be downloaded yet. It is kept for the wants on the film
(`Wants/`), and everything here is about not lying about when it can be had.

- **The film is named by the catalogue, never by a search of the tracker.** The tracker has nothing
  yet, which is the premise. What is kept is the IMDb id, so the sweep asks the tracker for one exact
  film rather than for a title that matches a hundred rows.
- **A row on the tracker is not availability.** A film in cinemas has camcorder recordings on the
  tracker within days, and the selection policy offers them, weighed down, because somebody who asks
  for one on purpose should be able to have it. A film is available once a release at or above the
  configured source floor is offered, and the floor is a web encode.
- **Nobody waits on something that is here.** Making a want for a film checks the library, the torrent
  client and the tracker, in that order, and each answers with what to do instead; only a film none of
  them has gets a wish, and only for a token that holds `cinema:wish`. A check that cannot be made is
  skipped rather than refused, because the sweep asks again within the hour and one message too many
  is the worst that skipping costs.
- **A wish is keyed by the film and shared.** Two people wanting the same film are one tracker call
  per sweep. The people on it are the wants on the film whose token held `cinema:wish`; the wish goes
  with the last of them.
- **The wish is kept because the catalogue's description of a film nothing has exists nowhere else.** A
  film that is not on the tracker has no torrent to tag and no manifest to write, so the description is
  a row in the database.
- **A wish that came out stays until its people let it go.** The sweep marks what became available
  and tells every person on it with an `available` notification (`Notifications/`), which is said once
  per person, so somebody wanting a film already out is told on the next pass. The want then lists
  with the release found for it. Which wishes are out is held in memory: the release found is the
  tracker's answer of the moment, and the first sweep after a restart asks again.
