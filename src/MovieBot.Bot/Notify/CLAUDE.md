# `/notify`: waiting on a film

`/notify` is for a film that cannot be downloaded yet. Everything here is about not lying about when
it can.

- **The film is named by the catalogue, never by a search of the tracker.** The tracker has nothing
  yet, which is the premise. The command autocompletes from the IMDb index and accepts a pasted link,
  and what is kept is the id, so the sweep asks the tracker for one exact film rather than for a title
  that matches a hundred rows.
- **A row on the tracker is not availability.** A film in cinemas has camcorder recordings on the
  tracker within days, and the selection policy offers them, weighed down, because somebody who asks
  for one on purpose should be able to have it. A film is available once a release at or above the
  configured source floor is offered, and the floor is a web encode.
- **Nobody waits on something that is here.** The library, the torrent client and the tracker are
  checked before a wish is made, in that order, and each answers with what to do instead. A check that
  cannot be made is skipped rather than refused, because the sweep asks again within the hour and one
  message too many is the worst that skipping costs.
- **A wish is keyed by the film and shared.** Two people asking for the same film are one tracker call
  per sweep and one message per channel, mentioning both.
- **The wish list is written down because a wish exists nowhere else.** A film that is not on the
  tracker has no torrent to tag and no manifest to write. It goes in the state directory systemd hands
  the service, whole and through a rename, on every change — the same as the launch cards, and for the
  same reason.
- **The announcement is a new message and names exactly who asked.** An edit notifies nobody. The
  mentions go in the message text, because a mention inside an embed renders and pings no one, and the
  allowed mentions are those ids alone.
- **A wish is forgotten by being told.** Whoever asked in a channel the message could not be sent to
  stays on the list for the next pass. A channel the bot can no longer see is treated as told, because
  retrying it would never work.
