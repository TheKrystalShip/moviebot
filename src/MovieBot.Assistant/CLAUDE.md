# moviebot-assistant

What people say or type to MovieBot is a turn here: the agent loop from `TheKrystalShip.Llm`, against
moviebot-llm, with the harness from `TheKrystalShip.Agent`. The bot relays what is said in a voice
channel; the Cinema site sends what is typed. Every act a tool takes is the API's
(`docs/api-contract.md`), reached through `MovieBot.Client` with the service key.

## The wire

The assistant wire contract, version 2.1 (`kgsm-llm/docs/wire-contract.md` in the KGSM workspace),
so one chat client serves this and the KGSM assistant alike. The frames and bodies are in
`Wire/Frames.cs`.

| Route | What it does |
|---|---|
| `POST /turn` | `{prompt, conversationId?, style?, room?}`: SSE with `Accept: text/event-stream`, one JSON answer otherwise |
| `POST /confirm` | `{token}`: carries out a proposal; `result` frame when streamed, `{text, success, outcome, result}` otherwise |
| `DELETE /turns/{id}` | stops a turn, running or waiting |
| `GET /conversations`, `GET`/`DELETE /conversations/{id}` | the caller's own chats, with what still waits in each |
| `POST /conversations/{id}/turns/{turnId}/feedback` | `{rating: up|down|null, note}` |
| `GET /commands`, `POST /commands/{name}` | `new` and `help` |
| `GET /events`, `POST /events/attach` | the caller's stream of chat-list changes, and the turns of the chat it is attached to |
| `GET /me`, `GET /speech` | who the caller is; whether this host hears and speaks (it does neither) |
| `POST /rooms/record` | service only: a room verb the bot carried out, written into the room's conversation |

**One frame is this product's own: `launch`**, `{titleId, name, requestedBy, sessionId?, replaced?,
alreadyWatching}` — a film put on in a room, or, with no `sessionId`, found for a person to open. Each
client presents it its own way: the bot posts a launch card, the site opens the player. A confirmed
proposal's `result` carries the API's own answer (`download`, `wish`, `keep`, `subtitle`) beside the
sentence, so a client words its message from facts rather than parsing text.

## Who is asking

- **A person carries the token the API gave them** at Discord sign-in, checked with `Auth:SigningKey`.
  This service mints nothing and keeps no sessions.
- **A service carries `X-MovieBot-Service`** (`Auth:ServiceKey`) **and names the person** in
  `X-MovieBot-Acting: userId|displayName`, the name percent-encoded. It names who, never what.
- **Only a service names a room.** A room is what gives a turn the room tools (`RoomTools.RoomOnly`)
  and the room's conversation, `room:<sessionId>`, shared by everyone in it; the bot knows which voice
  channel somebody is standing in. A person's turn names none, whatever the body says: it gets the
  library and fetching tools, and its conversation is `person:<userId>:<chatId>`, theirs alone.
- **In a room anybody may agree to a proposal; on the site only the person who asked.** A refused
  token is not spent, so a guess cannot cancel what its owner is about to approve.

## Rules the brain rests on

- **Every act is the API's, and the tools only decide what to ask it.** A download, a wish and a keep
  are carried out by the API as the person who asked, so the bot's slash commands and this service
  cannot disagree about what one is.
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
  agree, redeem one single-use token, expire, and are carried out as whoever asked.
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
  `add_wish` takes. Other films the words could mean are listed, earliest first. With no room, the
  film is found to open and a download is fetched for the library.
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
  compaction come from `TheKrystalShip.Agent` in tks-agent. What the tools do, which of them wait for a
  person, and the words a room is acted on in are this repository's.
- **A turn is not the request that started it.** It runs one at a time per conversation, up to three
  wait, and every surface of the conversation can attach to it; the request going away ends nothing,
  a stop does.
- **The warm-up sends what a room's turn sends**, and leaves the same request in
  `llm-warmup.json` beside the conversations for moviebot-llm to replay when it starts.
