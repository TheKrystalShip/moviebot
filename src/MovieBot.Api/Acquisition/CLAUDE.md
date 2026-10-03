# Fetching films

The API is the one writer of downloads, wishes and keeps; the bot and the assistant are clients of
these routes and present what they answer (`docs/api-contract.md`, "Fetching films"). The tracker,
the title index, the torrent client, the disk budget and the tag vocabulary are moviebot-acquire's,
and this directory is where the API calls them.

- **Only the service key and a member spend anything.** An Activity's token proves somebody signed
  in through Discord; it does not let them fill the disk or spend the tracker's ratio. A member's
  token says Discord confirmed they are in the server, which is what a slash command takes. A service
  names the person on every act; a member is the person, and what their request says about who asked
  is replaced by the token. Either way the tags, the keep records and the log record that person.
- **A download names a release this API offered.** `OfferedReleases` remembers what a search or a
  picker answered, and a pick is resolved against it. An id nothing offered is refused before the
  torrent client is asked, so a client cannot fetch whatever torrent id it likes.
- **An outcome is a word and the facts, never a sentence.** A Discord channel mentions people and
  names slash commands, a web page does neither, so the words are each client's.
- **A service that did not answer is a 502, never an empty answer.** No downloads and a torrent client
  that could not be asked are different facts.

## Keeps and the library's clock

How long a settled film stays in the library is this API's to say (`Library/LibraryClock`), and the
hand-off, the one process that writes the media roots, deletes what it names. The routes are in
`docs/api-contract.md`; these are the rules they rest on.

- **A keep is held by library id in `keeps.json`, never on the torrent.** The torrent is removed once
  its seeding is done and the film is in the library for much longer, so a keep that lived on it
  would protect nothing. Anyone may keep and anyone may let go; the record names who.
- **The rule is written once.** `LibraryClock` answers a film's stay, the list of stays and what is
  due from the same computation, so the page that says "3 days left" and the sweep that deletes cannot
  disagree. The hand-off applies the answer and decides nothing of its own.
- **Only a settled film is on the clock**: its directory is on the cold root and not also on the media
  root. The count runs from the later of the last play (`IPlayHistory`) and the moment it settled, so
  a film that settles long after it was last watched is not due the instant it lands.
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
  per sweep and one announcement per channel.
- **The wish list is written down because a wish exists nowhere else.** A film that is not on the
  tracker has no torrent to tag and no manifest to write. It goes in the state directory systemd hands
  the service, whole and through a rename, on every change.
- **A wish is forgotten by being told.** The sweep marks what became available and the client that
  announces it reports the channels it reached; only the people who asked in those channels come off.
  Somebody who asked without a channel stays on the wish.
