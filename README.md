# MovieBot

A Discord Activity that plays your own films to everyone in a voice channel, on one shared
timeline. Someone asks the bot for a film; the bot drops the room into a player where play,
pause and seek are shared, and volume, subtitle and audio track are each viewer's own.

Playback starts seconds after the request, while the film is still being transcoded.

## Status

Every piece is built and running.

| Piece | What it does |
|---|---|
| `MovieBot.Ingest` | probe, extract, transcode, manifest |
| `MovieBot.Api` | sessions, SignalR hub, media serving |
| `MovieBot.Bot` | the Discord surface: `/watch`, `/notify`, `/keep` |
| `MovieBot.Handoff` | carries a finished download into the library |
| `web/activity` | the player |

`MovieBot.Api`, `MovieBot.Bot` and `MovieBot.Handoff` run as systemd services and are served at
`movies.thekrystalship.com`. They build against the `moviebot-acquire` checkout beside this one.

## Requirements

- .NET 10 SDK
- `ffmpeg` and `ffprobe` on `PATH`
- An NVENC-capable NVIDIA GPU for `h264_nvenc`
- An OpenCL device, for HDR sources only — `tonemap_opencl` does the HDR to SDR conversion

## Ingest

```bash
dotnet build moviebot.slnx -c Release

# See what a film offers without transcoding anything
dotnet run --project src/MovieBot.Ingest -c Release -- "/path/to/film.mkv" --dry-run

# Prepare it for playback
dotnet run --project src/MovieBot.Ingest -c Release -- "/path/to/film.mkv" --out ./media
```

`--help` lists every option. The ones that matter: `--bitrate` (default `9M`), `--segment`
(default 2 s), `--tonemap auto|on|off`, `--force` to replace existing output, and
`--no-dialogue-boost` to skip the second mix.

### What it produces

```
media/<id>/
  manifest.json      what the film offers, and how far the transcode has reached
  poster.jpg         extracted from the container's own cover art, when present
  s3.vtt s4.vtt …    one WebVTT per text subtitle track, named by source stream index
  v0/index.m3u8      video rendition, H.264 High, fMP4 segments
  a0/index.m3u8      primary audio, AAC stereo
  a1/index.m3u8      further audio tracks, one directory each
  aN/index.m3u8      after the source's own tracks, a dialogue-boost mix of each feature track
```

The dialogue-boost mix raises the centre channel, where a film's dialogue lives, over the fronts
and surrounds, then compresses and normalises the result, so the talking is audible at a volume
the explosions do not punish. It is made after the main pass and listed once it is whole.

### Why it is watchable before it is finished

The playlists are HLS `EVENT` type: they only ever append, so a player can join and seek
within whatever has been written while ffmpeg continues. ffmpeg appends `#EXT-X-ENDLIST` when
it completes, which turns them into ordinary VOD playlists with nothing to clean up.

On an RTX 3060 a 1080p HDR source transcodes at around 10x realtime, so roughly twelve seconds
of film exist two seconds in, and the head reaches the end of a three-hour film about seventeen
minutes into the session. `manifest.json` carries `headSeconds` — the shortest of the written
playlists, since video without its audio is not playable — and the API refuses seeks beyond it.

Subtitles and cover art are extracted **before** the transcode starts, not alongside it. A
monolithic `.vtt` being appended to while a player fetches it once gives subtitles that stop
partway through the film.

### Track labelling

A single Blu-ray rip routinely carries fifty subtitle tracks. Two rules keep the menu usable,
both driven by what is actually in the container:

- **Labels come from the title tag, not the language code.** One real film carries three tracks
  tagged `chi` (Cantonese Traditional, Simplified, Traditional) and two tagged `spa`. Language
  codes alone produce identical rows.
- **Commentary is a separate group**, taken from ffmpeg's `comment` disposition. Thirteen of
  that film's forty-eight subtitle tracks are commentary.

Bitmap subtitles — PGS and VobSub — are pictures of text and cannot become WebVTT without OCR.
They are listed in the manifest as `available: false` with `reason: "needs-ocr"`, so a missing
language is explained rather than silently absent.

## API

```bash
Media__Root=/absolute/path/to/media dotnet run --project src/MovieBot.Api -c Release
```

Listens on `http://127.0.0.1:8099`.

| Route | Purpose |
|---|---|
| `GET /api/titles` | the library |
| `GET /api/titles/{id}` | one manifest, including the live transcode head |
| `GET /api/sessions/{id}` | current shared state |
| `GET,HEAD /media/{id}/**` | playlists, segments, subtitles, poster |
| `/hub/session` | SignalR: `Join`, `LoadTitle`, `Play`, `Pause`, `Seek` |

The server is authoritative and there is no host — anyone in a session can drive it, and the
state records who did. Every push carries the server clock and a monotonic revision, so a client
discards anything it has already seen and derives the position from the anchor rather than being
told a ticking number.

**Seeks past the transcode head are refused by the server**, which grants a position short of the
head and sends `SeekClamped` to the caller alone. Clamping in the client instead would let a
stale head produce a seek half the room accepts and half rejects.

Playlists for a title still transcoding are served `no-store`; segments never change once written
and are immutable for a year.

## Tests

```bash
dotnet test
```

The session tests drive two real SignalR clients against the app in-process, because the failures
worth catching — a push that never arrives, a clamp delivered to the wrong client — live in the
wiring rather than the logic.

## Layout

```
src/MovieBot.Core/      manifest and session contracts, and their serializer
src/MovieBot.Ingest/    the ingest CLI
src/MovieBot.Api/       library, media, sessions, SignalR hub
src/MovieBot.Bot/       the Discord surface
src/MovieBot.Handoff/   a finished download into the library
web/activity/           the player
deploy/                 the systemd units and the nginx front door
tests/MovieBot.Tests/   hub integration tests
```

## Licence

GPL-3.0-or-later. The repository ships code only: it plays films you already have.

## Access

Discord is the only surface. Everything except the sign-in, the page itself and cover art requires
a token, and a token is issued only after Discord confirms who somebody is — so a browser pointed
at the hostname can list nothing, fetch nothing and join no room. Two keys are required:
`Auth:SigningKey`, without which the API refuses to start, and `Auth:ServiceKey`, shared with the
bot and the hand-off, neither of which has a Discord user of its own to authenticate as.
