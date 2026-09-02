# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

MovieBot plays films you already own to everyone in a Discord voice channel, on one shared
timeline, inside a Discord Activity. It is a standalone project: it shares no code, no
packages and no deployment with the KGSM ecosystem it sits beside in this workspace.

The build plan — measured transcode figures, the sync protocol, the Discord constraints and the
phase order — is the authority for what gets built next.

## The one idea everything follows from

**Everyone watches the same position in the same film.** That is not a feature, it is the
premise, and most of the design falls out of it:

- One transcode serves the whole room, because there is only ever one playhead. Plex and
  Jellyfin open a session per viewer; this does not.
- A single transcode head has to outrun a single playhead, and it outruns it tenfold — which is
  what makes playback-while-transcoding practical rather than fiddly.
- **Shared state is small**: title, paused, position at an anchor time, rate, who changed it,
  revision, transcode head. Volume, subtitle choice, audio track and quality are per-viewer
  preferences and never go on the wire. Shared volume is a way for one person to deafen
  everyone else.

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

`media/` is generated and gitignored. Nothing under it is ever committed.

## API invariants

- **The server is authoritative and there is no host.** Clients send intent; the store decides,
  stamps the server clock and assigns the next revision. A client never pushes state.
- **Clamp seeks on the server, never in the client.** A client's head is always slightly stale,
  so client-side clamping yields a seek half the room accepts and half rejects.
- **`SeekClamped` goes to the caller alone.** Broadcasting it would show the whole room an error
  nobody else triggered.
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
- **`libplacebo` fails to initialise its filter graph on this host's ffmpeg build**, under every
  option combination tried. `tonemap_opencl` is the working path. If a future build fixes
  libplacebo it is the better filter, but verify before switching.
- **Decode on NVDEC (`-hwaccel cuda`), always.** Software decode of a 16 Mbps HEVC Main 10
  source costs 35 s of CPU per 40 s of film against 5.5 s on the GPU, and pins every core for
  the length of a feature. Frames land in system memory, which is where `tonemap_opencl`
  wants them.
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
- **A control that already answers a key keeps it.** The scrub bar handles the arrows itself when
  focused; taking them globally as well moves the film twice for one press.
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
  the announcement waits for. One tag answering both is what made an interrupted transcode
  unrecoverable — cleared to let the announcement out, it was no longer there to say the rest was
  owed.
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
- **The order of the work changes, not the invariants.** Subtitles still have to be demuxed from a
  whole file to be complete, so they are extracted after the main pass instead of before it, and a
  track is advertised in the manifest only once its file is whole. That is the same rule as before
  — never serve a `.vtt` that is still being written — reached from the other side.

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
