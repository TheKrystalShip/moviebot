# MovieBot.Bot

The Discord half. `/watch` resolves a film, opens the room's session on the API and hands the
room a launch — and when the film is not here, searches the tracker, starts the download and hands
the room the launch the moment the film can be watched. `/notify` watches for a film that cannot be
downloaded yet and says so when it can. `/voice join` brings the bot into a voice channel to listen,
so the film can be paused, resumed and skipped by saying so, and anything else said to it is put to
the assistant, which answers in the channel's chat.

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

The assistant adds a third: `assistant.db`, each voice channel's conversation with the assistant,
one conversation per room, which is what lets "why did it stop?" be answered a minute after somebody
said "pause". A room silent for `Assistant__IdleResetMinutes` starts its conversation over on the
next thing said in it, so the model is never handed an evening-old exchange to copy; every turn
stays in the file. Beside it the bot writes `llm-warmup.json`, the request that warms the model, for the
model's own unit to replay when it starts.

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
| `Voice__Enabled` | no | Whether the bot may listen in a voice channel at all. Off by default, because everyone in a channel it joins is heard. On, it still joins only when somebody runs `/voice join`. |
| `Voice__Triggers` | no | What addresses the bot, comma-separated. `appsettings.json` carries `okay computer, ok computer, okay computers, ok computers`, because the recogniser writes "okay" both ways and sometimes adds an s. Two full words carry a trigger through a three-second scan window where a one-syllable "hey" does not: whisper reads "hey" as "A", "Pay" or "K" often enough that, across fourteen synthetic voices with room noise added, "okay computer" was found in every command and "hey moviebot" in ten of fourteen. Near-miss chat ("okay, come on", "my computer crashed, okay") does not trigger; naming the Radiohead album does. The recogniser is primed with the name "MovieBot" and never with a trigger: whisper answers noise with the sentence it was primed with, so a trigger in the priming makes a breath address the bot. |
| `Voice__MaxCommandSeconds` | no | The longest a request may run before it is cut and taken as it stands. `appsettings.json` carries 7, because moviebot-speech reads commands in an eight-second window and audio that fills it makes recognition run away. |
| `Voice__CommandQuietMs` | no | How long somebody has to stop sounding after the trigger before a request that is not a room verb counts as finished. Defaults to 600. A room verb does not wait for it: "pause" is acted on as soon as two readings agree on it, while the room keeps talking. |
| `Voice__ScanWindowMs`, `Voice__ScanStrideMs` | no | How much of each person's latest speech is looked through for the trigger, and how often: 3000 and 500 by default. The window must stay under moviebot-speech's four-second scan window. |
| `Voice__LogTranscripts` | no | Whether what was heard is written to the log: requests at information level, and everything scanned for the trigger at debug. Off by default: a voice channel is full of things nobody said to the bot. |
| `Assistant__Enabled` | no | Whether a spoken request that is not a room verb is put to the model. Off by default; off, those requests are answered by nothing. |
| `Assistant__PromptDirectory` | no | Where `system.md` and `tools.json` are read from, relative to the binary. Defaults to `prompts`, which is where the build puts them. |
| `Assistant__OfferMinutes` | no | How long a proposed download, fetch, wish or keep waits for somebody to agree. Defaults to 10. |
| `Assistant__ConfirmWindowSeconds` | no | How long the bot listens for a spoken yes or no to a proposal without the trigger. Defaults to 20; 0 leaves the buttons as the only way to agree. |
| `Assistant__LibraryInContext` | no | The most films of the library written into every turn. Defaults to 60. |
| `Assistant__IdleResetMinutes` | no | How long a room's conversation sits silent before the next request or room verb starts it over. Defaults to 15; 0 keeps one conversation for the life of the room. |
| `Llm__Endpoint` | no | Where the model answers. `appsettings.json` carries moviebot-llm's `http://127.0.0.1:8190`, with the model's context window and a temperature of 0. |
| `Speech__SocketPath` | no | Where moviebot-speech answers. Defaults to `/run/moviebot-speech/speech.sock`, the same key the speech host reads. |
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

## Listening in a voice channel

`/voice join` brings the bot into the voice channel the person running it is in. From then on,
"okay computer, pause" stops the film for the room. What is heard goes to moviebot-speech for words,
and the words go first to a gate that knows a handful of verbs, with no model involved.

- **The trigger is heard while people keep talking.** Nobody watching a film with friends goes
  quiet before addressing the bot, so each person's last three seconds are looked through for the
  trigger every half second, on moviebot-speech's scan lane. Finding it plays the listening tone and
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
- **Everything else goes to the assistant**, described below, or is heard and left alone on a host
  that runs none.
- **libdave's own messages are routed through the bot's logging**, under `Discord.LibDave`. Only its
  warnings and errors reach the journal; `Logging__LogLevel__Discord.LibDave=Debug` brings back its
  per-interval decrypt statistics, which is the first thing to turn on when a voice connection
  misbehaves.
- **The log says how long it took.** Every act is logged with the milliseconds from the moment the
  speaker stopped talking to the moment the room changed. For a request that is not a verb, the quiet
  that ends it is inside that number, because the person waited through it.

## Asking the assistant

What the gate does not read goes to moviebot-llm, a small model on hotbox's card, with the room's
tools in its hands. It is the agent loop from `TheKrystalShip.Llm`, built per turn in this process,
so its tools are the bot's own commands: loading a film is `/watch`'s code, a download is the tracker
pick's, a wish is `/notify`'s and a keep is `/keep`'s.

- **It answers in writing, in the voice channel's chat.** The film's sound plays in every browser,
  so anything said out loud would be said over the film for everybody. What was heard is posted
  first, because a recogniser mishears and an answer about the wrong film is otherwise
  inexplicable.
- **The room and the library are in front of every turn.** They are read fresh and sent after the
  conversation and before the request, never in the instructions: the instructions come ahead of
  the tool catalog, and a byte that changes there makes the model read the catalog and the whole
  conversation again. A paused room is described as "waiting to be played", because measured on
  this model "carry on" against a room described only as paused came back as a pause or a question.
- **Moving the room happens at once; spending something waits.** Play, pause, seek and loading a
  film act immediately, as the person who asked. A download, a fetched subtitle, a wish and a keep
  are posted as a proposal with two buttons, and a spoken yes or no within
  `Assistant:ConfirmWindowSeconds` is the same answer. One token backs both, so whichever comes
  first acts and the other finds it spent. The window takes the next thing its speaker says, and
  anything that is neither a yes nor a no spends it without a word: in a room watching a film that
  is a remark about the film, and asking again would open a window that takes the next remark too. Anyone may agree; the act is done as whoever asked, so
  the download pings them and the wish is theirs. A proposal is held in memory and expires.
- **Asking for a film always ends in something to act on.** Watching, playing, finding and
  downloading a film are one tool. When the library has the film it goes on; when it does not, the
  best release the tracker offers is proposed, and when the tracker has none the model is given what
  a wish takes. A film named by its place in a series, "the second one" or "part two", is counted by
  the bot in the order the films came out.
- **A download is always of a release the model was shown.** A torrent id it writes without the
  tool having offered it is refused, and a release of a film the library already holds is refused
  too.
- **When the model writes nothing, what the tools said is the answer.** This model ends its turn
  straight after a tool result more often than not, so an act comes back as "Moved to 29:51." — the
  tool's own words, written for the room — rather than as silence. A proposal or a launch card
  posts its own message and adds nothing.
- **A written reply is held to what the turn did.** A reply claiming to have acted — "I have loaded
  Heat" — on a turn that moved no room, launched nothing and proposed nothing, and a figure of four
  or more digits that nothing the turn was given contains, each send the model back once with what
  is wrong; a second failure is posted with a correction under it. A reply that leaves a proposal
  unmentioned gets a line saying it waits for confirmation. The checks are
  `TheKrystalShip.Agent`'s, and the verbs a claim is recognised by are the room's
  (`RoomActionClaim`).
- **A reply asking for an IMDb or torrent id is sent back.** Nobody in a voice channel has one, and
  the tools find every id from a film's name. The room's conversation is what produces the ask: once
  a turn has answered "I need the IMDb ID", the model repeats it for the same request in that
  conversation, where a fresh one searches. `RoomIdRequest` re-prompts the turn to call the tools.
  `system.md` also tells the model to find things out with the tools rather than asking, which on
  its own fixed the same measured failure.
- **The room verbs are in the same conversation.** A pause the gate carried out is written in as the
  tool call it stands for, so a question about it later is answered by something that knows it
  happened.
- **One conversation per room**, each line attributed to whoever said it. It is compacted into a
  summary once a turn reports most of the context window used.
- **Answering does not hold up the room verbs.** A turn takes a second or more and every spoken
  command goes through one queue, so turns run beside it; turns in one room still run one at a time.
- **The instructions are short on purpose.** `prompts/system.md` is a few sentences. A longer one
  listing rules and naming tools made this model write its tool calls out as text, and routing
  failed across the board; the rules that matter live in each tool's description and in what each
  tool returns. `system.md` is read again every turn, `tools.json` once at startup, and the bot
  refuses to start when `tools.json` and the tools it implements disagree.
- **Warmed on startup.** The bot sends its real instructions and catalog through the model as it
  starts, and writes the same request for the model's unit to replay when that restarts.

`AssistantRoutingTests` checks which tool the shipped instructions and catalog lead the real model
to, and runs only when `MOVIEBOT_LIVE_LLM` names an endpoint:

```bash
ssh -N -L 18190:127.0.0.1:8190 hotbox &
MOVIEBOT_LIVE_LLM=http://127.0.0.1:18190 dotnet test --filter AssistantRoutingTests
```

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
