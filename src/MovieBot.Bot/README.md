# MovieBot.Bot

The Discord half. `/watch` resolves a film, opens the room's session on the API and hands the
room a launch — and when the film is not here, searches the tracker, starts the download and hands
the room the launch the moment the film can be watched. `/notify` watches for a film that cannot be
downloaded yet and says so when it can.

## It holds almost no state

Nothing about a room or a download is remembered between commands. The library is read from the
API each time somebody asks, and the session is named by the voice channel the requester is
standing in, so the same channel always reaches the same room: a restarted bot lands the next
request in the session that is already playing, and there is no table of channels to sessions
that could disagree with reality. A download's own notes live on the torrent.

Everything in a reply was measured while the command ran. The bot never opens a manifest, never
caches a title and never claims a state it did not read.

Two things are exceptions, and both for the same reason: they exist nowhere else to be read back
from. The wish list is the films people are waiting on and who asked — a film that is not on the
tracker yet has no torrent to tag and no manifest to write. The launch cards are the messages
that hand out a way into a room: which message, in which channel, carrying which invite, for
which film. The API has never heard of a Discord message, so a bot that did not write this down
would leave every card it had posted looking live for good.

Each is one JSON file in the state directory systemd hands the service, written whole and moved
into place on every change.

## Configuration

| Variable | Required | What it is |
|---|---|---|
| `MOVIEBOT_TOKEN` | yes | The bot token. A credential: it comes from the host's environment or from user-secrets, and never from a file under the repository. |
| `MOVIEBOT_CLIENTID` | no | The application id, which is also its OAuth2 client id. It names the application in the invite the bot writes to the log at startup. |
| `Discord__GuildIds__0` | yes | The server the bot serves. One index per server. Commands are registered per guild and an interaction from anywhere else is refused. |
| `Player__BaseUrl` | yes | Where the player is served. The launch link is this address plus its parameters. |
| `Api__BaseUrl` | no | Where the bot reaches the API. Defaults to `http://127.0.0.1:8099`. |
| `Api__PublicBaseUrl` | no | Where Discord's servers reach the API. Only the poster needs it, and without it the embed carries no image rather than a broken one. |
| `Launch__InviteGraceSeconds` | no | How long an Activity invite outlives the film it was made for. Defaults to 1800, matching the API's `Rooms:IdleTimeout`, which lands the invite and the room behind it at the same moment. |
| `Launch__CardsPath` | no | Where the standing launch cards are written. Defaults to `launches.json` in the directory `STATE_DIRECTORY` names, and to the working directory when there is none. |
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

The title option autocompletes from the live library while anything in it matches, so a person
picks a film that exists and the command receives its id rather than a guess at its name. Typing
it out works too: an id matches exactly, a full title matches whatever its case, and otherwise
every word has to begin a word in the film's id or title, in any order. A query that fits several
films is refused with the films it could have meant, because the wrong film is worse than being
asked again.

Once nothing in the library matches, the same menu searches the tracker instead, and the rows are
its ranked results with the release's quality, size and seeds beside the name. Picking one starts
the download and answers with the message that will show its progress. The film is transcoded as
it arrives, so it is watchable seconds after the transcode starts; the moment it is, the bot loads
it into the voice channel the person was standing in and posts the launch as a new message that
mentions them. Somebody who picks from outside a voice channel gets the download and the
announcement, and a `/watch` from a voice channel afterwards is the way in. Whichever room a
download was asked for is written on the torrent, so a bot restarted mid-download still starts the
film where it was asked for.

The reply is a link into the player carrying the room's session and the film:

```
<Player:BaseUrl>?session=<voice channel id>&title=<title id>
```

The link carries no identity and is not signed. It is unguessable and nothing more: anyone
holding it is in the room.

Which launch the reply carries is the only thing `ILaunchPresenter` decides. A presenter that
opens the player as an Activity inside the voice channel replaces the one that links to it, and
the command above it does not change.

## A card says when it is over

An Activity launch hands out a Discord invite, and Discord counts an invite's life from the moment
it is made rather than from the last person through it. The invite is therefore made to last the
film — its running time plus `Launch__InviteGraceSeconds` — so the card goes on being a way in for
as long as there is something to walk into. A flat window shorter than a film shuts the door while
the room is still watching, which nobody inside notices and nobody outside can explain.

The card is then kept up to date. Every half minute the rooms are read from the API, and a card
whose room has closed, or whose room has moved on to another film, is edited where it stands: it
goes grey, the button and the link on its title go, and the description says what happened and
that `/watch` in a voice channel starts the film again. The invite behind it is revoked in the
same pass, so the link somebody copied out of the card stops working when the card says it has.

A pass that cannot reach the API does nothing at all. Rooms it could not read are not rooms that
closed. Editing a message notifies nobody, which is the point: this is for whoever scrolls back
and finds the card, not for the room that has already moved on.

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
its way; and the tracker, in which case the reply names the release and says to pick it from
`/watch`'s search results.

The tracker is then asked about every film on the list on a slow clock, by IMDb id. A film counts
as available once a release is offered whose source is a web encode or better. When one is, a
new message goes to each channel people asked in, mentioning exactly those people, naming the
release and pointing at `/watch`. The wish is forgotten once its people have been told; a channel
the message could not be sent to keeps its people for the next pass.
