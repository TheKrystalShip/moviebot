# Deploying MovieBot

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
ssh hotbox 'mkdir -p ~/.config/moviebot'
rsync deploy/hotbox.settings.json hotbox:.config/moviebot/moviebot.settings.json
ssh hotbox 'sudo systemctl restart moviebot-api moviebot-bot moviebot-handoff'
```

- **Settings are two files and the environment.** Every service reads `moviebot.settings.json`
  twice: the copy of `src/moviebot.settings.json` published beside its binary holds the defaults,
  and `/home/heisen/.config/moviebot/moviebot.settings.json` (`deploy/hotbox.settings.json`, which
  holds only what hotbox changes) overrides them key by key. That second file is `heisen`'s XDG
  configuration file, so writing it takes no privilege, and a service picks a change up on its next
  restart. Each service logs `Settings:` with both paths when it starts.
- **Speech and the model are native builds of their own, made on hotrod for hotbox's CPU.**
  `vulkan/build.sh` builds whisper.cpp and llama.cpp against Vulkan with every CPU feature hotbox
  lacks switched off, and `vulkan/install.sh` puts them under `/opt/moviebot`; its README is the
  authority for why a prebuilt runtime cannot be used there. `moviebot-speech` is the .NET host over
  that whisper build and publishes like the other services.
- **Listening needs three system libraries on hotbox**: `opus`, `libsodium`, and `libdave`, which is
  Discord's end-to-end voice encryption and is packaged in tks-agent. Without libdave the bot runs
  and every voice connection is refused.
- **The API and the hand-off publish as native binaries.** `PublishAot` in each project, so the
  publish above runs the ahead-of-time compiler, needs `clang`, and takes a minute or two longer than
  a JIT publish. What lands is one executable beside `moviebot.settings.json` and, for the API, `wwwroot`,
  with nothing left to compile at run time. `--delete` on the rsync is what keeps a native publish
  from leaving old assemblies standing beside the new binary. The bot stays on the JIT, with tiering
  off: Discord.Net is built on reflection.
- **A binary says which commit it came from, and which `moviebot-acquire`.** The SDK stamps every
  assembly's informational version with its repository's HEAD, so one native executable carries the
  stamps of both checkouts that went into it. Since this repository compiles against the acquire
  sibling by path and is pinned to no release of it, that is the only record of which acquire is
  inside a given build:

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
  arrives only once the API is published after that script has run. One origin serves the page, the
  API, the media and the hub, because a Discord Activity maps one URL.
- **The units in `/etc/systemd/system/` are root-owned copies of `*.service` here, and every
  `systemctl` verb against them needs root.** There is no polkit grant on hotbox, so starting,
  stopping and restarting are as privileged as installing a changed unit: `systemctl restart` as the
  owning user is refused with *"interactive authentication required"* and nothing happens.
  Publishing the binaries is unprivileged and the restart that picks them up is not, so a deploy ends
  by saying what changed and handing over the command rather than working around the privilege.
- **`/etc/moviebot/moviebot.env` holds the credentials** all three units read, **and which tracker
  this is**: `Tracker__BaseUrl`, `Tracker__Username`, `Tracker__Passkey` and the tracker's category
  names as `Selection__AllowedCategories__0`, `__1` and so on. The bot and the hand-off refuse to
  start without the categories. Any other configuration that is not a credential belongs in
  `deploy/hotbox.settings.json`, where it is in the repository and reviewable; the tracker's
  identity is kept out of the repository the same way the passkey is. A unit's own `Environment=`
  lines are for the process, not for MovieBot: `PATH` and the driver's shader cache.
- **The state directories are the two things no re-ingest can rebuild.** systemd hands the API
  `/var/lib/moviebot` — the rooms and the subtitles fetched from outside, each of which cost one of a
  limited daily allowance — and the bot `/var/lib/moviebot-bot`, holding the wish list and the launch
  cards still standing.
- **The cold disk is named in the settings file and proved by a marker file.** `Media.ColdRoot` and
  `Handoff.ColdRoot` point at the volume finished films are kept on, and it is used only while a
  `.moviebot-cold` file sits at its root. That file is made once per host, as the owning user, on the
  mounted volume: `touch <cold root>/.moviebot-cold`. Without it both services run on the media root
  alone and say so.
- **nginx serves `movies.thekrystalship.com` in two halves, on two machines.** hotbox has no public
  address, so hotrod holds the name and routes it across the LAN by `server_name`, over an https hop
  made under hotbox's own name: the certificate on the far side is chosen by that SNI while the server
  block is chosen by the `Host` header, which travels through unchanged. hotbox's vhost terminates
  that hop and proxies to `127.0.0.1:8099` with `/hub/` upgraded and buffering off.
  `nginx-moviebot-ingress.conf` and `nginx-moviebot.conf` are the repository's copies, and they are
  deployed to different hosts.

  The consequence is worth stating plainly: **hotrod's nginx is in the path for every byte of every
  film.** The services, the GPU, the media and the state are all on hotbox, but the ingress is not,
  because there is one public address on this network and it is spoken for. It also holds the
  segments, so a synchronised room costs one copy across the switch. That cache is why a film's bytes
  are opened by a ticket in the URL and never by an `Authorization` header: the header is not part of
  the cache key, so a response kept from a request carrying one would be handed to the next request
  for that URL whether or not it proved anything. Segments have their own location because caching
  needs buffering on and a growing playlist needs it off.
- **A 502 in the second after an nginx reload is usually the old worker.** A reload lets the workers
  already running drain rather than killing them, so both configurations answer for a moment. The
  error log names the upstream it tried, which is what tells a stale worker apart from a real
  failure.
