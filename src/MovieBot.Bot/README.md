# MovieBot.Bot

The Discord half. `/watch` resolves a film, opens the room's session on the API and hands the
room a launch — and when the film is not here, searches the tracker, starts the download and hands
the room the launch the moment the film can be watched. `/show` fetches episodes of a show, a
season at a time. `/notify` watches for a film that
cannot be downloaded yet and says so when it can. `/voice join` brings the bot into a voice channel
to listen, so the film can be paused, resumed and skipped by saying so.

## It holds almost no state

Nothing about a room or a download is remembered between commands. The library is read from the
API each time somebody asks, and the session is named by the voice channel the requester is
standing in, so the same channel always reaches the same room: a restarted bot lands the next
request in the session that is already playing, and there is no table of channels to sessions
that could disagree with reality. A download's own notes live on the torrent.

Everything in a reply was measured while the command ran. The bot never opens a manifest, never
caches a title and never claims a state it did not read.

Downloads and wishes are the API's: the bot asks it to start one or wait on a film, and words in
Discord what it answers (`docs/api-contract.md`, "Fetching films").

## It acts as the person who asked

Every command, autocomplete and button is carried out with an access token for the Discord user who
used it. The bot exchanges their Discord user id for one at the identity provider (RFC 8693, as the
confidential client `Oidc:BotClientId`), and the token it gets back is that person's, with the bot
named as the client acting for them. So the API decides what each person may do from their own
account's actions — somebody who may watch but not download is told so, in their own reply — and
records them, not the bot. A token is held until 30 seconds before it expires and then exchanged
again.

A Discord account the provider has no approved account for is refused there, and the reply says so:
one switched off is told so, and one waiting for approval or never linked is told an administrator
has to approve it. The bot keeps nothing about who is linked; the provider is asked.

Work that names nobody — keeping the progress messages and launch cards current, announcing a
finished download — uses the service key (`Api:ServiceKey`).

One thing is an exception, because it exists nowhere else to be read back from. The launch cards
are the messages that hand out a way into a room: which message, in which channel, carrying which
invite, for which film. The API has never heard of a Discord message, so a bot that did not write
this down would leave every card it had posted looking live for good. They are one JSON file in
the state directory systemd hands the service, written whole and moved into place on every change.

## Configuration

Settings come from `moviebot.settings.json`, the file every MovieBot program shares: the copy
beside the binary carries the defaults, and a host's own copy in its XDG configuration directory
(`~/.config/moviebot/` of the account the bot runs as) overrides them. The table names each
setting as the environment variable that overrides it; in the file, `Voice__Triggers` is
`"Triggers"` under `"Voice"`. The top-level README's Configuration section has the full lookup.

| Variable | Required | What it is |
|---|---|---|
| `MOVIEBOT_TOKEN` | yes | The bot token. A credential: it comes from the host's environment or from user-secrets, and never from a file under the repository. |
| `Discord__ApplicationId` | no | The application id, set as `"ApplicationId"` under `"Discord"` in the settings file, or as `MOVIEBOT_CLIENTID` in the environment. It names the application in the invite the bot writes to the log at startup, and the API redeems the Activity's sign-in code as it. |
| `Oidc__Issuer` | yes | The identity provider the bot exchanges a Discord user's id at, for a token acting as them. |
| `Oidc__BotClientId` | no | The confidential client the bot is registered as at the provider. Defaults to `cinema-bot`. |
| `Oidc__BotClientSecret` | yes | That client's secret. A credential: environment only. |
| `Api__ServiceKey` | yes | The key the bot's background passes present to the API for work that names nobody, the one the API knows as `Auth__ServiceKey`. A credential. |
| `Discord__GuildIds__0` | yes | The server the bot serves. One index per server. Commands are registered per guild and an interaction from anywhere else is refused. |
| `Player__BaseUrl` | yes | Where the player is served. The launch link is this address plus its parameters. |
| `Api__BaseUrl` | no | Where the bot reaches the API. Defaults to `http://127.0.0.1:8099`. |
| `Api__PublicBaseUrl` | no | Where Discord's servers reach the API. Only the poster needs it, and without it the embed carries no image rather than a broken one. |
| `Launch__InviteGraceSeconds` | no | How long an Activity invite outlives the film it was made for. Defaults to 1800, matching the API's `Rooms:IdleTimeout`, which lands the invite and the room behind it at the same moment. |
| `Launch__CardsPath` | no | Where the standing launch cards are written. Defaults to `launches.json` in the directory `STATE_DIRECTORY` names, and to the working directory when there is none. |
| `Voice__Enabled` | no | Whether the bot may listen in a voice channel at all. Off by default, because everyone in a channel it joins is heard. On, it still joins only when somebody runs `/voice join`. |
| `Voice__Triggers` | no | What addresses the bot, comma-separated. `moviebot.settings.json` carries `okay computer, ok computer, okay computers, ok computers`, because the recogniser writes "okay" both ways and sometimes adds an s. Two full words carry a trigger through a three-second scan window where a one-syllable "hey" does not: whisper reads "hey" as "A", "Pay" or "K" often enough that, across fourteen synthetic voices with room noise added, "okay computer" was found in every command and "hey moviebot" in ten of fourteen. Near-miss chat ("okay, come on", "my computer crashed, okay") does not trigger; naming the Radiohead album does. The recogniser is primed with the name "MovieBot" and never with a trigger: whisper answers noise with the sentence it was primed with, so a trigger in the priming makes a breath address the bot. |
| `Voice__MaxCommandSeconds` | no | The longest a request may run before it is cut and taken as it stands. `moviebot.settings.json` carries 7, because the speech daemon reads commands in an eight-second window and audio that fills it makes recognition run away. |
| `Voice__CommandQuietMs` | no | How long somebody has to stop sounding after the trigger before a request that is not a room verb counts as finished. Defaults to 600. A room verb does not wait for it: "pause" is acted on as soon as two readings agree on it, while the room keeps talking. |
| `Voice__ScanWindowMs`, `Voice__ScanStrideMs` | no | How much of each person's latest speech is looked through for the trigger, and how often: 3000 and 500 by default. The window must stay under the speech daemon's four-second scan window. |
| `Voice__LogTranscripts` | no | Whether what was heard is written to the log: requests at information level, and everything scanned for the trigger at debug. Off by default: a voice channel is full of things nobody said to the bot. |
| `Speech__SocketPath` | no | The unix socket of the speech daemon the bot hears through. Blank by default, which means there is none and nothing is heard. |

The two credentials are read under their own names. They sit above the settings files and
underneath every other source, so `Discord:Token` and `Discord:ApplicationId` from user-secrets or
from the environment override them: a developer who cannot read the host's environment still has a
way in.

Every setting is checked at startup, and a missing or malformed value names itself and the
variable to set. A token Discord rejects is reported once as an error; the gateway then retries
on its own, so the bot stays up and keeps saying so.

```bash
dotnet user-secrets set "Discord:Token" "<token>" --project src/MovieBot.Bot

Player__BaseUrl=https://movies.example.com \
Discord__GuildIds__0=<guild id> \
dotnet run --project src/MovieBot.Bot -c Release
```

## Running it as a service

**systemd does not read `/etc/environment`.** A unit names the file holding the credentials, or
it starts with no token and fails in the shape of a Discord outage. Everything that is not a
credential belongs in the settings file:

```ini
[Service]
EnvironmentFile=/etc/moviebot/moviebot.env
```

## What the application needs in the portal

- A bot user, and its token.
- No privileged intents. The bot uses `Guilds` and `GuildVoiceStates`, both of which are
  unprivileged; the second is what tells it which voice channel somebody is standing in.
- Invited with the `bot` and `applications.commands` scopes, with permission to send messages and
  embed links in the channel the command is used in, and with Connect and Speak for listening. A bot
  already in a server keeps the role it was given, so adding a permission to the invite does not
  add it to that role. The bot logs an invite URL
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

## Fetching a show

```
/show show:<name> season:<n> [episodes:<1-3, 5>] [quality:<720p|1080p>]
```

A show is fetched a season at a time. The menu searches the catalogue for shows and never films,
and the tracker is asked once a show is picked, for every season it offers under the show's id.

- **The plan is shared.** `ShowPlan` in `MovieBot.Client` decides which releases fetch the picked
  episodes: a season's pack at the asked quality first, fetching only the picked files out of it; single
  episodes where there is no pack; another quality only where the asked one is not offered, said so
  in a follow-up.
- **720p unless 1080p is asked for.** A season is many hours, and 720p takes a third of the space.
- **Leaving episodes out takes the whole season**; naming them takes only those, as numbers and
  ranges.
- **Each download is a download.** Every planned torrent posts its own progress message and is
  announced when its first episode can be watched.

## Listening in a voice channel

`/voice join` brings the bot into the voice channel the person running it is in. From then on,
"okay computer, pause" stops the film for the room. What is heard goes to the speech daemon for words,
and the words go first to a gate that knows a handful of verbs.
`../../docs/voice-commands.md` is a showcase of what can be said, with examples.

- **The trigger is heard while people keep talking.** Nobody watching a film with friends goes
  quiet before addressing the bot, so each person's last three seconds are looked through for the
  trigger every half second, on the speech daemon's scan lane. Finding it plays the listening tone and
  starts taking the request from the word before the trigger.
- **A room verb is acted on as soon as it is said.** The request is read as it grows, and the moment
  two readings agree on something the gate calls a verb (`RoomVerbCompleteness`), the film moves —
  while the room carries on talking. Anything else is taken when the speaker has been quiet for a
  moment, or at seven seconds.
- **The gate reads the whole request and has three answers.** "Pause", "resume the film", "back
  fifteen", "skip forward a minute" are verbs. "Should we pause?" and "don't pause it" contain the
  word and are not the verb. "Go back" with no amount, "rewind a bit", and "go back 1:30" look like
  verbs and cannot be read safely, so they are not guessed at: a missed verb costs a slower answer,
  a misread one moves the film for everyone.
- **Numbers are read before punctuation is stripped.** Recognition writes "1:30" and "1.5", and
  flattened those become 130 and 15. Digits joined by a colon, point or comma make a request
  ambiguous.
- **"Go ahead" is not a direction.** It means carry on, and read as "skip ahead" it would move the
  film the first time somebody agreed with something.
- **The act is silent.** The film stopping is the acknowledgement, and the player already tells
  everyone who did it.
- **A verb needs a room holding a film.** Every write to a room creates it, so the room is looked up
  first — which does not — and a verb said in a channel watching nothing does nothing.
- **Whoever spoke is who did it.** The change is recorded under the speaker's account.
- **A play or a pause sends no position.** The bot is not watching and does not know where the film
  is; the API resolves "wherever the room is" under the lock that applies the change.
- **The room is told, or the bot does not stay.** Joining posts a notice in the channel the command
  ran in, because it is the only way anyone but the person who ran it learns they are being
  listened to. If that notice cannot be posted, the bot leaves again.
- **Everything else is heard and left alone.**
- **libdave's own messages are routed through the bot's logging**, under `Discord.LibDave`. Only its
  warnings and errors reach the journal; `Logging__LogLevel__Discord.LibDave=Debug` brings back its
  per-interval decrypt statistics, which is the first thing to turn on when a voice connection
  misbehaves.
- **The log says how long it took.** Every act is logged with the milliseconds from the moment the
  speaker stopped talking to the moment the room changed. For a request that is not a verb, the quiet
  that ends it is inside that number, because the person waited through it.

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

The API then asks the tracker about every film on the list on a slow clock, by IMDb id. A film
counts as available once a release is offered whose source is a web encode or better. When one is,
everybody waiting on it is told on Krystal Cinema, in their notifications and by a push, and the
wish shows the release found there; nothing more is said on Discord.
