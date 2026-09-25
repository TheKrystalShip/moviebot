# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

MovieBot plays films you already own to everyone in a Discord voice channel, on one shared timeline,
inside a Discord Activity. It stands apart from the KGSM ecosystem it sits beside in this workspace,
and follows its own conventions throughout.

**It builds against the checkout beside it.** `MovieBot.Api`, `MovieBot.Bot` and `MovieBot.Handoff`
each take a `ProjectReference` on `../../../moviebot-acquire/src/MovieBot.Acquire`, so
`moviebot-acquire` has to be checked out as a sibling directory for anything here to compile. The
reference is by path, which cuts both ways: a change to a public type over there breaks the build here
in the same pass, and a release of that repo is nothing this one is pinned to. `moviebot.slnx` lists
this repository's own projects, and they compile against one it does not name.

`moviebot-acquire` is the acquiring half of one pipeline: torrents, the tracker, the title index, the
disk budget, retention and the tag vocabulary all live there and are called from here. This repository
owns what happens to a file once it exists — probing it, transcoding it, serving it, and keeping a
room in step.

Three docs beside this one, each the authority for its own half: `docs/api-contract.md` for what
`MovieBot.Api` serves on the wire, `web/activity/README.md` for the player, and
`src/MovieBot.Bot/README.md` for the Discord surface. **The rules each part rests on live in a
`CLAUDE.md` beside its code**: `deploy/`, `src/MovieBot.Api/` (with `Sessions/` and `Subtitles/`),
`src/MovieBot.Core/` (the two disks), `src/MovieBot.Ingest/`, `src/MovieBot.Handoff/`,
`src/MovieBot.Bot/` (and each of its feature directories), and `web/activity/`.

## The one idea everything follows from

**Everyone watches the same position in the same film.** That is not a feature, it is the premise,
and most of the design falls out of it:

- One transcode serves the whole room, because there is only ever one playhead. Plex and Jellyfin open
  a session per viewer; this does not.
- A single transcode head has to outrun a single playhead, and it outruns it tenfold — which is what
  makes playback-while-transcoding practical rather than fiddly.
- **Shared state is small**: title, paused, position at an anchor time, rate, who changed it,
  revision, transcode head. Volume, subtitle choice, how subtitles are drawn, audio track and quality
  are per-viewer preferences and never go on the wire. Shared volume is a way for one person to deafen
  everyone else, and one person needing subtitles, at the size they read at, should not put them on
  five other screens.
- **The server is authoritative and there is no host.** Clients send intent; the API decides. Every
  control in the player moves the whole room.

## Commands

```bash
dotnet build moviebot.slnx -c Release
dotnet test                        # hub integration tests, two real SignalR clients
Media__Root=/absolute/path/to/media dotnet run --project src/MovieBot.Api -c Release
```

`media/` is generated and gitignored, and nothing under it is ever committed. It is a scratch library
for working here: the running API serves hotbox's `/home/heisen/moviebot/media`, which is a different
directory on a different machine, so an ingest run in this checkout changes nothing anybody is
watching. Ingesting into the library people open means running it on hotbox, against that root.

## Deploying

Everything runs on **hotbox** and is built here; **read `deploy/CLAUDE.md` before any deploy.** The
units are root-owned and hotbox has no polkit grant, so a deploy publishes and rsyncs unprivileged and
ends by handing over the `sudo systemctl restart` command rather than working around the privilege.

## Releases

A release is for somebody else's host. `packaging/` is the kit it carries (`install.sh`, generic
units, the env file naming every setting, nginx and qBittorrent examples) and `docs/deploying.md` is
the guide a newcomer follows; `deploy/` is hotbox's own configuration. **A change to what a service
needs to start — a new required setting, a new path, a new device — lands in both**, and in the
guide. Pushing a tag `v<version>` that matches the newest CHANGELOG heading (`scripts/version.sh`)
runs `.github/workflows/release.yml`, which builds on Ubuntu 22.04 because the native binaries take
the build machine's glibc as their floor. `scripts/package-release.sh` builds the same archive here.

## Conventions

- C# namespaces are `TheKrystalShip.*`, matching the GitHub org this publishes to.
- Present-tense canon in every doc and comment: describe how the thing works now. History belongs in
  the CHANGELOG and in commit messages, never in prose or code comments.
- No emoji anywhere — not in docs, comments, commit messages or CLI output.
- Commit per finished piece of work, including the version bump and CHANGELOG entry, and tag the bump
  `v<version>`.
- **A change that spans both repositories is one commit in each**, describing that repository's half.
  Address git with `git -C <repo>`: the two checkouts sit side by side, and a `cd` applies to every
  command after it in the same shell, so a commit or a tag meant for one lands in the other.
