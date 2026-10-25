# moviebot-assistant

The plugin that helps the people of Cinema find films and shows. The organization's assistant
harness (`tks-assistant`) is the assistant: the turn loop, the model, the conversations, the memory, the
skills and the reply guards. This service brings the domain: a prompt appendix, read-only tools over
the film database through MovieBot's API, five skills, the person's name, and the sign-in. It is built on
`TheKrystalShip.Agent.Plugin`; the wire it relays is that package's (`tks-assistant/docs/wire-contract.md`).

Read before changing: `../../../kgsm-docs/systems/moviebot/assistant.md` (the assistant in Cinema),
`../../../kgsm-docs/systems/assistant/plugins.md` (what a plugin is) and `docs/api-contract.md` (the routes
the tools read).

**The assistant has no name.** No prompt, label, reply or string names it; it is "the assistant", one
interface and one memory across products. A test fails the build if the profile or a string in the
service says otherwise.

## Shape

- **`profile/`** is the prose the harness is given: `prompt.md` (the appendix, placed inside
  `<plugin name="moviebot">`) and `skills/<name>/SKILL.md` with `references/`. The package reads the
  directory and registers it, again after a harness restart. Editing a file there is a redeploy; with
  `Assistant:TuneFrom` set to this directory on a development run it is read as it changes.
- **`Tools/`** one partial type per tool, declared with `[AssistantTool]` and a `[Description]` that
  is exactly what the model reads, over an `Args` record that is its schema. A tool runs for a signed-in
  person, as that person; `ToolRefusal` is what it throws for a reason the model should read, and any
  other failure is answered as "could not finish". `TitleText` writes what the API said, one title to a
  line; `TitleCards` the structured result beside it, serialized through `CardJson`. `Vocabulary` turns
  what a person says into what the list filters take: a genre or a language into the catalogue's own (read
  from its facets), a plot word into a keyword and a name into a person (through its terms), a title into
  its IMDb id.
- **`CinemaGuards.cs`** the reply guards, and the speech vocabulary in `Program.cs`: code, declared on
  the plugin builder.
- **`Api/`** the API as the asking person: `CinemaApi` (the routes the tools read, every refusal
  worded for the model as a `ToolRefusal`) and the DTOs (only the fields a tool reads, read through the
  source-generated `ApiJson`). Every list a tool gives is one `/api/catalogue/titles` query (`TitleFilter`):
  a search, a discovery, a person's credits, a ranking, a window of release dates, what the library holds
  ready. A title is its page at `/api/catalogue/titles/{imdbId}`; names resolve through
  `/api/catalogue/terms`, genres and languages through `/api/catalogue/facets`; the person's own are
  `/api/me/*` and `/api/anticipated`.
- **`Callers.cs`** sign-in: the access token on the request, validated as the API validates it. No token
  or a foreign one is `401`; the provider's token without `cinema:watch` is `403`; a provider whose keys
  cannot be fetched is `502`.
- **`CinemaTurnContext.cs`** the live block sent with each turn.
- **`Program.cs`** the domain registrations; the host, `/health` and the relayed routes are the
  package's (`tks-assistant/docs/writing-a-plugin.md`).

## The tools

`search_titles`, `title_details`, `person_credits`, `discover`, `find_by_clues`, `whats_new`, `my_taste`,
`anticipated`: all `read`, all always loaded. Nothing acts. Watching, getting a film, wanting a title and follows are buttons on a
title's page; the assistant answers with cards that open it. `find_by_clues` is where a half-remembered
film is found: the model extracts the clues and the tool resolves the people, reads their dated credits,
lists what has the look and the plot words, reads the best candidates' database pages, and ranks by how
many clues each fits, with the clues it fits, the ones it does not, and the ones it could not check.
`anticipated` lists the films people are waiting on, with how many wait on each and whether the asking
person is one of them; it needs the right to wait on films (`cinema:wish`) and says so when refused.
There are no room controls: whoever is in a room has the player.

## Invariants

- **Every call is the person's.** The tools reach the API with the access token the turn carries, so what
  a person sees is exactly what the API lets them, and `watched` is theirs. The service holds no key of
  the API's and no state of its own: the plugin keeps no data directory and announces nothing.
- **Facts come from the film database, never from the model.** A value the API left out is not written,
  and a word the catalogue holds nothing for is said back with what it does hold, never dropped from the
  question.
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
`Assistant:TuneFrom` (development), `Api:BaseUrl` and `Oidc:*` as the other programs read them.

## Tests

`tests/MovieBot.Tests/Assistant/` runs the real tools through `ToolTest` over `CinemaWorld`, an invented
film database and library behind the same HTTP client the service uses (`ReplayHandler`). A recording
of the live API replays the same way. `ProfileTests` holds the composed profile to
`tests/baseline/profile.json`, the profile registered with the harness, so a tool, a guard or a speech
setting cannot change what the model reads without that test saying so. `PluginHostTests` boots the service in
process against a fake harness and a fake provider.

`tests/MovieBot.Assistant.Suite` runs `cases.json` (a prompt, the tools expected, what the reply must and
must not say) through the plugin package's suite runner against a live harness and this plugin, as a
signed-in person. Its `README.md` says how the Cinema access token is supplied.
