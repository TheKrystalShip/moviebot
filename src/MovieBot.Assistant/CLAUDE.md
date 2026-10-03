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
  `X-MovieBot-Acting: userId|displayName`. It names who, never what.
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
- **A turn is not the request that started it.** It runs one at a time per conversation, up to three
  wait, and every surface of the conversation can attach to it; the request going away ends nothing,
  a stop does.
- **The warm-up sends what a room's turn sends**, and leaves the same request in
  `llm-warmup.json` beside the conversations for moviebot-llm to replay when it starts.
