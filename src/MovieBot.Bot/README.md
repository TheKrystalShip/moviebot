# MovieBot.Bot

The Discord half. `/watch` resolves a film, opens the room's session on the API and hands the
room a launch. `/find` searches the tracker and starts a download. `/notify` watches for a film
that cannot be downloaded yet and says so when it can.

## It holds almost no state

Nothing about a room or a download is remembered between commands. The library is read from the
API each time somebody asks, and the session is named by the voice channel the requester is
standing in, so the same channel always reaches the same room: a restarted bot lands the next
request in the session that is already playing, and there is no table of channels to sessions
that could disagree with reality. A download's own notes live on the torrent.

Everything in a reply was measured while the command ran. The bot never opens a manifest, never
caches a title and never claims a state it did not read.

The one exception is the wish list: the films people are waiting on and who asked. A film that is
not on the tracker yet has no torrent to tag and no manifest to write, so a wish for it exists
nowhere unless the bot writes it down. It is one JSON file in the state directory systemd hands
the service, written whole and moved into place on every change.

## Configuration

| Variable | Required | What it is |
|---|---|---|
| `MOVIEBOT_TOKEN` | yes | The bot token. A credential: it comes from the host's environment or from user-secrets, and never from a file under the repository. |
| `MOVIEBOT_CLIENTID` | no | The application id, which is also its OAuth2 client id. It names the application in the invite the bot writes to the log at startup. |
| `Discord__GuildIds__0` | yes | The server the bot serves. One index per server. Commands are registered per guild and an interaction from anywhere else is refused. |
| `Player__BaseUrl` | yes | Where the player is served. The launch link is this address plus its parameters. |
| `Api__BaseUrl` | no | Where the bot reaches the API. Defaults to `http://127.0.0.1:8099`. |
| `Api__PublicBaseUrl` | no | Where Discord's servers reach the API. Only the poster needs it, and without it the embed carries no image rather than a broken one. |
| `Notify__Path` | no | Where the wish list is written. Defaults to `wishes.json` in the directory `STATE_DIRECTORY` names, and to the working directory when there is none. |
| `Notify__SweepMinutes` | no | How often the tracker is asked about every film on the list. Defaults to 60. |
| `Notify__MinimumSource` | no | The least a release's source may be for a film to count as available: `Web` by default, so a camcorder recording of a film in cinemas does not announce it. |

The two credentials are read under their own names and sit underneath every other configuration
source, so `Discord:Token` and `Discord:ApplicationId` from user-secrets or from the environment
override them: a developer who cannot read the host's environment still has a way in.

Every setting is checked at startup, and a missing or malformed value names itself and the
variable to set. A token Discord rejects is reported once as an error; the gateway then retries
on its own, so the bot stays up and keeps saying so.

```bash
dotnet user-secrets set "Discord:Token" "<token>" --project src/MovieBot.Bot

Player__BaseUrl=https://movies.example.com \
Discord__GuildIds__0=<guild id> \
dotnet run --project src/MovieBot.Bot -c Release
```

`appsettings.json` in the output directory carries the defaults, and the host reads it from the
directory it runs in.

## Running it as a service

**systemd does not read `/etc/environment`.** A unit needs the credentials named explicitly, or
it starts with no token and fails in the shape of a Discord outage:

```ini
[Service]
EnvironmentFile=/etc/environment
Environment=Player__BaseUrl=https://movies.example.com
Environment=Discord__GuildIds__0=<guild id>
```

## What the application needs in the portal

- A bot user, and its token.
- No privileged intents. The bot uses `Guilds` and `GuildVoiceStates`, both of which are
  unprivileged; the second is what tells it which voice channel somebody is standing in.
- Invited with the `bot` and `applications.commands` scopes, and with permission to send
  messages and embed links in the channel the command is used in. The bot logs an invite URL
  carrying exactly those at startup.

## The launch

```
/watch title:<name or id>
```

The title option autocompletes from the live library, so a person picks a film that exists and
the command receives its id rather than a guess at its name. Typing it out works too: an id
matches exactly, a full title matches whatever its case, and otherwise every word has to begin a
word in the film's id or title, in any order. A query that fits several films is refused with
the films it could have meant, because the wrong film is worse than being asked again.

The reply is a link into the player carrying the room's session and the film:

```
<Player:BaseUrl>?session=<voice channel id>&title=<title id>
```

The link carries no identity and is not signed. It is unguessable and nothing more: anyone
holding it is in the room.

Which launch the reply carries is the only thing `ILaunchPresenter` decides. A presenter that
opens the player as an Activity inside the voice channel replaces the one that links to it, and
the command above it does not change.

## Waiting on a film

```
/notify add film:<name, or a link to the film's IMDb page>
/notify list
/notify cancel film:<one of the films you are waiting on>
```

`/notify add` is for a film that cannot be downloaded yet: out in cinemas, say, with a digital
release some months off. The film option autocompletes from the catalogue rather than from the
tracker, because the tracker has nothing to offer yet, and a pasted IMDb link is recognised
wherever the id appears in it, so somebody who found the film on IMDb does not search for it
twice. What is kept is the film's IMDb id and what the catalogue said about it.

Before anything is written down, the places the film might already be are checked: the library,
in which case the reply points at `/watch`; the torrent client, in which case it is already on
its way; and the tracker, in which case the reply names the release and points at `/find`.

The tracker is then asked about every film on the list on a slow clock, by IMDb id. A film counts
as available once a release is offered whose source is a web encode or better. When one is, a
new message goes to each channel people asked in, mentioning exactly those people, naming the
release and pointing at `/find`. The wish is forgotten once its people have been told; a channel
the message could not be sent to keeps its people for the next pass.
