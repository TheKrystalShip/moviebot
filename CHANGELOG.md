# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
