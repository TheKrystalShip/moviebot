# Deploying MovieBot

Four units run on **hotbox**, under `heisen`, out of `/opt/moviebot`: `moviebot-api`,
`moviebot-bot`, `moviebot-handoff` and `moviebot-assistant`, this repository's .NET services. The
assistant's model is hotrod's shared model server (`Llm` in `hotbox.settings.json`, its key in the
environment file). `moviebot-llm`, which holds a language model on hotbox's own card for room
turns, is installed and stopped, because the bot relays none (`Assistant.Enabled` off); it starts
again with that switch. All of it is built on hotrod, which is the machine holding the checkouts, the .NET SDK, `clang`
and node; hotbox carries the .NET runtime and nothing else of the toolchain. So a deploy of the
services is a publish here, an rsync there, and a restart:

```bash
./scripts/build-player.sh                                    # site and player -> the API's wwwroot
dotnet publish src/MovieBot.Api       -c Release -o /tmp/mb/api
dotnet publish src/MovieBot.Bot       -c Release -o /tmp/mb/bot
dotnet publish src/MovieBot.Handoff   -c Release -o /tmp/mb/handoff
dotnet publish src/MovieBot.Assistant -c Release -r linux-x64 --self-contained false -o /tmp/mb/assistant

rsync -a --delete /tmp/mb/api/       hotbox:/opt/moviebot/api/
rsync -a --delete /tmp/mb/bot/       hotbox:/opt/moviebot/bot/
rsync -a --delete /tmp/mb/handoff/   hotbox:/opt/moviebot/handoff/
rsync -a --delete /tmp/mb/assistant/ hotbox:/opt/moviebot/assistant/
ssh hotbox 'mkdir -p ~/.config/moviebot'
rsync deploy/hotbox.settings.json hotbox:.config/moviebot/moviebot.settings.json
ssh hotbox 'sudo systemctl restart moviebot-api moviebot-bot moviebot-handoff moviebot-assistant'
```

`moviebot-assistant` is reached by the bot on loopback and by the site at `/assistant/` on the vhost,
which strips the prefix (`nginx-moviebot.conf`).

The television app (`tv/README.md`) is built here and handed to hotbox on its own, into the API's
state directory, where `/tv.apk` serves it and no deploy of the services touches it:

```bash
tv/build.sh
rsync tv/out/krystal-cinema-tv-<version>.apk hotbox:/var/lib/moviebot/tv.apk
```

- **Settings are two files and the environment.** Every service reads `moviebot.settings.json`
  twice: the copy of `src/moviebot.settings.json` published beside its binary holds the defaults,
  and `/home/heisen/.config/moviebot/moviebot.settings.json` (`deploy/hotbox.settings.json`, which
  holds only what hotbox changes) overrides them key by key. That second file is `heisen`'s XDG
  configuration file, so writing it takes no privilege, and a service picks a change up on its next
  restart. Each service logs `Settings:` with both paths when it starts.
- **The model is a native build of its own, made on hotrod for hotbox's CPU.**
  `vulkan/build.sh` builds llama.cpp against Vulkan with every CPU feature hotbox lacks switched
  off, and `vulkan/install.sh` puts it under `/opt/moviebot`; its README is the authority for why a
  prebuilt runtime cannot be used there.
- **Listening needs three system libraries on hotbox**: `opus`, `libsodium`, and `libdave`, which is
  Discord's end-to-end voice encryption and is packaged in tks-assistant. Without libdave the bot runs
  and every voice connection is refused.
- **Finding an episode's intro and recap needs `chromaprint`** (`fpcalc` in `/usr/bin`, on the
  hand-off unit's `PATH` after the card's ffmpeg). Without it an episode gets only the markers its
  chapters name.
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
- **The site and its player reach people through the API.** `scripts/build-player.sh` installs
  Krystal Cinema (`web/site`) at the root of `src/MovieBot.Api/wwwroot`, and the API is what serves
  it, so a change arrives only once the API is published after that script has run. One origin serves
  the site, the player, the API, the media and the hub, because a Discord Activity maps one URL;
  Discord launches the Activity at the root, and the site's index takes that load to `/embed/`.
- **People sign in at tks-auth, which knows Cinema as an application.** The issuer is
  `https://auth.thekrystalship.com` (`Oidc.Issuer` in `deploy/hotbox.settings.json`), and Cinema is
  registered there with the id and audience `cinema` and its manifest at
  `https://movies.thekrystalship.com/.well-known/tks-actions.json`; its four clients are the site
  (`cinema-site`, public, redirect `https://movies.thekrystalship.com/signin`), a television
  (`cinema-tv`, public, the device grant), the API's exchange for
  the Activity (`cinema-activity`, confidential, presenting Cinema's Discord application) and the bot
  (`cinema-bot`, confidential, acting for linked Discord users). The two client secrets are
  `Oidc__ActivityClientSecret` and `Oidc__BotClientSecret` in the environment file. The API records
  the issuer in `/var/lib/moviebot/issuer` the first time it starts and refuses another until
  `Oidc.AdoptIssuer` names it.
- **The Activity's code is redeemed with Cinema's Discord application**, whose secret is
  `Discord__ClientSecret` in the environment file; an Activity never redirects, so the application
  needs no redirect registered for it.
- **The units in `/etc/systemd/system/` are root-owned copies of `*.service` here, and every
  `systemctl` verb against them needs root.** There is no polkit grant on hotbox, so starting,
  stopping and restarting are as privileged as installing a changed unit: `systemctl restart` as the
  owning user is refused with *"interactive authentication required"* and nothing happens.
  Publishing the binaries is unprivileged and the restart that picks them up is not, so a deploy ends
  by saying what changed and handing over the command rather than working around the privilege.
- **`/etc/moviebot/moviebot.env` holds the credentials** every unit reads — the bot's token, the
  Discord and identity-provider client secrets, `Auth__MediaTicketKey` that signs media tickets and
  the service key the hand-off and the bot's background passes present — **and which tracker
  this is**: `Tracker__BaseUrl`, `Tracker__Username`, `Tracker__Passkey` and the tracker's category
  names as `Selection__AllowedCategories__0`, `__1` and so on, with the show categories as
  `Selection__AllowedShowCategories__N`. The API, the bot and the hand-off refuse to start without
  the film categories; without the show ones no show is offered. Any other configuration that is not a credential belongs in
  `deploy/hotbox.settings.json`, where it is in the repository and reviewable; the tracker's
  identity is kept out of the repository the same way the passkey is. A unit's own `Environment=`
  lines are for the process, not for MovieBot: `PATH` and the driver's shader cache.
- **The state directories are what no re-ingest can rebuild.** systemd hands the API
  `/var/lib/moviebot` — the rooms, the wish list, the subtitles fetched from outside, each of
  which cost one of a limited daily allowance, and `push-keys.json`, the key pair every browser's push
  subscription is bound to, which cannot be made again without every device subscribing again —
  the bot `/var/lib/moviebot-bot`, holding the launch
  cards still standing, and the assistant `/var/lib/moviebot-assistant`,
  holding in `assistant.db` the conversations, what people asked it to remember, and its own
  notification key pair with the browsers registered against it, and beside that the warm-up request
  `moviebot-llm` replays when it is running and starts.
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
- **The ingress keeps a delivery log, `/var/log/nginx/moviebot-delivery.log` on hotrod**, one JSON
  line per request: the kernel's round-trip time and its variance on the viewer's connection, the
  time the whole answer took and how much of it was hotbox's, bytes sent, the cache's answer, and the
  player's `CMCD-*` headers. Because hotrod faces the internet, the round trip is a measurement of the
  route to the viewer, taken all the time, so a complaint about a slow evening can be checked against
  that evening. The address is kept as its /24 or /48 and nothing else about the viewer is. A viewing
  is one `sid` in `cmcd_session`:

  ```bash
  jq -c 'select(.cmcd_session | contains("<sid>"))
         | {time, uri, request_time, upstream_response, rtt_us, rttvar_us, cmcd_status, cmcd_request}' \
     /var/log/nginx/moviebot-delivery.log
  ```

  A slow request with a long `request_time` and a short `upstream_response` was slow on the route; a
  long `upstream_response` was slow on hotbox. `bs` in `cmcd_status` marks the request after a stall.
- **A 502 in the second after an nginx reload is usually the old worker.** A reload lets the workers
  already running drain rather than killing them, so both configurations answer for a moment. The
  error log names the upstream it tried, which is what tells a stale worker apart from a real
  failure.
