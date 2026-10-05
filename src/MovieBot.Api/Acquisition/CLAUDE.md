# Fetching films

The API is the one writer of downloads, wishes and keeps; the bot and the assistant are clients of
these routes and present what they answer (`docs/api-contract.md`, "Fetching films"). The tracker,
the title index, the torrent client, the disk budget and the tag vocabulary are moviebot-acquire's,
and this directory is where the API calls them.

- **Spending anything takes an action, and the person is the token's.** Watching does not let anybody
  fill the disk or spend the tracker's ratio: a download takes `cinema:download`, a wish `cinema:wish`
  and a keep `cinema:keep`, whichever client carries the token — the site's, the Activity's or one the
  bot exchanged for the Discord user it acts for. No request names who asked; the tags, the wish, the
  keep records and the log record the token's person, and the client acting for them where there is
  one.
- **A download names a release this API offered.** `OfferedReleases` remembers what a search or a
  picker answered, and a pick is resolved against it. An id nothing offered is refused before the
  torrent client is asked, so a client cannot fetch whatever torrent id it likes.
- **An outcome is a word and the facts, never a sentence.** A Discord channel mentions people and
  names slash commands, a web page does neither, so the words are each client's.
- **A service that did not answer is a 502, never an empty answer.** No downloads and a torrent client
  that could not be asked are different facts.

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
- **The next episode is fetched ahead, one and no further** (`Shows/NextEpisodes`), at the quality of
  the one being watched and out of the season's pack, so a season stays one torrent. More than one
  ahead is disk spent for a season's stay on episodes nobody may watch.
- **The tracker is asked about a show once per ten minutes** (`Shows/ShowOfferCache`): the show page,
  every episode on it, Watch now and the prefetcher all read the same answer, and the tracker caps
  how often a member may call it.

## Keeps and the library's clock

How long a settled film stays in the library is this API's to say (`Library/LibraryClock`), and the
hand-off, the one process that writes the media roots, deletes what it names. The routes are in
`docs/api-contract.md`; these are the rules they rest on.

- **A keep is held by unit in `keeps.json`, never on the torrent.** The torrent is removed once
  its seeding is done and the film is in the library for much longer, so a keep that lived on it
  would protect nothing. Anyone may keep and anyone may let go; the record names who.
- **A show's season stays and leaves as one** (`TitleLibrary.UnitOf`): a film is its own unit and an
  episode's is its season, because a season is what a person keeps and what "Leaving soon" names.
  Its clock runs from the latest play of any episode and the latest episode to settle, a keep on it
  keeps every episode, a room on one episode holds the season, and every settled episode in it falls
  due at the same moment.
- **The rule is written once.** `LibraryClock` answers a film's stay, the list of stays and what is
  due from the same computation, so the page that says "3 days left" and the sweep that deletes cannot
  disagree. The hand-off applies the answer and decides nothing of its own.
- **Only a settled film is on the clock**: its directory is on the cold root and not also on the media
  root. The count runs from the latest of the last play (`IPlayHistory`), the moment it settled, and
  the moment plays began to be recorded (`IPlayHistory.RecordingSince`): a film that settles long
  after it was last watched is not due the instant it lands, and a film with no recorded play is one
  nobody has played since recording began, which is all that can be known about it.
- **A kept film has no `leavesAt`, and neither does one that has not settled.** A date for a film
  that cannot leave would be invented.
- **A film a room holds is never due**, whether or not anybody is in the room this second.
- **A keep file that cannot be read stops the store from starting.** Reading it as "nobody keeps
  anything" would let every film leave on the next sweep; with no answer, nothing is deleted.
- **`/api/library/due` answers the service key alone.** It names what will be deleted, and only the
  hand-off has any business asking.
- **A keeper is shown by the name `KeeperNames` gives, and by id where it gives none.**

## Wishes

A wish is a film somebody wants that cannot be downloaded yet. Everything here is about not lying
about when it can.

- **The film is named by the catalogue, never by a search of the tracker.** The tracker has nothing
  yet, which is the premise. What is kept is the IMDb id, so the sweep asks the tracker for one exact
  film rather than for a title that matches a hundred rows.
- **A row on the tracker is not availability.** A film in cinemas has camcorder recordings on the
  tracker within days, and the selection policy offers them, weighed down, because somebody who asks
  for one on purpose should be able to have it. A film is available once a release at or above the
  configured source floor is offered, and the floor is a web encode.
- **Nobody waits on something that is here.** The library, the torrent client and the tracker are
  checked before a wish is made, in that order, and each answers with what to do instead. A check that
  cannot be made is skipped rather than refused, because the sweep asks again within the hour and one
  message too many is the worst that skipping costs.
- **A wish is keyed by the film and shared.** Two people asking for the same film are one tracker call
  per sweep.
- **The wish list is written down because a wish exists nowhere else.** A film that is not on the
  tracker has no torrent to tag and no manifest to write. It goes in the state directory systemd hands
  the service, whole and through a rename, on every change.
- **A wish that came out stays until its people let it go.** The sweep marks what became available
  and tells every subscriber with an `available` notification (`Notifications/`), which is said once
  per person, so somebody joining a wish already out is told on the next pass. The wish then lists
  with the release found for it, which is what the Notify page shows. Which wishes are out is held in
  memory: the release found is the tracker's answer of the moment, and the first sweep after a
  restart asks again.
