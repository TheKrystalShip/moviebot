# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

MovieBot plays films you already own to everyone in a Discord voice channel, on one shared timeline,
inside a Discord Activity; the same player is the site, Krystal Cinema. It stands apart from the KGSM
ecosystem it sits beside in this workspace and follows its own conventions throughout. How it meets the
shared systems (identity, assistant harness, speech, design system, build and publish):
`../kgsm-docs/systems/moviebot/README.md`; its place in the workspace:
`../kgsm-docs/components/moviebot.md`.

It runs as four services: `moviebot-api`, `moviebot-bot`, `moviebot-handoff` and `moviebot-assistant`.

**It builds against the checkout beside it.** `MovieBot.Api`, `MovieBot.Bot`, `MovieBot.Handoff` and
`MovieBot.Assistant` take a `ProjectReference` on `../../../moviebot-acquire/src/MovieBot.Acquire`, so
`moviebot-acquire` has to be checked out as a sibling directory for anything here to compile, and a
change to a public type over there breaks the build here in the same pass
(`../kgsm-docs/systems/moviebot/build-and-deploy.md`). `moviebot-acquire` owns torrents, the tracker,
the title index, the disk budget, retention and the tag vocabulary; this repository owns what happens to
a file once it exists — probing it, transcoding it, serving it, and keeping a room in step.

## Where the docs are

- `README.md`: what MovieBot is, how it works, requirements, configuration, authentication.
- `docs/api-contract.md`: what `MovieBot.Api` serves on the wire.
- `web/site/README.md`: Krystal Cinema, the site, and its player (which is also the Discord Activity).
- `src/MovieBot.Bot/README.md`: the Discord surface. `docs/voice-commands.md`: what can be said.
- `docs/deploying.md`: a newcomer's install. `tv/README.md`: the Android TV and Fire TV app, Java built
  with the SDK's own tools, on a version clock of its own tagged `tv-v<version>`.
- **The rules each part rests on live in a `CLAUDE.md` beside its code**: `deploy/`,
  `src/MovieBot.Api/` (with `Sessions/`, `Subtitles/`, `Acquisition/`, `Transcode/`, `Offline/` and
  `Notifications/`), `src/MovieBot.Core/` (the two disks), `src/MovieBot.Ingest/`,
  `src/MovieBot.Handoff/`, `src/MovieBot.Assistant/`, `src/MovieBot.Bot/` (and each feature directory
  under it), `web/site/src/player/` and `web/site/src/offline/`.

Sign-in, the assistant and the design system are shared systems: read
`../kgsm-docs/systems/moviebot/sign-in.md`, `../kgsm-docs/systems/moviebot/assistant.md` and
`../kgsm-docs/systems/design-system/README.md` before changing where they meet this code.

## Rules that hold everywhere

- **MovieBot signs nobody in.** It validates the provider's token and checks the action a route needs. A
  record is keyed by the provider's `sub` and nothing else; the service key is machine work that names
  nobody; a room's guest link names nobody and no record is keyed by a guest.
- **The Krystal Cinema design is the UI, and the API is the authority for its data.** A feature the
  design draws that the API lacks is added to the API, never drawn from a fixture and never left out for
  want of it.
- **Everyone watches the same position in the same film.** One transcode serves the whole room; the
  server is authoritative and there is no host: clients send intent, the API decides, and every control
  in the player moves the whole room.
- **Shared state is small**: title, paused, position at an anchor time, rate, who changed it, revision,
  transcode heads. Volume, subtitle choice and drawing, audio track and quality are per-viewer
  preferences and never go into shared state; a viewer's quality is reported to the server alone, for its
  transcode planning, and never broadcast.

## Commands

```bash
dotnet build moviebot.slnx -c Release
dotnet test                        # hub integration tests, two real SignalR clients
Media__Root=/absolute/path/to/media dotnet run --project src/MovieBot.Api -c Release
```

`media/` is generated and gitignored, and nothing under it is ever committed. It is a scratch library
for working here, distinct from the library a deployed API serves, so an ingest run in this checkout
changes nothing anybody is watching.

## Deploying

**Read `deploy/CLAUDE.md` before any deploy**; it holds the publish commands, the units and the host's
settings.

## Releases

A release is for somebody else's host. `packaging/` is the kit it carries (`install.sh`, generic
units, the env file naming every setting, nginx and qBittorrent examples) and `docs/deploying.md` is
the guide a newcomer follows; `deploy/` is the maintainers' own host configuration. **A change to what a
service needs to start — a new required setting, a new path, a new device — lands in both**, and in the
guide. Pushing a tag `v<version>` that matches the newest CHANGELOG heading (`scripts/version.sh`)
runs `.github/workflows/release.yml`, which builds on Ubuntu 22.04 because the native binaries take
the build machine's glibc as their floor. `scripts/package-release.sh` builds the same archive here.
Before a version is tagged, the site passes `npm run verify:tv-perf` in `web/site` against its
baseline (`web/site/verify/tvperf/README.md`).

## Conventions

- **Every setting is declared in `src/moviebot.settings.json`**, with its default. It is one file for
  every MovieBot program, shipped beside each binary; a host's copy in its XDG configuration
  directory overrides it, and the environment overrides both. Secrets and the tracker's identity are
  never in it, only in the environment. `MovieBotSettings` in moviebot-acquire is the lookup.
- C# namespaces are `TheKrystalShip.*`, matching the GitHub org this publishes to.
- Present-tense canon in every doc and comment: describe how the thing works now. History belongs in
  the CHANGELOG and in commit messages, never in prose or code comments.
- No emoji anywhere — not in docs, comments, commit messages or CLI output.
- Commit per finished piece of work, including the version bump and CHANGELOG entry, and tag the bump
  `v<version>`.
- **A change that spans both repositories is one commit in each**, describing that repository's half.
  Address git with `git -C <repo>`: the two checkouts sit side by side, and a `cd` applies to every
  command after it in the same shell, so a commit or a tag meant for one lands in the other.
