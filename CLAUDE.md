# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

MovieBot plays films you already own to everyone in a Discord voice channel, on one shared
timeline, inside a Discord Activity. It stands apart from the KGSM ecosystem it sits beside in
this workspace, and follows its own conventions throughout.

**It builds against the checkout beside it.** `MovieBot.Api`, `MovieBot.Bot` and
`MovieBot.Handoff` each take a `ProjectReference` on
`../../../moviebot-acquire/src/MovieBot.Acquire`, so `moviebot-acquire` has to be checked out as
a sibling directory for anything here to compile. The reference is by path, which cuts both ways:
a change to a public type over there breaks the build here in the same pass, and a release of
that repo is nothing this one is pinned to. `moviebot.slnx` lists this repository's own projects,
and they compile against one it does not name.

`moviebot-acquire` is the acquiring half of one pipeline: torrents, the tracker, the title index,
the disk budget, retention and the tag vocabulary all live there and are called from here. This
repository owns what happens to a file once it exists — probing it, transcoding it, serving it,
and keeping a room in step.

Three docs beside this one, each the authority for its own half: `docs/api-contract.md` for what
`MovieBot.Api` serves on the wire, `web/activity/README.md` for the player, and
`src/MovieBot.Bot/README.md` for the Discord surface.

## The one idea everything follows from

**Everyone watches the same position in the same film.** That is not a feature, it is the
premise, and most of the design falls out of it:

- One transcode serves the whole room, because there is only ever one playhead. Plex and
  Jellyfin open a session per viewer; this does not.
- A single transcode head has to outrun a single playhead, and it outruns it tenfold — which is
  what makes playback-while-transcoding practical rather than fiddly.
- **Shared state is small**: title, paused, position at an anchor time, rate, who changed it,
  revision, transcode head. Volume, subtitle choice, how subtitles are drawn, audio track and
  quality are per-viewer preferences and never go on the wire. Shared volume is a way for one
  person to deafen everyone else, and one person needing subtitles, at the size they read at,
  should not put them on five other screens.

## Commands

```bash
dotnet build moviebot.slnx -c Release
dotnet test                        # hub integration tests, two real SignalR clients

# inspect a source without spending a GPU on it
dotnet run --project src/MovieBot.Ingest -c Release -- "<file>" --dry-run

# ingest for real
dotnet run --project src/MovieBot.Ingest -c Release -- "<file>" --out ./media
```

```bash
# the API, over an absolute media root
Media__Root=/absolute/path/to/media dotnet run --project src/MovieBot.Api -c Release
```

The player is its own build, in `web/activity`:

```bash
cd web/activity
npm run check      # types only
npm run build      # -> dist/
npm run verify     # a real browser against a running API and page
```

`media/` is generated and gitignored, and nothing under it is ever committed. It is a scratch
library for working here: the running API serves hotbox's `/home/heisen/moviebot/media`, which is
a different directory on a different machine, so an ingest run in this checkout changes nothing
anybody is watching. Ingesting into the library people open means running it on hotbox, against
that root.

## Deploying

Five units run on **hotbox**, under `heisen`, out of `/opt/moviebot`: `moviebot-api`,
`moviebot-bot` and `moviebot-handoff`, which are this repository's .NET services, and
`moviebot-speech` and `moviebot-llm`, which hold whisper and the language model on the card. All of
it is built on hotrod, which is the machine holding the checkouts, the .NET SDK, `clang` and node;
hotbox carries the .NET runtime and nothing else of the toolchain. So a deploy of the three services
is a publish here, an rsync there, and a restart:

```bash
./scripts/build-player.sh                                    # player -> the API's wwwroot
dotnet publish src/MovieBot.Api     -c Release -o /tmp/mb/api
dotnet publish src/MovieBot.Bot     -c Release -o /tmp/mb/bot
dotnet publish src/MovieBot.Handoff -c Release -o /tmp/mb/handoff

rsync -a --delete /tmp/mb/api/     hotbox:/opt/moviebot/api/
rsync -a --delete /tmp/mb/bot/     hotbox:/opt/moviebot/bot/
rsync -a --delete /tmp/mb/handoff/ hotbox:/opt/moviebot/handoff/
ssh hotbox 'sudo systemctl restart moviebot-api moviebot-bot moviebot-handoff'
```

- **Speech and the model are native builds of their own, made on hotrod for hotbox's CPU.**
  `deploy/vulkan/build.sh` builds whisper.cpp and llama.cpp against Vulkan with every CPU feature
  hotbox lacks switched off, and `deploy/vulkan/install.sh` puts them under `/opt/moviebot`; its
  README is the authority for why a prebuilt runtime cannot be used there. `moviebot-speech` is the
  .NET host over that whisper build and publishes like the other services.
- **Listening needs three system libraries on hotbox**: `opus`, `libsodium`, and `libdave`, which is
  Discord's end-to-end voice encryption and is packaged in tks-agent. Without libdave the bot runs
  and every voice connection is refused.
- **The API and the hand-off publish as native binaries.** `PublishAot` in each project, so the
  publish above runs the ahead-of-time compiler, needs `clang`, and takes a minute or two longer
  than a JIT publish. What lands is one executable beside `appsettings.json` and, for the API,
  `wwwroot`, with nothing left to compile at run time. `--delete` on the rsync is what keeps a
  native publish from leaving old assemblies standing beside the new binary. The bot stays on the
  JIT, with tiering off: Discord.Net is built on reflection.
- **A binary says which commit it came from, and which `moviebot-acquire`.** The SDK stamps every
  assembly's informational version with its repository's HEAD, so one native executable carries
  the stamps of both checkouts that went into it. Since this repository compiles against the
  acquire sibling by path and is pinned to no release of it, that is the only record of which
  acquire is inside a given build:

  ```bash
  strings -n 20 /opt/moviebot/api/moviebot-api | grep -oE '[0-9]+\.[0-9]+\.[0-9]+\+[0-9a-f]{40}' | sort -u
  ```

  Two binaries built from one tree are byte-identical; two built at different commits differ even
  when no compiled source changed between them, because the stamp moves.
- **The hand-off reaches ffmpeg through the unit's own `PATH`.** hotbox's card is Pascal and its
  driver branch is the last one supporting it, while the distribution's ffmpeg is built against a
  newer NVENC API and refuses to encode. It still *lists* `h264_nvenc`, so a probe that greps the
  encoder list gets a false pass and the failure appears only when a transcode dies. The build at
  `/opt/ffmpeg-p2000` matches the driver and is kept off the global PATH; the unit names it, and
  `ffprobe` is in the same directory so the probe and the transcode cannot end up on different
  builds.
- **The player reaches people through the API.** `scripts/build-player.sh` installs the built page
  into `src/MovieBot.Api/wwwroot`, and the API is what serves it, so a change to `web/activity`
  arrives only once the API is published after that script has run. One origin serves the page,
  the API, the media and the hub, because a Discord Activity maps one URL.
- **The units in `/etc/systemd/system/` are root-owned copies of `deploy/*.service`, and every
  `systemctl` verb against them needs root.** There is no polkit grant on hotbox, so starting,
  stopping and restarting are as privileged as installing a changed unit: `systemctl restart` as
  the owning user is refused with *"interactive authentication required"* and nothing happens.
  Publishing the binaries is unprivileged and the restart that picks them up is not, so a deploy
  ends by saying what changed and handing over the command rather than working around the
  privilege.
- **`/etc/moviebot/moviebot.env` holds the credentials** all three units read. Configuration that
  is not a credential belongs in the unit's own `Environment=` lines, where it is in the
  repository and reviewable.
- **The state directories are the two things no re-ingest can rebuild.** systemd hands the API
  `/var/lib/moviebot` — the rooms and the subtitles fetched from outside, each of which cost one
  of a limited daily allowance — and the bot `/var/lib/moviebot-bot`, holding the wish list and
  the launch cards still standing.
- **The cold disk is named by each unit and proved by a marker file.** `Media__ColdRoot` and
  `Handoff__ColdRoot` point at the volume finished films are kept on, and it is used only while
  a `.moviebot-cold` file sits at its root. That file is made once per host, as the owning user,
  on the mounted volume: `touch <cold root>/.moviebot-cold`. Without it both services run on the
  media root alone and say so.
- **nginx serves `movies.thekrystalship.com` in two halves, on two machines.** hotbox has no
  public address, so hotrod holds the name and routes it across the LAN by `server_name`, over an
  https hop made under hotbox's own name: the certificate on the far side is chosen by that SNI
  while the server block is chosen by the `Host` header, which travels through unchanged. hotbox's
  vhost terminates that hop and proxies to `127.0.0.1:8099` with `/hub/` upgraded and buffering
  off. `deploy/nginx-moviebot-ingress.conf` and `deploy/nginx-moviebot.conf` are the repository's
  copies, and they are deployed to different hosts.

  The consequence is worth stating plainly: **hotrod's nginx is in the path for every byte of
  every film.** The services, the GPU, the media and the state are all on hotbox, but the ingress
  is not, because there is one public address on this network and it is spoken for.
- **A 502 in the second after an nginx reload is usually the old worker.** A reload lets the
  workers already running drain rather than killing them, so both configurations answer for a
  moment. The error log names the upstream it tried, which is what tells a stale worker apart
  from a real failure.

## API invariants

- **The server is authoritative and there is no host.** Clients send intent; the store decides,
  stamps the server clock and assigns the next revision. A client never pushes state.
- **Every type on the wire is named in a serializer context, and the two contexts are the only
  resolver.** `ApiJsonContext` holds this service's own shapes and `ManifestJsonContext` the Core
  library's, and nothing falls back to reflection, in the JIT build the tests run as much as in
  the native one, so an anonymous object or an unregistered type fails a test rather than a
  request. A list is looked up by the type the endpoint declares and written by the type it
  holds, so both are registered, and a list bound for the wire is built as a `List<T>`: a
  collection expression yields a type of the compiler's own that nothing has registered.
- **Every change to a room goes through one place, whichever door it arrived by.** A player sends
  intent down the hub; the bot and a spoken command post it over HTTP. Recording who did it and
  pushing the new state to the room are the same in both cases and are written once, in
  `RoomControls` — a second spelling would not disagree loudly, it would just let a change reach one
  door and miss the other.
- **A position omitted means "wherever the room is", and is resolved where the change is applied.**
  A player knows where its own playhead sits; a caller that is not watching does not. A missing
  position read as zero pauses the film *and* sends the room back to the opening titles, which looks
  like it worked because the film stops. For the same reason "back fifteen" is its own act rather
  than a read followed by a seek: in a playing room the round trip between the two comes out of the
  fifteen.
- **Clamp seeks on the server, never in the client.** A client's head is always slightly stale,
  so client-side clamping yields a seek half the room accepts and half rejects.
- **`SeekClamped` reaches the caller alone.** Broadcasting it would show the whole room an error
  nobody else triggered. Down the hub that is a message to the caller; over HTTP it rides in the
  response body, because the caller that asked is the one thing a broadcast cannot reach.
- **Resolve configuration inside the DI factory, not at the top of `Program.cs`.** Reading
  `builder.Configuration` while the builder is still being assembled misses sources added later —
  a test host's media root, for one — and the service silently points somewhere else.
- **Playlists for a transcoding title are `no-store`.** A cached growing playlist makes the film
  appear to end early, which looks exactly like a broken transcode.
- **Media answers HEAD as well as GET.** Browsers probe a URL before fetching it, and a 405 there
  reads as the file being unavailable.
- **CORS reflects the origin rather than enumerating one.** The Activity is served from Discord's
  proxy under an origin that is not known ahead of time, and a failed preflight is invisible: no
  status, no log, just a request that never happens.

## Ingest invariants

These are measured against real Blu-ray rips, not assumed. Changing one means re-measuring.

- **Probe with `-probesize 200M -analyzeduration 200M`.** With the defaults, ffprobe cannot size
  PGS subtitle streams: it prints `Could not find codec parameters` once per stream and may
  return incomplete data.
- **Subtitles and the poster are extracted before the main pass, never alongside it.** A
  monolithic `.vtt` being appended to while a player fetches it once gives subtitles that stop
  partway through the film.
- **Subtitles are repaired against double-encoding as they are extracted.** Releases ship tracks
  that were written as UTF-8, read back as Windows-1252 and written again, so a right single quote
  arrives as three characters. Nothing downstream can detect it: the file is valid UTF-8, valid
  WebVTT, and every byte survives the transcode intact. The repair works one run of non-ASCII
  characters at a time and keeps only what decodes strictly, which is what lets it leave a
  Portuguese `NÃO` or a Romanian diacritic alone — legitimate text almost never forms valid UTF-8
  when re-encoded that way, and the same characters are exactly what the corruption produces.
- **English is the track that gets checked.** It is the one anybody here selects, so it is
  repaired with the benefit of the doubt on the single case decoding cannot decide -- a closing
  quote whose last byte the encoder discarded is restored even with nothing in the document to
  prove that is what it was -- and it is inspected afterwards. The inspection is adjacency: two
  non-ASCII characters do not stand next to each other in English, where quotes, dashes and the
  accented letters of names and loanwords all appear singly between ASCII, while every form of this
  corruption produces two or three in a row. That catches mangling through an encoding the repair
  does not reverse, which a list of known-bad sequences would not.
- **The other languages keep the strict reading.** Relaxing it would gain nothing, because the
  strict pass already repairs everything a looser one would, and it would cost the Portuguese and
  Romanian tracks it currently leaves untouched: the characters a looser pass would act on are the
  ones those languages are spelled with.
- **The source is fingerprinted once it is whole, never while it is arriving.** Its release name,
  size, frame rate and OpenSubtitles hash are what an externally found subtitle is judged against,
  and the hash covers the last 64 KiB of the file. Space for a download is reserved before the bytes
  land, so hashing an arriving source reads zeroes and returns a confident wrong answer that nothing
  downstream can question. It is computed where the subtitles are, after the file is complete.
- **Only the languages a room reads are extracted, and what is left behind is named.** A disc
  carries thirty subtitle languages and the three anybody wants cannot be found among forty-eight
  rows. They are not a resource problem — every track comes out of a single demux pass either way —
  so this is a menu decision. The languages skipped are recorded on the manifest, because one that
  is simply absent from a film that plainly has it reads as a fault.
- **Matching a language accepts every spelling of it.** A container writes Romanian as `rum`,
  `ron` or `ro` depending on who muxed it, and the same holds for twenty-odd others. Matching one
  spelling and not another keeps nothing, which produces a film with no subtitles and no error.
- **A track with no language tag is always kept.** Releases ship one untagged subtitle often
  enough and it is usually the one worth having; dropping it on a filter leaves a film with none
  and nothing anywhere to explain why.
- **Playlists are `EVENT`, not `VOD`.** That is the whole mechanism behind playback starting
  seconds in. ffmpeg appends `#EXT-X-ENDLIST` on completion, so the playlist becomes a normal
  VOD by itself.
- **`headSeconds` is the shortest of the written playlists, read from the `#EXTINF` sums** — not
  ffmpeg's own progress. A frame the encoder has read is not watchable until its segment is
  closed and listed, and video that exists without its audio is not playable at all.
- **The manifest is written through a temp file and an atomic move.** The API polls it during a
  transcode and must never observe a partial document.
- **Tone-mapping is auto-detected from the transfer function** and must stay that way.
  Tone-mapping an SDR source washes it out exactly as failing to tone-map an HDR one does.
- **`libplacebo` fails to initialise its filter graph on the ffmpeg the transcode runs against**,
  under every option combination tried. `tonemap_opencl` is the working path, and it is measured
  on the card that does the work rather than assumed. If a future build fixes libplacebo it is the
  better filter, but verify before switching.
- **Decode on NVDEC (`-hwaccel cuda`), always.** Software decode of a 16 Mbps HEVC Main 10
  source costs 35 s of CPU per 40 s of film against 5.5 s on the GPU, and pins every core for
  the length of a feature. Frames land in system memory, which is where `tonemap_opencl`
  wants them.
- **Every rendition reads the source through its own demuxer.** The main pass opens the file once
  for the video and once per audio track. One demuxer shared between them runs at the pace of the
  fastest consumer and queues packets for the slower ones in memory without bound — measured at
  9 MB a second on a 1080p rip with four audio tracks, which exhausts hotbox a third of the way
  into a feature and gets the transcode killed. `ReadAheadGuard` takes the furthest of the
  descriptors as the read position.
- **The concurrency ceiling belongs to the card, and is measured rather than reasoned about.**
  `Handoff__MaxConcurrentIngests` is set per host, because a GPU saturates at an aggregate rate
  and jobs past that point only divide the same throughput into thinner slices. What matters is
  not the aggregate but the per-film rate, which has to stay well ahead of one playhead.
- **The GOP is pinned to the segment length** and `-force_key_frames` guarantees a keyframe on
  every boundary whatever the frame rate, so a seek lands on the frame it asked for.

## Hand-off invariants

`MovieBot.Handoff` carries a finished download into the library. It runs as its own service, and
each of the three reasons is a constraint rather than a preference: it writes into the media root,
which the bot is not permitted to do; a transcode takes minutes, so a bot restart would abandon it
halfway; and it holds the GPU, which has no business inside a gateway connection.

- **A film is announced when it becomes playable, not when the transcode ends.** The playlists
  grow as segments land, so a film is watchable seconds in against roughly ten times realtime.
  Waiting for the whole transcode holds a room for a quarter of an hour in front of a film that
  was already playing.
- **Playable means the manifest carries a head**, or reports itself ready. Announcing at the end
  of the *download* instead is too early by the other margin: the transcode has not started, so
  the manifest does not exist and `/watch` finds nothing.
- **The feature is the largest video file above a floor**, with samples and trailers excluded by
  name. Without the floor, a torrent holding only a sample yields the sample and the room watches
  ninety seconds of a film.
- **The library id comes from the same parser the search uses.** A second name parser written
  here would not disagree loudly; it would give one film two ids and nobody would notice until
  the library held both.
- **The unit keeps the GPU devices visible.** `PrivateDevices` would hide them and drop the
  pipeline onto the CPU, where a feature costs hours instead of minutes.
- **A transcode starts before the download finishes**, once enough has arrived for the container's
  index to be readable. The head start only covers the opening; the reader is held behind the
  arrived bytes from then on, so too small a threshold costs a pause rather than a broken film.
- **A whole file is ingested exactly as it always was.** `IngestOptions.Availability` is supplied
  only while a source is still arriving, so the ordinary path keeps the behaviour measured
  against it and nothing about the CLI changes.

## The two disks a film lives on

A film is made on one disk and kept on another. The transcode writes it in four-second pieces for
as long as it runs, and the download it was read from seeds beside it for a week; between them
that is what fills a disk. Reading a finished film back is one playhead at a little over a
megabyte a second, which any disk serves. So the media root holds the work in progress and the
cold root holds the library, and the library's capacity is the cold disk's.

- **A title is under exactly one root, and cold is resolved first.** The only moment it is under
  both is the one between the copy landing and the original being deleted, and both are whole for
  it, so every reader is already on the copy that is staying. A film made again under an id the
  library already holds has its settled copy cleared before the transcode starts, or the previous
  release would outlive the new one and be the one every room opened.
- **Which disk a film is on changes no URL and appears nowhere on the wire.** `/media/{id}/…` is
  answered by resolving the id per request, which is also what makes it safe to move a film out
  from under a room that is watching it.
- **A film settles when its manifest says it is ready, and not before.** The subtitles, the cover
  art and the preview sheet are all written before the status turns, so a film that reports itself
  ready is a directory nothing is going to add to.
- **The move is a copy, and it is ordered so that every failure before the last step is harmless.**
  The two roots are different filesystems, so nothing about it is atomic on its own: the film is
  assembled under `.incoming` on the cold root, checked against what was read, committed to the
  disk with one `sync -f`, and renamed into place — a rename within one filesystem, which either
  happened or did not. The copy on the media root goes last. A stop anywhere before that leaves
  the film exactly where it was and the next pass clears what was half written.
- **One film at a time.** The copy saturates the disk it writes to, and a second alongside it
  would divide the same throughput while the transcode this is making room for reads the disk
  being drained.
- **Timestamps are carried across.** A poster and a sheet of scrub previews keep their names and
  are served asking to be revalidated, so a film stamped with the moment it moved would have every
  viewer fetch all of it again for nothing.
- **A mount point is an ordinary directory when nothing is mounted on it.** A cold root that is
  really a directory on the disk being drained would be written to happily — films copied onto the
  disk they were being moved off, originals deleted, nothing throwing. `Media__ColdRoot` is used
  only when it holds a `.moviebot-cold` marker, which is made once by hand on the volume itself
  and is therefore present only when the volume is.
- **The cold disk is an upgrade, not a requirement.** With none configured, or with its volume
  gone, both services start and everything on the media root is served exactly as it is with one
  disk. The fault is reported — an error in the log, and on `/health` — rather than absorbed, and
  what has settled is missing from the library until the volume is back, which is the honest
  answer. Nothing is pruned while it is gone: a film on a volume this pass cannot see looks
  exactly like one already deleted, and removing its torrent would leave the directory to come
  back with the disk with nothing pointing at it.
- **Nothing seek-heavy goes on the cold disk.** It is a platter: sequential streaming is well
  within budget and random IO is a cliff. Large finished media files, and nothing else — no
  database, no journal, no scratch space.

## Fetching a subtitle from outside

- **One person fetches, the whole room gets it.** A fetched subtitle is stored against the film,
  not the viewer, and appears in everyone's menu. Which track each viewer selects stays their own
  choice, as every subtitle choice always has been.
- **They live outside the media root.** Everything under that root is regenerable from the source
  file and these are not: each cost one of a limited daily allowance. A re-ingest replaces a
  title's directory wholesale, the transcode rewrites the manifest inside it every few seconds,
  and the API holds that root read-only. All three point the same way.
- **The manifest on disk never mentions them.** They are folded in as it is served, so a subtitle
  fetched while a manifest sits unchanged in the cache still appears without a restart.
- **The offset is applied before the file is written, not by the viewer.** Where the film carries
  a track that came out of it, a fetched subtitle is measured against that and shifted to match,
  so what lands on disk already fits.
- **The reference has to be a track that came out of the file.** It fits by construction; a
  previously fetched one might be wrong, and measuring against a wrong one propagates its error
  confidently.
- **A menu row names the release, not the language.** Three fetched subtitles otherwise give three
  rows all reading "English" and no way to choose between them, which is the same failure the
  track labelling rules avoid for embedded tracks.

## Confirming a subtitle fits

- **A person watching is the only ground truth.** Frame rate, release name and hash are proxies
  for whether a subtitle looks right on screen. Somebody confirming one answers that directly, so
  a confirmation outranks every measurement — including a later one of ours that disagrees, which
  is why a confirmed track is never re-fetched or re-shifted.
- **How far in it was confirmed is recorded, because drift only shows up late.** A track confirmed
  two minutes in has not been cleared of drift; one confirmed near the end has. Without the
  position the weaker claim reads as the stronger one.
- **Anyone may confirm and anyone may undo it, and the row says who did.** A wrong confirmation is
  fixed by one click from whoever notices, which beats deciding who is allowed to.
- **A confirmation that names a track the film no longer has is dropped, not honoured.** Ids come
  from stream indices and a re-ingest can hand the same number to a different track.
- **A confirmed track stops the menu searching the index.** There is nothing to interrupt anyone
  for once somebody has settled it, so the search waits to be asked for.
- **Nothing is claimed about a track that came out of the film.** There is no frame rate to
  disagree with and no release to mismatch, so an unconfirmed embedded track says nothing at all.
  A quality mark there would be inventing the one answer that matters.
- **Silence is the ordinary answer in the picker.** Most uploads declare nothing useful. Marking
  every one of them leaves the whole list marked and the marks meaning nothing, so only a strong
  claim or a real problem earns one.
- **A row carries a mark, not a sentence.** Rows already hold release names, and a phrase beside
  each one pushes the list past the height of the menu. The glyphs are typography rather than
  pictures, so they take the row's colour and size, and what they mean rides on the mark itself
  where a pointer and a screen reader both reach it. Colour agrees with the glyph rather than
  carrying the meaning alone.
- **The menu has to fit without being scrolled.** The candidate list is ranked, so showing more of
  it only adds worse answers underneath the good ones while pushing the good ones off screen.

## Starting playback

- **A rejected play is two different things and they are not treated alike.** The browser refusing
  one for want of a gesture is a person's problem to solve; a play abandoned because this client
  seeked underneath it is this client's. Asking somebody to press a button for the second leaves
  them pressing it repeatedly, each press starting the same race again.
- **Nothing is corrected while the film cannot play forward.** A playhead waiting for data drifts
  from the room by definition. Seeking it to catch up throws away the buffer it was waiting for,
  so the correction produces the stall it was correcting, further behind each time. Waiting is the
  correction.
- **A play is never issued in the same breath as a seek.** One interrupts the other, the element
  returns to paused, and the pause it reports is indistinguishable from a person stopping the film.
  Playback the room wants waits for the playhead to arrive instead.
- **A pause raised while a seek is in flight is machinery, not a decision.** Publishing it stops
  the room every time one person's playhead moves.
- **A seek is never issued while one is running.** The second abandons the first, and during the
  opening buffer that restarts the load.
- **The library answers a click on the film, and nothing else does.** It has toggled playback on
  a click since before any of this was written, and it knows not to when the click was on a
  control. A second handler behind it toggles the film back, which reads as playback refusing to
  start.
- **Everything except the poster needs the token, including anything CSS fetches.** A background
  image is fetched by the browser and carries no header of ours, so it is fetched in code and drawn
  from a blob. The poster is open because Discord's servers draw embeds with it; a sheet of frames
  from the film is not that.
- **No control offers a press before anything can answer one.** Waiting is shown as waiting. The
  only button a person is asked for is the one that appears when the browser wants a gesture.
- **A message that answers a question waits until the question is answered.** Whether the room
  holds a film takes a round trip, and saying it holds none while that is in the air tells
  everybody arriving to a film that there isn't one.

## The player's controls

- **Every control moves the whole room.** There is one playhead and no host, so a space bar pauses
  the film for everybody rather than for whoever pressed it. That is the premise, not a hazard, but
  it is why nothing is bound that a hand resting on a keyboard could trigger and why the scrub bar
  shows where a seek would land before it is made.
- **A control that already answers a key keeps it.** The scrub bar and the volume slider each
  handle the arrows themselves when focused; taking them globally as well moves the film, or the
  volume, twice for one press.
- **Volume is the one key that moves nobody else, and it is still shown in the middle of the
  screen.** Up and down step this viewer's loudness by five points, as does a wheel turned over
  the volume control, and each step flashes where the volume landed in the same place a seek
  flashes what it did. The slider is on a bar nobody is looking at while the film plays, and one
  step is not something an ear can be sure it heard.
- **What somebody else did to the room is said on screen, to everyone but them.** A pause looks
  like a stall and a seek inside the scene looks like nothing, so the bubble names who did what
  and, for a seek, where the film went. Only a state that advanced the room's revision counts: a
  resync hands back a state already seen, and announcing it would report an act nobody took. One
  function, `describeChange`, words it for the bubble and for the line under the player.
- **The subtitle menu belongs to one film.** What the index offered is forgotten when the film
  changes, and a search that lands after the change is dropped, or the menu shows the last film's
  subtitles under the next one. The film's own tracks are read again every time the panel is
  opened: what a film gains is pushed to the room as it lands, and the re-read is the guarantee
  behind the push, so a track that became available is shown available whatever reached the page
  in between.
- **Fullscreen exists only where the browser grants it.** Inside Discord's iframe the API is not
  given to an Activity, so the key and the double click do nothing there rather than failing — and
  the player already fills the frame, which is what they would have been for.
- **A preview sheet is bounded by pixels, not by a count of frames.** A browser holds four bytes
  for every pixel of it for as long as the film is open, so that is the real limit rather than the
  size of the file — and it means larger frames buy themselves fewer of them. Both of the sheet's
  dimensions stay inside four thousand pixels, which is what older hardware will hold as one
  texture.
- **Previews come from one sheet, not one file each.** A preview is wanted the instant a pointer
  lands on the bar, and a request per frame would spend the whole hover fetching. Only keyframes
  are decoded to build it, which is what keeps it bounded by how fast the file reads rather than by
  the length of the film.
- **A chapter title that is only a timestamp is no title.** Muxers write the chapter's own start
  time into its name routinely, and shown beside the time under the pointer that reads as a second
  clock disagreeing with the first.
- **The spinner waits before it appears.** A stall shorter than a moment is a stutter, and flashing
  at one is worse than ignoring it.

## Fetching a film through `/watch`

There is one command for watching a film, and a film that is not here yet is fetched through it
rather than through a second one. Everything here is about the seam between the two.

- **The library answers first, and alone.** The menu offers the tracker only once nothing in the
  library matches what was typed. A row offering to fetch a film beside the row that plays it is
  how a library ends up holding the same film twice.
- **A tracker row is told from a library row by its value alone.** The value is all a picked row
  sends back, and a library id is a slug that can be entirely digits, so a torrent id is carried
  under a prefix (`TrackerPick`) rather than bare.
- **A pick from a voice channel is a request to watch, and the download is the means.** The room
  is written on the torrent, and whichever pass of the watcher sees the film become watchable
  loads it into that room exactly as `/watch` loads a film that was already here, then posts the
  same launch, mentioning the person who asked. A pick from outside a voice channel is a download
  and an announcement, and nothing more.
- **The library id a film goes under is written by the hand-off, never derived by the bot.** The
  hand-off tags the torrent with it before the transcode starts, so the pass that sees the film
  become watchable is already holding the id it will answer to. Parsing the release name a second
  time would not disagree loudly; it would open nothing.
- **The reply to a pick is the waiting state, not the launch.** A film takes at least a minute to
  become watchable and an interaction token does not outlast a slow download, so the launch is a
  separate message posted when the film can be opened, which is also the only kind of message
  that reaches the person who walked away.
- **Watchable is waited for, not downloaded.** The transcode starts while the file is arriving and
  the mark that the film can be opened is set seconds into it. The watcher acts on that mark
  wherever the download has got to; waiting for the download to finish would sit on a playable
  film for the length of the download.
- **A room that changed its mind is left alone.** If the room already holds the film by the time
  it can be watched, it is left where it is; if it holds another, it is switched, because that is
  what the person asked for and what `/watch` does.

## Telling somebody about a download

- **Editing a message notifies nobody.** Discord sends no notification for an edit, so a progress
  message that becomes "ready" reaches only whoever is already looking at it. The announcement is
  therefore a separate, new message — that is what pings.
- **Discord meters message edits per channel, not per message.** Several downloads in one channel
  share one allowance, and their timers landing together is what would spend it, so every edit
  goes through one paced queue. Discord asks that limits not be hard coded and be read from its
  response headers instead; the floor here sits well under the observed allowance and the library
  holds the real one.
- **An edit that would change nothing is not sent.** A download stalled at a third for ten minutes
  is otherwise sixty identical edits, each spending the channel's allowance to say the same thing.
- **The progress message is fetched through its channel, never through the interaction.** An
  interaction token lasts fifteen minutes and a download does not, so editing through the
  interaction stops working partway through a long film.
- **One definition of every embed.** `DownloadEmbed` is used by the command, the updater and the
  announcement. A second spelling would not disagree loudly, it would just produce two messages
  about one film that look like they came from different programs.

## Waiting on a film

`/notify` is for a film that cannot be downloaded yet. Everything here is about not lying about
when it can.

- **The film is named by the catalogue, never by a search of the tracker.** The tracker has
  nothing yet, which is the premise. The command autocompletes from the IMDb index and accepts a
  pasted link, and what is kept is the id, so the sweep asks the tracker for one exact film
  rather than for a title that matches a hundred rows.
- **A row on the tracker is not availability.** A film in cinemas has camcorder recordings on the
  tracker within days, and the selection policy offers them, weighed down, because somebody who
  asks for one on purpose should be able to have it. A film is available once a release at or
  above the configured source floor is offered, and the floor is a web encode.
- **Nobody waits on something that is here.** The library, the torrent client and the tracker
  are checked before a wish is made, in that order, and each answers with what to do instead. A
  check that cannot be made is skipped rather than refused, because the sweep asks again within
  the hour and one message too many is the worst that skipping costs.
- **A wish is keyed by the film and shared.** Two people asking for the same film are one tracker
  call per sweep and one message per channel, mentioning both.
- **The wish list is written down because a wish exists nowhere else.** A film that is not on the
  tracker has no torrent to tag and no manifest to write. It goes in the state directory systemd
  hands the service, whole and through a rename, on every change — the same as the launch cards,
  and for the same reason.
- **The announcement is a new message and names exactly who asked.** An edit notifies nobody. The
  mentions go in the message text, because a mention inside an embed renders and pings no one,
  and the allowed mentions are those ids alone.
- **A wish is forgotten by being told.** Whoever asked in a channel the message could not be sent
  to stays on the list for the next pass. A channel the bot can no longer see is treated as told,
  because retrying it would never work.

## Letting a film go

Disk is finite, and a downloaded film that nobody keeps leaves after a week of seeding. The rule
itself, and the clock it runs on, are `Retention` in the acquire library; this is about who acts
on it and what is checked first.

- **The clock is the torrent client's seeding time, never the calendar.** The tracker credits
  seeding only while the client is running and the torrent is active, and the client's own count
  covers the same stretches and survives its restarts. A week is a week of seeding, and a machine
  that was off for a month has moved nothing closer to leaving.
- **Nothing is removed under the tracker's minimum plus a margin, whatever the window says.** The
  client's clock can only run ahead of the tracker's, by the announces that never landed, so the
  floor carries a margin and the window sits days above it.
- **The hand-off prunes, because it is the only process allowed to write the media root.** The bot
  and the API hold that root read-only, and the torrent's files go through the client rather than
  being deleted from under it, which would leave the client announcing a file it no longer has.
- **The title directory goes first, then the torrent.** A failure between the two leaves a torrent
  that the next pass finds again; the other order leaves a directory nothing points at.
- **A directory is deleted only when its manifest names the source this download arrived as.** A
  film fetched twice as two releases lands under one id, the second ingest replacing the first's
  directory, and the older torrent's turn to go must not take the newer transcode with it. A
  mismatch removes the download alone and says so.
- **A film a room holds is not pruned.** The API is asked which titles the rooms hold, whether or
  not anybody is in them this second, and if it cannot answer the pass removes nothing. The API
  forgets an idle room on its own, so a room blocks a prune for at most that idle window.
- **A film still owing its transcode is left alone.** The ingest deletes and rewrites its
  directory, and two processes doing that to one directory is how a film ends up half written. One
  owed for a week is a fault to read about in the log, not a film to remove.
- **A title with no download behind it is on no clock.** A film put in the library by hand owes
  the tracker nothing and carries no seeding time, so retention does not apply to it and the
  launch says nothing about how long it stays.
- **A keep is a tag on the torrent, and it names who set it.** Every process that decides a
  download's fate already reads its tags. Anyone may keep a film and anyone may let it go, and the
  row says who did, which beats deciding who is allowed to.
- **Fetched subtitles and confirmations outlive the film.** They are keyed by the library id, a
  film fetched again lands under the same id and gets them back, and each cost a daily allowance
  to obtain.
- **The launch and the ready announcement say how long a film stays in one sentence**,
  `KeepCommand.Notice`, so the message that starts a film and the one that announced it cannot
  disagree about when it leaves. The list is the short form of the same figures.

## Naming a film

A release name is what a film arrives as. It is not what the film is called.

- **A film is named by the catalogue wherever the catalogue has been asked.** `FilmMetadata`
  resolves an IMDb id — the tracker supplies one, and a parsed name and year are searched when it
  did not — and what comes back is the name, the year, the billing and the poster. `manifest.Title`
  becomes that name, and `manifest.Film` keeps the rest. Which cut arrived stays in
  `source.release`, which is the only place it was ever a fact.
- **It is resolved before the transcode, never after.** A film is watchable, and announced, within
  seconds of the first segments landing. Metadata added when the transcode finishes arrives a
  quarter of an hour after every message that would have shown it.
- **A film named wrongly is worse than a film not named.** Everything downstream believes it, so a
  search that cannot be sure returns nothing: a year given and matched by nothing finds nothing
  rather than the closest thing.
- **There is one spelling of a name and a year written together**, `Release.Display`, because
  three of them is how the message that starts a download and the message that says it is ready
  end up disagreeing about what film they are talking about.
- **A release name is still shown, beside the name and never instead of it.** Which encode arrived
  is worth knowing; it is just not what the film is called.
- **The two places that keep the release name as the headline are the search results and a
  download in progress.** Neither has been through the catalogue, and both are about a file rather
  than about a film.
- **The poster is the small image in an embed, not the large one.** Posters are portrait, and a
  large one fills a message with artwork nobody asked to look at.
- **A source that shipped its own cover art keeps it.** It came with the release, and the
  catalogue's is only ever a stand-in for a film that has none.

## Listening in a voice channel

`/voice join` brings the bot into a voice channel to listen, and "hey MovieBot, pause" stops the
film. `src/MovieBot.Bot/README.md` is the authority for the surface; these are the rules it rests on.

- **The gate comes before the model.** What is heard goes to moviebot-speech for words and the words
  go to `RoomVerbs`, a gate that knows a handful of verbs. A component that moves the room for
  everyone in it should read the same words the same way every time, so the common verbs never
  depend on a model's judgement. Only what the gate does not read goes to the assistant.
- **The gate matches the whole utterance and has three answers.** "Should we pause?" contains the
  word and is not the verb. A phrasing that looks like a verb and cannot be read safely — no amount,
  a vague one, two acts, or digits joined by punctuation that flattening would fuse — is ambiguous
  and not guessed at, because a misread verb moves the film for everybody.
- **A verb needs a room holding a film.** Every write to a room creates one, so the room is read
  first and a verb in a channel watching nothing does nothing.
- **A play or a pause from the bot sends no position.** The API resolves "wherever the room is"
  under the lock that applies the change; a position guessed by something not watching would move
  the film as well as stopping it.
- **Listening is announced in the channel, or it does not happen.** The notice is the only way
  anyone but the person who ran `/voice join` learns they are heard.
- **The voice pipeline is `TheKrystalShip.Discord.Voice`, shared with kgsm-bot, and it lives under
  the org's root namespace.** Inside any `TheKrystalShip.*` namespace a bare `Discord.X` resolves to
  `TheKrystalShip.Discord` first and fails, so Discord.Net types are written `global::Discord.X`.

## Asking the assistant

Anything said to the bot that the gate does not read is a turn of the agent loop from
`TheKrystalShip.Llm`, against moviebot-llm, answered in the voice channel's chat.
`src/MovieBot.Bot/README.md` is the authority for the surface; these are the rules it rests on.

- **The brain runs in the bot, and its tools are the bot's commands.** A download has to post its
  progress message and a wish lives in the bot's state directory, so the acts it can take are
  `WatchCommand`, `DownloadCommand`, `NotifyCommand` and `KeepCommand`, called directly. A tool
  that did its own version would not disagree loudly; it would leave a download without its
  progress message.
- **Live state goes in the turn's context, never in the instructions.** The chat template renders
  the instructions ahead of the tool catalog, so a changing byte there re-reads the catalog and the
  conversation every turn. `AgentTurn.Context` is sent after the conversation.
- **`enable_thinking` is sent false on every request.** This model family reasons when the variable
  is left unset, which triples the time to a tool call.
- **The instructions stay short.** A long `system.md` naming tools and rules made this 2B model
  write tool calls out as text. Guidance lives in tool descriptions and in what each tool returns,
  and `AssistantRoutingTests` against the real model is how a wording change is judged.
- **What a tool returns is split between the room and the model.** The sentence a person could read
  is recorded, and what to do next is appended for the model alone. The model often writes nothing
  after a tool result, and the recorded sentence is then the reply, so it must never name a tool.
- **Acting on the room is immediate; spending something is proposed.** Proposals wait for a button
  or a spoken yes, redeem one single-use token, expire, and are carried out as whoever asked.
- **A room verb is written into the room's conversation** as the tool call it stands for, so the
  model knows what happened to the room without having done it.
- **A written reply is held to what the turn did**, by the checks in `TheKrystalShip.Agent`: a claim
  of acting on a turn that did nothing, and a figure nothing the turn was given contains, re-prompt
  once and are then corrected. `RoomTools.Acted` is what a claim is held against, and it must cover
  every tool that changes something, or an honest "I've paused it" is contradicted.
- **The harness around the loop is shared, and the room is not.** Reading `system.md` and
  `tools.json`, the catalog's agreement with `RoomTools.Names`, proposal tokens, the reply checks and
  compaction come from `TheKrystalShip.Agent` in tks-agent. What the tools do, which of them wait for a
  person, and the words a room is acted on in are this repository's.

## Keeping a room

A room is the only thing in the system that exists nowhere else. The films are on disk and the
subtitles are on disk; what a room is watching and where it has got to lived in memory alone, so
restarting the API took the film out from under everybody in it.

- **A room is written down, and put back.** `SessionJournal` holds every room in the state
  directory systemd hands over. It is read before the server begins listening — a room restored
  after the first client has joined is a room that client was already told did not exist.
- **A restored room keeps its epoch and its revision.** It is the same run of the same room rather
  than a new one wearing its name, so a client connected across the restart goes on applying
  pushes instead of discarding every one of them, and its reconnect resync hands back what it
  already had. Nothing about the restart reaches a person watching.
- **Position survives for free**, because it is a place at an instant rather than a number that
  ticks. A room that was playing when the server stopped is playing, at the right place, when it
  starts again — however long that took.
- **Nobody is restored into a room.** Membership is a live connection and every one of them died
  with the server. Restoring the names would leave a room reporting people who are connected to
  nothing, which the reaper would then never forget.
- **The journal does not resurrect what the reaper would have forgotten.** A room idle past the
  window is dropped on the way back in, because a link into a room is supposed to stop working.
- **Writing is on a two-second delay.** A drag along the scrub bar is a burst of changes and each
  would be a whole file. A planned stop writes on the way out and loses nothing.
- **`/health` says how many rooms are occupied and by how many people, and no names.** It is what
  makes restarting something that can be looked at first rather than found out about afterwards.

## When a launch stops opening

A launch card is a door. The room behind it closes, and left alone the card reads exactly as it
did when it was posted: the same film, the same button, nothing anywhere to say why pressing it
does nothing.

- **An invite is made to last the film.** Discord counts an invite's life from the moment it is
  made, never from the last person through it, so the invite is given the film's running time plus
  `Launch:InviteGraceSeconds`. A flat window shorter than a film shuts the door on a room that is
  still watching: everybody inside carries on and nobody else can get in, which is invisible from
  both sides.
- **The grace matches the API's `Rooms:IdleTimeout`**, which lands the invite and the room behind
  it at the same moment rather than leaving one to outlive the other.
- **An invite always expires.** An age of zero reads as never to Discord, so a floor stands under
  whatever is configured. A permanent way into the server is not a thing a film hands out.
- **A card that has stopped being a way in says so where it stands.** It goes grey, the button and
  the link on its title go, and the description says what happened and that `/watch` in a voice
  channel starts the film again. The film, the poster and the fields stay: scrolling back to what
  an evening watched is worth being able to do.
- **A room that moved on to another film is told apart from a room that closed.** One is still
  watching and this card describes the wrong thing; the other is over. A room holding nothing
  counts as closed, since a room forgotten and opened again by somebody arriving is an empty room
  wearing the same name.
- **The invite is revoked with the card**, so a link copied out of one stops working when the card
  says it has.
- **A card is written down, because it exists nowhere else.** Which message, in which channel,
  carrying which invite, for which film. The API has never heard of a Discord message, so an
  unwritten card is one a restarted bot leaves looking live for good.
- **A pass that cannot reach the API does nothing.** Rooms it could not read are not rooms that
  closed, and marking every card over on a moment's trouble is worse than the silence it replaces.
- **The card is edited, never replaced.** An edit notifies nobody, which is right: this is for
  whoever scrolls back and finds it, not for the room that has already moved on.

## Changing the film

`/watch` in a room that is already watching something is how the film is changed, for everyone.

- **The switch is one act.** The room is handed the new film from its start, and whether it is
  playing is left as it was: a room that was playing goes on playing the new film, a paused one
  stays paused. Loading a film paused and waiting for somebody to press play is a change followed
  by a wait, and the wait is what made it feel like nothing had happened.
- **Asking for the film the room already holds leaves it where it is.** Loading it again would
  send everyone back to the beginning, and the person asking almost always wants the way in.
  The reply says the room is already watching it and hands over the launch.
- **The reply says what it did to the room.** Started, switched from what to what, or already
  watching: three headlines from one place, so the two presenters cannot describe the same act
  differently. A switch names the film it replaced by its name, never its id.
- **Nothing in the player changes for a switch.** A state naming a different title is the same
  push the library click has always produced, and the player follows it: the old source is torn
  down, the new one loaded, and the room's state applied once the film is in the element.

## Saying what a room is watching

Discord shows it in three places, and each is a different surface with a different reach.

- **The bot's status names a film only while exactly one room is watching one.** There is one
  status for the whole bot, so two rooms are counted rather than named, and no room at all is no
  status at all: a placeholder beside the name never changes, which reads as never having worked.
  Discord lets a bot set nothing beyond a name and a type here, so the second line stays empty.
- **The line under a voice channel is per room, and only an occupied room gets one.** It is the
  one surface the whole server sees without opening anything. The position is written to the
  minute and every distinct line is one request, so the clock sets the pace of the writes: a
  playing film changes its line once a minute and a paused one never does. Discord requires
  Manage Channels as well as Set Voice Channel Status from a bot that is not itself connected to
  the channel, and both are in the invite the bot logs.
- **The bot recognises its own lines and clears only those.** It keeps no record of the channels
  it wrote under across a restart, and a line left standing after the room behind it ended would
  otherwise stay until the next film in that channel. A person's own channel status has a
  different shape and is never touched.
- **What the bot says is read from the API on every pass, never remembered.** The listing carries
  what each room is watching and how many are in it, and no names: who is in a room is the room's
  business.
- **Each viewer's own presence comes from the Activity, not from the bot.** It is the only surface
  Discord gives real rich presence to: the film's name, a poster, and a bar that runs from where
  the room is to the end of the film. The bar is two instants and Discord draws the rest, so
  nothing is sent on a timer; a paused film says so and carries no clock. It needs the
  `rpc.activities.write` scope, which is the second of exactly two the Activity asks for.
- **The poster is fetched by Discord's servers, from the public address.** The page's origin is
  Discord's proxy, which is reachable from inside the Activity and from nowhere else, so the API
  publishes its public address on `/api/config` and the page builds the poster URL from that.

## Getting a film watchable

Transcoding alongside the download exists so a film can be watched while it arrives. Everything
here is about not losing that.

- **Films are ingested concurrently, up to a ceiling.** A film is watchable seconds after its own
  transcode starts and not before, so one taken strictly in turn does not exist yet: not in the
  library, not by name, however much of it has arrived. They share one card and each runs slower
  for it — around twelve times realtime alone, six apiece with two — and every one of them still
  outruns a person watching several times over, which is the only rate that matters.
- **A sweep never restarts what it is already running.** Sweeps carry on while a transcode does,
  and an ingest replaces a title's directory wholesale, so a second start would delete what the
  first is writing. The in-flight set keyed by download hash is what prevents it.
- **A film owing a transcode and a film being watchable are different facts with different tags.**
  `ingest` survives until the transcode finishes; `watchable` is set seconds into it and is what
  the announcement waits for. One tag cannot answer both: clearing it to let the announcement out
  would leave nothing to say the rest of the transcode is still owed, and an interrupted one
  unrecoverable.
- **What a film gains while people are watching it is pushed to them.** The preview sheet is
  written after the main pass, and so are the subtitles of a source that was still arriving, so
  the manifest that marks a film ready is the first to name them. The API watches the manifests
  of the titles occupied rooms hold and sends the fresh copy down the hub as `TitleChanged`, and
  the player adopts what changed: the head, the sheet, the subtitle list. There is no poll in
  the player; a page that could not be heard from asks once on the way back. It goes over the
  hub rather than a second channel because every viewer already holds an authenticated
  connection, and a subtitle fetched from outside is announced by its route, since the file lives
  beside the manifest and the manifest's timestamp says nothing happened.
- **A poster is put on disk before the manifest names it.** A manifest is read the moment it
  exists, and a surface that fetches artwork on its own servers caches the 404 rather than trying
  again. The catalogue's poster is already a local file and waits for nothing; a source's own
  cover art has to be demuxed out of it, so for a film still downloading it is named only once it
  has been extracted.

## Reading a source that is still arriving

Transcoding a file while it downloads is only safe while the reader stays behind the writer, and
nothing enforces that on its own.

- **Reading past what has arrived returns zeros, not an error and not the end of the file.** Space
  for the whole file is claimed when the download starts. ffmpeg encodes the zeros, and the result
  is a film with stillness and silence in it and nothing anywhere to say so.
- **`ReadAheadGuard` watches rather than trusts.** The reader's position comes from the kernel
  (`/proc/<pid>/fdinfo`), the arrived length from whoever is fetching the file. When the gap
  closes the process is stopped and resumed when it opens.
- **The guard fails closed.** If it cannot find the descriptor, it refuses rather than running on:
  a position it cannot read pauses, and one it never found at all aborts the transcode. Running
  unguarded produces corruption that looks like success, which is worse than a failure that says so.
- **A guard that cannot do its job kills the transcode.** It is awaited after the transcode so its
  message replaces ffmpeg's, which would only report that it was killed.
- **Subtitles wait for the whole file.** They have to be demuxed from a complete one, so where a
  source is still arriving they are extracted after the main pass, and a track is advertised in
  the manifest only once its own file is whole. A `.vtt` that is still being written is never
  served.

## Track labelling

Driven entirely by what the container says, because the sample film makes every shortcut fail:

- **Label from the `title` tag, not the language code.** Three tracks tagged `chi`, two `spa`,
  two `por`, two `fre`, two `eng`. Code-only labels produce identical menu rows.
- **Split on the `comment` disposition.** Thirteen of forty-eight subtitle tracks are commentary.
- Inside the commentary group the word "Commentary" is stripped, since the group is already
  named — what remains qualifies the language. A title long enough to name *who* is speaking is
  kept whole.
- **Bitmap subtitles are listed, not dropped**, with `available: false` and a reason. A language
  that is simply absent looks like a bug.

## Conventions

- C# namespaces are `TheKrystalShip.*`, matching the GitHub org this publishes to.
- Present-tense canon in every doc and comment: describe how the thing works now. History
  belongs in the CHANGELOG and in commit messages, never in prose or code comments.
- No emoji anywhere — not in docs, comments, commit messages or CLI output.
- Commit per finished piece of work, including the version bump and CHANGELOG entry, and tag
  the bump `v<version>`.
- **A change that spans both repositories is one commit in each**, describing that repository's
  half. Address git with `git -C <repo>`: the two checkouts sit side by side, and a `cd` applies
  to every command after it in the same shell, so a commit or a tag meant for one lands in the
  other.
