# moviebot-assistant

What people say or type to MovieBot is a turn here: the agent loop from `TheKrystalShip.Llm`, against
the model server `Llm.Endpoint` names — moviebot-llm on the same machine, or a model server shared
with other assistants, reached with the key in `Llm__LlamaCpp__ApiKey` — with the harness from
`TheKrystalShip.Agent`. The bot relays what is said in a voice
channel; the Cinema site sends what is typed. Every act a tool takes is the API's
(`docs/api-contract.md`), reached through `MovieBot.Client` with the access token of the person the
turn is for.

## The wire

The organization's assistant wire contract, version 2.1 (`tks-assistant/docs/wire-contract.md`), so one
chat client serves this and the KGSM assistant alike. Every route below except `/me` and `/rooms/record`
is `TheKrystalShip.Agent.Service`'s, the same code on every assistant, standing on this service's
seams: `CinemaCallers` (who is asking), `CinemaTurnRunner` and `CinemaTurnRequests` (what a turn says,
and how a request names one), `CinemaConfirmations` (agreeing to a proposal) and
`CinemaPendingProposals` (`Wire/CinemaTurns.cs`). What is this product's own — the frames the assistant
produces, a proposal, a film's card, `launch`, and the bodies of a relayed turn and a room's record — is
in `MovieBot.Client/AssistantWire.cs`, written as this service writes it, an absent field left out.

| Route | What it does |
|---|---|
| `POST /turn` | `{prompt, conversationId?, style?, room?}`: SSE with `Accept: text/event-stream`, one JSON answer otherwise |
| `POST /confirm` | `{token}`: carries out a proposal; `result` frame when streamed, `{text, success, outcome, result}` otherwise |
| `DELETE /turns/{id}` | stops a turn, running or waiting |
| `GET /conversations`, `GET`/`DELETE /conversations/{id}` | the caller's own chats, with what still waits in each |
| `POST /conversations/{id}/turns/{turnId}/feedback` | `{rating: up|down|null, note}` |
| `GET /commands`, `POST /commands/{name}`, `GET /tools` | `help`, `tools`, `compact`, `new` and `memory` |
| `GET /memories`, `GET /memories/limits`, `PUT`/`DELETE /memories/{key}` | what the caller asked to be remembered, read and corrected by hand |
| `GET /push/key`, `POST`/`DELETE /push/subscribe`, `GET /push/devices`, `POST /push/actions/{handle}` | notifications of a proposal waiting on its asker; the last is a notification's button and anonymous |
| `GET /events`, `POST /events/attach` | the caller's stream of chat-list changes, and the turns of the chat it is attached to |
| `GET /me`, `GET /speech` | who the caller is; whether this host hears and speaks (it does neither) |
| `POST /rooms/record` | relayed only: a room verb the bot carried out, written into the room's conversation |

**One frame is this product's own: `launch`**, `{titleId, name, requestedBy, sessionId?, replaced?,
alreadyWatching}` — a film put on in a room, or, with no `sessionId`, found for a person to open. Each
client presents it its own way: the bot posts a launch card, the site opens the player. A confirmed
proposal's `result` carries the API's own answer (`download`, `watch`, `wish`, `subtitle`) beside the
sentence, so a client words its message from facts rather than parsing text.

## Who is asking

- **Every caller carries a person's access token, and the turn acts with it.** The token is validated
  as the API validates it — the issuer `Oidc:Issuer`, the audience `cinema`, ES256, `at+jwt` — and
  must hold `cinema:watch`. The tools reach the API with that same token, so what a person may do here
  is exactly what the API lets them do, and a refusal comes back as one a tool words. This service
  mints nothing, keeps no sessions and holds no key of its own.
- **A relayed turn is a token with an `act` claim**: a client acting for the person, which is the bot
  with the token it exchanged for the Discord user who spoke.
- **Only a relayed turn names a room.** A room is what gives a turn the room tools
  (`RoomTools.RoomOnly`) and the room's conversation, `room:<sessionId>`, shared by everyone in it; the
  bot knows which voice channel somebody is standing in. A person's own turn names none, whatever the
  body says: it gets the library and fetching tools, and its conversation is
  `person:<userId>:<chatId>`, theirs alone.
- **In a room anybody may agree to a proposal; on the site only the person who asked.** A refused
  token is not spent, so a guess cannot cancel what its owner is about to approve. The proposal is
  carried out with the token of whoever agrees, since they are the one spending it.
- **A command a relaying client sends names its room in `X-Assistant-Room`**, the room's session id,
  and acts on the room's conversation; from anybody else the header is ignored and the command acts on
  the caller's own chat. `CinemaCallers` is the one place a request is resolved to a conversation, so
  a command and a turn address the same one.

## Commands, memory and notifications

- **Every command needs `cinema:watch`**, what talking to the assistant needs, read off the token's
  actions. `/new` in a room starts the room's conversation over and needs the same: anybody in a room
  may move it, so anybody may clear what it was saying. `think` is not offered, because every request
  is sent with thinking off.
- **Memory belongs to a person's own chats.** A room is shared by whoever is in the channel and starts
  over after a silence, so nothing said there is anybody's to keep: a room's turn is offered no memory
  tools and has no memory owner, and `/memory` there says nothing is remembered. A person's chats share
  their memory, `person:<userId>`: the model writes it with `remember`, `forget` and `recall`
  (`RoomTools.Memories`, from `TheKrystalShip.Agent`), what is remembered is in the turn's context, and
  the person corrects it through `/memories`. The tools are in the site's catalog because
  `AssistantRoutingTests` against the real model route a person's requests correctly with them listed;
  a catalog change is judged the same way.
- **A proposal waiting on a person who has left every surface is announced by Web Push**, with this
  service's own key pair in `assistant.db`; a room's proposals are answered in the room and are never
  announced. **A notification can cancel a proposal and cannot agree to one**: a proposal is carried
  out by the API as whoever agrees, with their token, and a button tapped on a locked phone carries
  none. Confirm answers that the chat is where to agree, and the proposal keeps waiting there
  (`CinemaConfirmationPushHost`). Krystal Cinema's dock does not subscribe: its origin's service worker
  is subscribed with MovieBot.Api's key, and a registration carries one.

## Rules the brain rests on

- **Every act is the API's, and the tools only decide what to ask it.** A download and a wish are
  carried out by the API as the person whose token asks, so the bot's slash commands and this
  service cannot disagree about what one is.
- **Live state goes in the turn's context, never in the instructions.** The chat template renders the
  instructions ahead of the tool catalog, so a changing byte there re-reads the catalog and the
  conversation every turn. `AgentTurn.Context` is sent after the conversation.
- **`enable_thinking` is sent false on every request.** This model family reasons when the variable is
  left unset, which triples the time to a tool call.
- **The instructions stay short.** A long prompt naming tools and rules made this 2B model write tool
  calls out as text. Guidance lives in tool descriptions and in what each tool returns, and
  `AssistantRoutingTests` against the real model is how a wording change is judged. `system.md` is a
  room's, `site.md` a person's.
- **What a tool returns is split between people and the model.** The sentence a person could read is
  recorded, and what to do next is appended for the model alone. The model often writes nothing after
  a tool result, and the recorded sentence is then the reply, so it must never name a tool.
- **Acting on the room is immediate; spending something is proposed.** Proposals wait for somebody to
  agree, redeem one single-use token, expire, and are carried out as whoever agrees.
- **A written reply is held to what the turn did**, by the checks in `TheKrystalShip.Agent`: a claim of
  acting on a turn that did nothing, and a figure nothing the turn was given contains, re-prompt once
  and are then corrected. `RoomTools.Acted` is what a claim is held against, and it must cover every
  tool that changes something. A reply that leaves a proposal unmentioned has the pending note
  appended, so it is in the text every client renders.
- **A reply asking for an id, or announcing a film nothing put on, is sent back.** `RoomIdRequest`
  sends back a reply asking for an id the tools find themselves: the model repeats such a reply from
  its own conversation, and a line in the instructions does not stop it on every history.
  `RoomLoadingClaim` sends back "Loading Heat..." on a turn that put nothing on, which has no subject
  for the first-person check to find.
- **Asking for a film is one tool, `watch_film`, and it always comes back with something to act on.**
  Given a search and a load as separate tools, the model stops at the first answer and says a film is
  not in the library without asking the tracker. `watch_film` puts the film on when the library has
  it. Otherwise it asks the tracker, puts on a library film the title index names under another
  spelling, and proposes the ranker's best release, because after a tool answers the model ends its
  turn far more often than it calls the next one. When the tracker has no release it gives the imdb id
  `add_wish` takes. Other films the words could mean are listed, earliest first. With no room, a film
  that plays now is found to open, and one that has to be fetched is proposed as Watch now
  (`POST /api/watch`): agreeing opens a room of the person's own, the site goes to it, and the film
  plays there while it is prepared. A person's own chat names no release and offers no other one,
  since which file a film is prepared from is the server's choice; a room picks among releases with
  `download_film`.
- **`watch_film` reads what the model wrote the way the film is listed, not the way it was typed.** The
  model retypes ids with the punctuation changed, words doubled and the year invented, so the matcher
  compares words and ignores punctuation. A film named whole under a year nobody said is put on,
  because the year is the model's; when the person said a number, the year may be what picks the film,
  so it is not put on. Replies to a film that could not be picked are written for the model: the films
  it could have been by name, earliest first.
- **A film named by its place in a series is counted here, in the order the films came out.** "The
  second Pirates of the Caribbean" and "step up 2" are read from the person's words, which the model
  often drops when it writes the name down, and the catalogue's films sharing the name are counted by
  year. The index's own order is what people search for this week.
- **Carrying on is `resume`, never `play`.** A tool named `play` is what the model calls for "play"
  followed by a film's name, and the room carries on with the film it already holds.
- **A room's conversation lasts as long as the room keeps talking.** Once it has been silent for
  `Assistant:IdleResetMinutes`, the next request or room verb starts it over with the store's `Reset`,
  which replays nothing before it and keeps every turn on disk. Requests to a room are mostly
  unrelated, and a follow-up comes within minutes of what it follows. A person's chat is never started
  over: a new one is theirs to start.
- **A room's conversation is part of what is measured.** A request that routes correctly in a fresh
  conversation can fail in a room's real one, because earlier replies are examples the model copies. A
  routing failure seen live is reproduced by replaying that room's turns before it is judged fixed;
  `tests/MovieBot.Tests/Replays` holds those turns and `RoomReplayRoutingTests` replays them.
- **The harness around the loop is shared, and the films are not.** Reading the prompts and
  `tools.json`, the catalog's agreement with `RoomTools.Names`, proposal tokens, the reply checks and
  compaction come from `TheKrystalShip.Agent` in tks-assistant. What the tools do, which of them wait for a
  person, and the words a room is acted on in are this repository's.
- **A turn is not the request that started it.** The organization's engine runs it: one at a time per
  conversation with up to three waiting, every surface of the conversation can attach to it, the
  request going away ends nothing, and a stop does. A relayed turn answered whole runs through the
  engine like any other, so a room's turns wait their turn.
- **The warm-up sends what the turns that come send**: a room's where the bot relays rooms here
  (`Assistant.Enabled`), a person's otherwise, since on a model server shared with other assistants a
  request no turn repeats takes a slot another assistant was keeping warm. It leaves the same request
  in `llm-warmup.json` beside the conversations for moviebot-llm to replay when it starts.
