# moviebot-assistant

The plugin that helps the people of Krystal Cinema find films and shows. The organization's assistant
harness (`tks-assistant`) is the assistant: the turn loop, the model, the conversations, the memory, the
skills and the reply guards. This service brings the domain: a prompt appendix, six read-only tools over
the film database through MovieBot's API, five skills, the person's name, and the sign-in. It is built on
`TheKrystalShip.Agent.Plugin`; the wire it relays is that package's (`tks-assistant/docs/wire-contract.md`).

**The assistant has no name.** No prompt, label, reply or string names it; it is "the assistant", one
interface and one memory across products. A test fails the build if the profile or a string in the
service says otherwise.

## Shape

- **`profile/`** is what the harness is given, and the canon for what the model reads: `prompt.md` (the
  appendix, placed inside `<plugin name="moviebot">`), `tools.json` (every tool's exact description and JSON
  schema), `guards.json`, `speech.json`, and `skills/<name>/SKILL.md` with `references/`. The package reads
  the directory and registers it, again after a harness restart. Editing a file there is a redeploy.
- **`Tools/`** one class per tool over `CinemaTool`, which runs for a signed-in person, as that person, and
  turns a refusal or an unforeseen failure into a result the model can read. `ToolCall` carries the
  arguments and the API as that person. `Say` writes what the API said, one title to a line, and
  `TitleCards` the structured result beside it.
- **`Api/`** the API as the asking person: `CinemaApi` (the routes the tools read), the DTOs (only the
  fields a tool reads), and `Recording`, the recording of the API's answers and the handlers that write
  and replay it.
- **`Callers.cs`** sign-in: the access token on the request, validated as the API validates it.
- **`CinemaTurnContext.cs`** the live block sent with each turn.

## The tools

`search_titles`, `title_details`, `person_credits`, `discover`, `whats_new`, `my_taste`: all `read`, all
always loaded. Nothing acts. Watching, getting a film, notify me, watch later and follows are buttons on a
title's page; the assistant answers with cards that open it. There are no room controls: whoever is in a
room has the player.

## Invariants

- **Every call is the person's.** The tools reach the API with the access token the turn carries, so what
  a person sees is exactly what the API lets them, and `watched` is theirs. The service holds no key of
  the API's and no state of its own: the plugin keeps no data directory and announces nothing.
- **Facts come from the film database, never from the model.** A value the API left out is not written.
  The database has a tag for black and white and none for colour: a title not tagged is "not tagged", never
  "in colour", and a title whose keywords have not arrived is "not known". A title the database holds no
  IMDb id for is named in words and has no card.
- **A card is a title with a page.** Every tool that names titles returns a `titles` card:
  `{kind: "titles", titles: [{imdbId, kind, title, year?, posterUrl?, inLibrary, watched}]}`. A result
  with no title to show carries none. The site draws a card only for a title the reply names.
- **A result says what it matched and what to loosen.** `discover` writes what each word became (`Matched`)
  and, when nothing fits, which filter to take away first, so the model changes the question rather than
  repeating it or filling the list from memory.
- **The prefix is byte-stable.** Nothing volatile (a date, a count) goes in `prompt.md` or a tool
  description; the person's name rides in the per-turn context and the date is the harness's.
- **A tool takes no name the harness keeps for itself** (`ask_question`, `question`, memory, search,
  fetch, skills).

## Configuration

`Assistant:Listen` (loopback, where nginx proxies the site), `Assistant:HarnessAddress`,
`Assistant:PluginKey` (the key issued for `moviebot` at the harness, set only in the environment as
`Assistant__PluginKey`), `Assistant:RecordApiTo` (a file every API answer is appended to, as a recording a
test replays; it holds whatever the API said to the person who asked, so it is not committed),
`Api:BaseUrl` and `Oidc:*` as the other programs read them.

## Tests

`tests/MovieBot.Tests/Assistant/` runs the real tools over `CinemaWorld`, an invented film database and
library behind the same HTTP client the service uses (`ReplayHandler`). A recording of the live API replays
the same way. `ToolContexts` reaches the plugin package's tool context by reflection, and is the one place
the suite depends on the package's internals.
