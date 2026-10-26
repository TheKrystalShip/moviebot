# MovieBot.Api invariants

`docs/api-contract.md` is the authority for what this serves on the wire. Rooms are
`Sessions/CLAUDE.md`; subtitles fetched from outside are `Subtitles/CLAUDE.md`; downloads, wishes
and the library's clock are `Acquisition/CLAUDE.md`; the plan the hand-off transcodes by is
`Transcode/CLAUDE.md`; notifications, pushes and invites are `Notifications/CLAUDE.md`; saving a
film onto a device is `Offline/CLAUDE.md`.

- **The server is authoritative and there is no host.** Clients send intent; the store decides,
  stamps the server clock and assigns the next revision. A client never pushes state.
- **Every type on the wire is named in a serializer context, and the two contexts are the only
  resolver.** `ApiJsonContext` holds this service's own shapes and `ManifestJsonContext` the Core
  library's, and nothing falls back to reflection, in the JIT build the tests run as much as in the
  native one, so an anonymous object or an unregistered type fails a test rather than a request. A
  list is looked up by the type the endpoint declares and written by the type it holds, so both are
  registered, and a list bound for the wire is built as a `List<T>`: a collection expression yields a
  type of the compiler's own that nothing has registered.
- **Everything the API keeps is one SQLite database, `Data/CinemaDatabase`** (`moviebot.db` in the
  state directory, or `Database:Path`; the tables are `Data/Schema`). Each read takes a pooled
  connection and each change is one immediate transaction, after every change before it, so a store
  that reads before it writes (a want's flags, a notification's key) decides inside the transaction
  it writes in. A store keeps nothing in memory but what a listing reads on every row
  (`PersonDirectory`), written through. What a person did is columns. Served bytes are files and
  nothing else is: fetched WebVTT under `Subtitles:Root`, and the catalogue's images in its own
  directory. The file is `0600`, since it holds the push key pair. A file at an earlier schema version
  is brought forward by `Schema`'s steps, each taking the file from one version to the next in one
  transaction and carrying across the records a changed table held, and a file at a version this build
  does not know stops the API from starting.
- **Resolve configuration inside the DI factory, not at the top of `Program.cs`.** Reading
  `builder.Configuration` while the builder is still being assembled misses sources added later — a
  test host's media root, for one — and the service silently points somewhere else.
- **Playlists for a transcoding title are `no-store`, and served from one open file.** A cached
  growing playlist makes the film appear to end early, which looks exactly like a broken transcode.
  ffmpeg replaces the playlist by renaming a fresh copy over it, so one served by path takes its
  length from one copy and its bytes from the next, and breaks off mid-playlist.
- **MovieBot signs nobody in; every route names the action it needs** (`Auth/`; the token rules, the
  service key, the guest link, the media ticket and the issuer pin are `docs/api-contract.md`, "Signing
  in", and `../../../kgsm-docs/systems/moviebot/sign-in.md`). `CinemaActions` declares the actions once:
  the checks name them and `/.well-known/tks-actions.json` is written from the same list. A route
  without `RequireAction`, `RequireActionOrService`, `RequireService` or `AllowAnonymous` admits
  nobody, because the fallback policy is that. The person is always the token's — a request body
  never names who is asking.
- **The service key and a guest link name nobody.** A route that acts for a person never takes the
  service key; a guest link is authenticated only by the routes that name it (`Sessions/CLAUDE.md`,
  Guests).
- **A film's bytes are opened by a media ticket, never by the header and the ticket together.** The
  ticket is the same string for every viewer of a film, which makes their requests one cacheable URL; a
  shared cache refuses to store a response to a request carrying `Authorization`.
- **Media answers HEAD as well as GET.** Browsers probe a URL before fetching it, and a 405 there
  reads as the file being unavailable.
- **Which disk a film is on changes no URL.** `/media/{id}/…` resolves the id per request (see
  `src/MovieBot.Core/CLAUDE.md`), which is also what makes it safe to move a film out from under a
  room that is watching it.
- **CORS reflects the origin rather than enumerating one.** The Activity is served from Discord's
  proxy under an origin that is not known ahead of time, and a failed preflight is invisible: no
  status, no log, just a request that never happens.
- **A path that is no route and names no file is a page.** Cinema names its pages and rooms by
  path, so the fallback answers any of them with the site's index; the Activity is the same page at
  `/embed/`. It is the fallback rather than the default-files middleware that does this, because an
  endpoint matched ahead of the static files means they never see the request. Paths under `/api`,
  `/media` and `/hub` stay a 404.
- **Everything about a title is the catalogue's, and the API reaches no source** (`Catalogue/`;
  `../MovieBot.Catalogue/CLAUDE.md`). `moviebot-catalogue` keeps one entry per title, IMDb's datasets
  the base with the film database and Wikidata over it, in `catalogue.db` and `images/` under
  `Catalogue:Directory`; the API opens it read-only through `MovieBot.Catalogue.Store`
  (`CatalogueSetup`) and serves it under `/api/catalogue` and `/images`. Nothing the API serves asks
  TMDB, IMDb or Wikidata, so no request waits on anybody else. A catalogue that cannot be read is
  `CatalogueState.Problem`, a `503` that says why on the catalogue's routes and a title with no card
  everywhere else; a title the catalogue does not list is served with no card, never with one made up.
- **A title is one card, the same everywhere** (`docs/api-contract.md`, Catalogue). Every view naming
  a title (the library's listings, a show's episodes, wants, follows, plans, offline packages, rooms,
  Watch now) carries the catalogue's `TitleCard`, read through `Catalogue/CatalogueTitles`, which holds
  each card five minutes, so a page of a hundred titles reads the catalogue once. A title's poster is
  its card's; in the library `Library/TitlePosters` serves that poster at `/media/{id}/poster.jpg`, and
  the poster the hand-off put beside the title only for one the catalogue does not list. A source's own
  cover art is never extracted (`src/MovieBot.Handoff/CLAUDE.md`).
- **Every list is the one list query, joined with the asking person** (`Catalogue/CatalogueEndpoints`,
  `CatalogueViewers`). The person's parameters (`unwatched`, `inLibrary`, `here`) and the card's `you`
  are joined from the library as listed, the watch record and the wants when a page is read, and are
  never kept with the catalogue. A parameter the query does not take is refused, because a list that
  ignores a filter answers a question nobody asked. The facets are counted from every entry's facet
  values held in memory (`Catalogue/FacetIndex`, read again every ten minutes), since counting them
  from the file scans every title a query names without its picks, the whole catalogue for a page with
  no other filter.
- **Words typed into Discover are read by `Catalogue/QuestionReader`** (pure: a closed grammar over
  IMDb's genres and the catalogue's languages, passed in) into the list query's filters
  (`Catalogue/CatalogueQuestions`), so a misread name is a refusal and never a wrong answer.
- **Discover is list queries** (`Catalogue/DiscoverIndex`). Every shelf is a name and a list query by
  the catalogue's ids (genres, keywords, companies, people, Wikidata items, franchises, rankings); the
  heads of the shelves the same for everybody (posters, counts) are read every thirty minutes in the
  background, so `/api/catalogue/discover` answers from memory, and the person's rows and a series'
  progress are joined when it is read. "Popular here" names nobody and no count.
- **An episode is numbered as its release numbers it, everywhere** (`Shows/EpisodeNumbering`). The
  tracker, the torrent's files, the library and the subtitle index agree on it; the film database
  sometimes does not, most often by counting a special or a promotion as a season's first episode.
  `CatalogueTitles.Show` hands out every show's seasons already put into the releases' numbering, so
  no reader translates. The evidence is the episode names release and file names carry after their
  marker, from the library and from the show's torrents in the client (`EpisodeNumberingWarmer`, every
  ten minutes): each name matching the catalogue's episode of the same name in that season pins that
  episode to its release's number, which holds a season the releases order differently from the
  catalogue, and every episode between takes the offset of the anchor below it in the catalogue's
  order. A catalogue episode before the first release episode is a special and leaves the season. A still is found by the release's numbering
  (`/images/titles/{imdbId}/sNNeMM`). A season with no names to match keeps the catalogue's numbering.
- **The shelf is listed from memory.** `Library/LibraryListing` rebuilds the library's list every
  ten seconds in the background, and `/api/titles`, `/api/shows`, `/api/me/shows/{imdbId}`, the library's
  pages (`/api/library/{scope}`, `Library/ListedPages`) and a title's neighbours read that list. Building it asks the filesystem about every title, and on a host short
  of memory while a transcode runs, the kernel forgets those answers within seconds and the cold
  root is a spinning disk: built per request, a listing costs a second. A change on disk reaches the
  shelf within ten seconds. What must be exact reads `TitleLibrary` directly: a title's own
  manifest, a room's head, and whether a film is here before anything is fetched.
- **A title's category is its card's, beside its kind** (`Library/TitleCategories`). The kind is how a
  title is built and the category where people look for it: anime is what holds Animation among its
  genres and was made in Japanese (`docs/api-contract.md`, Categories); a library title with no card is
  general.
- **An episode is a title, and a show is its episodes grouped.** Every route that serves a title
  serves an episode unchanged; `/api/shows` groups the library's episodes by the show their manifest
  names, and `/api/me/shows/{imdbId}` joins the catalogue's seasons with the episodes the library holds
  and the person's progress in them. An episode's `card` on `/api/titles` is its show's.
- **A person's own settings are asked for by token, never by id.** `/api/me/preferences`,
  `/api/me/tracks/{titleId}` and `/api/me/sorts` answer for whoever the token names, so there is no route by which anybody reads or
  writes anybody else's; the service key names nobody and is refused there. Progress, history, follows and wants
  (`/api/me/progress`, `/api/me/history`, `/api/me/follows`, `/api/me/wants`) are asked for the same
  way.
- **How a person watches is one record the API keeps, checks and pushes** (`People/Preferences`,
  `People/FilmTracks`, `People/ViewerPush`). Subtitle style and quality are the account's
  preferences; the subtitle and audio track are kept per person and film in `viewer_tracks`, so every
  device the person watches a film on, and a phone driving a television, read the same one. The API
  refuses what the film or the ranges do not have rather than bending it: a track the title's manifest
  does not list (a subtitle that can be shown, an audio track), a quality that is neither Auto nor a
  height, a style outside the ranges `SubtitleStyle` keeps. A quality is the person's default for every
  film, so it is held to what a picture can be, never to one film's rungs. A change is pushed to
  `PersonGroups.Everything(person)` (`PreferencesChanged`, `TracksChanged`): the person's every connection
  with an account, a playing player's and a phone's remote among them, and never the room's groups, so no
  room state, roster or `StateChanged` moves for it. Each write is stamped with a `revision` inside the
  transaction that keeps it (`People/Revisions`), one stream per person for preferences and one per person
  and film for tracks, so a device can drop a push older than what it holds. A guest has no account and keeps their choices in
  their own browser.
- **A followed show is watched by `Shows/FollowWatcher`, and what it announces is kept.** A follow
  starts from a baseline, the latest episode already due, so only later episodes are news. An episode
  is due from the day after its air date, an air date being a date read against UTC, and is looked for
  until `Follow:GiveUpDays` past it; only a show with an episode due is asked of the tracker, once per
  `Follow:AskMinutes` per episode. An offered episode is fetched once for every follower who wants it,
  at the best quality any of them watches at, and what was announced is kept beside the follows so a
  restart never tells anybody twice. A new episode comes into view as the catalogue lists it.
  The rules for who is told what are in `Notifications/CLAUDE.md`.
- **A want is keyed by the person and the title's IMDb id, never by a library id** (`Wants/`). The
  library is what comes and goes; a film wanted outlives leaving it and is the same want when it comes
  back, and a title that was never here is wanted from its catalogue page the same way. Whether it is
  here, whether the asking person watched it, where a show's person is and how many others want it are
  joined in when the want is read, from the library as listed, the watch record and the other wants,
  and never stored. Wanting takes `cinema:watch`; what the server may spend on a want is captured from
  the token each time it is written (`may_fetch` from `cinema:download`, `may_wish` from `cinema:wish`),
  as a follow's auto-download is. A want is named to others only while it is visible, and a just-me one
  is counted by nobody else. It ends when its person removes it or finishes the title:
  `WatchRecorder` sees the finishing, for a film and for a show's last aired episode. The wish for a
  film nothing has yet is kept while some want on it has `may_wish`, and goes with the last, in the
  transaction that ends that want (`Wants/WantStore`).
- **What a person watched comes from the room, the one source of where a film is** (`People/`). A
  room's state is a position at an instant and everything between two changes follows from it, so
  nothing polls the rooms. `SessionStore` raises `Watched` when a stretch of playing ends — a pause,
  a seek, a film changed, somebody leaving, the server stopping, a player coming back alone carrying
  the room on from its own playhead — and `WatchRecorder` credits each person who was watching with
  where it got to. What a room is playing right now is newer than any
  record and is read from the room (`SessionStore.Viewing`), so progress and "last played" never
  lag the room. A paused room records nothing: the library's clock reads each film's last play to
  decide when an unwatched film leaves, and a room left paused overnight must not look like a play. A person forgetting a film removes
  their own entry and progress and leaves the film's last play. `PersonDirectory` is what turns an
  account into a name, learned from every token validated; a record never shows an id where a name
  belongs. When somebody was last seen is held in memory alone.
- **The API publishes what a page needs to sign in on `/api/config`**, including its public address:
  the Activity's origin is Discord's proxy, reachable only from inside the Activity, and the poster a
  viewer's presence shows is fetched by Discord's servers, so the page builds the poster URL from that
  address. The Activity signs in through `Discord/ActivitySignIn.cs` (`docs/api-contract.md`).

## What a film gains while people are watching it is pushed to them (`Library/TitleChanges.cs`)

The preview sheet is written after the main pass, and so are the subtitles of a source that was still
arriving, so the manifest that marks a film ready is the first to name them. The API watches the
manifests of the titles occupied rooms hold and sends the fresh copy down the hub as `TitleChanged`,
and the player adopts what changed: the head, the sheet, the subtitle list. There is no poll in the
player; a page that could not be heard from asks once on the way back. It goes over the hub rather
than a second channel because every viewer already holds an authenticated connection, and a subtitle
fetched from outside is announced by its route, since the file lives beside the manifest and the
manifest's timestamp says nothing happened.
