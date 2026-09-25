# `/watch`: watching, fetching, and changing the film

There is one command for watching a film, and a film that is not here yet is fetched through it rather
than through a second one.

## Fetching a film through `/watch`

- **The library answers first, and alone.** The menu offers the tracker only once nothing in the
  library matches what was typed. A row offering to fetch a film beside the row that plays it is how a
  library ends up holding the same film twice.
- **A tracker row is told from a library row by its value alone.** The value is all a picked row sends
  back, and a library id is a slug that can be entirely digits, so a torrent id is carried under a
  prefix (`TrackerPick`) rather than bare.
- **A pick from a voice channel is a request to watch, and the download is the means.** The room is
  written on the torrent, and whichever pass of the watcher sees the film become watchable loads it
  into that room exactly as `/watch` loads a film that was already here, then posts the same launch,
  mentioning the person who asked. A pick from outside a voice channel is a download and an
  announcement, and nothing more.
- **The library id a film goes under is written by the hand-off, never derived by the bot.** The
  hand-off tags the torrent with it before the transcode starts, so the pass that sees the film become
  watchable is already holding the id it will answer to. Parsing the release name a second time would
  not disagree loudly; it would open nothing.
- **The reply to a pick is the waiting state, not the launch.** A film takes at least a minute to
  become watchable and an interaction token does not outlast a slow download, so the launch is a
  separate message posted when the film can be opened, which is also the only kind of message that
  reaches the person who walked away.
- **Watchable is waited for, not downloaded.** The transcode starts while the file is arriving and the
  mark that the film can be opened is set seconds into it. The watcher acts on that mark wherever the
  download has got to; waiting for the download to finish would sit on a playable film for the length
  of the download.
- **A room that changed its mind is left alone.** If the room already holds the film by the time it can
  be watched, it is left where it is; if it holds another, it is switched, because that is what the
  person asked for and what `/watch` does.

## Changing the film

`/watch` in a room that is already watching something is how the film is changed, for everyone.

- **The switch is one act.** The room is handed the new film from its start, and whether it is playing
  is left as it was: a room that was playing goes on playing the new film, a paused one stays paused.
  Loading a film paused and waiting for somebody to press play is a change followed by a wait, and the
  wait is what makes it feel like nothing happened.
- **Asking for the film the room already holds leaves it where it is.** Loading it again would send
  everyone back to the beginning, and the person asking almost always wants the way in. The reply says
  the room is already watching it and hands over the launch.
- **The reply says what it did to the room.** Started, switched from what to what, or already
  watching: three headlines from one place, so the two presenters cannot describe the same act
  differently. A switch names the film it replaced by its name, never its id.
