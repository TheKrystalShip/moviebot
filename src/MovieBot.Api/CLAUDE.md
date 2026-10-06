# MovieBot.Api invariants

`docs/api-contract.md` is the authority for what this serves on the wire. Rooms are
`Sessions/CLAUDE.md`; subtitles fetched from outside are `Subtitles/CLAUDE.md`; downloads, wishes
and the library's clock are `Acquisition/CLAUDE.md`; the plan the hand-off transcodes by is
`Transcode/CLAUDE.md`; notifications, pushes and invites are `Notifications/CLAUDE.md`.

- **The server is authoritative and there is no host.** Clients send intent; the store decides,
  stamps the server clock and assigns the next revision. A client never pushes state.
- **Every type on the wire is named in a serializer context, and the two contexts are the only
  resolver.** `ApiJsonContext` holds this service's own shapes and `ManifestJsonContext` the Core
  library's, and nothing falls back to reflection, in the JIT build the tests run as much as in the
  native one, so an anonymous object or an unregistered type fails a test rather than a request. A
  list is looked up by the type the endpoint declares and written by the type it holds, so both are
  registered, and a list bound for the wire is built as a `List<T>`: a collection expression yields a
  type of the compiler's own that nothing has registered.
- **Resolve configuration inside the DI factory, not at the top of `Program.cs`.** Reading
  `builder.Configuration` while the builder is still being assembled misses sources added later — a
  test host's media root, for one — and the service silently points somewhere else.
- **Playlists for a transcoding title are `no-store`.** A cached growing playlist makes the film
  appear to end early, which looks exactly like a broken transcode.
- **MovieBot signs nobody in; every route names the action it needs** (`Auth/`). A person's access
  token is minted by the OpenID Connect provider `Oidc:Issuer` names and validated here against its key
  set, its issuer, the audience `cinema`, ES256 and `at+jwt`, so a token minted for anything else is
  refused. The actions it holds are the `tks_actions` claim, and `CinemaActions` declares them once:
  the checks name them and `/.well-known/tks-actions.json` is written from the same list. A route
  without `RequireAction`, `RequireActionOrService`, `RequireService` or `AllowAnonymous` admits
  nobody, because the fallback policy is that. The person is always the token's — a request body
  never names who is asking — and an `act` claim, a client acting for them, is logged beside them.
- **The issuer is pinned.** The first start records `Oidc:Issuer` in `Oidc:IssuerRecord`, and a later
  start against another refuses until `Oidc:AdoptIssuer` names the new one: every record here is keyed
  by the provider's `sub`, and another provider's `sub` would be somebody else.
- **The service key is machine work that names nobody.** The hand-off and the bot's background
  passes present it; a route that acts for a person never takes it.
- **A film's bytes are opened by a media ticket.** The access token says who somebody is, which a
  segment has no use for; the ticket names one title and an hour, is signed with
  `Auth:MediaTicketKey`, is the same string for every viewer of that film, and so makes their
  requests one cacheable URL. A client sends the ticket or the header and never both: a shared cache
  refuses to store a response to a request carrying the header.
- **Media answers HEAD as well as GET.** Browsers probe a URL before fetching it, and a 405 there
  reads as the file being unavailable.
- **Which disk a film is on changes no URL.** `/media/{id}/…` resolves the id per request (see
  `src/MovieBot.Core/CLAUDE.md`), which is also what makes it safe to move a film out from under a
  room that is watching it.
- **CORS reflects the origin rather than enumerating one.** The Activity is served from Discord's
  proxy under an origin that is not known ahead of time, and a failed preflight is invisible: no
  status, no log, just a request that never happens.
- **A path that is no route and names no file is a page.** Krystal Cinema names its pages and rooms by
  path, so the fallback answers any of them with the site's index; the Activity is the same page at
  `/embed/`. It is the fallback rather than the default-files middleware that does this, because an
  endpoint matched ahead of the static files means they never see the request. Paths under `/api`,
  `/media` and `/hub` stay a 404.
- **The film database is reached by this service alone, and asked once per film.** `Films/FilmStore`
  keeps each description and every image it names under one directory per film in the state
  directory, serves its own copies at `/films/{imdbId}/…`, and asks again only once a copy is
  `Tmdb:RefreshDays` old; a database that cannot be reached then costs freshness and nothing else.
  `FilmWarmer` keeps every library film, every wished film and every listed title described, so a
  listing reads only what is kept and never waits on the network. No page talks to the database or its image store.
  A show is kept the same way by `Shows/ShowStore` under `shows/`, its description and every season's
  episodes asked for together with their stills, and served at `/shows/{imdbId}/…`; the warmer keeps
  every show the library holds described. An episode's show is never asked of the film side.
  `Films/PopularNow` keeps the week's trending films and shows under `popular/`, the list and one
  poster per entry and nothing more, asked for every six hours and served at `/api/popular` and
  `/popular/{imdbId}.jpg`; an entry's page asks for the rest of it when it is opened.
  A search result's rating and overview are `Films/TitleGlances`, one lookup by IMDb id per title
  kept under `glances/`: a title already described answers from its description, a search waits
  for new lookups at most a second and a half, and a kept one is refreshed in the background, after
  a year for a title out over a year and after a week for anything newer. A result's poster is the
  one its page shows: the described title's kept poster, or the glance's, fetched once beside it and
  served at `/glances/{imdbId}/poster.jpg`. `Films/Catalogue` browses the database by a person and by
  a genre, keeping each answer under `catalogue/`, a person's credits for a week and a genre's most
  popular titles for a day, and offers every title it lists to the glances, so its poster is served
  the same way; a person's portrait is kept with their credits. A description whose cast carries no
  person id is due at once, since that id is what leads from a cast member to their work.
  `Acquisition/TitleSearch` holds the title index's answer for a name in memory for an hour.
- **The shelf is listed from memory.** `Library/LibraryListing` rebuilds the library's list every
  ten seconds in the background, and `/api/titles`, `/api/shows`, `/api/shows/{imdbId}` and a title's
  neighbours read that list. Building it asks the filesystem about every title, and on hotbox, short
  of memory while a transcode runs, the kernel forgets those answers within seconds and the cold
  root is a spinning disk: built per request, a listing costs a second. A change on disk reaches the
  shelf within ten seconds. What must be exact reads `TitleLibrary` directly: a title's own
  manifest, a room's head, and whether a film is here before anything is fetched.
- **An episode is a title, and a show is its episodes grouped.** Every route that serves a title
  serves an episode unchanged; `/api/shows` and `/api/shows/{imdbId}` group the library's episodes by
  the show their manifest names and join them with the show's description, and an episode's
  `details` on `/api/titles` are its show's.
- **A person's own settings are asked for by token, never by id.** `/api/me/preferences` answers for
  whoever the token names, so there is no route by which anybody reads or writes anybody else's; the
  service key names nobody and is refused there. Progress, history, follows and lists
  (`/api/me/progress`, `/api/me/history`, `/api/me/follows`, `/api/me/lists`) are asked for the same
  way.
- **A followed show is watched by `Shows/FollowWatcher`, and what it announces is kept.** A follow
  starts from a baseline, the latest episode already due, so only later episodes are news. An episode
  is due from the day after its air date, an air date being a date read against UTC, and is looked for
  until `Follow:GiveUpDays` past it; only a show with an episode due is asked of the tracker, once per
  `Follow:AskMinutes` per episode. An offered episode is fetched once for every follower who wants it,
  at the best quality any of them watches at, and what was announced is persisted beside the follows
  (`follows.json`) so a restart never tells anybody twice. A followed show's description is asked of
  the film database again after `Follow:RefreshHours`, which is what brings a new episode into view.
  The rules for who is told what are in `Notifications/CLAUDE.md`.
- **A list is keyed by the title's IMDb id, never by a library id** (`Lists/`). The library is what
  comes and goes; a film saved for later outlives leaving it and is the same entry when it comes
  back, and a title that was never here is saved from its catalogue page the same way. Whether it is
  here, and whether the asking person watched it, are joined in when the list is read, from the
  library as listed and the watch record, and never stored. A list is shown to its owner and its
  members only, so somebody else asking for it is answered as if it did not exist. `later` in a route
  is the asking person's own Watch later, which has no members. Everything is in `lists.json`.
- **What a person watched comes from the room, the one source of where a film is** (`People/`). A
  room's state is a position at an instant and everything between two changes follows from it, so
  nothing polls the rooms. `SessionStore` raises `Watched` when a stretch of playing ends — a pause,
  a seek, a film changed, somebody leaving, the server stopping — and `WatchRecorder` credits each
  person who was watching with where it got to. What a room is playing right now is newer than any
  record and is read from the room (`SessionStore.Viewing`), so progress and "last played" never
  lag the room. A paused room records nothing: the library's clock reads each film's last play to
  decide when an unwatched film leaves, and a room left paused overnight must not look like a play. A person forgetting a film removes
  their own entry and progress and leaves the film's last play. `PersonDirectory` is what turns an
  account into a name, learned from every token validated; a record never shows an id where a name
  belongs. When somebody was last seen is held in memory alone.
- **The API publishes what a page needs to sign in on `/api/config`**: the issuer, the site's client
  id and the television's, the actions claim, and its public address. The Activity's origin is Discord's proxy,
  reachable only from inside the Activity, and the poster a viewer's presence shows is fetched by
  Discord's servers, so the page builds the poster URL from that address.
- **The Activity signs in through the API** (`Discord/ActivitySignIn.cs`). It has no browser to
  redirect, so `/api/auth/discord/callback` redeems the code its SDK was given with Cinema's Discord
  application and exchanges the Discord access token at the provider as the confidential client
  `Oidc:ActivityClientId`; the provider decides who that Discord account is and whether it may in.

## What a film gains while people are watching it is pushed to them (`Library/TitleChanges.cs`)

The preview sheet is written after the main pass, and so are the subtitles of a source that was still
arriving, so the manifest that marks a film ready is the first to name them. The API watches the
manifests of the titles occupied rooms hold and sends the fresh copy down the hub as `TitleChanged`,
and the player adopts what changed: the head, the sheet, the subtitle list. There is no poll in the
player; a page that could not be heard from asks once on the way back. It goes over the hub rather
than a second channel because every viewer already holds an authenticated connection, and a subtitle
fetched from outside is announced by its route, since the file lives beside the manifest and the
manifest's timestamp says nothing happened.
