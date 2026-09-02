# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.21.0] - 2026-09-02

### Fixed

- Films no longer wait a whole transcode each for their turn. The hand-off took them strictly in
  turn, so the second and third films anybody asked for were invisible until the ones ahead
  finished — not in `/watch`, not announced, not openable, however much had downloaded. Since a
  film is watchable seconds after its own transcode starts, that cost exactly what transcoding
  alongside the download was for. Up to three now run at once, which still leaves each of them
  running several times faster than anyone can watch.
- An interrupted transcode is picked up again. Becoming watchable cleared the tag that recorded
  the work as owed, so anything stopping the hand-off after that point — a crash, a deploy — left
  the film marked failed with nothing to retry it, while the log claimed it stayed owed. The two
  facts are separate tags now: what is owed survives until the transcode finishes, and what is
  watchable is its own mark.
- A poster is on disk before the manifest names one. The manifest advertised artwork from the
  moment it was written and the file only arrived when the download completed, so every film
  ingested while downloading served a 404 for its whole download — and a surface that fetches
  artwork on its own servers caches that.

## [1.20.0] - 2026-09-02

### Added

- Rooms survive a restart. What a room is watching and where it has got to lived in memory and
  nowhere else, so restarting the API took the film out from under everybody in it: the player
  carried on playing against a server that no longer knew what it was, the screen said nothing was
  playing, and seeking stopped working. Every room is now written to the state directory and read
  back before the server begins listening, keeping its epoch and its revision — so it is the same
  run of the same room, a connected client goes on applying pushes rather than discarding them,
  and nothing about the restart reaches a person watching. Position needs no special handling: it
  is a place at an instant rather than a number that ticks, so a room that was playing is still
  playing, at the right place, however long the restart took.
- `/health` reports how many rooms are occupied and by how many people, so a restart is something
  that can be looked at first rather than found out about afterwards. No names: the route is open.

### Fixed

- A client that finds its room holding nothing offers the film it is already showing. It offered
  the one named in the page's address, which an Activity never has — it is launched from an invite
  that names nothing — so the recovery was inert in the only place it was needed.

## [1.19.1] - 2026-09-02

### Fixed

- Scrub previews are the frames they claim to be. The sheet was built by asking the decoder to
  skip everything but keyframes, which is several times faster and silently wrong on some files:
  on the one HEVC source here it returned smeared frames belonging to no moment in the film, which
  reached the scrub bar looking like a corrupt encode of a film that plays perfectly. Every frame
  is decoded now, on the card where there is one — thirteen seconds per ten minutes of film
  against thirty-five, and a thirtieth of the processor time, which is time the transcode wants.
  A machine with no card, or one whose driver refuses the file, decodes it itself.
- Artwork is checked rather than trusted for a year. A poster and a preview sheet keep their names
  and are rewritten in place, so serving them as immutable left whoever had already looked at a
  broken one holding it until the file name changed, which it never does. Segments, which really
  never change, are untouched.

### Added

- `moviebot-ingest <source> --thumbnails` rebuilds the previews for a title already in the
  library. Everything a preview is made from is in the source file, so correcting a sheet costs
  four minutes rather than re-encoding the film behind it.

## [1.19.0] - 2026-09-02

### Added

- Films are named by the catalogue rather than by the file they arrived in. The hand-off resolves
  the film before the transcode starts — by the IMDb id the tracker supplies, or by the name and
  year parsed out of the release — and keeps its name, year, top billing and poster in the
  manifest. Before the transcode rather than after it, because a film is watchable and announced
  within seconds of the first segments landing, and metadata added at the end arrives a quarter of
  an hour after every message that would have shown it.
- Every film has artwork. A source that shipped its own cover art keeps it; every other film gets
  the catalogue's poster, fetched at the width it will be seen at and written beside the film.
- `moviebot-handoff --backfill` gives films already in the library what a film ingested from now
  on gets on the way in. The four already here are done.

### Changed

- Every message and link about a film shows the film's name. The launch card leads with it and
  carries the billing, the poster and a link to the film's page; the announcement that a download
  is ready names the film rather than the release; picking a film by name matches either. The
  release name is still shown beside the name, because which encode arrived is worth knowing — it
  is just not what the film is called.
- The two places that still lead with the release name are the search results and a download in
  progress. Neither has been through the catalogue, and both are about a file rather than a film.
- The poster is an embed's small image rather than its large one. Posters are portrait, and a
  large one fills a message with artwork nobody asked to look at.

## [1.18.0] - 2026-09-02

### Fixed

- A film paused and resumed no longer jumps forward by however long it was paused, and seeking no
  longer stops working afterwards. Rooms live in memory, so a room the server builds again —
  restarted, or swept for being empty — counts revisions from zero; a client that kept comparing
  those against the run before discarded every push it was sent for as long as its page stayed
  open. It then drove the film from a state nothing could correct: drift correction extrapolated
  the old anchor across the whole pause and hard-seeked the playhead forward the moment playback
  resumed, and every seek it published was accepted by the server and thrown away on arrival, so
  the scrub bar and the arrow keys moved nothing. Each state now carries the run of the room it
  belongs to, and a client that sees a new one starts counting again.
- A room the server has built again is told which film it holds, so a restart leaves the player
  on the film rather than on an empty room it can no longer be moved off.
- Nothing is derived forward from a room that cannot be heard from. The anchor runs whether or
  not the film does, so correcting to it while disconnected is a guess about where a film that may
  have been stopped has got to.
- A derived position is clamped to the length of the film. A room left playing derives one that
  keeps growing, and seeking a media element past the end of what it holds never completes —
  which blocks every seek made after it.
- Opening a film still being transcoded no longer drags the room to the transcode head. The media
  element seeks for reasons of its own, and the start position a player picks for a playlist still
  being written is the end of it; a seek is published by the control that made it and never by the
  element reporting one.
- A reconnect resynchronises. What `Join` hands back answers where the room is rather than
  announcing that it moved, so it is applied whatever revision it carries, and coming back to a
  backgrounded tab asks the same question.

## [1.17.0] - 2026-09-02

### Added

- A zoom lane on the scrub bar. Resting a pointer on the bar for a moment opens a panel above it
  holding the three minutes of film around that point as a strip of frames, ruled every ten
  seconds and named every thirty, with the chapters and the playhead that fall inside those
  minutes drawn where they belong. It is a control of its own: clicking or dragging inside it
  seeks. Two hours across a bar puts several seconds under every pixel, which finds a scene and
  cannot find a moment inside one; the lane is worth about thirty times that, so a second is a
  distance rather than a rounding error — and the frames are what make it a magnifier rather than
  a ruler, since only a picture says whether the second under the pointer is the wanted one.
- It draws as many frames as the sheet actually holds for those minutes, so none is repeated and
  none thrown away, and the strip's own shape sets how tall the lane is.
- The bar below never changes what a pixel is worth while the lane is open, so a long move is
  never trapped behind a precise one. A caret in the lane says which part of the window the coarse
  bar is pointing at, the window moves when the pointer leaves it, and pointing away from both
  closes it.

### Changed

- The control bar is held up by a count of what is being read on it rather than a flag, so a menu
  closing no longer drops the bar out from under the zoom lane, or the other way round.

## [1.16.0] - 2026-09-02

### Added

- A seek from the keyboard says so. An arrow key puts a round badge in the middle of the film
  reading how far the playhead moved, and takes it away again inside half a second. Five seconds
  inside a scene routinely passes without a visible cut, so before this a key that worked and a
  key that never registered looked the same. Presses inside one window are one gesture and are
  summed, so a held key reads as a single growing number.

### Changed

- Glass is the player's one surface. The control bar, every menu, the scrub preview, notices, the
  connection pill, the name card and the badge above are all the same blurred, translucent panel
  from the same handful of tokens, so the film stays visible under whatever is being read and a
  new panel is the right thing by default rather than by being written out again.

## [1.15.1] - 2026-09-02

### Changed

- The settings menu is the same glass as the control bar it rises out of, rather than a panel in
  front of the film.
- Its rows have room. Everything in them is a size somebody can read from across a room rather
  than from arm's length, and the space between them is the difference between a list and a wall
  of text.

## [1.15.0] - 2026-09-02

### Changed

- Scrub previews are twice the size. It is the thing somebody is looking at when they seek, so it
  is sized to be looked at. How many frames a sheet holds is now a pixel budget rather than a
  count, since a browser holds four bytes for every pixel of it the whole time a film is open, and
  larger frames mean fewer of them: a two-hour film gets one every twenty-five seconds or so
  rather than every seventeen.
- No focus ring around the film itself. The frame takes focus because the library makes it
  focusable, not because it is a control, and a ring around the whole picture tells nobody
  anything. The controls inside it keep theirs, and only when a keyboard put it there.

## [1.14.3] - 2026-09-02

### Fixed

- Playback starts on the first click and stays started. A click was being answered twice: once by
  the library, which has toggled playback on a click all along, and once more by a timer behind it
  waiting to find out whether a double click was coming. The film played for the length of that
  wait and then stopped.
- Double clicking does nothing. It asked for fullscreen, and the player already is the screen.
- The scrub bar's preview shows the film rather than a grey box. A background image is fetched by
  the browser, which carries none of this Activity's token, so the sheet of frames was answering
  401. It is fetched with the token and drawn from what comes back, the same way subtitles are.

## [1.14.2] - 2026-09-02

### Fixed

- Pressing play no longer stops the film a moment later. A play interrupted by a seek is rejected
  and the element goes back to paused, and the pause it then reports was published to the room as
  though somebody had decided to stop it. Playback the room wants now waits for the playhead to
  arrive instead of racing it, and a pause raised by a seek in flight is machinery rather than
  anybody's decision.
- The controls sit on one line. Everything in the bar is centred by the box it is in, rather than
  by giving each glyph a line height equal to the bar — which lines up only while those two numbers
  agree and drops a button half a row when they stop.
- A setting's value sits at the far side of its row rather than against the name it belongs to.

### Added

- Every intent the room receives is logged with who sent it and which connection it came down. A
  room that pauses itself is either a client publishing a pause or a player stopping without saying
  so, and those have nothing in common but the symptom.

## [1.14.1] - 2026-09-02

### Fixed

- Starting a film no longer needs anybody to fight it. A play interrupted by one of this client's
  own corrective seeks was being read as the browser refusing to play without a gesture, so a
  button appeared asking for one — and pressing it started the same race again, which is what made
  playback take several attempts to begin.
- Nothing is corrected while the film cannot play forward. A playhead waiting for data drifts from
  the room by definition, and seeking it to catch up throws away the buffer it was waiting for,
  which is the same stall again and further behind.
- A seek is not issued while one is already running. Abandoning a seek mid-flight during the
  opening buffer restarts the load.
- "Nothing playing yet" appears only once the room is known to hold no film. Whether it holds one
  takes a round trip to answer, and the message was being shown while that was still in the air —
  telling everybody arriving to a film that there wasn't one.
- There is no play button before there is something to play. Waiting is shown as waiting, and the
  only button a person is asked to press is the one that appears when the browser genuinely wants
  a gesture.

## [1.14.0] - 2026-09-02

### Changed

- Audio and subtitles sit behind one settings cog rather than a control each. The lists inside it
  render lists and nothing else: opening, closing, the lock that holds the control bar open and the
  way back out belong to the menu hosting them.
- The control bar is glass over the film rather than a panel beneath it, with a hairline along its
  top edge.
- Everything is a quarter larger, and the scrub bar and volume slider are grabbed by targets far
  taller than the lines they draw. Aiming at a six-pixel bar is a test of precision; the target
  around it is nearer thirty.
- The mute control is an icon rather than an emoji.

### Fixed

- The scrub bar's preview bubble disappears when the pointer leaves it. It never did: the hidden
  attribute works through a user agent display rule, and the one that laid the bubble out beat it,
  so the bubble stayed until the whole control bar faded.

## [1.13.0] - 2026-09-02

### Added

- The scrub bar previews the frame under the pointer, from a sheet written at ingest. One image
  holds every frame, so a preview appears the instant a pointer lands rather than after a fetch.
- Chapter ticks on the bar, with the chapter's name in the preview. Nothing is generated or
  guessed: a disc already records these, and a release that carries none simply has none.
- The keys a player is expected to answer — space and k to play, the arrows and j and l to seek,
  f, m and c for fullscreen, mute and subtitles. Every one of them moves the whole room, so
  nothing is bound that a hand resting on a keyboard could trigger.
- A click on the film plays or pauses it and a double click goes fullscreen, where the browser
  grants fullscreen at all.
- A spinner once playback has been waiting long enough that it is not a stutter. Playback can
  outrun a transcode here, and a stall otherwise looks like nothing happening.
- The total time can be clicked to show what is left instead.

## [1.12.0] - 2026-09-02

### Added

- The scrub bar shows the time under the pointer before a seek is made. It matters more here than
  in a player watched alone: a seek moves the whole room, so reading the moment first is the
  difference between choosing one and discovering one. Past the transcode head it says the film is
  not ready there, which is otherwise only learned by trying and being refused.

## [1.11.1] - 2026-09-02

### Changed

- The subtitle picker fits without scrolling. Six candidates are offered rather than twelve, since
  the list is ranked and a longer one only adds worse answers below the good ones, and the rows are
  tighter.
- Rows carry a mark rather than a sentence. The words are on the mark, where a pointer and a screen
  reader both reach them, and the whole state of a row is on the row itself. The confirm control is
  the confirmation as well as the action, so nothing says it twice.

## [1.11.0] - 2026-09-02

### Added

- Only the subtitle languages a room reads are extracted from a download. A disc carries thirty
  and the three rows anybody wants cannot be found among forty-eight, so the hand-off keeps English
  and the ingest run by hand still keeps everything.
- What was left behind is named in one line rather than thirty rows, so a language missing from a
  film that plainly has it is answered instead of reading as a fault.

## [1.10.0] - 2026-09-02

### Added

- The subtitle picker. What the room already has is listed first and renders at once; what the
  index offers is fetched underneath it, and choosing one both adds it for everyone and switches
  the person who asked to it.
- A track can be confirmed by whoever watched with it. That is the only ground truth there is —
  frame rate, release name and hash are all proxies for whether a subtitle looks right on screen,
  and a person watching is that question answered directly. A confirmation outranks every
  measurement, including one of our own that disagrees with it.
- A confirmation records how much of the film had been watched. Drift only shows up late, so a
  track confirmed near the end has been cleared of it where one confirmed two minutes in has not,
  and the menu says which.
- A film with a confirmed track does not search the index when its menu is opened. There is
  nothing worth interrupting anyone for once somebody has settled it, so the search waits to be
  asked for.

## [1.9.0] - 2026-09-02

### Added

- Subtitles can be fetched from an outside index when the ones a film shipped with do not fit it.
  Searching is free and only fetching spends the day's allowance, so every candidate is judged
  first and what was compared is shown rather than summarised: whether it was indexed against this
  exact file, whether its frame rate matches, and whether it came off the same kind of source.
- A fetched subtitle is measured against a track that came out of the film itself, and the offset
  that comes back is applied before it is written. What lands on disk already fits.
- A title records which film it is. Kept apart from the fingerprint of the file it was made from,
  because an id for the film stays true across every copy of it where a hash describes only one.

### Changed

- One person fetching a subtitle adds it for the whole room; which track each viewer selects stays
  their own choice. They are stored outside the media root, so a re-ingest that replaces a title's
  directory cannot take with it the one thing in the pipeline that is not regenerable for free.

## [1.8.0] - 2026-09-02

### Added

- A title records what it was made from: the release name, the file size, its frame rate and the
  hash subtitles are uploaded against. It is what tells a viewer whether a subtitle found elsewhere
  was timed against this exact release or against a different one, and it outlives the source file
  itself.

## [1.7.0] - 2026-09-02

### Added

- The English subtitle track is checked once it has been repaired, and a track that is still wrong
  says so at ingest instead of forty minutes into a film. The check is that two non-ASCII
  characters do not stand next to each other in English, which catches mangling through encodings
  the repair does not reverse.

### Changed

- English is repaired with the benefit of the doubt on the one case that cannot be decided by
  decoding: a closing quote whose last byte the encoder discarded is restored even with nothing in
  the document to prove that is what it was. Every other language keeps the strict reading, where
  a run that cannot be proven is left as it is.

### Fixed

- Text mangled through Latin-1 is repaired alongside text mangled through Windows-1252. The two
  encodings differ only in the block the corruption passes through, and the reversal now carries
  that block back to its bytes rather than refusing it.

## [1.6.0] - 2026-09-02

### Added

- Subtitle tracks are repaired on extraction when the release shipped them written as UTF-8, read
  back as Windows-1252 and written again, which turns a right single quote into three characters.
  Nothing downstream can spot it, because the file is valid UTF-8 and valid WebVTT and every byte
  survives the transcode. The repair works one run of non-ASCII characters at a time and keeps only
  what decodes strictly, so the same characters occurring legitimately in Portuguese, Romanian or
  Vietnamese are left exactly as they are.

## [1.5.0] - 2026-09-02

### Added

- A download's message shows where it has got to: a bar, a percentage, a speed and an estimate,
  and the number of seeds it is connected to. A slow download is visible as slow rather than
  indistinguishable from a broken one, and none connected says why.

### Changed

- Every message about a download is built from one definition, used by the command that starts
  one, the updater that keeps it current and the announcement when it is ready.

## [1.4.0] - 2026-09-02

### Added

- The person who asked for a film is mentioned when it arrives, so nobody has to watch a channel
  waiting for it. Only that one account can be notified by the message: it is named by id rather
  than by allowing mentions generally, so nothing composed from what somebody typed can ping
  anyone.

## [1.3.0] - 2026-09-02

### Added

- A film starts transcoding while it is still downloading, so it is watchable within seconds of
  being asked for rather than after the download. Measured on a slow torrent: the transcode began
  at 7 percent downloaded.
- The transcoder is held behind the part of the download that has arrived, and stopped when it
  catches up. On that same torrent it was held back seventeen times; without it the film would
  have been encoded partly from unwritten file, with nothing to show anything was wrong.

### Changed

- Subtitles are extracted after the main pass when the source is still arriving, since they have
  to be demuxed from a whole file to be complete. A track is advertised only once its file is
  whole, which is the same rule as before reached from the other side.
- A film is named for what it is rather than for how it was encoded. A container's title tag is
  routinely the release name again, so the name comes from the same parse that builds the id.

## [1.2.0] - 2026-09-02

### Added

- A finished download is turned into something the player can open, without anybody running the
  ingest by hand. It runs as its own service: it writes where the bot may not, it takes minutes
  where a bot restart would abandon it, and it holds the GPU.
- A film is announced the moment it becomes playable rather than when its transcode ends, which
  on a feature is a quarter of an hour earlier.
- A download that arrives but cannot be prepared says so, rather than being indistinguishable
  from one still being worked on.

## [1.1.0] - 2026-09-02

### Added

- `/find` searches a tracker for a film that is not in the library yet and starts it downloading,
  with the results offered as you type. It is answered by the repository beside this one, which is
  referenced as a library.
- A film is announced in the channel it was asked for once it has finished downloading. What to
  announce and where is kept on the torrent rather than in the bot, so a restart in the middle of
  a long download still announces it.

### Changed

- Both slash commands autocomplete a title and mean opposite things by it, so the autocomplete
  handler routes on the command name. `/watch` searches films already on disk; `/find` searches
  for ones that by definition are not.
- The bot unit reads `/home` rather than having it masked. The disk budget is measured off the
  filesystem, and a masked directory reads as zero used — a ceiling that never stops anything.

## [1.0.0] - 2026-09-01

### Security

- Discord is the only way in. The library, the films, the rooms and the hub were reachable by
  anyone who knew the hostname: `/api/titles` listed the collection, `/media` served the segments,
  and a stranger could read any room and change what it was playing. All of it now requires a
  token, and a token is issued only after Discord has confirmed who somebody is.
- The API refuses to start without `Auth:SigningKey`, because a missing key would otherwise mean
  a server that quietly lets everybody in.
- The bot proves itself with `Auth:ServiceKey`, having no Discord user of its own to be.
- A plain browser says where to go rather than failing one request at a time against a black
  screen. Cover art stays open: Discord's servers fetch it to render an embed and carry no token,
  and a poster is not the film.

## [0.9.0] - 2026-09-01

### Fixed

- The volume slider is proportional to loudness. `HTMLMediaElement.volume` is a linear amplitude
  multiplier and hearing is not linear in amplitude, so half the slider was about -6 dB and
  sounded closer to two thirds as loud. The slider now holds the loudness asked for and the media
  element is given its 5/3 power, which puts half the slider at -10 dB — half as loud. It replaces
  the player library's volume panel, which has no way to hold a position different from the
  amplitude it sets.

### Added

- Rooms are forgotten after 30 minutes with nobody in them, so a link handed out for one evening
  stops being a way back into it. The window starts when the last person leaves, so a film
  playing to a full room is never at risk however long it runs, and an old link opens a room with
  nothing in it rather than resuming what was playing. Configured by `Rooms:IdleTimeout`.
- The Discord invite an Activity launch produces expires on the same schedule. It gates joining
  only, so people already watching are unaffected when it lapses.

## [0.8.0] - 2026-09-01

### Fixed

- The film chosen with the slash command reaches the Activity. The bot named the room after the
  voice channel and the Activity named it after the SDK instance, so the two were never in the
  same room and the choice arrived nowhere. Both now key on the channel, and the bot loads the
  title through `POST /api/sessions/{id}/title` — an Activity opens at a URL the bot never wrote,
  so a query string cannot carry it the way a browser link does.

### Changed

- The page is the player. The library, the participant list, the panel and its toggle are gone,
  and the frame takes the whole viewport, which is what maximising was for. A room with no film
  says how to fill one instead of showing a library to pick from.
- The browser checks reach a film the way the product does — named at launch — rather than by
  clicking a library that no longer exists.

### Removed

- The forty-second test clip is out of the media library, so it is never offered by autocomplete.
  It stays as a fixture for the browser checks, which run against their own media root.

## [0.7.0] - 2026-09-01

### Changed

- The page gives its room to the film. The top banner and the strip under the player are gone;
  the film's name and who last touched playback moved into the side panel, and the connection
  state became a marker on the frame that shows itself only when something is wrong.
- The side panel collapses, and stays collapsed — the choice is a viewer's own, kept like volume.

### Added

- A control that fills the frame with the film. Discord's iframe withholds the Fullscreen API, so
  a real fullscreen button cannot work inside an Activity and is offered only on a plain page;
  what can be reclaimed is the page around the player. Escape gives it back.

## [0.6.0] - 2026-09-01

### Added

- `POST /api/auth/discord/callback`: the Activity's sign-in exchange. The client secret redeems
  the code here because a secret shipped to a browser is not a secret; the player handles only
  the code and the token that comes back, which is its own.
- `GET /api/config`: serves the application id, which is public, so the player needs no
  build-time configuration and no rebuild when it changes.
- `discordEnvironment.ts`: the player as an Activity. Two things differ from the browser page and
  they are the two the environment seam exists to hold — the room is the SDK's instance and the
  viewer is who Discord says they are. The API, media and hub are the same relative paths, since
  a root mapping forwards them.
- `ActivityLaunchPresenter`: the bot opens the film in the voice channel. When it cannot — no
  application id, a channel it cannot see, a missing Create Instant Invite — it says which and
  falls back to the browser link rather than failing quietly.

### Changed

- Playback starts at half volume. A film mastered for a cinema is punishing at full on laptop
  speakers. It is a starting point, not a shared setting: anyone who has set their own keeps it.

## [0.5.0] - 2026-09-01

### Added

- The API serves the player from `wwwroot`, so one origin carries the page, the API, the media
  and the hub — a Discord Activity then needs one URL mapping rather than several.
- `scripts/build-player.sh` builds the player and installs it there.
- `deploy/`: systemd units for the API and the bot, and the nginx vhost. Secrets come from a
  mode-600 `EnvironmentFile`, because systemd does not read `/etc/environment` and that file is
  world-readable.
- A name gate: the plain link carries no identity, so the player asks once and remembers.

### Fixed

- A launch link naming a film the room is already showing no longer reloads it. Loading a title
  resets the position, so a friend clicking the link mid-film sent everyone back to the start.
- The invite URL the bot logs omitted `CREATE_INSTANT_INVITE`. Installing from it produced a bot
  that could not create Activity invites, failing later as though Discord were at fault.

## [0.4.0] - 2026-09-01

### Added

- `MovieBot.Bot`: a Discord.Net bot with a slash command that resolves a film, opens the room's
  session and replies with a launch link and a poster embed. It holds no state: the session is
  named by the voice channel, so two people asking in the same channel reach the same room with
  nothing remembering that the first one asked.
- `ILaunchPresenter`, the seam between resolving a film and handing someone into the player. The
  link presenter is what exists; an Activity presenter replaces it without the command changing.
- A guild allow-list, because a verified application cannot stop being addable.
- The poster is attached only when an address Discord's own servers can reach is configured. An
  unreachable image renders as a broken embed, which reads as a broken bot.

## [0.3.0] - 2026-09-01

### Added

- `web/activity`: the player, as a plain page. video.js and hls.js, audio and subtitle menus
  built from the manifest and split into feature and commentary, per-viewer preferences in
  `localStorage`, and a scrub bar drawn at the film's true duration with the transcoded region
  shaded — the player library only knows what the playlist advertises, which is wrong for a
  title still being written.
- Shared play, pause and seek over the hub, with revision gating, position derived from the
  anchor against a server-clock offset, echo suppression by comparison, and graduated drift
  correction that nudges the rate for a small gap and seeks only for a large one.
- A browser verification suite: it plays the film, reads the menus, drives two viewers through
  play, pause and seek from either side, knocks one out of step to watch it recover, and edits
  the fixture manifest to a transcoding state to exercise the seek clamp.

## [0.2.1] - 2026-09-01

### Added

- `MasterPlaylist`: ingest writes an HLS master playlist binding the audio renditions to the
  video, and the manifest points at it. Without one a player handed a bare video rendition plays
  the film silently with no audio track to switch to.

### Fixed

- `docs/api-contract.md` described echo suppression as a flag cleared when the expected media
  event arrives. A seek interrupted by a second seek raises no `seeked` at all, so such a flag
  stays set and swallows the next thing the viewer does. The rule is now to compare against the
  position that was applied.

## [0.2.0] - 2026-09-01

### Added

- `MovieBot.Api`: the library, media and session surface. Serves manifests and HLS with the
  content types hls.js requires, holds shared session state, and pushes it over SignalR.
- Server-authoritative sessions with monotonic revisions and a server-clock timestamp on every
  push, so reordered or duplicated pushes and skewed client clocks are all harmless.
- Head-aware seek clamping. A seek past what has been transcoded is granted short of the head
  and answered with `SeekClamped` to the caller alone.
- `no-store` on playlists for a title still transcoding; segments served immutable.
- `MovieBot.Tests`: two real SignalR clients against the app in-process, covering propagation,
  revision ordering, clamping, participant broadcast and position derivation.

## [0.1.0] - 2026-09-01

### Added

- `MovieBot.Core`: the manifest contract — title, duration, transcode status and head, video
  rendition, audio tracks and subtitle tracks — with a source-generated camelCase serializer and
  an atomic writer, so a reader polling during a transcode never sees a half-written document.
- `MovieBot.Ingest`: the ingest CLI. Probes a source with ffprobe, extracts every text subtitle
  track to WebVTT and the container's cover art, then transcodes to HLS with `h264_nvenc`,
  writing `EVENT` playlists so the film is playable within seconds of the transcode starting.
- NVDEC decoding via `-hwaccel cuda`. Software-decoding a 16 Mbps HEVC Main 10 source costs
  35 s of CPU per 40 s of film against 5.5 s on the GPU, and saturates every core for the
  length of a feature while the GPU sits half idle.
- HDR detection from the source transfer function, with `tonemap_opencl` converting PQ and HLG
  sources to BT.709 and SDR sources left untouched. `--tonemap` overrides the detection.
- Subtitle classification into text and bitmap codecs. Bitmap tracks are listed as unavailable
  with a reason rather than dropped.
- Track labelling from the container's title tags, with commentary split out by ffmpeg's
  `comment` disposition.
- `--dry-run`, which prints the manifest a source would produce without transcoding it.

[1.0.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v1.0.0
[0.9.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.9.0
[0.8.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.8.0
[0.7.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.7.0
[0.6.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.6.0
[0.5.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.5.0
[0.4.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.4.0
[0.3.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.3.0
[0.2.1]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.2.1
[0.2.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.2.0
[0.1.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.1.0
