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
- **Dialogue boost.** Optionally (`Handoff.DialogueBoost`), every feature audio track gets a second
  mix that lifts dialogue over music and effects.
- **Discord-native.** Slash commands find, fetch and launch films, and an optional voice mode lets a
  room pause, resume and seek by speaking, with anything else answered by a small local language
  model.

## How it works

```
               /watch, /show, voice                            what is said and typed
  Discord ─────────────────────────────►  MovieBot.Bot ──────────────► moviebot-assistant
     │                                         │                              │   ▲
     │ Activity (/embed/)                      │                              │   │ chat
     ▼                                         ▼                              ▼   │
  web/site ◄────── HLS, SignalR ──►     MovieBot.Api  ◄─────────────────── web/site
  (the player)                           │        ▲                     (Cinema)
     moviebot-acquire: tracker,  ◄───────┘        └── manifests, segments ── MovieBot.Ingest
     downloads, wishes                                                           ▲
                                                  MovieBot.Handoff ──────────────┘
                                                  (finished download → library)
```

1. **Ingest** probes a film with `ffprobe`, extracts its subtitles, and transcodes it to
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
   posts the launch. When the film is not in the library it asks the API, which finds and downloads
   it through [moviebot-acquire](https://github.com/TheKrystalShip/moviebot-acquire) and keeps a
   wish for each film people want that nothing has yet.
5. **The hand-off** watches for downloads, picks the feature file, and runs ingest while the
   download is still arriving, so the film is announced as soon as it can be played. It also
   removes a download's torrent and files once it has seeded long enough, and removes a settled film
   from the library once it has gone its time without a play. Which films are on disk is the server's
   business alone: nothing anybody sees says whether a film is here or has to be fetched, and a film
   that has left is fetched again like any other.
6. **The assistant** helps people find films and shows: one they half remember, one by someone they
   like, one that fits a mood. It is a plugin to the organization's assistant harness, which runs the
   model, the conversations and the memory; the plugin reads the catalogue and each person's
   history, wants and follows through the API, as that person, and answers with cards that open a
   title's page. It acts on nothing: watching, getting a film and following are buttons on the page.
   It speaks the same chat protocol as the KGSM assistant, so one chat client serves both.
7. **Cinema** is the site: the library in a browser, for the people the identity provider
   lets watch. It searches,
   fetches and waits on films through the API as the person signed in, carries the assistant
   in a dock, and opens the player, in a room of its own or in one a voice channel is watching.

A single transcode serves the whole room because there is only one playhead. On an NVENC GPU a
1080p source transcodes at around ten times realtime, far ahead of anyone watching it.

## Discord commands

| Command | What it does |
|---|---|
| `/watch title:<name>` | Loads a film into the room for the voice channel you are in, downloading it first if needed |
| `/show show:<name> season:<n>` | Fetches episodes of a show, a season at a time |
| `/notify add\|list\|cancel` | Wants a film that cannot be downloaded yet, and announces when it can |
| `/voice join\|status\|leave` | Listens in the voice channel for spoken commands |

See [`src/MovieBot.Bot/README.md`](src/MovieBot.Bot/README.md) for the bot, and
[`docs/voice-commands.md`](docs/voice-commands.md) for what can be said to it.

## Repository layout

```
src/MovieBot.Core/      manifest and session contracts, and their serializer
src/MovieBot.Client/    the API client the bot and the assistant share
src/MovieBot.Ingest/    the ingest CLI: probe, extract, transcode, manifest
src/MovieBot.Api/       library, media serving, sessions, SignalR hub, downloads and wishes
src/MovieBot.Assistant/ the assistant: the plugin that finds films, over the harness's chat protocol
src/MovieBot.Bot/       the Discord bot
src/MovieBot.Handoff/   moves finished downloads into the library
src/MovieBot.Catalogue/ the catalogue: every film and show, from IMDb, TMDB and Wikidata
web/site/               Cinema, the site, and its player, which is also the Discord Activity
packaging/              the release's installer, systemd units and configuration examples
deploy/                 the maintainers' host configuration and native GPU builds
docs/                   deployment guide, API contract, voice command reference
scripts/                player build, release packaging, version
tests/MovieBot.Tests/   unit and hub integration tests
```

## Requirements

- .NET 10 SDK
- Node.js and npm, for the player
- `ffmpeg` and `ffprobe` on `PATH`, and `fpcalc` (chromaprint) beside them, which finds the intro and
  recap of a show's episodes; without it a title is still made and only chapters mark what to skip
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
`--dialogue-boost` to add the dialogue mix.

Output for one film:

```
media/<id>/
  manifest.json      the film's tracks, and how far the transcode has reached
  poster.jpg         the title index's poster, when the hand-off fetched one
  s3.vtt s4.vtt …    one WebVTT file per text subtitle track, named by source stream index
  v0/index.m3u8      video: H.264 High, fMP4 segments
  a0/index.m3u8      primary audio: AAC stereo
  a1/index.m3u8 …    further audio tracks, then any dialogue-boost mix of each feature track
```

Bitmap subtitles (PGS, VobSub) cannot become WebVTT without OCR. They appear in the manifest as
`available: false` with `reason: "needs-ocr"`.

### Run the API and the player

Settings come from `~/.config/moviebot/moviebot.settings.json` (see
[Configuration](#configuration)), and anything can be overridden in the environment. For a quick
local run, override the library path and supply the secrets:

```bash
Media__Root=/absolute/path/to/media \
Auth__MediaTicketKey=<at least 32 random characters> \
Oidc__Issuer=<the identity provider's issuer> \
dotnet run --project src/MovieBot.Api -c Release
```

The API listens on `http://127.0.0.1:8099`. The site, with its player, is built into the API's
`wwwroot` with `scripts/build-player.sh`. The API reads it from disk on every request, so a new build
installed there needs no restart. It is served on its own for development:

```bash
cd web/site
npm install          # @thekrystalship/krystal comes from GitHub Packages, which wants a token
npm run dev          # http://localhost:5175, talking to the API on :8099 and the assistant on :8191
```

Everybody signs in at the identity provider: the site as its OpenID Connect client, and the Activity
through the Embedded App SDK and the API, which needs `Discord__ApplicationId`,
`Discord__ClientSecret` and `Oidc__ActivityClientSecret` for it, with the application's URL mapping
pointed at wherever the API is reachable. See [Authentication](#authentication).

### Run the bot

The bot needs a Discord application with a bot user, invited with the `bot` and
`applications.commands` scopes, and a tracker account:

```bash
dotnet user-secrets set "Discord:Token" "<token>" --project src/MovieBot.Bot

Player__BaseUrl=https://movies.example.com \
Discord__GuildIds__0=<guild id> \
Oidc__Issuer=<the identity provider's issuer> Oidc__BotClientSecret=<the bot client's secret> \
Api__ServiceKey=<the API's Auth__ServiceKey> \
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
Secrets go there rather than in the file: the bot token, the Discord and identity-provider client
secrets, the media ticket and service keys, the tracker account and its category names. Each service logs which files it read
when it starts.

The API keeps everything it knows in one SQLite database, `moviebot.db` in the state directory
systemd hands it (`Database:Path` names another): every person's records, the rooms, the wants and wishes,
and the notifications and push keys (`notifications.db`, `vapid.json`); fetched subtitles sit beside
it as files. What Cinema knows about every title is the catalogue's, which `moviebot-catalogue` keeps
in its own state directory and the API reads. It needs nothing installed; the native SQLite ships with
the binary.

## API

| Route | Purpose |
|---|---|
| `GET /api/titles` | the library |
| `GET /api/titles/{id}` | one manifest, including the live transcode head |
| `GET /api/sessions/{id}` | a room's current shared state |
| `GET,HEAD /media/{id}/**` | playlists, segments, subtitles, poster |
| `/hub/session` | SignalR: `Join`, `LoadTitle`, `Play`, `Pause`, `Seek` |
| `GET /.well-known/tks-actions.json` | the Cinema actions each route checks, for the identity provider |

Every state push carries the server clock and a monotonic revision, so a client discards anything
it has already seen. Playlists for a title still transcoding are served `no-store`; segments never
change once written and are cached as immutable.

The full wire contract, including the manifest schema and the rules a client must follow, is in
[`docs/api-contract.md`](docs/api-contract.md).

## Authentication

MovieBot signs nobody in. It is a client and a resource server of an OpenID Connect provider,
tks-auth or any other that mints ES256 access tokens
for the audience `cinema` listing the person's Cinema actions in a claim. Everything except sign-in,
the pages, the configuration, cover art and the action manifest requires such a token, and each route
checks the action it needs: `cinema:watch`, `cinema:download` or `cinema:wish`.

- `Oidc:Issuer` names the provider. The API records it the first time it starts and refuses to start
  against another until `Oidc:AdoptIssuer` names the new one, since every record is kept by the
  provider's account ids.
- The site is a public client (`Oidc:SiteClientId`) with the redirect `<origin>/signed-in`, and on a
  television a public client allowed the device authorization grant (`Oidc:TvClientId`), which signs
  out by revoking its refresh token.
- The Activity and the bot are confidential clients that exchange a Discord identity for a person's
  token at the provider: `Oidc:ActivityClientId` with `Oidc:ActivityClientSecret`, and
  `Oidc:BotClientId` with `Oidc:BotClientSecret`.
- `Auth:MediaTicketKey` signs the tickets a film's bytes are opened with. The API refuses to start
  without it.
- `Auth:ServiceKey` is shared with the bot and the hand-off, for machine work that names nobody.

[`docs/deploying.md`](docs/deploying.md) has the registration the provider needs.

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
- [`web/site/README.md`](web/site/README.md): Cinema, the site, and its player
- [`src/MovieBot.Bot/README.md`](src/MovieBot.Bot/README.md): the Discord bot and its configuration
- [`docs/voice-commands.md`](docs/voice-commands.md): what can be said to the bot
- [`CHANGELOG.md`](CHANGELOG.md): release history

## License

[GPL-3.0-or-later](LICENSE). This repository contains code only and ships no media: MovieBot plays
films you already have.
