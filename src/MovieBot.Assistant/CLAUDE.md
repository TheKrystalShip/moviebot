# moviebot-assistant

The plugin that helps the people of Cinema find films and shows and plan to watch them together. The
organization's assistant harness (`tks-assistant`) is the assistant: the turn loop, the model, the
conversations, the memory, the skills and the reply guards. This service brings the domain: a prompt
appendix, tools that read MovieBot's API and propose wants and plans, the skills, the person's name and
clock, and the sign-in. It is built on
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
  its IMDb id. A proposal goes through `Proposals`, which runs the change as the person who confirms;
  `TitleLookup` and `PeopleLookup` turn what the model names into an id, refusing with the candidates
  rather than guessing; `PersonClock` reads and writes times in the person's zone; `PlanText` writes a
  plan.
- **`CinemaGuards.cs`** the reply guards, and the speech vocabulary in `Program.cs`: code, declared on
  the plugin builder.
- **`Api/`** the API as the asking person: `CinemaApi` over `MovieBot.Client`'s `MovieBotApiClient`,
  the client the bot shares, in the shapes the API publishes (`MovieBot.Core`), with every failure worded
  for the model as a `ToolRefusal`. The plugin declares no shape of the API's and makes no HTTP call of its
  own: a route or a shape a tool needs is added to the client and to Core. Every list a tool gives is one
  `/api/catalogue/titles` query (the client's `TitleFilter`):
  a search, a discovery, a person's credits, a ranking, a window of release dates, what the library holds
  ready. A title is its page at `/api/catalogue/titles/{imdbId}`; names resolve through
  `/api/catalogue/terms`, genres and languages through `/api/catalogue/facets`; the person's own are
  `/api/me/*`, `/api/anticipated`, `/api/titles/{imdbId}/wanters`, `/api/people` and `/api/plans`.
- **`Callers.cs`** sign-in: the access token on the request, validated as the API validates it. No token
  or a foreign one is `401`; the provider's token without `cinema:watch` is `403`; a provider whose keys
  cannot be fetched is `502`. The browser's time zone (`X-Time-Zone`) rides on the caller when this host
  knows it.
- **`CinemaTurnContext.cs`** the live blocks sent with each turn: the person's name, and the time it is
  for them when their zone's clock is not the harness host's.
- **`Program.cs`** the domain registrations; the host, `/health` and the relayed routes are the
  package's (`tks-assistant/docs/writing-a-plugin.md`).

## The tools

`search_titles`, `title_details`, `person_credits`, `discover`, `find_by_clues`, `whats_new`, `my_taste`,
`anticipated`, `wanted_together` and `my_plans` read, always loaded. `want` proposes putting titles on the
person's Want to watch or taking them off, and `make_plan` a plan, only with the times and people the
person gave, both always loaded; `answer_plan` and `change_plan` (suggest a time, invite, pick the time,
cancel) are brought by the `watch-together` skill. Every proposal is checked against what the API would refuse before it is made (the plan limits
are Core's `PlanLimits`), so a person never confirms something that fails for a reason known in advance;
picking and cancelling are the maker's alone. Watching, getting a film and follows are buttons on a
title's page; the assistant answers with cards that open it. `find_by_clues` is where a half-remembered
film is found: the model extracts the clues and the tool resolves the people, reads their dated credits,
lists what has the look and the plot words, reads the best candidates' database pages, and ranks by how
many clues each fits, with the clues it fits, the ones it does not, and the ones it could not check.
`anticipated` lists the films people are waiting on, with how many wait on each and whether the asking
person is one of them; it needs the right to wait on films (`cinema:wish`) and says so when refused.
There are no room controls: whoever is in a room has the player.

## Invariants

- **Every call is the person's.** The tools reach the API with the access token the turn carries, and a
  proposal runs with the token of the person who confirms it, so what a person sees and does is exactly
  what the API lets them, and `watched` is theirs. The service holds no key of the API's and no state of
  its own: a proposal waits in memory, the plugin keeps no data directory and announces nothing.
- **Nothing changes inside a turn.** A tool that changes something proposes it (`ToolClass.Staged`), and
  the reply says it is waiting on the person.
- **Facts come from the film database, never from the model.** A value the API left out is not written,
  and a word the catalogue holds nothing for is said back with what it does hold, never dropped from the
  question.
- **A card is a title or a plan with a page.** Every tool that names titles returns a `titles` card:
  `{kind: "titles", titles: [{imdbId, kind, title, year?, posterUrl?, inLibrary, watched}]}`; `my_plans` and
  a confirmed `make_plan` return a `plans` card: `{kind: "plans", plans: [{id, imdbId, kind, title, year?,
  posterUrl?, state, fixedTime?}]}`. A result with nothing to show carries none. The site draws a card only
  for a title the reply names.
- **A result says what it matched and what to loosen.** `discover` writes what each word became (`Matched`)
  and, when nothing fits, which filter to take away first, so the model changes the question rather than
  repeating it or filling the list from memory.
- **The prefix is byte-stable.** Nothing volatile (a date, a count) goes in `prompt.md` or a tool
  description; the person's name and clock ride in the per-turn context and the date is the harness's.
- **Times are the person's.** A time the model writes is read in the person's zone, and a time a tool
  writes is written in it; the API is sent instants.
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
setting cannot change what the model reads without that test saying so. A proposal is confirmed in a test
as the plugin's confirm route does (`ConfirmAsync`), and `BodyTap` keeps what each write sent, so a test
holds both what the person was shown and what reached the API. `PluginHostTests` boots the service in
process against a fake harness and a fake provider.

`tests/MovieBot.Assistant.Suite` runs `cases.json` (a prompt, the tools expected, what the reply must and
must not say) through the plugin package's suite runner against a live harness and this plugin, as a
signed-in person. Its `README.md` says how the Cinema access token is supplied.
