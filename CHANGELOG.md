# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.46.0] - 2026-09-17

### Fixed

- The listening tone after "hey MovieBot" plays whole. The reading of the finished sentence found
  the trigger again and stopped the bot talking, and the tone its opening had started was what it
  stopped.
- Saying "hey MovieBot" while the recogniser is busy with somebody else still gets the tone, a
  little later, instead of none. The opening of the sentence is read again when the recogniser
  frees up.

### Changed

- The listening tone is a short notification sound that starts at once.

## [1.45.0] - 2026-09-17

### Changed

- Asking the assistant for a film is one tool, `watch_film`, in place of `load_title`,
  `search_library`, `search_catalogue` and `search_tracker`. It puts the film on when the library has
  it, and otherwise proposes downloading the best release the tracker offers, which plays in the room
  once somebody confirms. When the tracker has no release it gives the id a wish takes, and other
  films the words could mean are named. A room asking for Twilight and Step Up was told only that
  they were not in the library.
- A film named by its place in a series — "the second Pirates of the Caribbean", "step up 2",
  "part two" — is counted by the bot in the order the films came out, whether it is in the library or
  has to be downloaded.
- The assistant's tool for carrying on a paused film is `resume`. Named `play`, it was what the model
  called for "play" followed by a film's name, and the room went on with the film it already held.

## [1.44.2] - 2026-09-16

### Fixed

- `/voice leave` takes the bot out of the voice channel. It reported leaving while Discord kept the
  bot listed in the channel, because the connection was closed without telling Discord.
- A voice session that cannot decrypt anybody's speech rejoins by itself. The silence every client
  sends after speaking passes through undecrypted, and it kept such a session looking healthy for as
  long as people talked into it. Both through `TheKrystalShip.Discord.Voice` 1.4.1.
- The bot hears every speaker in a voice channel. Discord.Net's libdave binding passes user ids
  without a terminating NUL, so a speaker whose id is shorter than one formatted just before it was
  read by libdave as a different user, and every frame they sent failed to decrypt while the rest of
  the room was heard. The bot pins `Discord.Net.Dave` 3.20.2-tks.1, built with the terminator.

## [1.44.1] - 2026-09-16

### Fixed

- Asking the assistant for a film that is in the library puts it on, even after the room's
  conversation has taught the model to retype the film's id. In the live room the model asked for
  `pirates-of-the-caribbean_the_curse_of_the-black-pearl-2004` and similar for the 2003 film, and
  every one was refused, including when the film was named in full. The matcher now ignores
  punctuation, and a film named whole under a year nobody said is taken as that film. When somebody
  did say a number the year may be what picks the film, so it is offered by name instead.
- A film the assistant cannot pick is answered for the model rather than with `/watch`'s wording:
  the films it could have been, by name and earliest first, or that it is not in the library. The
  same list is how a `/watch` for an ambiguous name is now ordered.
- "Loading Pirates of the Caribbean..." on a turn that put nothing on is sent back to the model and
  corrected if it happens again (`RoomLoadingClaim`).

## [1.44.0] - 2026-09-16

### Changed

- A room's conversation with the assistant starts over once the room has been silent for
  `Assistant:IdleResetMinutes`, 15 by default. The model was replaying every turn since the room's
  first request, which in the live room was more than half of each request by the next morning and
  handed it last night's replies to copy. Every turn stays in `assistant.db`; the model is simply
  not shown what came before the silence. A room verb after the gap starts the conversation over
  too, so the pause it records is still there for "why did it stop?".

## [1.43.1] - 2026-09-16

### Fixed

- A download asked for in a room whose conversation had once been answered "I need the IMDb ID" is
  searched for instead of answered the same way again. The model copies its own earlier replies,
  so the request routed in a fresh conversation and failed in the room's real one. The instructions
  tell it to find things out with the tools rather than ask, and a reply asking for an IMDb or
  torrent id is sent back to the model to call them (`RoomIdRequest`).

## [1.43.0] - 2026-09-16

### Fixed

- A transcode no longer runs the host out of memory. The main pass read the source through one
  demuxer feeding the video and every audio track, which runs at the pace of the fastest consumer
  and queues the rest without bound: 9 MB a second on a 1080p rip with four audio tracks, so
  hotbox killed the transcode a third of the way into the film and the hand-off started it again
  from the beginning, every time. Each rendition now opens the source itself, which holds flat at a
  few hundred megabytes and runs faster. `ReadAheadGuard` takes the furthest of the open
  descriptors as the read position.
- A pause after "hey moviebot," no longer hands the next thing said to the bot. At a 500 ms silence
  gap that pause ended the sentence, leaving the trigger alone and the door open for whatever came
  after it. The gap is 800 ms.
- Remarks made while the bot waits for a yes or no to a proposal are no longer taken as answers and
  asked about again. Anything that is neither spends the window silently; the buttons stay.
- Requests longer than four seconds are no longer mangled. moviebot-speech encodes eight seconds of
  audio (220 ms an utterance on the P2000, against 114 ms for four), and whisper given a clip
  longer than its window repeated phrases instead of stopping.
- A trigger heard in the opening of a sentence and misheard in the whole of it is answered, through
  `TheKrystalShip.Discord.Voice` 1.4.0. The tone had played and nothing answered.
- "Download the second Pirates of the Caribbean movie" downloads Dead Man's Chest. `search_catalogue`
  lists films in the order they came out, where the index orders them by popularity and the model
  took the second row, Dead Men Tell No Tales.

### Changed

- Transcripts of everything heard are logged on hotbox while the triggers are tuned
  (`Voice__LogTranscripts` in the unit).

## [1.42.1] - 2026-09-15

### Fixed

- Sitting silent in a voice channel no longer sets the bot listening. The recogniser was primed with
  the trigger phrases, and whisper answers a breath, a keyboard or line hiss with the sentence it was
  primed with, so noise came back as "Hey moviebot.": a tone, ten seconds of listening without the
  trigger, and the next noise put to the assistant as a request, often opening the door again. It is
  primed with the name alone now, which invents no trigger from noise and still hears a spoken one.

## [1.42.0] - 2026-09-15

### Added

- The assistant's replies are held to what their turn did. A reply that says it loaded, paused or
  downloaded something on a turn that did none of it is sent back to the model once, and posted with
  a correction if it says so again; the same goes for a figure of four digits or more that nothing the
  turn was given contains. A reply that proposes something without mentioning it gets a line saying
  it waits for confirmation.

### Changed

- The assistant's prompt reading, tool catalog, proposal tokens and conversation compaction come from
  `TheKrystalShip.Agent`, shared with kgsm-llm's assistant, rather than from copies of its own. A
  catalog that disagrees with the tools is refused naming both directions in one message.

## [1.41.0] - 2026-09-15

### Added

- MovieBot answers. Anything said to it in a voice channel that is not one of the room verbs goes
  to the model on hotbox's card, with the room and the library in front of it, and the answer is
  posted in the voice channel's chat. "Go back a bit, I missed that", "jump to an hour in", "put on
  Collateral instead", "carry on", "what are we watching?" and "why did it stop?" all work, and so
  does "download Inception", which searches the catalogue and the tracker and proposes the release.
  The room verbs still go through the gate first, with no model involved, and act exactly as fast.

  Moving the room happens at once, as the person who asked. A download, a fetched subtitle, a wish
  and a keep are proposals, posted with two buttons; a spoken yes or no straight afterwards is the
  same answer, and whichever comes first acts. Each is carried out by the same code as `/watch`,
  `/notify` and `/keep`, so a download started this way reports its progress and starts in the
  room when it can be watched.

  A room verb is written into the room's conversation, so "why did it stop?" is answered with who
  paused it. The bot warms the model with its real instructions and catalog when it starts, and
  leaves that request for the model's unit to replay when it restarts.

  `Assistant__Enabled` switches it on, and the unit sets it.

### Changed

- The subtitle search and fetch shapes are part of the shared contract library, so the bot reads
  the same types the API writes. Nothing on the wire changed.

## [1.40.1] - 2026-09-15

### Changed

- A spoken sentence counts as finished after 500 ms of silence rather than 800. That silence is most
  of the wait between saying "pause" and the film stopping — at 800 ms it was 1.07 to 1.26 s end to
  end on hotbox — and the room verbs are short enough that the shorter pause does not cut them off.

### Fixed

- libdave no longer fills the bot's journal. It wrote every skipped silent frame straight to standard
  output, several a second, and on the first voice session that was 363 of 519 lines. Its messages go
  through the bot's logging now, where only its warnings and errors are shown by default.

## [1.40.0] - 2026-09-15

### Added

- MovieBot listens. `/voice join` brings the bot into the voice channel you are in, and "hey
  MovieBot, pause" stops the film for the room — as do "resume", "back fifteen" and "skip forward a
  minute". There is no model in the bot: what is heard goes to moviebot-speech for words, and the
  words go to a gate that knows a handful of verbs and reads the whole utterance. "Should we pause?"
  contains the word and is not the verb. A phrasing that looks like a verb and cannot be read safely
  — "go back" with no amount, "rewind a bit", "go back 1:30" — is not guessed at, because a misread
  verb moves the film for everybody.

  The act is silent, because the film stopping is the acknowledgement and the player already says
  who did it. It is recorded under the speaker's own account, it needs the room to be holding a
  film, and it is logged with the milliseconds from the moment the speaker stopped talking to the
  moment the room changed.

  Joining posts a notice in the channel saying the bot is listening, because that notice is the only
  way anyone but the person who ran the command learns they are heard. If it cannot be posted the
  bot leaves again.

### Changed

- `RoomChanged` is in the shared contract rather than the API, so the bot reads the type the API
  writes.
- The invite the bot logs asks for Connect and Speak as well. A bot already in a server keeps the
  role it was given.

### Fixed

- The test host journals its rooms somewhere of its own. It had been writing `rooms.json` beside the
  test binary, and the API restores every room in its journal on start, so one run's rooms turned up
  in the next.
- A room-control test listened for the play before the load's own push had arrived, and over long
  polling the load could land second and be read as the play.
- The repository names its package feeds. It had been restoring the speech packages through the
  user-level configuration, which also lists a local folder that could satisfy a package the real
  feed does not have.

## [1.39.0] - 2026-09-14

### Added

- A room can be driven from outside it. `play`, `pause`, `seek` and `seek-relative` are POSTs
  beside the title endpoint that was already there, so the bot — and, next, something somebody
  said out loud — can stop a film without holding a connection to the room.

  **A position is optional, and leaving it out means "wherever the room is".** A player knows
  where its own playhead sits and says so; a caller that is not watching does not, and a missing
  position read as zero would pause the film *and* send the room back to the opening titles. That
  failure looks like success, because the film stops either way. The position is resolved where
  the change is applied, under the same lock.

  **`seek-relative` is its own act because the film is moving.** Reading the position and then
  seeking to it minus fifteen spends a round trip in between, and in a playing room that round
  trip comes out of the fifteen. Before the start is the start; past the transcode head is clamped
  like any other seek, and the clamp comes back in the response body — over the hub it goes to the
  caller alone, and an HTTP caller is the one thing a broadcast cannot reach.

### Changed

- Every change to a room goes through `RoomControls`, whichever door it arrived by. Recording who
  did it and pushing the new state to everybody watching are the same act for a player on the hub
  and for the bot over HTTP, and the title endpoint was already carrying its own copy of half of
  it. One implementation cannot let a change reach one door and miss the other.

## [1.38.0] - 2026-09-14

### Added

- MovieBot hears. `moviebot-speech` holds one whisper model on hotbox's card and answers a unix
  socket, so what is said in a voice channel can become text without anything leaving the
  machine. It listens and never speaks: no synthesiser is registered, which keeps a gigabyte of
  models and the runtime behind them off this host entirely, and a surface that asks it to speak
  is told there is nothing to ask — an absence the protocol has always had a word for.

  It runs its own build of whisper rather than a prebuilt one, and both ways a prebuilt fails are
  invisible until they are expensive. The CUDA build carries no PTX for a Pascal card and would
  drop to the processor at a fortieth of the speed without a word; the Vulkan build loads on this
  machine, enumerates the card, and then core-dumps on the first transcription, because its CPU
  backend is compiled for AVX2 and this Athlon has none. The build in `deploy/vulkan` is made for
  this processor and links Vulkan as a hard dependency, so it either works or the service refuses
  to start.

  It is resident and warm before anyone speaks. The model loads at startup rather than on first
  use, and the daemon puts one request through itself, because a loaded model is not a ready one:
  the card compiles its compute pipelines the first time a graph actually runs. Whisper also
  encodes a thirty-second window whatever it is given, so the window is set to four seconds — the
  difference between 870 ms and 183 ms for the same clip. Measured through the socket on hotbox:
  285-309 ms for an utterance, and 429 ms for the first one after a restart.

## [1.37.0] - 2026-09-14

### Added

- The two pieces of inference a spoken command needs run on hotbox's card: whisper to turn what
  somebody said into text, gemma to turn that text into a room command. Both are built from
  source by `deploy/vulkan/build.sh` and installed to `/opt/moviebot` by `install.sh`, and
  `moviebot-llm.service` holds the model resident so nobody pays a load in front of a room.
  They are built rather than installed from a repository for three reasons that each cost a
  measurement to find. hotbox's Athlon has neither AVX2 nor BMI2 while the machine that builds
  for it has both, and ggml compiles for the builder by default, so the obvious build produces
  something that dies on the first request that reaches the wrong kernel; the build now pins the
  instruction set and refuses to ship an object containing an opcode that machine cannot run.
  The distribution builds ggml's backends as modules loaded from a path compiled into the
  library, where a backend that is not found is not an error but a silent fall back to the
  processor at a fortieth of the speed; linked as an ordinary dependency it either resolves at
  exec or the service does not start. And each build carries the ggml it was built against,
  rather than meeting a different one at runtime.

  The model is warm before the service is called active. The port opens before the card can
  compute — Vulkan builds its compute pipelines on the first request that needs them, and the
  prompt prefix is only cached once something has been prefilled through it — so a warm-up runs
  as `ExecStartPost` and systemd waits for it. It warms only the shape it sends: replaying its
  own request takes 241 ms while a first request carrying a different prompt and catalog takes
  1,093 ms, so the body it sends lives in `warmup.json` and is the catalog the assistant
  actually asks with.

## [1.36.0] - 2026-09-12

### Added

- A viewer sets how subtitles are drawn for themselves: text colour, the band behind it and how
  opaque it is, size, how far off the bottom of the frame they sit, an outline or a shadow, the
  family and the weight. It is a panel behind the same cog as the tracks, answered by a sample cue
  drawn by the function that draws the ones over the film, and it is one viewer's own — held once
  per browser rather than per title, because it is about the eyes reading it, and never sent.
  Untouched it is white on a black band at the size the frame gives it, which is what a cue is
  drawn as with nothing said about it.

### Changed

- Cues are drawn by the player library on every browser, `nativeTextTracks` off. A cue the browser
  draws takes the operating system's caption settings and nothing the page can say about it, so
  the one code path holds everywhere rather than everywhere except an iPhone.

### Fixed

- The player checks reach the menus through the cog that holds them, and read an unavailable
  track's reason off the mark that carries it.

## [1.35.3] - 2026-09-08

### Fixed

- One spinner stands in the middle of the frame. video.js draws its own whenever the element
  waits for data, in its own design, and the player's own answers three questions rather than
  that one — a film being loaded, a playhead being moved to where the room is, and a stall that
  has lasted long enough to be worth showing — so the library's is off and the player's is what
  a person sees.
- The control bar appears when the film does. Its styling says how the bar looks and no longer
  what its display is, so when it arrives is the library's answer again: on screen once the film
  has started, which is the moment a control can answer a press rather than sit over a frame
  holding nothing.

## [1.35.2] - 2026-09-08

### Fixed

- The player drives the media element video.js ends up with rather than the tag it was handed.
  Where a media element cannot be moved into the frame the library builds — iOS — the library
  clones the tag, disposes the original and plays the clone, and everything here reaches the
  element directly: the transcode is attached to it, the room's timeline is applied to it, and
  the events it raises are what tell the room somebody pressed play. Holding the tag pointed all
  of that at an element no longer in the document, leaving an iPhone on the film's poster with
  controls that answered, a play button that changed shape and a film that never arrived, while
  every other browser played it.

## [1.35.1] - 2026-09-07

### Fixed

- A settled copy of a film that is being made again is cleared by the pass that can see both
  disks, and not only by the ingest. The ingest clears it as the transcode starts, which does
  nothing while the cold volume is away, and a copy that survived that would be resolved ahead of
  the film being made for the whole of its transcode: a room that asked for the new release would
  watch the old one until it finished. Only a manifest saying the film is being made counts as
  evidence — a directory carrying none is not a reason to delete the one finished copy of a film.

## [1.35.0] - 2026-09-07

### Added

- Films are made on one disk and kept on another. `Media__ColdRoot` and `Handoff__ColdRoot` name
  a second volume; a finished title moves there and the library is the two roots together, with
  the cold one resolved first. A title is under exactly one root, which disk it is under changes
  no URL, and nothing about it appears on the wire.
- The move is a copy, ordered so that every failure before the last step leaves the film where it
  was: assembled under `.incoming` on the cold root, checked against what was read, committed with
  one `sync -f`, renamed into place, and only then deleted from the media root. Timestamps are
  carried across, so artwork that is served asking to be revalidated is not refetched wholesale.
- `Media__ColdRoot` is used only while a `.moviebot-cold` marker sits at its root, made by hand on
  the volume. A mount point is an ordinary directory when nothing is mounted on it, and a cold root
  that is really a directory on the disk being drained would take films off that disk and put them
  straight back on it.
- `/health` carries `coldStorage`, which names what is wrong with that volume and is absent when
  nothing is.

### Changed

- Both services start whether or not the cold volume is there, report the fault and serve
  everything on the media root. Nothing is pruned while it is missing: a film on a volume a pass
  cannot see looks exactly like one already deleted.

## [1.34.0] - 2026-09-06

### Changed

- The three services run on hotbox, which holds the media root, the state directories, the
  torrent client and the GPU that transcodes. They are built on hotrod, which holds the
  checkouts and the toolchain, and published across by rsync.
- `movies.thekrystalship.com` is served in two halves on two machines. There is one public
  address on this network and hotbox does not hold it, so hotrod publishes the name and routes
  it across the LAN over an https hop made under hotbox's own name: the certificate on the far
  side is chosen by that SNI while the server block is chosen by the Host header. The two nginx
  files are `deploy/nginx-moviebot-ingress.conf` and `deploy/nginx-moviebot.conf`.
- The hand-off reaches ffmpeg through its unit's own `PATH`. Where a card's driver branch is
  older than the NVENC API the distribution's ffmpeg is built against, that ffmpeg still lists
  `h264_nvenc` and cannot run it, so a matching build is named by the unit and kept off the
  global PATH.
- `Handoff__MaxConcurrentIngests` is set per host, against the aggregate rate the card actually
  saturates at rather than a figure carried over from another one.

## [1.33.0] - 2026-09-06

### Changed

- A film opens at the position the room is already at, rather than at its beginning. The player
  is handed that position as it loads the source, so the first segments it fetches are the ones
  being watched instead of the opening it would otherwise have to seek away from.
- The controls stay held, with the spinner up, until that first seek lands. A play issued in
  that window fires with whatever position the video element is holding, which is zero, and
  takes the whole room back to the start of the film.

## [1.32.0] - 2026-09-04

### Changed

- The API is a native binary, for the same reason the hand-off is: what it held after a day was
  the JIT's code and the type data built around it, not the rooms, and a process compiled ahead
  of time has neither. Its heap is collected on one thread rather than one per core, since it
  belongs to a handful of rooms and the film itself never touches it, and the globalization data
  is not loaded, since every comparison is ordinal.
- Everything the API puts on the wire is named in a serializer context, and the two contexts are
  the only resolver the hub and the endpoints use. The bodies that were anonymous objects are
  records now, with the same fields under the same names, and a type left out of the contexts
  fails a test rather than a request.

## [1.31.0] - 2026-09-04

### Changed

- The hand-off is a native binary. What a long-running .NET process holds is the code the JIT
  has written and the type data the runtime builds around it, and a process compiled ahead of
  time has neither: its code and its type system are data in the binary, paged in from the file.
  Every comparison in the pipeline is ordinal or invariant, so the globalization data is not
  loaded either. Nothing about what it does changes; it idles at a quarter of what it did.

## [1.30.1] - 2026-09-04

### Changed

- The three services hold less of the JIT. What each process holds is not its heap, which is a
  few megabytes in every one of them, but the code the JIT has compiled and the type data behind
  it, and tiered PGO keeps that growing for as long as a service runs: every method it finds hot is
  held as three copies with counters beside them. PGO is off in all three. The bot and the hand-off
  also compile each method once, at full optimisation, since a worker that runs for weeks has no
  startup to protect; the API keeps tiering, so a restart mid-film answers quickly.

## [1.30.0] - 2026-09-04

### Fixed

- An Activity invite lasts the film it was made for. Discord counts an invite's life from the
  moment it is made rather than from the last person through it, so a flat half hour shut the door
  on every film longer than one: everybody already watching carried on, and the card they came
  through stopped letting anybody else in, with nothing on either side to say so. The invite is
  now given the film's running time plus `Launch:InviteGraceSeconds`, which defaults to the half
  hour the API waits before forgetting an empty room, so the invite and the room behind it run out
  together. An age of zero reads as never expiring to Discord, so a floor stands under whatever is
  configured.

### Added

- A launch card says when it has stopped being a way in. The rooms are read from the API every
  half minute, and a card whose room has closed — or whose room has moved on to another film — is
  edited where it stands: it goes grey, the button and the link on its title go, and the
  description says what happened and that `/watch` in a voice channel starts the film again. The
  film, the poster and the fields stay, because scrolling back to what an evening watched is worth
  being able to do. A room that closed and a room watching something else are told apart, since
  one of them is still going.
- The invite behind an expired card is revoked in the same pass, so a link copied out of a card
  stops working when the card says it has.
- The cards still standing are written to `launches.json` in the state directory, configurable
  with `Launch:CardsPath`. Which message hands out which invite into which room exists in Discord
  and nowhere else — the API has never heard of a Discord message — so a bot that did not write it
  down would leave every card it had posted looking live for good. A pass that cannot reach the
  API does nothing at all: rooms it could not read are not rooms that closed.

## [1.29.0] - 2026-09-03

### Fixed

- `/watch` finds a film released abroad under another name. The tracker names a film the way the
  country that made it named it, so searching it for an English title matched no release at all:
  "The furious 2025" found 45 rows and offered none, while the same film by its id returns nine,
  every one named `Huo.zhe.yan`. The menu now identifies the film in the title index and asks the
  tracker for it by id. Pasting an IMDb link works for the same reason, being a film already
  named.
- The menu fills in as somebody types. The filter that keeps a search for "Heat" from answering
  with "Dead Heat" was applied to half a title, which is never equal to one, so every keystroke
  before the last offered nothing: "eurotri" offered none of six where "eurotrip" offered five.
- A row saying why nothing is on offer, where the menu came back empty and read exactly like a
  tracker that had not answered. Picking that row submits the text as typed.

### Changed

- A row shows the film's name beside the name the release carries when the two differ, so
  `Huo zhe yan` under a search for "The Furious" reads as the film that was asked for.

## [1.28.1] - 2026-09-03

### Fixed

- A channel Discord refuses is given up on, rather than asked again every ten seconds. A film
  announced into a channel the bot cannot see, and a progress message in one, met the same refusal
  on every pass for as long as the download was kept: thousands of them in an evening, each
  spending the channel's rate allowance to be told the same thing, and the announcement never
  arrived either way. A refusal over permission, or about something that is gone, now retires the
  tag that asked for it and says once which channel was refused and what the bot needs there.

## [1.28.0] - 2026-09-03

### Added

- A downloaded film is let go after a week of seeding, unless somebody keeps it. The hand-off
  prunes: the title's directory first, then the torrent and its files through the client. The
  week is measured on the torrent client's own seeding clock, which counts only while this
  machine is on and the torrent is active, and nothing is removed under the tracker's minimum plus
  a margin whatever the window is set to. A film a room holds waits until the room lets go, a film
  still owing its transcode is left to the log, and a title directory is deleted only when its
  manifest names the source this download arrived as, so an older release's turn to go cannot take
  a newer one's transcode with it. `Retention__SeedDays`, `Retention__TrackerMinimumHours` and
  `Retention__MarginHours` set the figures, and the hand-off reads `Api__ServiceKey` to ask the API
  which films the rooms hold.
- `/keep add` keeps a film on disk until somebody runs `/keep remove`, and `/keep list` shows every
  film on disk with who keeps it or how much more seeding it has before it leaves. The keep is a
  tag on the torrent and names who set it; anyone may keep a film and anyone may let it go.
- A launch and a ready announcement say how long the film stays, or who keeps it.

## [1.27.0] - 2026-09-02

### Added

- The up and down arrows step the volume by five points, and so does the wheel turned over the
  volume control. Each step flashes where the volume landed, with the step beside it, in the
  middle of the screen where a seek from the keyboard flashes what it did.
- What somebody else did to the room is said in the same place, to everyone in it: who paused,
  who resumed, who moved the film and to where, for long enough to read. The person who did it
  is not told, since they pressed the key. The line under the player uses the same words.

### Fixed

- The subtitle menu's search results followed the room from one film to the next. Changing the
  film with `/watch` left the OpenSubtitles list showing what it had found for the previous film;
  the list is forgotten with the film now, and a search that lands after the change is dropped.
- Subtitles a film carries are shown available once they are, for a viewer who opened the film
  while it was still transcoding. The film's own tracks are read again every time the subtitle
  panel is opened, so the menu is right whatever reached the page while it was closed.

## [1.26.0] - 2026-09-02

### Changed

- `/watch` fetches a film that is not here yet, so there is one command. Its menu lists the
  library while anything in it matches, and once nothing does the same menu shows the tracker's
  ranked results, with the release's quality, size and seeds beside each name. Picking one starts
  the download and, because the film is transcoded as it arrives, loads it into the voice channel
  the person was standing in the moment it can be watched, posting the launch as a new message
  that mentions them. The reply to the pick is the download's progress message, which names the
  room the film will play in. A pick from outside a voice channel downloads and announces, as
  before.
- The hand-off writes the id a film goes under in the library onto its torrent before the
  transcode starts, so the bot opens the film by the id it was actually given rather than by a
  second parse of the release name.
- The download watcher acts on a film becoming watchable wherever the download has got to. It
  waited for the download to finish first, which held the announcement of a playable film for the
  length of the download, and it now looks every ten seconds rather than every thirty, since
  somebody is waiting in a voice channel for it.
- A download's progress message says when the film is already watchable.
- `/notify` points at `/watch` where it pointed at `/find`.

### Removed

- `/find`. Everything it did is reached through `/watch`.

## [1.25.0] - 2026-09-02

### Added

- `/notify` tells a person when a film that cannot be downloaded yet can be. `/notify add` takes
  the film's name, autocompleted from the catalogue, or a pasted link to its IMDb page; `/notify
  list` shows what a person is waiting on and `/notify cancel` takes them off it. The bot asks the
  tracker about every film on the list once an hour and posts a new message, mentioning exactly
  the people who asked in that channel, naming the release it found and pointing at `/find`. A
  film counts as available only once a web encode or better is offered: a film in cinemas has
  camcorder recordings on the tracker within days, and those are not what anybody is waiting for.
- Asking is refused, with the reason, when the film is already in the library, already
  downloading, or already on the tracker, so nobody waits on something that is here.
- The wish list is the one thing the bot writes down, in the state directory systemd hands it.
  The unit now carries `StateDirectory=moviebot-bot`.

## [1.24.0] - 2026-09-02

### Added

- What a film gains while people are watching it reaches them without a reload. The API watches
  the manifests of the titles occupied rooms hold and pushes the fresh copy over the hub as
  `TitleChanged`; the player adopts the head, the preview sheet and the subtitle list from it.
  A subtitle fetched or confirmed by one person is announced the same way, so the whole room's
  menu changes at once. The player's five-second manifest poll is gone.

### Fixed

- Preview thumbnails appear for a film opened while it was still transcoding. The sheet is written
  after the main pass and the manifest names it only once the film is ready, and the player took
  nothing but the head from a fresh manifest, so everyone who opened a film early scrubbed a bar
  that previewed nothing for the whole film. The subtitles a source that was still downloading
  gains at the same point were lost the same way.

## [1.23.0] - 2026-09-02

### Changed

- `/watch` changes the film for a room that is already watching one, and the room keeps playing.
  The new film is loaded from its start and whether the room is playing is left as it was, so a
  room mid-film goes straight on into the next one rather than sitting paused until somebody
  presses play. The reply says it switched, and names the film it replaced.
- Asking for the film the room already holds no longer restarts it. The room is left exactly
  where it is and the reply hands over the way in.
- The hub's `LoadTitle` follows the same rule: from the start, playing if the room was playing.

## [1.22.0] - 2026-09-02

### Added

- Discord says what is being watched. The bot's status reads "Watching Dune (2021)" while one room
  is watching a film, counts them when several are, and shows nothing when none is. The line under
  each room's voice channel names the film and where it has got to, to the minute, or says it is
  paused, and is cleared when the room empties — including a line left behind by a bot that died
  mid-film, which it recognises as its own on the next start. Both are read from a new room listing
  on the API on every pass, so the bot remembers nothing. The bot needs Set Voice Channel Status
  and Manage Channels for the second, and the invite it logs carries them.
- Each viewer's own presence comes from the Activity: the film's name and poster beside their
  name, a bar running from where the room is to the end of the film while it plays, "Paused" when
  it is not, and how many others are in the room. This is the one place Discord gives real rich
  presence to, and it costs one more OAuth scope, so the Activity asks once more for consent the
  first time it is opened after this.
- `/api/config` carries the API's public address, from `Api__PublicBaseUrl`, which is where
  Discord's servers fetch a poster from.

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
