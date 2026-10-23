# Deploying MovieBot

Four units run on **hotbox**, under `heisen`, out of `/opt/moviebot`: `moviebot-api`,
`moviebot-bot`, `moviebot-handoff` and `moviebot-assistant`, this repository's .NET services.
`moviebot-assistant` is a plugin to the organization's assistant harness on hotrod, which runs the
model and holds the conversations; it presents the key issued for `moviebot` there. All of it is built
on hotrod, which holds the checkouts, the .NET SDK, `clang` and node; hotbox carries the .NET runtime
and nothing else of the toolchain (`../../kgsm-docs/systems/moviebot/build-and-deploy.md`). A deploy is
one script run from hotrod, with no arguments and nothing to set:

```bash
deploy/deploy.sh            # everything: the services, then the site
deploy/deploy-services.sh   # the four services: publish, rsync to /opt/moviebot, settings, restart
deploy/deploy-site.sh       # the site and its player alone, into /opt/moviebot/api/wwwroot; restarts nothing
```

Each script holds the host, the paths and the units itself and checks its own result: the services
script waits for the API's `/health` after the restart, and the site script checks that the API serves
the index it just installed.

`moviebot-assistant` is reached by the site at `/assistant/` on the vhost, which strips the prefix
(`nginx-moviebot.conf`).

The phone app (`phone/README.md`) and the television app (`tv/README.md`) deploy on their own, each
built as a release here and installed in the API's state directory, where `/phone.apk` and `/tv.apk`
serve it and no deploy of the services touches it:

```bash
deploy/deploy-app.sh            # both
deploy/deploy-app.sh phone      # one: phone or tv
```

- **Settings are two files and the environment.** Every service reads `moviebot.settings.json`
  twice: the copy of `src/moviebot.settings.json` published beside its binary holds the defaults,
  and `~/.config/moviebot/moviebot.settings.json` (`deploy/hotbox.settings.json`, which
  holds only what hotbox changes) overrides them key by key. That second file is `heisen`'s XDG
  configuration file, so writing it takes no privilege, and a service picks a change up on its next
  restart. Each service logs `Settings:` with both paths when it starts.
- **The assistant reaches its harness with a key of its own.** `Assistant__HarnessAddress` and
  `Assistant__PluginKey` are in `/etc/moviebot/assistant.env` (root-owned, group `heisen`, 0640), which
  the unit reads beside `moviebot.env`. The harness's firewall admits hotbox alone, and the plugin
  registers its profile again whenever the harness restarts.
- **Listening needs three system libraries on hotbox**: `opus`, `libsodium`, and `libdave`, which is
  Discord's end-to-end voice encryption and is packaged in tks-assistant. Without libdave the bot runs
  and every voice connection is refused.
- **Finding an episode's intro and recap needs `chromaprint`** (`fpcalc` in `/usr/bin`, on the
  hand-off unit's `PATH` after the card's ffmpeg). Without it an episode gets only the markers its
  chapters name.
- **The API and the hand-off publish as native binaries** (`PublishAot`): the publish needs `clang`
  and takes a minute or two longer than a JIT publish, and `--delete` on the rsync is what keeps it
  from leaving old assemblies beside the new binary. The bot stays on the JIT, with tiering off:
  Discord.Net is built on reflection.
- **A binary says which commit it came from, and which `moviebot-acquire`.** The SDK stamps every
  assembly's informational version with its repository's HEAD, so a native executable carries the
  stamps of both checkouts that went into it, the only record of which acquire is inside a build:

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
- **The site and its player reach people through the API's web root, deployed on their own.** Krystal
  Cinema (`web/site`, built by `scripts/build-site.sh`) is installed into `/opt/moviebot/api/wwwroot`.
  The API reads it from disk on every request, so a site deploy reaches people at once without
  restarting anything. The services deploy copies the API with `wwwroot` excluded, so it neither ships
  a site nor deletes the one installed; the API serves `wwwroot` only if it exists when it starts. Its hashed assets land before the index that names them, and an earlier build's assets stay
  a fortnight for a page still open on one. One origin serves the site, the player, the API, the media
  and the hub, because a Discord Activity maps one URL; Discord launches the Activity at the root, and
  the site's index takes that load to `/embed/`.
- **People sign in at tks-auth, which knows Cinema as an application** (the model:
  `../../kgsm-docs/systems/moviebot/sign-in.md`). The issuer is `https://auth.thekrystalship.com`
  (`Oidc.Issuer` in `deploy/hotbox.settings.json`); Cinema is registered there with the id and audience
  `cinema` and its manifest at `https://movies.thekrystalship.com/.well-known/tks-actions.json`, and
  its clients are `cinema-site`, `cinema-tv`, `cinema-activity` and `cinema-bot`. The two confidential
  clients' secrets are `Oidc__ActivityClientSecret` and `Oidc__BotClientSecret` in the environment
  file.
- **The Activity's code is redeemed with Cinema's Discord application**, whose secret is
  `Discord__ClientSecret` in the environment file; an Activity never redirects, so the application
  needs no redirect registered for it.
- **The units in `/etc/systemd/system/` are root-owned copies of `*.service` here, and every
  `systemctl` verb against them needs root.** There is no polkit grant on hotbox, so starting,
  stopping and restarting are as privileged as installing a changed unit: `systemctl restart` as the
  owning user is refused with *"interactive authentication required"* and nothing happens, so the
  restart goes through `rsudo hotbox systemctl restart …` from hotrod.
- **A deploy restarts the services whether or not anybody is watching.** A room is kept and
  comes back as it was, the player reconnects its hub on its own, and the video it has buffered keeps
  playing through the gap, so nobody in a room notices the restart. `/health` counting occupied rooms
  is no reason to hold a deploy back.
- **The hand-off is restarted only while it is making nothing.** A restart starts every transcode it
  is running again from the beginning, which rewrites the playlists of a film people are watching
  under them. While an `ffmpeg` or `fpcalc` runs in its unit's cgroup, `deploy-services.sh` restarts
  the other three and leaves a transient unit on hotbox, `moviebot-handoff-restart`, that restarts the
  hand-off once the last has ended; the new binary is already in place meanwhile. A later deploy
  replaces a restart still waiting. Restarting it by hand while it transcodes costs the same.
- **`/etc/moviebot/moviebot.env` holds the credentials** every unit reads — the bot's token, the
  Discord and identity-provider client secrets, `Auth__MediaTicketKey` that signs media tickets and
  the service key the hand-off and the bot's background passes present — **and which tracker
  this is**: `Tracker__BaseUrl`, `Tracker__Username`, `Tracker__Passkey` and the tracker's category
  names as `Selection__AllowedCategories__0`, `__1` and so on, with the show categories as
  `Selection__AllowedShowCategories__N`. The API, the bot and the hand-off refuse to start without
  the film categories; without the show ones no show is offered. Any other configuration that is not a credential belongs in
  `deploy/hotbox.settings.json`, where it is in the repository and reviewable; the tracker's
  identity is kept out of the repository the same way the passkey is. A unit's own `Environment=`
  lines are for the process, not for MovieBot: `PATH`.
- **The state directories are what no re-ingest can rebuild.** systemd hands the API
  `/var/lib/moviebot`: `moviebot.db`, which holds every person's records, the rooms and the wish list;
  `notifications.db`, every person's notifications and push subscriptions; `vapid.json`, the push key pair
  every browser's subscription is bound to (it cannot be made again without every device subscribing
  again); and the subtitles fetched from outside, each of which cost one of
  a limited daily allowance. The artwork beside them is the film database's and is fetched again if
  lost. The bot has `/var/lib/moviebot-bot`, holding the launch cards still standing. The assistant keeps no state of its own: the conversations and what people
  asked it to remember are the harness's.
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
  is one `sid` in `cmcd_session`, the playback a player's trouble report in the API's journal names
  (`journalctl -u moviebot-api | grep "player in"`, `src/MovieBot.Api/Sessions/PlayerTrouble.cs`), so a
  person's report leads to every request that playback made:

  ```bash
  jq -c 'select(.cmcd_session | contains("<sid>"))
         | {time, uri, request_time, upstream_response, rtt_us, rttvar_us, cmcd_status, cmcd_request}' \
     /var/log/nginx/moviebot-delivery.log
  ```

  A slow request with a long `request_time` and a short `upstream_response` was slow on the route; a
  long `upstream_response` was slow on hotbox. `bs` in `cmcd_status` marks the request after a stall.
- **What went wrong where nobody can see it is in the journals, one line each.** A player says what
  went wrong with its film (a fatal error, a stuck playhead, a device store that did not answer, a film
  that never started) in the API's journal, at most six lines a minute per connection. The API says when
  a transcode somebody is watching began again from nothing (`Library/TitleChanges`), and the hand-off
  says so as it deletes what was already written (`starting again from nothing`).
- **A 502 in the second after an nginx reload is usually the old worker.** A reload lets the workers
  already running drain rather than killing them, so both configurations answer for a moment. The
  error log names the upstream it tried, which is what tells a stale worker apart from a real
  failure.
