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
