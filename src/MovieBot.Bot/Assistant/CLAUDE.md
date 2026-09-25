# Asking the assistant

Anything said to the bot that the room-verb gate does not read is a turn of the agent loop from
`TheKrystalShip.Llm`, against moviebot-llm, answered in the voice channel's chat. `../README.md` is the
authority for the surface; these are the rules it rests on.

- **The brain runs in the bot, and its tools are the bot's commands.** A download has to post its
  progress message and a wish lives in the bot's state directory, so the acts it can take are
  `WatchCommand`, `DownloadCommand`, `NotifyCommand` and `KeepCommand`, called directly. A tool that
  did its own version would not disagree loudly; it would leave a download without its progress
  message.
- **Live state goes in the turn's context, never in the instructions.** The chat template renders the
  instructions ahead of the tool catalog, so a changing byte there re-reads the catalog and the
  conversation every turn. `AgentTurn.Context` is sent after the conversation.
- **`enable_thinking` is sent false on every request.** This model family reasons when the variable is
  left unset, which triples the time to a tool call.
- **The instructions stay short.** A long `system.md` naming tools and rules made this 2B model write
  tool calls out as text. Guidance lives in tool descriptions and in what each tool returns, and
  `AssistantRoutingTests` against the real model is how a wording change is judged.
- **What a tool returns is split between the room and the model.** The sentence a person could read is
  recorded, and what to do next is appended for the model alone. The model often writes nothing after
  a tool result, and the recorded sentence is then the reply, so it must never name a tool.
- **Acting on the room is immediate; spending something is proposed.** Proposals wait for a button or
  a spoken yes, redeem one single-use token, expire, and are carried out as whoever asked.
- **A room verb is written into the room's conversation** as the tool call it stands for, so the model
  knows what happened to the room without having done it.
- **A written reply is held to what the turn did**, by the checks in `TheKrystalShip.Agent`: a claim of
  acting on a turn that did nothing, and a figure nothing the turn was given contains, re-prompt once
  and are then corrected. `RoomTools.Acted` is what a claim is held against, and it must cover every
  tool that changes something, or an honest "I've paused it" is contradicted. `RoomIdRequest` sends
  back a reply asking the room for an id the tools find themselves: the model repeats such a reply from
  its own conversation, and a line in the instructions does not stop it on every history.
  `RoomLoadingClaim` sends back "Loading Heat..." on a turn that put nothing on, which has no subject
  for the first-person check to find.
- **Asking for a film is one tool, `watch_film`, and it always comes back with something to act on.**
  Given a search and a load as separate tools, the model stops at the first answer and tells the room a
  film is not in the library without asking the tracker. `watch_film` puts the film on when the library
  has it. Otherwise it asks the tracker, puts on a library film the title index names under another
  spelling, and proposes the ranker's best release, because after a tool answers the model ends its
  turn far more often than it calls the next one. When the tracker has no release it gives the imdb id
  `add_wish` takes. Other films the words could mean are listed, earliest first.
- **`watch_film` reads what the model wrote the way the film is listed, not the way it was typed.** The
  model retypes ids with the punctuation changed, words doubled and the year invented, so the matcher
  compares words and ignores punctuation. A film named whole under a year nobody said is put on,
  because the year is the model's; when the person said a number, the year may be what picks the film,
  so it is not put on. Replies to a film that could not be picked are written for the model: the films
  it could have been by name, earliest first, never the `/watch` wording.
- **A film named by its place in a series is counted by the bot, in the order the films came out.**
  "The second Pirates of the Caribbean" and "step up 2" are read from the person's words, which the
  model often drops when it writes the name down, and the catalogue's films sharing the name are
  counted by year. The index's own order is what people search for this week.
- **Carrying on is `resume`, never `play`.** A tool named `play` is what the model calls for "play"
  followed by a film's name, and the room carries on with the film it already holds.
- **A room's conversation lasts as long as the room keeps talking.** Once it has been silent for
  `Assistant:IdleResetMinutes`, the next request or room verb starts it over with the store's `Reset`,
  which replays nothing before it and keeps every turn on disk. Requests to a room are mostly
  unrelated, and a follow-up comes within minutes of what it follows.
- **A room's conversation is part of what is measured.** A request that routes correctly in a fresh
  conversation can fail in a room's real one, because earlier replies are examples the model copies. A
  routing failure seen live is reproduced by replaying that room's turns before it is judged fixed;
  `tests/MovieBot.Tests/Replays` holds those turns and `RoomReplayRoutingTests` replays them.
- **The harness around the loop is shared, and the room is not.** Reading `system.md` and
  `tools.json`, the catalog's agreement with `RoomTools.Names`, proposal tokens, the reply checks and
  compaction come from `TheKrystalShip.Agent` in tks-agent. What the tools do, which of them wait for a
  person, and the words a room is acted on in are this repository's.
