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

# inspect a source without spending a GPU on it
dotnet run --project src/MovieBot.Ingest -c Release -- "<file>" --dry-run

# ingest for real
dotnet run --project src/MovieBot.Ingest -c Release -- "<file>" --out ./media
```

`media/` is generated and gitignored. Nothing under it is ever committed.

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
