# Fetching films

The API is the one writer of downloads, wishes and keeps; the bot and the assistant are clients of
these routes and present what they answer (`docs/api-contract.md`, "Fetching films"). The tracker,
the title index, the torrent client, the disk budget and the tag vocabulary are moviebot-acquire's,
and this directory is where the API calls them.

- **Only the service key spends anything.** A room token proves somebody signed in through Discord;
  it does not let them fill the disk or spend the tracker's ratio. The service names the person on
  every act, and the tags and the log record that person.
- **A download names a release this API offered.** `OfferedReleases` remembers what a search or a
  picker answered, and a pick is resolved against it. An id nothing offered is refused before the
  torrent client is asked, so a client cannot fetch whatever torrent id it likes.
- **An outcome is a word and the facts, never a sentence.** A Discord channel mentions people and
  names slash commands, a web page does neither, so the words are each client's.
- **A service that did not answer is a 502, never an empty answer.** No downloads and a torrent client
  that could not be asked are different facts.

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
