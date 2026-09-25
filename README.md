# MovieBot

A Discord Activity for watching films together. MovieBot plays films from your own library to
everyone in a Discord voice channel, on one shared timeline: play, pause and seek move the whole
room, while volume, subtitles and audio track stay each viewer's own.

Playback starts seconds after a film is requested, while it is still being transcoded.

## Features

- **One shared timeline.** The server holds the room's state and every viewer follows it. There is
  no host: anyone in the room can drive it, and the state records who did.
- **Watchable before it is finished.** Films are transcoded to HLS and can be played and seeked
  within as soon as the first segments exist.
- **Per-viewer preferences.** Volume, subtitle track, subtitle styling, audio track and quality are
  never shared.
- **Usable track menus.** Subtitle and audio tracks are labelled from the container's own title
  tags, and commentary tracks are grouped separately.
- **Dialogue boost.** Every feature audio track gets a second mix that lifts dialogue over music and
  effects.
- **Discord-native.** Slash commands find, fetch and launch films, and an optional voice mode lets a
  room pause, resume and seek by speaking, with anything else answered by a small local language
  model.

## How it works

```
                 /watch, /download, voice
  Discord  ───────────────────────────────►  MovieBot.Bot
     │                                          │   │
     │ Activity (player)                        │   └─► moviebot-acquire: tracker search, downloads
     ▼                                          ▼
  web/activity  ◄── HLS, SignalR ──►  MovieBot.Api  ◄── manifests, segments ──  MovieBot.Ingest
                                                                                     ▲
                                              MovieBot.Handoff ──────────────────────┘
                                              (finished download → library)
```

1. **Ingest** probes a film with `ffprobe`, extracts subtitles and cover art, and transcodes it to
   H.264 video and AAC audio in fragmented-MP4 HLS segments. Its playlists are HLS `EVENT` type, so
   they only ever grow and a player can join while ffmpeg is still writing. A `manifest.json`
   describes the film's tracks and how far the transcode has reached.
2. **The API** serves the library, the media files and a SignalR hub carrying each room's shared
   state. It is authoritative: clients send intent, and the server decides. A seek beyond what has
   been transcoded is clamped by the server, so every viewer in a room agrees on where it landed.
3. **The player** is a web page, run inside Discord as an Activity, built on video.js and hls.js.
   It derives the playback position from the server's clock and the room's last change, rather
   than being sent a ticking number.
4. **The bot** is the Discord surface. It resolves a film, opens the room's session on the API and
   posts the launch. When the film is not in the library it uses
   [moviebot-acquire](https://github.com/TheKrystalShip/moviebot-acquire) to find and download it.
5. **The hand-off** watches for downloads, picks the feature file, and runs ingest while the
   download is still arriving, so the film is announced as soon as it can be played. It also
   prunes downloads nobody has kept.

A single transcode serves the whole room because there is only one playhead. On an NVENC GPU a
1080p source transcodes at around ten times realtime, far ahead of anyone watching it.

## Discord commands

| Command | What it does |
|---|---|
| `/watch title:<name>` | Loads a film into the room for the voice channel you are in, downloading it first if needed |
| `/download film:<name>` | Fetches a film without playing it, so it is ready later |
| `/notify add\|list\|cancel` | Waits on a film that cannot be downloaded yet, and announces when it can |
| `/keep add\|remove\|list` | Keeps a downloaded film past its retention period, or lets it go |
| `/voice join\|status\|leave` | Listens in the voice channel for spoken commands |

See [`src/MovieBot.Bot/README.md`](src/MovieBot.Bot/README.md) for the bot, and
[`docs/voice-commands.md`](docs/voice-commands.md) for what can be said to it.

## Repository layout

```
src/MovieBot.Core/      manifest and session contracts, and their serializer
src/MovieBot.Ingest/    the ingest CLI: probe, extract, transcode, manifest
src/MovieBot.Api/       library, media serving, sessions, SignalR hub
src/MovieBot.Bot/       the Discord bot
src/MovieBot.Handoff/   moves finished downloads into the library
src/MovieBot.Speech/    speech recognition host for voice commands
web/activity/           the player
packaging/              the release's installer, systemd units and configuration examples
deploy/                 the maintainers' host configuration and native GPU builds
docs/                   deployment guide, API contract, voice command reference
scripts/                player build, release packaging, version
tests/MovieBot.Tests/   unit and hub integration tests
```

## Requirements

- .NET 10 SDK
- Node.js and npm, for the player
- `ffmpeg` and `ffprobe` on `PATH`
- An NVENC-capable NVIDIA GPU, for `h264_nvenc`
- An OpenCL device, for HDR sources only: `tonemap_opencl` converts HDR to SDR
- A checkout of [moviebot-acquire](https://github.com/TheKrystalShip/moviebot-acquire) beside this
  one. `MovieBot.Api`, `MovieBot.Bot` and `MovieBot.Handoff` reference it by relative path:

  ```
  parent/
    moviebot/
    moviebot-acquire/
  ```

## Getting started

### Build and test

```bash
dotnet build moviebot.slnx -c Release
dotnet test
```

The session tests drive two real SignalR clients against the API in-process, since the failures
worth catching live in the wiring rather than the logic.

### Ingest a film

```bash
# See what a film offers without transcoding anything
dotnet run --project src/MovieBot.Ingest -c Release -- "/path/to/film.mkv" --dry-run

# Prepare it for playback
dotnet run --project src/MovieBot.Ingest -c Release -- "/path/to/film.mkv" --out ./media
```

`--help` lists every option. The most common are `--bitrate` (default `9M`), `--segment` (default
2 seconds), `--tonemap auto|on|off`, `--force` to replace existing output, and
`--no-dialogue-boost` to skip the dialogue mix.

Output for one film:

```
media/<id>/
  manifest.json      the film's tracks, and how far the transcode has reached
  poster.jpg         the container's own cover art, when present
  s3.vtt s4.vtt …    one WebVTT file per text subtitle track, named by source stream index
  v0/index.m3u8      video: H.264 High, fMP4 segments
  a0/index.m3u8      primary audio: AAC stereo
  a1/index.m3u8 …    further audio tracks, then a dialogue-boost mix of each feature track
```

Bitmap subtitles (PGS, VobSub) cannot become WebVTT without OCR. They appear in the manifest as
`available: false` with `reason: "needs-ocr"`.

### Run the API and the player

Settings come from `~/.config/moviebot/moviebot.settings.json` (see
[Configuration](#configuration)), and anything can be overridden in the environment. For a quick
local run, override the library path and supply the secrets:

```bash
Media__Root=/absolute/path/to/media \
Auth__SigningKey=<at least 32 random characters> \
Discord__ApplicationId=<application id> \
Discord__ClientSecret=<OAuth2 client secret> \
dotnet run --project src/MovieBot.Api -c Release
```

The API listens on `http://127.0.0.1:8099`. The player is built into the API's `wwwroot` with
`scripts/build-player.sh`, or served on its own for development:

```bash
cd web/activity
npm install
npm run dev          # http://localhost:5173, talking to the API on :8099
```

Viewers sign in through Discord, so the player is used from inside the Activity, with the
application's URL mapping pointed at wherever the API is reachable.

### Run the bot

The bot needs a Discord application with a bot user, invited with the `bot` and
`applications.commands` scopes, and a tracker account:

```bash
dotnet user-secrets set "Discord:Token" "<token>" --project src/MovieBot.Bot

Player__BaseUrl=https://movies.example.com \
Discord__GuildIds__0=<guild id> \
Tracker__BaseUrl=<tracker address> Tracker__Username=<name> Tracker__Passkey=<passkey> \
Selection__AllowedCategories__0=<tracker category> \
dotnet run --project src/MovieBot.Bot -c Release
```

Every setting, including voice and the assistant, is documented in
[`src/MovieBot.Bot/README.md`](src/MovieBot.Bot/README.md#configuration).

## Configuration

Every MovieBot program reads one settings file, [`src/moviebot.settings.json`](src/moviebot.settings.json),
which declares each setting with its default and ships beside every binary. A host's own copy
lives in its XDG configuration directory, and is read in place of the defaults key by key:

1. `$XDG_CONFIG_HOME/moviebot/moviebot.settings.json`, which is `~/.config/moviebot/` for the
   account the services run as, or else
2. `moviebot/moviebot.settings.json` under each directory in `$XDG_CONFIG_DIRS`, by default
   `/etc/xdg/moviebot/`.

The environment overrides any single key, written `Section__Key` (`Download__MaximumGiB=500`).
Secrets go there rather than in the file: the bot token, the OAuth2 client secret, the signing and
service keys, the tracker account and its category names. Each service logs which files it read
when it starts.

## API

| Route | Purpose |
|---|---|
| `GET /api/titles` | the library |
| `GET /api/titles/{id}` | one manifest, including the live transcode head |
| `GET /api/sessions/{id}` | a room's current shared state |
| `GET,HEAD /media/{id}/**` | playlists, segments, subtitles, poster |
| `/hub/session` | SignalR: `Join`, `LoadTitle`, `Play`, `Pause`, `Seek` |

Every state push carries the server clock and a monotonic revision, so a client discards anything
it has already seen. Playlists for a title still transcoding are served `no-store`; segments never
change once written and are cached as immutable.

The full wire contract, including the manifest schema and the rules a client must follow, is in
[`docs/api-contract.md`](docs/api-contract.md).

## Authentication

Discord is the only way in. Everything except sign-in, the player page and cover art requires a
token, and a token is issued only after Discord confirms who somebody is. The API needs two keys:

- `Auth:SigningKey` signs viewer tokens. The API refuses to start without it.
- `Auth:ServiceKey` is shared with the bot and the hand-off, which have no Discord user of their own
  to sign in as.

## Deployment

Each [release](https://github.com/TheKrystalShip/moviebot/releases) carries
`moviebot-linux-x64.tar.gz`: every service built for linux-x64, with an installer, systemd units, a
settings file listing every key to fill in, and an nginx site. **[`docs/deploying.md`](docs/deploying.md)**
walks through a deployment step by step, from the download to watching a film.

The kit lives in [`packaging/`](packaging/), and `scripts/package-release.sh` builds the same
archive from a checkout. [`deploy/`](deploy/) holds the maintainers' own host configuration,
including the GPU builds voice commands run on.

## Releases

Pushing a tag `v<version>` that matches the newest entry in [`CHANGELOG.md`](CHANGELOG.md) builds
the archive and publishes it as a GitHub release. Every push to `main` and every pull request is
built and tested, with moviebot-acquire checked out beside this repository.

## Documentation

- [`docs/deploying.md`](docs/deploying.md): deploying a release, step by step
- [`docs/api-contract.md`](docs/api-contract.md): what the API serves on the wire
- [`web/activity/README.md`](web/activity/README.md): the player
- [`src/MovieBot.Bot/README.md`](src/MovieBot.Bot/README.md): the Discord bot and its configuration
- [`docs/voice-commands.md`](docs/voice-commands.md): what can be said to the bot
- [`CHANGELOG.md`](CHANGELOG.md): release history

## License

[GPL-3.0-or-later](LICENSE). This repository contains code only and ships no media: MovieBot plays
films you already have.
