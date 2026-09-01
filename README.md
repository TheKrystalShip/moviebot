# MovieBot

A Discord Activity that plays your own films to everyone in a voice channel, on one shared
timeline. Someone asks the bot for a film; the bot drops the room into a player where play,
pause and seek are shared, and volume, subtitle and audio track are each viewer's own.

Playback starts seconds after the request, while the film is still being transcoded.

## Status

The ingest pipeline is built and runs. Nothing else exists yet.

| Piece | State |
|---|---|
| `MovieBot.Ingest` — probe, extract, transcode, manifest | built |
| `MovieBot.Api` — sessions, SignalR hub, media serving | not started |
| `MovieBot.Bot` — Discord.Net slash command, Activity invite | not started |
| `web/activity` — the player | not started |

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
(default 4 s), `--tonemap auto|on|off`, and `--force` to replace existing output.

### What it produces

```
media/<id>/
  manifest.json      what the film offers, and how far the transcode has reached
  poster.jpg         extracted from the container's own cover art, when present
  s3.vtt s4.vtt …    one WebVTT per text subtitle track, named by source stream index
  v0/index.m3u8      video rendition, H.264 High, fMP4 segments
  a0/index.m3u8      primary audio, AAC stereo
  a1/index.m3u8      further audio tracks, one directory each
```

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

## Layout

```
src/MovieBot.Core/      manifest contract and its serializer
src/MovieBot.Ingest/    the ingest CLI
```

## Licence

GPL-3.0-or-later. The repository ships code only: it plays films you already have.
